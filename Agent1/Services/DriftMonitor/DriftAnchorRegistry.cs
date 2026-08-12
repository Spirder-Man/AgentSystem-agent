using System;
using System.Collections.Generic;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace Agent1.Services.DriftMonitor;

/// <summary>
/// 认知漂移监测·锚点注册表 —— 测量仪器的"基准电压源"服务。
///
/// 职责：
///   · 读取 drift_anchors 表（表由 db/migrations/004_drift_anchor_baseline.sql 创建）
///   · 版本管理：血谱校准后 BumpVersion，历史锚点保留供事后复算
///   · 为 Phase 2 断言比对提供查询入口（SearchAnchorsAsync）
///
/// 安全：
///   · 敏感锚点（JWT_KEY / AUTH_ACCOUNTS_JSON 等）只登记"键名存在"语义，
///     真实值禁止落库；HashSensitiveValue 仅用于需要脱敏比对的场景。
/// </summary>
public class DriftAnchorRegistry
{
    private readonly IDatabaseService _db;

    public DriftAnchorRegistry(IDatabaseService db) => _db = db;

    // ═══════════════════════════════════════════
    // 纯逻辑（可单测）
    // ═══════════════════════════════════════════

    /// <summary>SHA-256 十六进制哈希（敏感值脱敏比对用；同值同哈希，稳定可复算）</summary>
    public static string HashSensitiveValue(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>严重度规范化：非法值收敛到 [0, 2]</summary>
    public static int ClampSeverity(int severity) => Math.Clamp(severity, 0, 2);

    // ═══════════════════════════════════════════
    // 生命周期
    // ═══════════════════════════════════════════

    /// <summary>
    /// 启动检查：表是否存在。缺失只警告不崩溃——表属于迁移脚本（004）的职责，
    /// 未执行迁移时系统照常启动（参照 ChemicalKnowledgeGraph 的空图启动模式）。
    /// </summary>
    public async Task EnsureInitializedAsync()
    {
        try
        {
            using var conn = await _db.GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM drift_anchors LIMIT 1";
            cmd.ExecuteScalar();
            Console.WriteLine("🧭 DriftAnchorRegistry: drift_anchors 表就绪（锚点版本 v" + await GetCurrentVersionAsync() + "）");
        }
        catch (Exception ex)
        {
            Console.WriteLine("⚠️ DriftAnchorRegistry: drift_anchors 表不存在或不可用——认知漂移监测未激活。");
            Console.WriteLine($"   原因: {ex.Message}");
            Console.WriteLine("   请执行迁移: psql -f db/migrations/004_drift_anchor_baseline.sql");
        }
    }

    /// <summary>当前锚点版本（表缺失或空表时返回 0）</summary>
    public async Task<int> GetCurrentVersionAsync()
    {
        try
        {
            using var conn = await _db.GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(MAX(version), 0) FROM drift_anchors";
            var result = cmd.ExecuteScalar();
            return Convert.ToInt32(result);
        }
        catch
        {
            return 0;
        }
    }

    // ═══════════════════════════════════════════
    // 查询
    // ═══════════════════════════════════════════

    /// <summary>全量查询锚点（version 为空时取当前版本）</summary>
    public async Task<List<DriftAnchor>> GetAllAnchorsAsync(int? version = null)
    {
        var anchors = new List<DriftAnchor>();
        using var conn = await _db.GetConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = version.HasValue
            ? "SELECT id, domain, entity_type, entity_key, canonical_value, value_hash, severity, version, source, created_at FROM drift_anchors WHERE version = @v ORDER BY domain, entity_key"
            : "SELECT id, domain, entity_type, entity_key, canonical_value, value_hash, severity, version, source, created_at FROM drift_anchors WHERE version = (SELECT MAX(version) FROM drift_anchors) ORDER BY domain, entity_key";
        if (version.HasValue)
            cmd.Parameters.Add(new NpgsqlParameter("v", version.Value));
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            anchors.Add(ReadAnchor(reader));
        return anchors;
    }

    /// <summary>
    /// 按实体键搜索锚点（Phase 2 断言比对入口）。
    /// 键名精确匹配优先，无精确命中时退化为 LIKE 模糊匹配。
    /// </summary>
    public async Task<List<DriftAnchor>> SearchAnchorsAsync(string entityKey, int? version = null)
    {
        var anchors = new List<DriftAnchor>();
        using var conn = await _db.GetConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = version.HasValue
            ? "SELECT id, domain, entity_type, entity_key, canonical_value, value_hash, severity, version, source, created_at FROM drift_anchors WHERE version = @v AND (entity_key = @k OR entity_key LIKE @like) ORDER BY (entity_key = @k) DESC, entity_key"
            : "SELECT id, domain, entity_type, entity_key, canonical_value, value_hash, severity, version, source, created_at FROM drift_anchors WHERE version = (SELECT MAX(version) FROM drift_anchors) AND (entity_key = @k OR entity_key LIKE @like) ORDER BY (entity_key = @k) DESC, entity_key";
        if (version.HasValue)
            cmd.Parameters.Add(new NpgsqlParameter("v", version.Value));
        cmd.Parameters.Add(new NpgsqlParameter("k", entityKey));
        cmd.Parameters.Add(new NpgsqlParameter("like", "%" + entityKey + "%"));
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            anchors.Add(ReadAnchor(reader));
        return anchors;
    }

    // ═══════════════════════════════════════════
    // 写操作（版本管理）
    // ═══════════════════════════════════════════

    /// <summary>
    /// 新增/更新锚点：写入当前版本，键名冲突则覆盖（ON CONFLICT DO UPDATE）。
    /// 严重度自动收敛到 [0,2]。
    /// </summary>
    public async Task UpsertAnchorAsync(DriftAnchor anchor)
    {
        using var conn = await _db.GetConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO drift_anchors (domain, entity_type, entity_key, canonical_value, value_hash, severity, version, source)
            VALUES (@domain, @etype, @ekey, @cval, @vhash, @severity, @version, @source)
            ON CONFLICT (entity_key, version) DO UPDATE SET
              domain = EXCLUDED.domain,
              entity_type = EXCLUDED.entity_type,
              canonical_value = EXCLUDED.canonical_value,
              value_hash = EXCLUDED.value_hash,
              severity = EXCLUDED.severity,
              source = EXCLUDED.source
            """;
        cmd.Parameters.Add(new NpgsqlParameter("domain", anchor.Domain));
        cmd.Parameters.Add(new NpgsqlParameter("etype", anchor.EntityType));
        cmd.Parameters.Add(new NpgsqlParameter("ekey", anchor.EntityKey));
        cmd.Parameters.Add(new NpgsqlParameter("cval", anchor.CanonicalValue));
        cmd.Parameters.Add(new NpgsqlParameter("vhash", (object?)anchor.ValueHash ?? DBNull.Value));
        cmd.Parameters.Add(new NpgsqlParameter("severity", ClampSeverity(anchor.Severity)));
        cmd.Parameters.Add(new NpgsqlParameter("version", anchor.Version <= 0 ? await GetCurrentVersionAsync() : anchor.Version));
        cmd.Parameters.Add(new NpgsqlParameter("source", anchor.Source));
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 血谱校准后递增版本（v → v+1）。旧版本锚点保留，供历史断言复算——
    /// 这是"测量仪器防自漂移"的关键：锚点版本跟随血谱校准，而非跟随 AI 输出。
    /// 返回新版本号；新版本锚点由后续 UpsertAnchor 填充。
    /// </summary>
    public async Task<int> BumpVersionAsync()
    {
        var current = await GetCurrentVersionAsync();
        return current + 1;
    }

    // ═══════════════════════════════════════════
    // 统计
    // ═══════════════════════════════════════════

    /// <summary>分域统计（当前版本）：每条 = 分域 + 锚点数 + 结构级锚点数</summary>
    public async Task<List<DomainStats>> GetStatsAsync()
    {
        var stats = new List<DomainStats>();
        using var conn = await _db.GetConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT domain,
                   COUNT(*) AS total,
                   COUNT(*) FILTER (WHERE severity = 2) AS sev2
            FROM drift_anchors
            WHERE version = (SELECT MAX(version) FROM drift_anchors)
            GROUP BY domain
            ORDER BY domain
            """;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            stats.Add(new DomainStats(
                Domain: reader.GetString(0),
                Total: reader.GetInt32(1),
                Severity2Count: reader.GetInt32(2)));
        }
        return stats;
    }

    // ═══════════════════════════════════════════
    // 私有
    // ═══════════════════════════════════════════

    private static DriftAnchor ReadAnchor(IDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        Domain = reader.GetString(1),
        EntityType = reader.GetString(2),
        EntityKey = reader.GetString(3),
        CanonicalValue = reader.GetString(4),
        ValueHash = reader.IsDBNull(5) ? null : reader.GetString(5),
        Severity = reader.GetInt16(6),
        Version = reader.GetInt32(7),
        Source = reader.GetString(8),
        CreatedAt = reader.GetDateTime(9)
    };
}

/// <summary>分域统计 DTO</summary>
public record DomainStats(string Domain, int Total, int Severity2Count);
