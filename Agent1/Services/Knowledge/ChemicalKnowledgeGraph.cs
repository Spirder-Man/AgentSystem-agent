using System.Collections.Concurrent;
using Agent1.Models;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Agent1.Services;

/// <summary>
/// 化工知识图谱服务 — 替代 ChemicalSubstanceDatabase 硬编码静态类。
/// 
/// 架构：
/// - 数据持久化在 PostgreSQL 中，支持运行时增删改
/// - 启动时全量加载到内存图结构，查询零 DB 延迟
/// - 提供与原 ChemicalSubstanceDatabase 完全兼容的 API
/// - 集成 ChemicalNamingInference 处理未收录物质
/// </summary>
public interface IChemicalKnowledgeGraph
{
    ChemicalSubstance? Lookup(string name);
    ChemicalSubstance? LookupByCas(string casNumber);
    List<ChemicalSubstance> Search(string keyword, int maxResults = 5);
    IReadOnlyList<ChemicalSubstance> GetAll();
    int Count { get; }

    StorageIncompatibilityRule? CheckCompatibility(string substanceA, string substanceB);
    SafetyDistanceRule? GetSafetyDistance(string facilityPair);
    IReadOnlyList<SafetyDistanceRule> GetAllSafetyDistances();
    RegulationVersion? GetRegulationVersion(string number);
    IReadOnlyList<RegulationVersion> GetAllRegulationVersions();

    // 运行时变更
    void AddSubstance(ChemicalSubstance substance);
    void AddAlias(string substanceName, string alias);
    void EnsureInitialized();
}

public class ChemicalKnowledgeGraph : IChemicalKnowledgeGraph
{
    // ════════════════════════════════════════
    // 内存图结构
    // ════════════════════════════════════════

    private readonly Dictionary<int, ChemicalSubstance> _nodes = new();
    private readonly Dictionary<string, int> _nameIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<int>> _aliasIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(int, int), StorageIncompatibilityRule> _incompatEdges = new();

    private readonly List<SafetyDistanceRule> _safetyDistances = new();
    private readonly List<RegulationVersion> _regulationVersions = new();
    private int _nextId = 1;

    private readonly string _connectionString;
    private readonly ChemicalNamingInference _naming;
    private readonly ILogger<ChemicalKnowledgeGraph>? _logger;
    private bool _initialized;
    private readonly object _lock = new();

    public int Count
    {
        get { lock (_lock) return _nodes.Count; }
    }

    public ChemicalKnowledgeGraph(
        Config.AppConfig config,
        ChemicalNamingInference naming,
        ILogger<ChemicalKnowledgeGraph>? logger = null)
    {
        var db = config.Database;
        var csb = new Npgsql.NpgsqlConnectionStringBuilder
        {
            Host = db.Host,
            Port = db.Port,
            Database = db.DatabaseName,
            Username = db.Username
        };
        if (!string.IsNullOrEmpty(db.Password))
            csb.Password = db.Password;
        _connectionString = csb.ToString();
        _naming = naming;
        _logger = logger;
    }

    // ════════════════════════════════════════
    // 初始化：从 PostgreSQL 全量加载
    // ════════════════════════════════════════

    public void EnsureInitialized()
    {
        if (_initialized) return;
        lock (_lock)
        {
            if (_initialized) return;
            LoadFromDatabase();
            _initialized = true;
            _logger?.LogInformation("ChemicalKnowledgeGraph 初始化完成: {Count} 物质, {Alias} 别名, {Incomp} 禁忌边",
                _nodes.Count, _aliasIndex.Sum(kv => kv.Value.Count), _incompatEdges.Count);
        }
    }

    private void LoadFromDatabase()
    {
        try
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();

            // ── 加载物质节点 ──
            using var cmd = new NpgsqlCommand(@"
                SELECT id, name, name_en, cas_number, un_number, formula, physical_state,
                       flash_point_c, boiling_point_c, explosive_lower, explosive_upper,
                       auto_ignition_c, relative_density, vapor_density, major_hazard_threshold_tons
                FROM chemical_substances ORDER BY id", conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetInt32(0);
                var sub = new ChemicalSubstance
                {
                    Name = reader.GetString(1),
                    NameEn = reader.GetString(2),
                    CasNumber = reader.GetString(3),
                    UnNumber = reader.GetString(4),
                    Formula = reader.GetString(5),
                    PhysicalState = reader.GetString(6),
                    FlashPointC = reader.IsDBNull(7) ? null : reader.GetDouble(7),
                    BoilingPointC = reader.IsDBNull(8) ? null : reader.GetDouble(8),
                    ExplosiveLowerLimit = reader.IsDBNull(9) ? null : reader.GetDouble(9),
                    ExplosiveUpperLimit = reader.IsDBNull(10) ? null : reader.GetDouble(10),
                    AutoIgnitionTempC = reader.IsDBNull(11) ? null : reader.GetDouble(11),
                    RelativeDensity = reader.IsDBNull(12) ? null : reader.GetDouble(12),
                    VaporDensity = reader.IsDBNull(13) ? null : reader.GetDouble(13),
                    MajorHazardThresholdTons = reader.IsDBNull(14) ? 0 : reader.GetDouble(14),
                };
                _nodes[id] = sub;
                _nameIndex[sub.Name] = id;
                if (id >= _nextId) _nextId = id + 1;
            }
            reader.Close();

            // ── 加载别名 ──
            using var aliasCmd = new NpgsqlCommand(
                "SELECT substance_id, alias_text FROM chemical_aliases", conn);
            using var aliasReader = aliasCmd.ExecuteReader();
            while (aliasReader.Read())
            {
                var sid = aliasReader.GetInt32(0);
                var alias = aliasReader.GetString(1);
                if (!_aliasIndex.ContainsKey(alias))
                    _aliasIndex[alias] = new List<int>();
                _aliasIndex[alias].Add(sid);
                if (_nodes.TryGetValue(sid, out var sub))
                    sub.Aliases.Add(alias);
            }
            aliasReader.Close();

            // ── 加载危险类别 ──
            using var hcCmd = new NpgsqlCommand(
                "SELECT substance_id, category, gb_standard, sub_category FROM chemical_hazard_categories", conn);
            using var hcReader = hcCmd.ExecuteReader();
            while (hcReader.Read())
            {
                var sid = hcReader.GetInt32(0);
                if (_nodes.TryGetValue(sid, out var sub))
                    sub.HazardCategories.Add(new HazardCategoryRef
                    {
                        Category = hcReader.GetString(1),
                        GbStandard = hcReader.GetString(2),
                        SubCategory = hcReader.GetString(3)
                    });
            }
            hcReader.Close();

            // ── 加载禁忌类别 ──
            using var icCmd = new NpgsqlCommand(
                "SELECT substance_id, incompatible_with FROM chemical_incompatible_categories", conn);
            using var icReader = icCmd.ExecuteReader();
            while (icReader.Read())
            {
                var sid = icReader.GetInt32(0);
                if (_nodes.TryGetValue(sid, out var sub))
                    sub.IncompatibleWith.Add(icReader.GetString(1));
            }
            icReader.Close();

            // ── 加载精确禁忌边 ──
            using var ieCmd = new NpgsqlCommand(
                "SELECT substance_a_id, substance_b_id, is_compatible, reason, regulation_ref FROM chemical_incompatibilities", conn);
            using var ieReader = ieCmd.ExecuteReader();
            while (ieReader.Read())
            {
                var a = ieReader.GetInt32(0);
                var b = ieReader.GetInt32(1);
                _incompatEdges[(a, b)] = new StorageIncompatibilityRule
                {
                    SubstanceA = _nodes.TryGetValue(a, out var sa) ? sa.Name : "",
                    SubstanceB = _nodes.TryGetValue(b, out var sb) ? sb.Name : "",
                    IsCompatible = ieReader.GetBoolean(2),
                    Reason = ieReader.IsDBNull(3) ? null : ieReader.GetString(3),
                    RegulationRef = ieReader.IsDBNull(4) ? null : ieReader.GetString(4)
                };
            }
            ieReader.Close();

            // ── 加载安全距离 ──
            using var sdCmd = new NpgsqlCommand(
                "SELECT facility_pair, min_distance_m, regulation_ref FROM chemical_safety_distances", conn);
            using var sdReader = sdCmd.ExecuteReader();
            while (sdReader.Read())
            {
                _safetyDistances.Add(new SafetyDistanceRule
                {
                    FacilityPair = sdReader.GetString(0),
                    MinDistanceMeters = sdReader.GetDouble(1),
                    RegulationRef = sdReader.IsDBNull(2) ? null : sdReader.GetString(2)
                });
            }
            sdReader.Close();

            // ── 加载法规版本 ──
            using var rvCmd = new NpgsqlCommand(
                "SELECT regulation_number, title, current_version, has_full_text, deprecated_versions, change_notes FROM chemical_regulation_versions", conn);
            using var rvReader = rvCmd.ExecuteReader();
            while (rvReader.Read())
            {
                var deprecatedRaw = rvReader.IsDBNull(4) ? "" : rvReader.GetString(4);
                _regulationVersions.Add(new RegulationVersion
                {
                    RegulationNumber = rvReader.GetString(0),
                    Title = rvReader.GetString(1),
                    CurrentVersion = rvReader.GetString(2),
                    HasFullText = rvReader.GetBoolean(3),
                    DeprecatedVersions = string.IsNullOrWhiteSpace(deprecatedRaw)
                        ? new List<string>()
                        : deprecatedRaw.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(d => d.Trim()).ToList(),
                    ChangeNotes = rvReader.IsDBNull(5) ? null : rvReader.GetString(5)
                });
            }
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01")
        {
            // 表不存在 — 首次部署，migration 尚未执行
            _logger?.LogWarning("ChemicalKnowledgeGraph: 数据库表不存在，将以空图启动。请先执行 migration: 002_chemical_knowledge_graph.sql");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "ChemicalKnowledgeGraph 加载失败");
            throw;
        }
    }

    // ════════════════════════════════════════
    // 查询 API
    // ════════════════════════════════════════

    public ChemicalSubstance? Lookup(string name)
    {
        EnsureInitialized();
        if (string.IsNullOrWhiteSpace(name)) return null;
        var trimmed = name.Trim();

        lock (_lock)
        {
            // 别名 → 标准名
            if (_aliasIndex.TryGetValue(trimmed, out var ids) && ids.Count > 0)
            {
                var sid = ids[0];
                if (_nodes.TryGetValue(sid, out var sub)) return sub;
            }
            // 直接标准名查
            if (_nameIndex.TryGetValue(trimmed, out var id) && _nodes.TryGetValue(id, out var s))
                return s;
        }
        return null;
    }

    public ChemicalSubstance? LookupByCas(string casNumber)
    {
        EnsureInitialized();
        lock (_lock)
            return _nodes.Values.FirstOrDefault(s =>
                string.Equals(s.CasNumber, casNumber, StringComparison.OrdinalIgnoreCase));
    }

    public List<ChemicalSubstance> Search(string keyword, int maxResults = 5)
    {
        EnsureInitialized();
        lock (_lock)
        {
            return _nodes.Values
                .Where(s => s.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                            || s.Aliases.Any(a => a.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                            || s.CasNumber.Contains(keyword))
                .Take(maxResults)
                .ToList();
        }
    }

    public IReadOnlyList<ChemicalSubstance> GetAll()
    {
        EnsureInitialized();
        lock (_lock)
            return _nodes.Values.ToList();
    }

    // ════════════════════════════════════════
    // 兼容性查询
    // ════════════════════════════════════════

    public StorageIncompatibilityRule? CheckCompatibility(string substanceA, string substanceB)
    {
        EnsureInitialized();
        var a = Lookup(substanceA);
        var b = Lookup(substanceB);
        if (a == null || b == null) return null;

        int aId, bId;
        lock (_lock)
        {
            if (!_nameIndex.TryGetValue(a.Name, out aId)) return null;
            if (!_nameIndex.TryGetValue(b.Name, out bId)) return null;

            // 精确规则优先
            if (_incompatEdges.TryGetValue((aId, bId), out var exact)) return exact;
            if (_incompatEdges.TryGetValue((bId, aId), out var exactRev)) return exactRev;
        }

        // 类别级判定：检查禁忌类别交叉
        foreach (var incat in a.IncompatibleWith)
        {
            foreach (var hc in b.HazardCategories)
            {
                if (hc.Category.Contains(incat, StringComparison.OrdinalIgnoreCase)
                    || incat.Contains(hc.Category, StringComparison.OrdinalIgnoreCase))
                {
                    return new StorageIncompatibilityRule
                    {
                        SubstanceA = a.Name, SubstanceB = b.Name,
                        IsCompatible = false,
                        Reason = $"{a.Name}({incat})与{b.Name}({hc.Category})存在配伍禁忌",
                        RegulationRef = "GB 15603"
                    };
                }
            }
        }

        // 同类兼容推断
        var aCats = a.HazardCategories.Select(h => h.Category).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var bCats = b.HazardCategories.Select(h => h.Category).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (aCats.Overlaps(bCats))
        {
            return new StorageIncompatibilityRule
            {
                SubstanceA = a.Name, SubstanceB = b.Name,
                IsCompatible = true,
                Reason = $"同属{string.Join("/", aCats.Intersect(bCats))}类别，一般可同库分区存放",
                RegulationRef = "GB 15603"
            };
        }

        return null;
    }

    // ════════════════════════════════════════
    // 安全距离查询
    // ════════════════════════════════════════

    public SafetyDistanceRule? GetSafetyDistance(string facilityPair)
    {
        EnsureInitialized();
        var key = facilityPair.Trim();
        lock (_lock)
            return _safetyDistances.FirstOrDefault(s =>
                s.FacilityPair.Contains(key, StringComparison.OrdinalIgnoreCase)
                || key.Contains(s.FacilityPair, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<SafetyDistanceRule> GetAllSafetyDistances()
    {
        EnsureInitialized();
        return _safetyDistances;
    }

    // ════════════════════════════════════════
    // 法规版本查询
    // ════════════════════════════════════════

    public RegulationVersion? GetRegulationVersion(string number)
    {
        EnsureInitialized();
        var normalizedQuery = KnowledgeBaseService.NormalizeGbNumbers(number);
        lock (_lock)
            return _regulationVersions.FirstOrDefault(r =>
                KnowledgeBaseService.NormalizeGbNumbers(r.RegulationNumber)
                    .Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || normalizedQuery.Contains(
                    KnowledgeBaseService.NormalizeGbNumbers(r.RegulationNumber),
                    StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<RegulationVersion> GetAllRegulationVersions()
    {
        EnsureInitialized();
        return _regulationVersions;
    }

    // ════════════════════════════════════════
    // 运行时变更
    // ════════════════════════════════════════

    public void AddSubstance(ChemicalSubstance substance)
    {
        EnsureInitialized();
        lock (_lock)
        {
            // 内存图
            var id = _nextId++;
            _nodes[id] = substance;
            _nameIndex[substance.Name] = id;
            foreach (var alias in substance.Aliases)
            {
                if (!_aliasIndex.ContainsKey(alias))
                    _aliasIndex[alias] = new List<int>();
                _aliasIndex[alias].Add(id);
            }
        }

        // 持久化到 PostgreSQL
        try
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();
            using var cmd = new NpgsqlCommand(@"
                INSERT INTO chemical_substances (name, name_en, cas_number, un_number, formula, physical_state,
                    flash_point_c, boiling_point_c, explosive_lower, explosive_upper,
                    auto_ignition_c, relative_density, vapor_density, major_hazard_threshold_tons)
                VALUES (@n, @ne, @cas, @un, @fm, @ps, @fp, @bp, @el, @eu, @ai, @rd, @vd, @mh)
                RETURNING id", conn);
            cmd.Parameters.AddWithValue("n", substance.Name);
            cmd.Parameters.AddWithValue("ne", substance.NameEn);
            cmd.Parameters.AddWithValue("cas", substance.CasNumber);
            cmd.Parameters.AddWithValue("un", substance.UnNumber);
            cmd.Parameters.AddWithValue("fm", substance.Formula);
            cmd.Parameters.AddWithValue("ps", substance.PhysicalState);
            cmd.Parameters.AddWithValue("fp", (object?)substance.FlashPointC ?? DBNull.Value);
            cmd.Parameters.AddWithValue("bp", (object?)substance.BoilingPointC ?? DBNull.Value);
            cmd.Parameters.AddWithValue("el", (object?)substance.ExplosiveLowerLimit ?? DBNull.Value);
            cmd.Parameters.AddWithValue("eu", (object?)substance.ExplosiveUpperLimit ?? DBNull.Value);
            cmd.Parameters.AddWithValue("ai", (object?)substance.AutoIgnitionTempC ?? DBNull.Value);
            cmd.Parameters.AddWithValue("rd", (object?)substance.RelativeDensity ?? DBNull.Value);
            cmd.Parameters.AddWithValue("vd", (object?)substance.VaporDensity ?? DBNull.Value);
            cmd.Parameters.AddWithValue("mh", substance.MajorHazardThresholdTons);
            var dbId = (int)(cmd.ExecuteScalar() ?? 0);

            // 持久化别名
            foreach (var alias in substance.Aliases)
            {
                using var aCmd = new NpgsqlCommand(
                    "INSERT INTO chemical_aliases (substance_id, alias_text) VALUES (@sid, @at) ON CONFLICT DO NOTHING", conn);
                aCmd.Parameters.AddWithValue("sid", dbId);
                aCmd.Parameters.AddWithValue("at", alias);
                aCmd.ExecuteNonQuery();
            }

            // 持久化危险类别
            foreach (var hc in substance.HazardCategories)
            {
                using var hcCmd = new NpgsqlCommand(
                    "INSERT INTO chemical_hazard_categories (substance_id, category, gb_standard, sub_category) VALUES (@sid, @c, @gs, @sc)", conn);
                hcCmd.Parameters.AddWithValue("sid", dbId);
                hcCmd.Parameters.AddWithValue("c", hc.Category);
                hcCmd.Parameters.AddWithValue("gs", hc.GbStandard);
                hcCmd.Parameters.AddWithValue("sc", hc.SubCategory ?? "");
                hcCmd.ExecuteNonQuery();
            }

            // 持久化禁忌类别
            foreach (var ic in substance.IncompatibleWith)
            {
                using var icCmd = new NpgsqlCommand(
                    "INSERT INTO chemical_incompatible_categories (substance_id, incompatible_with) VALUES (@sid, @iw)", conn);
                icCmd.Parameters.AddWithValue("sid", dbId);
                icCmd.Parameters.AddWithValue("iw", ic);
                icCmd.ExecuteNonQuery();
            }

            _logger?.LogInformation("ChemicalKnowledgeGraph: 新增物质 {Name} (id={Id})", substance.Name, dbId);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "ChemicalKnowledgeGraph: 持久化新增物质失败 {Name}", substance.Name);
        }
    }

    public void AddAlias(string substanceName, string alias)
    {
        EnsureInitialized();
        lock (_lock)
        {
            if (!_nameIndex.TryGetValue(substanceName, out var id))
                throw new InvalidOperationException($"物质 '{substanceName}' 不存在");

            if (!_aliasIndex.ContainsKey(alias))
                _aliasIndex[alias] = new List<int>();
            _aliasIndex[alias].Add(id);

            if (_nodes.TryGetValue(id, out var sub) && !sub.Aliases.Contains(alias))
                sub.Aliases.Add(alias);
        }

        // 持久化
        try
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();
            using var cmd = new NpgsqlCommand(@"
                INSERT INTO chemical_aliases (substance_id, alias_text)
                SELECT id, @at FROM chemical_substances WHERE name = @n
                ON CONFLICT DO NOTHING", conn);
            cmd.Parameters.AddWithValue("n", substanceName);
            cmd.Parameters.AddWithValue("at", alias);
            cmd.ExecuteNonQuery();
            _logger?.LogInformation("ChemicalKnowledgeGraph: 为 {Name} 新增别名 {Alias}", substanceName, alias);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "ChemicalKnowledgeGraph: 持久化别名失败 {Name} -> {Alias}", substanceName, alias);
        }
    }
}
