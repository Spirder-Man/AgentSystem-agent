using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace Agent1.Services.DriftMonitor;

/// <summary>
/// 认知漂移监测·门面 —— 测量链路编排（采集 → 抽取 → 比对 → 度量 → 入库）。
///
/// Phase 2 提供被动采集：对任意一段 AI 输出文本调用 RecordTurnAsync，
/// 即可得到该轮对话的漂移率并画出随轮次/上下文长度变化的漂移曲线。
///
/// 幂等性：同一 (session, turn, triggerType) 重放测量时覆盖更新，
/// 不重复堆积——对应 drift_probes 的 UNIQUE 约束。
/// </summary>
public class DriftMonitor
{
    private readonly IDatabaseService _db;
    private readonly DriftAnchorRegistry _registry;
    private readonly DriftClaimExtractor _extractor;
    private readonly DriftMetricsService _metrics;

    public DriftMonitor(IDatabaseService db, DriftAnchorRegistry registry,
        DriftClaimExtractor extractor, DriftMetricsService metrics)
    {
        _db = db;
        _registry = registry;
        _extractor = extractor;
        _metrics = metrics;
    }

    // ═══════════════════════════════════════════
    // 采集（Phase 2 核心入口）
    // ═══════════════════════════════════════════

    /// <summary>
    /// 记录一轮对话并测量漂移：抽取断言 → 比对锚点 → 计算漂移率 → 入库。
    /// 表不存在时静默跳过（锚点基线 004/005 迁移未执行时不影响主链路）。
    /// </summary>
    public async Task<DriftProbeResult> RecordTurnAsync(
        string sessionId, int turnNo, int contextTokens, string text,
        string triggerType = "reply")
    {
        // 1. 加载当前版本锚点
        var anchors = await LoadAnchorsSafelyAsync();
        if (anchors == null)
            return EmptyResult();

        // 2. 抽取断言 → 3. 比对 → 4. 度量
        var claims = _extractor.Extract(text, anchors);
        var matches = claims
            .Select(c => new ClaimMatch
            {
                Anchor = c.Anchor,
                Score = DriftMatcher.MatchScore(text, c.Anchor.CanonicalValue),
                ActualTokens = c.MentionedTokens
            })
            .ToList();
        var result = _metrics.Compute(matches);

        // 5. 入库（幂等覆盖）
        await SaveProbeAsync(sessionId, turnNo, contextTokens, triggerType, result,
            await _registry.GetCurrentVersionAsync(), matches);

        return result;
    }

    /// <summary>
    /// 主动探针测量（Phase 3 核心入口）：黄金问题回答 → 强制断言 → 漂移量入库。
    ///
    /// 幂等设计：(sessionId, 模板id, 'probe') 唯一——同一探针反复测量覆盖更新，
    /// 不堆积；不同探针槽位不同互不干扰，按 vessel 可独立聚合。
    /// </summary>
    public async Task<DriftProbeResult> RecordProbeAsync(
        string probeKey, string answerText, string sessionId = "probe")
    {
        // 1. 加载锚点 + 探针模板
        var anchors = await LoadAnchorsSafelyAsync();
        if (anchors == null)
            return EmptyResult();
        var template = (await GetTemplatesAsync())
            .FirstOrDefault(t => t.ProbeKey == probeKey)
            ?? throw new ArgumentException($"探针模板不存在: {probeKey}");

        // 2. 解析期望锚点（EntityKey 精确匹配；探针权重优先于锚点权重——
        //    模板行是探针的配置源，以后单独调权重不动锚点）
        DriftAnchor? targetAnchor = anchors.FirstOrDefault(a => a.EntityKey == template.AnchorKey);
        if (targetAnchor != null && template.Severity > 0)
            targetAnchor.Severity = template.Severity;

        // 3. 强制断言 + 全量去重 → 4. 比对 → 5. 度量
        var claims = _extractor.BuildProbeClaims(answerText, targetAnchor, anchors);
        var matches = claims
            .Select(c => new ClaimMatch
            {
                Anchor = c.Anchor,
                Score = DriftMatcher.MatchScore(answerText, c.Anchor.CanonicalValue),
                ActualTokens = c.MentionedTokens
            })
            .ToList();
        var result = _metrics.Compute(matches);

        // 6. 入库（幂等：session + 模板id槽位 + trigger_type='probe'）
        await SaveProbeAsync(sessionId, (int)template.Id, null, "probe", result,
            await _registry.GetCurrentVersionAsync(), matches, probeKey);

        return result;
    }

    /// <summary>探针模板清单（drift_probe_templates 行，按 id 升序）</summary>
    public async Task<List<DriftProbeTemplate>> GetTemplatesAsync(bool enabledOnly = false)
    {
        var templates = new List<DriftProbeTemplate>();
        using var conn = await _db.GetConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = enabledOnly
            ? """
              SELECT id, probe_key, vessel, domain, question, anchor_key, severity, enabled, source, version, created_at
              FROM drift_probe_templates WHERE enabled ORDER BY id
              """
            : """
              SELECT id, probe_key, vessel, domain, question, anchor_key, severity, enabled, source, version, created_at
              FROM drift_probe_templates ORDER BY id
              """;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            templates.Add(new DriftProbeTemplate
            {
                Id = reader.GetInt64(0),
                ProbeKey = reader.GetString(1),
                Vessel = reader.GetString(2),
                Domain = reader.GetString(3),
                Question = reader.GetString(4),
                AnchorKey = reader.GetString(5),
                Severity = reader.GetInt16(6),
                Enabled = reader.GetBoolean(7),
                Source = reader.IsDBNull(8) ? null : reader.GetString(8),
                Version = reader.GetInt32(9),
                CreatedAt = reader.GetDateTime(10)
            });
        }
        return templates;
    }

    // ═══════════════════════════════════════════
    // 查询（漂移曲线数据源）
    // ═══════════════════════════════════════════

    /// <summary>某会话全部测量批次（按轮次升序，画漂移曲线用）</summary>
    public async Task<List<DriftProbe>> GetTrendAsync(string sessionId)
    {
        var probes = new List<DriftProbe>();
        using var conn = await _db.GetConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, session_id, turn_no, trigger_type, context_tokens,
                   claim_count, match_count, drift_score, domain_breakdown, anchor_version, created_at
            FROM drift_probes
            WHERE session_id = @s
            ORDER BY turn_no
            """;
        cmd.Parameters.Add(new NpgsqlParameter("s", sessionId));
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            probes.Add(ReadProbe(reader));
        return probes;
    }

    /// <summary>某会话最近一次测量批次 + 断言明细</summary>
    public async Task<DriftProbe?> GetLatestAsync(string sessionId)
    {
        using var conn = await _db.GetConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, session_id, turn_no, trigger_type, context_tokens,
                   claim_count, match_count, drift_score, domain_breakdown, anchor_version, created_at
            FROM drift_probes
            WHERE session_id = @s
            ORDER BY turn_no DESC
            LIMIT 1
            """;
        cmd.Parameters.Add(new NpgsqlParameter("s", sessionId));
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;

        var probe = ReadProbe(reader);
        reader.Close();

        probe.Details = await GetDetailsAsync(probe.Id);
        return probe;
    }

    /// <summary>某测量批次的断言明细</summary>
    public async Task<List<DriftDetail>> GetDetailsAsync(long probeId)
    {
        var details = new List<DriftDetail>();
        using var conn = await _db.GetConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, probe_id, entity_key, domain, severity, expected, actual, match
            FROM drift_details
            WHERE probe_id = @p
            ORDER BY severity DESC, id
            """;
        cmd.Parameters.Add(new NpgsqlParameter("p", probeId));
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            details.Add(new DriftDetail
            {
                Id = reader.GetInt64(0),
                ProbeId = reader.GetInt64(1),
                EntityKey = reader.GetString(2),
                Domain = reader.GetString(3),
                Severity = reader.GetInt16(4),
                Expected = reader.IsDBNull(5) ? null : reader.GetString(5),
                Actual = reader.IsDBNull(6) ? null : reader.GetString(6),
                Match = Convert.ToDouble(reader.GetDecimal(7))
            });
        }
        return details;
    }

    // ═══════════════════════════════════════════
    // 私有
    // ═══════════════════════════════════════════

    private async Task<List<DriftAnchor>?> LoadAnchorsSafelyAsync()
    {
        try
        {
            return await _registry.GetAllAnchorsAsync();
        }
        catch
        {
            // 锚点表不存在（004 未执行）→ 测量静默跳过，不抛给调用方
            return null;
        }
    }

    private static DriftProbeResult EmptyResult() => new();

    private async Task SaveProbeAsync(string sessionId, int turnNo, int? contextTokens,
        string triggerType, DriftProbeResult result, int anchorVersion, List<ClaimMatch> matches,
        string? probeKey = null)
    {
        using var conn = await _db.GetConnectionAsync();

        // 批次 upsert（幂等键：session + turn + triggerType）
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO drift_probes
                    (session_id, turn_no, trigger_type, context_tokens, claim_count, match_count,
                     drift_score, domain_breakdown, anchor_version)
                VALUES (@s, @t, @tt, @ct, @cc, @mc, @ds, @dd, @av)
                ON CONFLICT (session_id, turn_no, trigger_type) DO UPDATE SET
                  context_tokens = EXCLUDED.context_tokens,
                  claim_count = EXCLUDED.claim_count,
                  match_count = EXCLUDED.match_count,
                  drift_score = EXCLUDED.drift_score,
                  domain_breakdown = EXCLUDED.domain_breakdown,
                  anchor_version = EXCLUDED.anchor_version,
                  created_at = now()
                RETURNING id
                """;
            cmd.Parameters.Add(new NpgsqlParameter("s", sessionId));
            cmd.Parameters.Add(new NpgsqlParameter("t", turnNo));
            cmd.Parameters.Add(new NpgsqlParameter("tt", triggerType));
            cmd.Parameters.Add(new NpgsqlParameter("ct", (object?)contextTokens ?? DBNull.Value));
            cmd.Parameters.Add(new NpgsqlParameter("cc", result.ClaimCount));
            cmd.Parameters.Add(new NpgsqlParameter("mc", result.MatchCount));
            cmd.Parameters.Add(new NpgsqlParameter("ds", Math.Round(result.DriftScore, 4)));
            cmd.Parameters.Add(new NpgsqlParameter("dd", NpgsqlDbType.Jsonb)
            {
                Value = JsonSerializer.Serialize(result.DomainBreakdown)
            });
            cmd.Parameters.Add(new NpgsqlParameter("av", anchorVersion));
            var probeId = Convert.ToInt64(cmd.ExecuteScalar());

            // 明细重写：先删旧（级联清理），再插新——保持幂等
            using (var del = conn.CreateCommand())
            {
                del.CommandText = "DELETE FROM drift_details WHERE probe_id = @p";
                del.Parameters.Add(new NpgsqlParameter("p", probeId));
                del.ExecuteNonQuery();
            }

            foreach (var m in matches)
            {
                using var ins = conn.CreateCommand();
                ins.CommandText = """
                    INSERT INTO drift_details
                        (probe_id, entity_key, domain, severity, expected, actual, match, probe_key)
                    VALUES (@p, @k, @d, @sev, @exp, @act, @m, @pk)
                    """;
                ins.Parameters.Add(new NpgsqlParameter("p", probeId));
                ins.Parameters.Add(new NpgsqlParameter("k", m.Anchor.EntityKey));
                ins.Parameters.Add(new NpgsqlParameter("d", m.Anchor.Domain));
                ins.Parameters.Add(new NpgsqlParameter("sev", m.Anchor.Severity));
                ins.Parameters.Add(new NpgsqlParameter("exp", m.Anchor.CanonicalValue));
                ins.Parameters.Add(new NpgsqlParameter("act", m.ActualTokens.Count > 0
                    ? string.Join(", ", m.ActualTokens)
                    : "(未提及任何基准标记)"));
                ins.Parameters.Add(new NpgsqlParameter("m", m.Score));
                ins.Parameters.Add(new NpgsqlParameter("pk", (object?)probeKey ?? DBNull.Value));
                ins.ExecuteNonQuery();
            }
        }
    }

    private static DriftProbe ReadProbe(IDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        SessionId = reader.GetString(1),
        TurnNo = reader.GetInt32(2),
        TriggerType = reader.GetString(3),
        ContextTokens = reader.IsDBNull(4) ? null : reader.GetInt32(4),
        ClaimCount = reader.GetInt32(5),
        MatchCount = reader.GetInt32(6),
        DriftScore = Convert.ToDouble(reader.GetDecimal(7)),
        DomainBreakdown = reader.IsDBNull(8)
            ? new List<DomainDrift>()
            : JsonSerializer.Deserialize<List<DomainDrift>>(reader.GetString(8)) ?? new List<DomainDrift>(),
        AnchorVersion = reader.GetInt32(9),
        CreatedAt = reader.GetDateTime(10)
    };
}

/// <summary>测量批次 DTO（drift_probes 行）</summary>
public class DriftProbe
{
    public long Id { get; set; }
    public string SessionId { get; set; } = "";
    public int TurnNo { get; set; }
    public string TriggerType { get; set; } = "reply";
    public int? ContextTokens { get; set; }
    public int ClaimCount { get; set; }
    public int MatchCount { get; set; }
    public double DriftScore { get; set; }
    public List<DomainDrift> DomainBreakdown { get; set; } = new();
    public int AnchorVersion { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>断言明细（仅 GetLatestAsync 填充）</summary>
    public List<DriftDetail>? Details { get; set; }
}

/// <summary>探针模板 DTO（drift_probe_templates 行）</summary>
public class DriftProbeTemplate
{
    public long Id { get; set; }
    public string ProbeKey { get; set; } = "";
    public string Vessel { get; set; } = "";
    public string Domain { get; set; } = "";
    public string Question { get; set; } = "";
    public string AnchorKey { get; set; } = "";
    public int Severity { get; set; }
    public bool Enabled { get; set; } = true;
    public string? Source { get; set; }
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
}

/// <summary>断言明细 DTO（drift_details 行）</summary>
public class DriftDetail
{
    public long Id { get; set; }
    public long ProbeId { get; set; }
    public string EntityKey { get; set; } = "";
    public string Domain { get; set; } = "";
    public int Severity { get; set; }
    public string? Expected { get; set; }
    public string? Actual { get; set; }
    public double Match { get; set; }
}
