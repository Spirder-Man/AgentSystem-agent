using Microsoft.Data.Sqlite;
using Agent1.Models;

namespace Agent1.Services;

/// <summary>
/// 化工危化品结构化数据库服务 v2 —— 零失误架构核心组件。
/// 
/// 设计原则:
///   1. 危险类别/安全距离/临界量等确定性数据 100% 走 SQLite 查询
///   2. 数据库未命中的化学品 → 返回 null，由调用方触发标准化拒绝
///   3. 别名自动还原为标准名称，支持模糊匹配
/// 
/// 与 ChemicalSubstanceDatabase 的关系:
///   - ChemicalSubstanceDatabase 保留为内存缓存层（快路径）
///   - ChemicalDatabaseService 为权威数据源（SQLite 持久化）
/// </summary>
public class ChemicalDatabaseService
{
    private readonly string _connectionString;
    private bool _initialized = false;
    private static readonly Lazy<ChemicalDatabaseService> _instance = new(() => new ChemicalDatabaseService());

    /// <summary>全局单例（自动初始化）</summary>
    public static ChemicalDatabaseService Instance => _instance.Value;

    public ChemicalDatabaseService(string? dbPath = null)
    {
        var dataDir = dbPath ?? Path.Combine(AppContext.BaseDirectory, "Data");
        Directory.CreateDirectory(dataDir);
        var dbFile = Path.Combine(dataDir, "chemical_substances.db");
        _connectionString = $"Data Source={dbFile}";
    }

    /// <summary>初始化数据库：建表 + 种子数据</summary>
    public async Task InitializeAsync()
    {
        if (_initialized) return;

        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        try
        {
            // 执行 schema
            var schemaPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "db", "chemical_substances_v2.sql");
            if (!File.Exists(schemaPath))
                schemaPath = Path.Combine(AppContext.BaseDirectory, "db", "chemical_substances_v2.sql");
            if (!File.Exists(schemaPath))
            {
                // 兜底：内联 schema
                await CreateSchemaInlineAsync(conn);
            }
            else
            {
                var schema = await File.ReadAllTextAsync(schemaPath);
                // 逐条执行（SQLite 不支持批量）
                foreach (var stmt in SplitSqlStatements(schema))
                {
                    using var cmd = new SqliteCommand(stmt, conn);
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            // 检查是否已有数据
            using var checkCmd = new SqliteCommand("SELECT COUNT(*) FROM substances", conn);
            var count = (long)(await checkCmd.ExecuteScalarAsync())!;
            if (count == 0)
            {
                await SeedDataAsync(conn);
                Console.WriteLine("   ✅ 化工数据库 v2 初始化完成，种子数据已写入");
            }
            else
            {
                Console.WriteLine($"   ✅ 化工数据库 v2 已就绪 ({count} 种化学品)");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ⚠️ 数据库初始化异常（系统将以降级模式运行）: {ex.Message}");
        }

        _initialized = true;
    }

    // ════════════════════════════════════════
    // 公共查询接口
    // ════════════════════════════════════════

    /// <summary>按名称查询化学品完整信息（含别名还原）</summary>
    public async Task<ChemicalSubstance?> LookupAsync(string name)
    {
        await InitializeAsync();
        if (string.IsNullOrWhiteSpace(name)) return null;

        var trimmed = name.Trim();
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        // 先查别名
        var aliasSql = @"SELECT s.id FROM substances s
            INNER JOIN substance_aliases sa ON sa.substance_id = s.id
            WHERE sa.alias = @name COLLATE NOCASE";
        using var aliasCmd = new SqliteCommand(aliasSql, conn);
        aliasCmd.Parameters.AddWithValue("@name", trimmed);
        var substanceId = await aliasCmd.ExecuteScalarAsync() as long?;

        // 别名未命中则直接查主表
        if (!substanceId.HasValue)
        {
            var directSql = "SELECT id FROM substances WHERE name = @name COLLATE NOCASE";
            using var directCmd = new SqliteCommand(directSql, conn);
            directCmd.Parameters.AddWithValue("@name", trimmed);
            substanceId = await directCmd.ExecuteScalarAsync() as long?;

            // 模糊匹配兜底
            if (!substanceId.HasValue)
            {
                var fuzzySql = @"SELECT id, name FROM substances 
                    WHERE name LIKE @pattern COLLATE NOCASE 
                    ORDER BY length(name) LIMIT 1";
                using var fuzzyCmd = new SqliteCommand(fuzzySql, conn);
                fuzzyCmd.Parameters.AddWithValue("@pattern", $"%{trimmed}%");
                using var reader = await fuzzyCmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    substanceId = reader.GetInt64(0);
                    Console.WriteLine($"   🔍 模糊匹配: \"{trimmed}\" → \"{reader.GetString(1)}\"");
                }
            }
        }

        if (!substanceId.HasValue) return null;

        return await LoadSubstanceAsync(conn, substanceId.Value);
    }

    /// <summary>按 CAS 号查询</summary>
    public async Task<ChemicalSubstance?> LookupByCasAsync(string casNumber)
    {
        await InitializeAsync();
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var sql = "SELECT id FROM substances WHERE cas_number = @cas COLLATE NOCASE";
        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@cas", casNumber.Trim());
        var id = await cmd.ExecuteScalarAsync() as long?;
        if (!id.HasValue) return null;

        return await LoadSubstanceAsync(conn, id.Value);
    }

    /// <summary>模糊搜索化学品名称（最多返回 maxResults 条）</summary>
    public async Task<List<ChemicalSubstance>> SearchAsync(string keyword, int maxResults = 5)
    {
        await InitializeAsync();
        var results = new List<ChemicalSubstance>();
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var sql = @"SELECT id FROM substances 
            WHERE name LIKE @kw COLLATE NOCASE
            LIMIT @limit";
        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@kw", $"%{keyword}%");
        cmd.Parameters.AddWithValue("@limit", maxResults);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var sub = await LoadSubstanceAsync(conn, reader.GetInt64(0));
            if (sub != null) results.Add(sub);
        }
        return results;
    }

    /// <summary>获取危险类别列表（带 GB 编号）</summary>
    public async Task<List<HazardCategoryRef>> GetHazardCategoriesAsync(int substanceId)
    {
        await InitializeAsync();
        var categories = new List<HazardCategoryRef>();
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var sql = "SELECT category, gb_standard, sub_category, hazard_code FROM hazard_categories WHERE substance_id = @id";
        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", substanceId);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            categories.Add(new HazardCategoryRef
            {
                Category = reader.GetString(0),
                GbStandard = reader.GetString(1),
                SubCategory = reader.IsDBNull(2) ? null : reader.GetString(2)
            });
        }
        return categories;
    }

    /// <summary>获取安全距离</summary>
    public async Task<SafetyDistanceRule?> GetSafetyDistanceAsync(string facilityPair)
    {
        await InitializeAsync();
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        // 精确匹配
        var exactSql = @"SELECT facility_pair, min_distance_m, regulation_ref, clause_ref, notes 
            FROM safety_distances WHERE facility_pair = @pair COLLATE NOCASE";
        using var exactCmd = new SqliteCommand(exactSql, conn);
        exactCmd.Parameters.AddWithValue("@pair", facilityPair.Trim());
        using (var reader = await exactCmd.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync())
                return ReadSafetyDistance(reader);
        }

        // 别名匹配
        var aliasSql = @"SELECT facility_pair, min_distance_m, regulation_ref, clause_ref, notes 
            FROM safety_distances WHERE facility_alias LIKE @alias COLLATE NOCASE";
        using var aliasCmd = new SqliteCommand(aliasSql, conn);
        aliasCmd.Parameters.AddWithValue("@alias", $"%{facilityPair.Trim()}%");
        using (var reader = await aliasCmd.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync())
                return ReadSafetyDistance(reader);
        }

        // 模糊匹配（双向）
        var fuzzySql = @"SELECT facility_pair, min_distance_m, regulation_ref, clause_ref, notes 
            FROM safety_distances 
            WHERE facility_pair LIKE @kw COLLATE NOCASE OR @kw2 LIKE '%' || facility_pair || '%'
            LIMIT 1";
        using var fuzzyCmd = new SqliteCommand(fuzzySql, conn);
        fuzzyCmd.Parameters.AddWithValue("@kw", $"%{facilityPair.Trim()}%");
        fuzzyCmd.Parameters.AddWithValue("@kw2", facilityPair.Trim());
        using (var reader = await fuzzyCmd.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync())
                return ReadSafetyDistance(reader);
        }

        return null;
    }

    /// <summary>获取所有安全距离规则</summary>
    public async Task<List<SafetyDistanceRule>> GetAllSafetyDistancesAsync()
    {
        await InitializeAsync();
        var rules = new List<SafetyDistanceRule>();
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var sql = "SELECT facility_pair, min_distance_m, regulation_ref, clause_ref, notes FROM safety_distances";
        using var cmd = new SqliteCommand(sql, conn);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rules.Add(ReadSafetyDistance(reader));

        return rules;
    }

    /// <summary>检查两种化学品储存兼容性</summary>
    public async Task<StorageIncompatibilityRule?> CheckCompatibilityAsync(string substanceA, string substanceB)
    {
        await InitializeAsync();
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        // 精确规则表查询
        var exactSql = @"SELECT is_compatible, reason, regulation_ref 
            FROM storage_compatibility_rules
            WHERE (substance_a = @a COLLATE NOCASE AND substance_b = @b COLLATE NOCASE)
               OR (substance_a = @b COLLATE NOCASE AND substance_b = @a COLLATE NOCASE)
            LIMIT 1";
        using var exactCmd = new SqliteCommand(exactSql, conn);
        exactCmd.Parameters.AddWithValue("@a", substanceA.Trim());
        exactCmd.Parameters.AddWithValue("@b", substanceB.Trim());
        using (var reader = await exactCmd.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync())
            {
                return new StorageIncompatibilityRule
                {
                    SubstanceA = substanceA, SubstanceB = substanceB,
                    IsCompatible = reader.GetInt32(0) == 1,
                    Reason = reader.GetString(1),
                    RegulationRef = reader.IsDBNull(2) ? "" : reader.GetString(2)
                };
            }
        }

        // 类别级推理：加载双方的危险类别+禁忌列表
        var subA = await LookupAsync(substanceA);
        var subB = await LookupAsync(substanceB);
        if (subA == null || subB == null) return null;

        // 检查 A 的禁忌类别是否包含 B 的危险类别
        foreach (var incat in subA.IncompatibleWith)
        {
            foreach (var hc in subB.HazardCategories)
            {
                if (hc.Category.Contains(incat, StringComparison.OrdinalIgnoreCase)
                    || incat.Contains(hc.Category, StringComparison.OrdinalIgnoreCase))
                {
                    return new StorageIncompatibilityRule
                    {
                        SubstanceA = subA.Name, SubstanceB = subB.Name,
                        IsCompatible = false,
                        Reason = $"{subA.Name}({incat})与{subB.Name}({hc.Category})存在配伍禁忌",
                        RegulationRef = "GB 15603"
                    };
                }
            }
        }

        // 同类兼容推断
        var aCats = subA.HazardCategories.Select(h => h.Category).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var bCats = subB.HazardCategories.Select(h => h.Category).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (aCats.Overlaps(bCats))
        {
            return new StorageIncompatibilityRule
            {
                SubstanceA = subA.Name, SubstanceB = subB.Name,
                IsCompatible = true,
                Reason = $"同属{string.Join("/", aCats.Intersect(bCats))}类别，一般可同库分区存放",
                RegulationRef = "GB 15603"
            };
        }

        return null;
    }

    /// <summary>获取重大危险源临界量</summary>
    public async Task<double?> GetMajorHazardThresholdAsync(string substanceName)
    {
        var sub = await LookupAsync(substanceName);
        return sub?.MajorHazardThresholdTons;
    }

    /// <summary>获取法规版本信息</summary>
    public async Task<RegulationVersion?> GetRegulationVersionAsync(string regulationNumber)
    {
        await InitializeAsync();
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var normalizedQuery = KnowledgeBaseService.NormalizeGbNumbers(regulationNumber);
        var sql = "SELECT regulation_number, title, current_version, deprecated_versions, has_full_text, change_notes FROM regulation_versions";
        using var cmd = new SqliteCommand(sql, conn);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var dbReg = reader.GetString(0);
            var normalizedDb = KnowledgeBaseService.NormalizeGbNumbers(dbReg);
            if (normalizedDb.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || normalizedQuery.Contains(normalizedDb, StringComparison.OrdinalIgnoreCase))
            {
                return new RegulationVersion
                {
                    RegulationNumber = dbReg,
                    Title = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    CurrentVersion = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    DeprecatedVersions = reader.IsDBNull(3) ? new() : reader.GetString(3).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList(),
                    HasFullText = !reader.IsDBNull(4) && reader.GetInt32(4) == 1,
                    ChangeNotes = reader.IsDBNull(5) ? "" : reader.GetString(5)
                };
            }
        }
        return null;
    }

    /// <summary>获取化学品总数</summary>
    public async Task<int> GetCountAsync()
    {
        await InitializeAsync();
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        var cmd = new SqliteCommand("SELECT COUNT(*) FROM substances", conn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    // ════════════════════════════════════════
    // 内部辅助方法
    // ════════════════════════════════════════

    private async Task<ChemicalSubstance?> LoadSubstanceAsync(SqliteConnection conn, long id)
    {
        var sql = @"SELECT name, name_en, cas_number, un_number, formula, physical_state,
            flash_point_c, boiling_point_c, explosive_lower, explosive_upper, auto_ignition_c,
            relative_density, vapor_density, major_hazard_threshold_tons
            FROM substances WHERE id = @id";
        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        var sub = new ChemicalSubstance
        {
            Name = reader.GetString(0),
            NameEn = reader.IsDBNull(1) ? null : reader.GetString(1),
            CasNumber = reader.IsDBNull(2) ? "" : reader.GetString(2),
            UnNumber = reader.IsDBNull(3) ? "" : reader.GetString(3),
            Formula = reader.IsDBNull(4) ? "" : reader.GetString(4),
            PhysicalState = reader.IsDBNull(5) ? "" : reader.GetString(5),
            FlashPointC = reader.IsDBNull(6) ? null : reader.GetDouble(6),
            BoilingPointC = reader.IsDBNull(7) ? null : reader.GetDouble(7),
            ExplosiveLowerLimit = reader.IsDBNull(8) ? null : reader.GetDouble(8),
            ExplosiveUpperLimit = reader.IsDBNull(9) ? null : reader.GetDouble(9),
            AutoIgnitionTempC = reader.IsDBNull(10) ? null : reader.GetDouble(10),
            RelativeDensity = reader.IsDBNull(11) ? null : reader.GetDouble(11),
            VaporDensity = reader.IsDBNull(12) ? null : reader.GetDouble(12),
            MajorHazardThresholdTons = reader.IsDBNull(13) ? 0 : reader.GetDouble(13)
        };

        // 加载危险类别
        sub.HazardCategories = await GetHazardCategoriesAsync((int)id);

        // 加载别名
        var aliasSql = "SELECT alias FROM substance_aliases WHERE substance_id = @id";
        using var aliasCmd = new SqliteCommand(aliasSql, conn);
        aliasCmd.Parameters.AddWithValue("@id", id);
        using var aliasReader = await aliasCmd.ExecuteReaderAsync();
        while (await aliasReader.ReadAsync())
            sub.Aliases.Add(aliasReader.GetString(0));

        // 加载禁忌类别
        var incompatSql = "SELECT incompatible_with FROM incompatibility_categories WHERE substance_id = @id";
        using var incompatCmd = new SqliteCommand(incompatSql, conn);
        incompatCmd.Parameters.AddWithValue("@id", id);
        using var incompatReader = await incompatCmd.ExecuteReaderAsync();
        while (await incompatReader.ReadAsync())
            sub.IncompatibleWith.Add(incompatReader.GetString(0));

        return sub;
    }

    private static SafetyDistanceRule ReadSafetyDistance(SqliteDataReader reader)
    {
        return new SafetyDistanceRule
        {
            FacilityPair = reader.GetString(0),
            MinDistanceMeters = reader.GetDouble(1),
            RegulationRef = reader.GetString(2)
        };
    }

    private static List<string> SplitSqlStatements(string sql)
    {
        return sql.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }

    private async Task CreateSchemaInlineAsync(SqliteConnection conn)
    {
        var schema = @"
CREATE TABLE IF NOT EXISTS substances (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL UNIQUE, name_en TEXT, cas_number TEXT, un_number TEXT,
    formula TEXT, physical_state TEXT, flash_point_c REAL, boiling_point_c REAL,
    explosive_lower REAL, explosive_upper REAL, auto_ignition_c REAL,
    relative_density REAL, vapor_density REAL, major_hazard_threshold_tons REAL
);
CREATE TABLE IF NOT EXISTS hazard_categories (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    substance_id INTEGER NOT NULL REFERENCES substances(id) ON DELETE CASCADE,
    category TEXT NOT NULL, gb_standard TEXT NOT NULL, sub_category TEXT, hazard_code TEXT
);
CREATE TABLE IF NOT EXISTS substance_aliases (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    substance_id INTEGER NOT NULL REFERENCES substances(id) ON DELETE CASCADE,
    alias TEXT NOT NULL UNIQUE
);
CREATE TABLE IF NOT EXISTS incompatibility_categories (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    substance_id INTEGER NOT NULL REFERENCES substances(id) ON DELETE CASCADE,
    incompatible_with TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS storage_compatibility_rules (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    substance_a TEXT NOT NULL, substance_b TEXT NOT NULL,
    is_compatible INTEGER NOT NULL, reason TEXT NOT NULL, regulation_ref TEXT
);
CREATE TABLE IF NOT EXISTS safety_distances (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    facility_pair TEXT NOT NULL, facility_alias TEXT,
    min_distance_m REAL NOT NULL, regulation_ref TEXT NOT NULL,
    clause_ref TEXT, notes TEXT
);
CREATE TABLE IF NOT EXISTS regulation_versions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    regulation_number TEXT NOT NULL, title TEXT, current_version TEXT,
    deprecated_versions TEXT, has_full_text INTEGER DEFAULT 0, change_notes TEXT
);";
        foreach (var stmt in SplitSqlStatements(schema))
        {
            using var cmd = new SqliteCommand(stmt, conn);
            await cmd.ExecuteNonQueryAsync();
        }
        Console.WriteLine("   ✅ 数据库 schema 内联创建完成");
    }

    // ════════════════════════════════════════
    // 种子数据
    // ════════════════════════════════════════

    private async Task SeedDataAsync(SqliteConnection conn)
    {
        using var tx = conn.BeginTransaction();
        try
        {
            await SeedSubstancesAsync(conn, tx);
            await SeedSafetyDistancesAsync(conn, tx);
            await SeedCompatibilityRulesAsync(conn, tx);
            await SeedRegulationVersionsAsync(conn, tx);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private async Task SeedSubstancesAsync(SqliteConnection conn, SqliteTransaction tx)
    {
        // ── 易燃液体类 ──
        await InsertSubstance(conn, tx, "苯", "Benzene", "71-43-2", "1114", "C6H6", "液体",
            -11, 80.1, 1.2, 8.0, 560, 0.88, 2.77, 50,
            new[] { ("易燃液体", "GB 30000.7", "类别2", null), ("致癌性", "GB 30000.23", "类别1A", ""),
                    ("严重眼损伤/刺激", "GB 30000.20", "类别2", null), ("特异性靶器官毒性 反复接触", "GB 30000.26", "类别1", ""),
                    ("吸入危害", "GB 30000.27", "类别1", null) },
            new[] { "纯苯", "安息油" }, new[] { "氧化剂", "强酸", "硝酸", "高锰酸钾" });

        await InsertSubstance(conn, tx, "甲苯", "Toluene", "108-88-3", "1294", "C7H8", "液体",
            4, 110.6, 1.2, 7.1, 535, 0.87, 3.14, 500,
            new[] { ("易燃液体", "GB 30000.7", "类别2", null), ("皮肤腐蚀/刺激", "GB 30000.19", "类别2", ""),
                    ("特异性靶器官毒性 反复接触", "GB 30000.26", "类别2", null), ("吸入危害", "GB 30000.27", "类别1", null) },
            new[] { "甲基苯", "Toluol" }, new[] { "氧化剂", "强酸", "硝酸" });

        await InsertSubstance(conn, tx, "二甲苯", "Xylene", "1330-20-7", "1307", "C8H10", "液体",
            25, 138.5, 1.0, 7.0, 463, 0.86, 3.66, 500,
            new[] { ("易燃液体", "GB 30000.7", "类别3", null), ("皮肤腐蚀/刺激", "GB 30000.19", "类别2", ""),
                    ("吸入危害", "GB 30000.27", "类别1", null) },
            new[] { "混合二甲苯", "Xylol" }, new[] { "氧化剂", "强酸" });

        await InsertSubstance(conn, tx, "甲醇", "Methanol", "67-56-1", "1230", "CH3OH", "液体",
            11, 64.7, 6.0, 36.5, 464, 0.79, 1.11, 500,
            new[] { ("易燃液体", "GB 30000.7", "类别2", null), ("急性毒性", "GB 30000.18", "类别3（经口/经皮/吸入）", ""),
                    ("特异性靶器官毒性 一次接触", "GB 30000.25", "类别1", null) },
            new[] { "木醇", "木精", "甲基醇" }, new[] { "氧化剂", "强酸", "硝酸", "过氧化物" });

        await InsertSubstance(conn, tx, "丙酮", "Acetone", "67-64-1", "1090", "C3H6O", "液体",
            -18, 56.2, 2.5, 12.8, 465, 0.79, 2.0, 500,
            new[] { ("易燃液体", "GB 30000.7", "类别2", null), ("严重眼损伤/刺激", "GB 30000.20", "类别2", ""),
                    ("特异性靶器官毒性 一次接触", "GB 30000.25", "类别3", null) },
            new[] { "二甲基酮", "醋酮" }, new[] { "氧化剂", "硝酸", "过氧化氢", "高锰酸钾" });

        await InsertSubstance(conn, tx, "乙醇", "Ethanol", "64-17-5", "1170", "C2H5OH", "液体",
            13, 78.3, 3.3, 19.0, 363, 0.79, 1.59, 500,
            new (string, string, string?, string?)[] { ("易燃液体", "GB 30000.7", "类别2", null) },
            new[] { "酒精", "火酒" }, new[] { "氧化剂", "硝酸", "过氧化物" });

        await InsertSubstance(conn, tx, "乙酸", "Acetic acid", "64-19-7", "2789", "CH3COOH", "液体",
            39, 118.1, 4.0, 19.9, 463, 1.05, 2.07, 0,
            new[] { ("易燃液体", "GB 30000.7", "类别3", null), ("皮肤腐蚀/刺激", "GB 30000.19", "类别1A", ""),
                    ("金属腐蚀物", "GB 30000.17", "类别1", null) },
            new[] { "醋酸", "冰醋酸", "冰乙酸" }, new[] { "氧化剂", "硝酸", "过氧化氢", "高锰酸钾", "铬酸", "碱", "氢氧化钠" });

        // ── 氧化剂类 ──
        await InsertSubstance(conn, tx, "过氧化氢", "Hydrogen peroxide", "7722-84-1", "2015", "H2O2", "液体",
            null, 150.2, null, null, null, 1.44, null, 50,
            new[] { ("氧化性液体", "GB 30000.14", "类别1", null), ("皮肤腐蚀/刺激", "GB 30000.19", "类别1A", ""),
                    ("急性毒性", "GB 30000.18", "类别4（经口/经皮/吸入）", null) },
            new[] { "双氧水", "过氧化氢溶液" }, new[] { "易燃液体", "有机物", "还原剂", "丙酮", "乙醇", "甲醇" });

        await InsertSubstance(conn, tx, "硝酸", "Nitric acid", "7697-37-2", "2031", "HNO3", "液体",
            null, 83, null, null, null, 1.5, 2.0, 100,
            new[] { ("氧化性液体", "GB 30000.14", "类别2", null), ("皮肤腐蚀/刺激", "GB 30000.19", "类别1A", ""),
                    ("金属腐蚀物", "GB 30000.17", "类别1", null), ("急性毒性", "GB 30000.18", "类别3（吸入）", null) },
            new[] { "硝镪水", "浓硝酸", "稀硝酸" }, new[] { "易燃液体", "碱", "有机物", "还原剂", "金属粉末", "氰化物" });

        await InsertSubstance(conn, tx, "硝酸铵", "Ammonium nitrate", "6484-52-2", "1942", "NH4NO3", "固体",
            null, 210, null, null, null, 1.72, null, 50,
            new (string, string, string?, string?)[] { ("氧化性固体", "GB 30000.15", "类别2", null), ("爆炸物", "GB 30000.2", "类别1", null) },
            new[] { "硝铵", "NH4NO3" }, new[] { "易燃液体", "易燃固体", "有机物", "硫磺", "金属粉末", "还原剂" });

        await InsertSubstance(conn, tx, "高锰酸钾", "Potassium permanganate", "7722-64-7", "1490", "KMnO4", "固体",
            null, null, null, null, null, 2.7, null, 100,
            new[] { ("氧化性固体", "GB 30000.15", "类别2", null), ("急性毒性", "GB 30000.18", "类别4（经口）", ""),
                    ("对水生环境危害", "GB 30000.28", "类别1", null) },
            new[] { "灰锰氧", "PP粉" }, new[] { "易燃液体", "易燃固体", "有机物", "还原剂", "甘油", "乙醇" });

        await InsertSubstance(conn, tx, "重铬酸钠", "Sodium dichromate", "10588-01-9", "3085", "Na2Cr2O7", "固体",
            null, null, null, null, null, 2.35, null, 50,
            new[] { ("氧化性固体", "GB 30000.15", "类别2", null), ("致癌性", "GB 30000.23", "类别1B", ""),
                    ("生殖细胞致突变性", "GB 30000.22", "类别1B", null), ("急性毒性", "GB 30000.18", "类别2（经口）", ""),
                    ("皮肤腐蚀/刺激", "GB 30000.19", "类别1B", null), ("对水生环境危害", "GB 30000.28", "类别1", null) },
            new[] { "红矾钠" }, new[] { "易燃液体", "易燃固体", "有机物", "还原剂" });

        // ── 毒性气体类 ──
        await InsertSubstance(conn, tx, "氯", "Chlorine", "7782-50-5", "1017", "Cl2", "气体（加压液化）",
            null, -34.5, null, null, null, 1.47, 2.48, 5,
            new[] { ("加压气体", "GB 30000.6", "液化气体", null), ("氧化性气体", "GB 30000.5", "类别1", ""),
                    ("急性毒性", "GB 30000.18", "类别2（吸入）", null), ("皮肤腐蚀/刺激", "GB 30000.19", "类别2", ""),
                    ("严重眼损伤/刺激", "GB 30000.20", "类别2", null), ("对水生环境危害", "GB 30000.28", "类别1", null) },
            new[] { "液氯", "氯气", "绿气" }, new[] { "氨", "氢", "乙炔", "烃类", "金属粉末", "还原剂", "可燃物" });

        await InsertSubstance(conn, tx, "氨", "Ammonia", "7664-41-7", "1005", "NH3", "气体（加压液化）",
            null, -33.4, 15.0, 28.0, 651, 0.82, 0.59, 10,
            new[] { ("易燃气体", "GB 30000.3", "类别2", null), ("加压气体", "GB 30000.6", "液化气体", ""),
                    ("急性毒性", "GB 30000.18", "类别3（吸入）", null), ("皮肤腐蚀/刺激", "GB 30000.19", "类别1B", ""),
                    ("对水生环境危害", "GB 30000.28", "类别1", null) },
            new[] { "氨气", "液氨", "阿摩尼亚" }, new[] { "氧化剂", "卤素", "酸", "次氯酸盐", "氯", "氯化氢", "溴", "碘", "环氧乙烷" });

        await InsertSubstance(conn, tx, "硫化氢", "Hydrogen sulfide", "7783-06-4", "1053", "H2S", "气体",
            null, -60.3, 4.3, 46.0, 260, null, 1.19, 5,
            new[] { ("易燃气体", "GB 30000.3", "类别1", null), ("加压气体", "GB 30000.6", "液化气体", ""),
                    ("急性毒性", "GB 30000.18", "类别2（吸入）", null), ("对水生环境危害", "GB 30000.28", "类别1", null) },
            new[] { "氢硫酸", "硫化氢气" }, new[] { "氧化剂", "硝酸", "过氧化氢", "氯气" });

        // ── 易燃气体类 ──
        await InsertSubstance(conn, tx, "乙炔", "Acetylene", "74-86-2", "1001", "C2H2", "气体（溶解）",
            -18, -84, 2.5, 82.0, 305, null, 0.91, 1,
            new[] { ("易燃气体", "GB 30000.3", "类别1", null), ("加压气体", "GB 30000.6", "溶解气体", ""),
                    ("爆炸物", "GB 30000.2", "不安定爆炸物（无空气也可爆炸）", null) },
            new[] { "电石气", "乙炔气" }, new[] { "氧", "氧化剂", "卤素", "铜", "银", "汞及其化合物" });

        await InsertSubstance(conn, tx, "氢气", "Hydrogen", "1333-74-0", "1049", "H2", "气体（压缩）",
            null, -252.8, 4.0, 75.0, 500, 0.07, 0.07, 5,
            new (string, string, string?, string?)[] { ("易燃气体", "GB 30000.3", "类别1", null), ("加压气体", "GB 30000.6", "压缩气体", null) },
            new[] { "氢" }, new[] { "氧化剂", "氧", "卤素", "氯" });

        // ── 腐蚀品类 ──
        await InsertSubstance(conn, tx, "硫酸", "Sulfuric acid", "7664-93-9", "1830", "H2SO4", "液体",
            null, 330, null, null, null, 1.84, 3.4, 100,
            new[] { ("皮肤腐蚀/刺激", "GB 30000.19", "类别1A", null), ("金属腐蚀物", "GB 30000.17", "类别1", ""),
                    ("严重眼损伤/刺激", "GB 30000.20", "类别1", null) },
            new[] { "磺镪水", "发烟硫酸", "硫酸水" }, new[] { "易燃液体", "碱", "有机物", "还原剂", "金属粉末", "氰化物", "高锰酸钾" });

        await InsertSubstance(conn, tx, "盐酸", "Hydrochloric acid", "7647-01-0", "1789", "HCl", "液体（氯化氢水溶液）",
            null, 108.6, null, null, null, 1.18, 1.27, 0,
            new[] { ("金属腐蚀物", "GB 30000.17", "类别1", null), ("皮肤腐蚀/刺激", "GB 30000.19", "类别1B", ""),
                    ("严重眼损伤/刺激", "GB 30000.20", "类别1", null), ("特异性靶器官毒性 一次接触", "GB 30000.25", "类别3", null) },
            new[] { "氢氯酸", "氯化氢溶液", "盐镪水" }, new[] { "碱", "氧化剂", "氰化物", "金属", "胺类", "氨", "氢氧化钠" });

        await InsertSubstance(conn, tx, "氢氧化钠", "Sodium hydroxide", "1310-73-2", "1823", "NaOH", "固体",
            null, 1388, null, null, null, 2.13, null, 0,
            new[] { ("金属腐蚀物", "GB 30000.17", "类别1", null), ("皮肤腐蚀/刺激", "GB 30000.19", "类别1A", ""),
                    ("严重眼损伤/刺激", "GB 30000.20", "类别1", null) },
            new[] { "烧碱", "火碱", "苛性钠", "固碱" }, new[] { "酸", "氯化氢", "铝", "锌", "锡", "硝基化合物", "氰化氢" });

        await InsertSubstance(conn, tx, "氢氧化钾", "Potassium hydroxide", "1310-58-3", "1813", "KOH", "固体",
            null, 1320, null, null, null, 2.04, null, 0,
            new[] { ("金属腐蚀物", "GB 30000.17", "类别1", null), ("皮肤腐蚀/刺激", "GB 30000.19", "类别1A", ""),
                    ("急性毒性", "GB 30000.18", "类别4（经口）", null) },
            new[] { "苛性钾", "钾碱" }, new[] { "酸", "氯化氢", "铝", "锌", "锡" });

        await InsertSubstance(conn, tx, "氢氟酸", "Hydrofluoric acid", "7664-39-3", "1790", "HF", "液体",
            null, 19.5, null, null, null, 1.15, 0.7, 1,
            new[] { ("急性毒性", "GB 30000.18", "类别1（经皮）", null), ("皮肤腐蚀/刺激", "GB 30000.19", "类别1A", ""),
                    ("金属腐蚀物", "GB 30000.17", "类别1", null) },
            new[] { "氟化氢溶液", "氟氢酸" }, new[] { "碱", "氨", "氨水", "玻璃", "硅酸盐", "金属" });

        // ── 剧毒品类 ──
        await InsertSubstance(conn, tx, "氰化钠", "Sodium cyanide", "143-33-9", "1689", "NaCN", "固体",
            null, 1496, null, null, null, 1.6, null, 1,
            new (string, string, string?, string?)[] { ("急性毒性", "GB 30000.18", "类别1（经口/经皮/吸入）", null), ("对水生环境危害", "GB 30000.28", "类别1", null) },
            new[] { "山奈", "山奈钠", "氰化钠盐" }, new[] { "酸", "氧化剂", "硝酸", "盐酸", "硫酸", "水" });

        // ── 环氧乙烷 ──
        await InsertSubstance(conn, tx, "环氧乙烷", "Ethylene oxide", "75-21-8", "1040", "C2H4O", "气体（加压液化）",
            null, 10.7, 3.0, 100.0, 429, null, 1.52, 10,
            new[] { ("易燃气体", "GB 30000.3", "类别1", null), ("加压气体", "GB 30000.6", "液化气体", ""),
                    ("急性毒性", "GB 30000.18", "类别3（吸入）", null), ("致癌性", "GB 30000.23", "类别1B", ""),
                    ("生殖细胞致突变性", "GB 30000.22", "类别1B", null) },
            new[] { "氧化乙烯", "EO" }, new[] { "氨", "酸", "碱", "水", "醇类", "聚合引发剂" });

        // ── 其他有机物 ──
        await InsertSubstance(conn, tx, "甲醛", "Formaldehyde", "50-00-0", "2209", "CH2O", "液体（甲醛溶液）",
            50, -19.5, 7.0, 73.0, 430, 1.08, 1.03, 5,
            new[] { ("易燃液体", "GB 30000.7", "类别3（甲醛溶液）", null), ("急性毒性", "GB 30000.18", "类别3（经口/经皮/吸入）", ""),
                    ("皮肤腐蚀/刺激", "GB 30000.19", "类别1B", null), ("致癌性", "GB 30000.23", "类别1B", ""),
                    ("皮肤致敏", "GB 30000.21", "类别1", null) },
            new[] { "福尔马林", "甲醛溶液", "蚁醛", "甲醛水" }, new[] { "氧化剂", "硝酸", "过氧化氢", "胺类", "氨" });

        await InsertSubstance(conn, tx, "苯乙烯", "Styrene", "100-42-5", "2055", "C8H8", "液体",
            31, 145, 1.1, 8.0, 490, 0.91, 3.6, 500,
            new[] { ("易燃液体", "GB 30000.7", "类别3", null), ("皮肤腐蚀/刺激", "GB 30000.19", "类别2", ""),
                    ("严重眼损伤/刺激", "GB 30000.20", "类别2", null), ("致癌性", "GB 30000.23", "类别2", ""),
                    ("特异性靶器官毒性 反复接触", "GB 30000.26", "类别1", null) },
            new[] { "乙烯基苯", "苏合香烯", "ST" }, new[] { "过氧化物", "氧化剂", "强酸", "过氧化氢", "聚合引发剂" });

        await InsertSubstance(conn, tx, "三氯甲烷", "Chloroform", "67-66-3", "1888", "CHCl3", "液体",
            null, 61.2, null, null, null, 1.48, 4.12, 0,
            new[] { ("急性毒性", "GB 30000.18", "类别4（经口/经皮）", null), ("皮肤腐蚀/刺激", "GB 30000.19", "类别2", ""),
                    ("严重眼损伤/刺激", "GB 30000.20", "类别2", null), ("致癌性", "GB 30000.23", "类别2", ""),
                    ("特异性靶器官毒性 反复接触", "GB 30000.26", "类别1", null) },
            new[] { "氯仿", "哥罗仿" }, new[] { "强碱", "碱金属", "铝" });

        // ── 气体 / 液化气体 ──
        await InsertSubstance(conn, tx, "氯化氢", "Hydrogen chloride", "7647-01-0", "1050", "HCl", "气体（液化）",
            null, -85, null, null, null, null, 1.27, 20,
            new[] { ("加压气体", "GB 30000.6", "液化气体", null), ("急性毒性", "GB 30000.18", "类别3（吸入）", ""),
                    ("皮肤腐蚀/刺激", "GB 30000.19", "类别1A", null), ("金属腐蚀物", "GB 30000.17", "类别1", null) },
            new[] { "氯化氢气", "盐酸气", "无水盐酸" }, new[] { "碱", "胺类", "氨", "氢氧化钠", "活泼金属" });

        await InsertSubstance(conn, tx, "二氧化硫", "Sulfur dioxide", "7446-09-5", "1079", "SO2", "气体（液化）",
            null, -10, null, null, null, 1.46, 2.26, 20,
            new[] { ("加压气体", "GB 30000.6", "液化气体", null), ("急性毒性", "GB 30000.18", "类别3（吸入）", ""),
                    ("皮肤腐蚀/刺激", "GB 30000.19", "类别1B", null), ("金属腐蚀物", "GB 30000.17", "类别1", null) },
            new[] { "亚硫酸酐", "亚硫酐" }, new[] { "氨", "碱", "强还原剂" });

        await InsertSubstance(conn, tx, "氧气", "Oxygen", "7782-44-7", "1072", "O2", "气体（压缩/液化）",
            null, -183, null, null, null, 1.14, 1.11, 200,
            new (string, string, string?, string?)[] { ("氧化性气体", "GB 30000.5", "类别1", null), ("加压气体", "GB 30000.6", "压缩气体/冷冻液化气体", null) },
            new[] { "液氧", "氧气瓶", "O2" }, new[] { "易燃物", "还原剂", "油类", "乙炔", "氢气" });

        // ── 固体危险品 ──
        await InsertSubstance(conn, tx, "硫磺", "Sulfur", "7704-34-9", "1350", "S8", "固体",
            207, 444.6, null, null, 232, 2.07, null, 0,
            new (string, string, string?, string?)[] { ("易燃固体", "GB 30000.8", "类别2", null) },
            new[] { "硫黄", "硫磺粉" }, new[] { "氧化剂", "硝酸铵", "高锰酸钾", "氯酸盐", "硝酸盐" });

        await InsertSubstance(conn, tx, "铝粉", "Aluminium powder", "7429-90-5", "1396", "Al", "固体（粉末）",
            null, 2470, null, null, null, 2.7, null, 0,
            new (string, string, string?, string?)[] { ("遇水放出易燃气体", "GB 30000.13", "类别2", null), ("易燃固体", "GB 30000.8", "（粉尘有爆炸性）", null) },
            new[] { "银粉", "铝银粉" }, new[] { "氧化剂", "酸", "碱", "硝酸铵", "高锰酸钾", "卤代烃", "水" });

        // ── 氨溶液 ──
        await InsertSubstance(conn, tx, "氨溶液", "Ammonia solution", "1336-21-6", "2672", "NH3·H2O", "液体",
            null, 38, null, null, null, 0.91, null, 10,
            new[] { ("皮肤腐蚀/刺激", "GB 30000.19", "类别1B", null), ("严重眼损伤/刺激", "GB 30000.20", "类别1", ""),
                    ("对水生环境危害", "GB 30000.28", "类别1", null) },
            new[] { "氨水", "氢氧化铵", "阿摩尼亚水" }, new[] { "酸", "盐", "卤素", "次氯酸盐", "氯", "氢氟酸", "氯化氢" });

        // ── 丙三醇(甘油) ──
        await InsertSubstance(conn, tx, "丙三醇", "Glycerol", "56-81-5", "", "C3H8O3", "液体",
            160, 290, null, null, 370, 1.26, 3.1, 0,
            Array.Empty<(string, string, string?, string?)>(),
            new[] { "甘油" }, new[] { "氧化剂", "高锰酸钾", "硝酸", "铬酸", "过氧化物" });
    }
    /// <summary>
    /// 插入物质数据
    /// </summary>
    private async Task InsertSubstance(SqliteConnection conn, SqliteTransaction tx,
        string name, string nameEn, string cas, string un, string formula, string state,
        double? flashPt, double? boilPt, double? expLow, double? expHigh, double? autoIgn,
        double? relDens, double? vapDens, double threshold,
        (string category, string gbStandard, string? subCategory, string? hazardCode)[] categories,
        string[] aliases, string[] incompatWith)
    {
        var sql = @"INSERT INTO substances (name, name_en, cas_number, un_number, formula, physical_state,
            flash_point_c, boiling_point_c, explosive_lower, explosive_upper, auto_ignition_c,
            relative_density, vapor_density, major_hazard_threshold_tons) VALUES
            (@n, @ne, @cas, @un, @f, @ps, @fp, @bp, @el, @eu, @ai, @rd, @vd, @mht);
            SELECT last_insert_rowid();";
        // [P0 FIX] 显式传入 transaction，防止 Microsoft.Data.Sqlite 抛出 pending local transaction 异常
        using var cmd = new SqliteCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("@n", name);
        cmd.Parameters.AddWithValue("@ne", (object?)nameEn ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cas", cas);
        cmd.Parameters.AddWithValue("@un", un);
        cmd.Parameters.AddWithValue("@f", formula);
        cmd.Parameters.AddWithValue("@ps", state);
        cmd.Parameters.AddWithValue("@fp", (object?)flashPt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@bp", (object?)boilPt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@el", (object?)expLow ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@eu", (object?)expHigh ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ai", (object?)autoIgn ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@rd", (object?)relDens ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@vd", (object?)vapDens ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@mht", threshold);

        var subId = (long)(await cmd.ExecuteScalarAsync())!;

        // 插入危险类别
        foreach (var (cat, gb, subCat, hCode) in categories)
        {
            var catSql = @"INSERT INTO hazard_categories (substance_id, category, gb_standard, sub_category, hazard_code)
                VALUES (@sid, @cat, @gb, @sc, @hc)";
            using var catCmd = new SqliteCommand(catSql, conn, tx);
            catCmd.Parameters.AddWithValue("@sid", subId);
            catCmd.Parameters.AddWithValue("@cat", cat);
            catCmd.Parameters.AddWithValue("@gb", gb);
            catCmd.Parameters.AddWithValue("@sc", (object?)subCat ?? DBNull.Value);
            catCmd.Parameters.AddWithValue("@hc", (object?)hCode ?? DBNull.Value);
            await catCmd.ExecuteNonQueryAsync();
        }

        // 插入别名
        foreach (var alias in aliases)
        {
            var aliasSql = "INSERT OR IGNORE INTO substance_aliases (substance_id, alias) VALUES (@sid, @a)";
            using var aliasCmd = new SqliteCommand(aliasSql, conn, tx);
            aliasCmd.Parameters.AddWithValue("@sid", subId);
            aliasCmd.Parameters.AddWithValue("@a", alias);
            await aliasCmd.ExecuteNonQueryAsync();
        }

        // 插入禁忌类别
        foreach (var incat in incompatWith)
        {
            var incatSql = "INSERT INTO incompatibility_categories (substance_id, incompatible_with) VALUES (@sid, @iw)";
            using var incatCmd = new SqliteCommand(incatSql, conn, tx);
            incatCmd.Parameters.AddWithValue("@sid", subId);
            incatCmd.Parameters.AddWithValue("@iw", incat);
            await incatCmd.ExecuteNonQueryAsync();
        }
    }

    private async Task SeedSafetyDistancesAsync(SqliteConnection conn, SqliteTransaction tx)
    {
        var distances = new (string facilityPair, string? alias, double distance, string reg, string? clause, string? notes)[]
        {
            ("储罐-储罐", null, 15, "GB 50160", null, ""),
            ("储罐-建筑", null, 25, "GB 50160", null, ""),
            ("储罐-消防通道", null, 15, "GB 50160", null, ""),
            ("储罐-厂区边界", null, 30, "GB 50160", null, ""),
            ("液化烃储罐-储罐", null, 20, "GB 50160", "不小于相邻较大罐直径的0.75倍", ""),
            ("液化烃储罐-厂区围墙", "液化烃储罐-围墙,液化烃储罐与厂区围墙", 35, "GB 50160", null, "甲A类"),
            ("液化烃储罐-办公楼", null, 35, "GB 50160", null, ""),
            ("甲类仓库-建筑", null, 20, "GB 50160 / GB 50016", null, ""),
            ("甲类仓库-明火点", "甲类仓库-明火,甲类仓库与明火点", 30, "GB 50160", null, "明火点包括锅炉房、加热炉、火炬等"),
            ("甲类仓库-办公楼", "甲类仓库与办公楼", 30, "GB 50160", null, ""),
            ("甲类仓库-厂内道路", null, 15, "GB 50016", null, ""),
            ("甲类工艺装置-重要设施", "甲类工艺装置-中控室,甲类装置-重要设施", 30, "GB 50160", null, "重要设施包括中央控制室、变配电站等"),
            ("甲类工艺装置-明火点", null, 30, "GB 50160", null, ""),
            ("乙炔气柜-建筑", null, 25, "GB 50160", null, ""),
            ("乙炔气柜-办公楼", "乙炔气柜与办公楼", 25, "GB 50160", null, "甲类，按重要公共建筑对待"),
            ("氨罐-厂外道路", "氨罐与厂外道路", 20, "GB 50160", null, "乙类"),
            ("氢气长管拖车-明火点", "氢气长管拖车停车位-明火点,氢气拖车-明火", 25, "GB 50160", null, "氢气属甲类易燃气体"),
            ("消防站-甲类装置", "消防站-甲类工艺装置,消防站与甲类装置", 15, "GB 50160", null, ""),
            ("氯气储存区-居住区", "氯气储存区与居住区", 200, "GB 50160", "依据重大危险源等级", "剧毒气体，安全防护距离"),
            ("甲类厂房-甲类厂房", null, 12, "GB 50016", null, ""),
            ("易燃液体储罐-装卸站", null, 15, "GB 50160", null, ""),
            ("甲类仓库-明火点", "甲库-明火", 30, "GB 50160", null, "明火点包括锅炉房、加热炉、火炬等"),
        };

        foreach (var d in distances)
        {
            var sql = @"INSERT INTO safety_distances (facility_pair, facility_alias, min_distance_m, regulation_ref, clause_ref, notes)
                VALUES (@fp, @fa, @dm, @rr, @cr, @n)";
            using var cmd = new SqliteCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("@fp", d.facilityPair);
            cmd.Parameters.AddWithValue("@fa", (object?)d.alias ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@dm", d.distance);
            cmd.Parameters.AddWithValue("@rr", d.reg);
            cmd.Parameters.AddWithValue("@cr", (object?)d.clause ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@n", (object?)d.notes ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task SeedCompatibilityRulesAsync(SqliteConnection conn, SqliteTransaction tx)
    {
        var rules = new (string a, string b, int compat, string reason, string? reg)[]
        {
            ("苯", "丙酮", 1, "同类易燃液体可同库分区存放", "GB 15603"),
            ("硝酸", "乙酸", 0, "氧化剂与易燃液体严禁同库，硝酸为强氧化剂", "GB 15603"),
            ("氢氧化钠", "盐酸", 0, "酸碱中和放热反应，严禁同库混存", "GB 15603"),
            ("甲醇", "硝酸", 0, "氧化剂与易燃液体严禁同库，可能引发火灾爆炸", "GB 15603"),
            ("过氧化氢", "丙酮", 0, "强氧化剂与易燃液体严禁同库，过氧化氢遇有机物剧烈分解", "GB 15603"),
            ("氨", "氯化氢", 0, "酸性气体与碱性气体混合产生氯化铵烟雾，严禁同区", "GB 15603"),
            ("氯", "氨", 0, "氯与氨反应生成三氯化氮(易爆)，严禁同区混存", "GB 15603"),
            ("甲苯", "二甲苯", 1, "同类易燃液体（均为C类）可同库分区存放", "GB 15603"),
            ("高锰酸钾", "丙三醇", 0, "强氧化剂与易燃液体(甘油)严禁混存，接触可能自燃", ""),
            ("环氧乙烷", "氨", 0, "环氧乙烷遇氨可能发生聚合反应放热爆炸", ""),
            ("丙酮", "乙醇", 1, "同类易燃液体可同库分区存放", "GB 15603"),
            ("硝酸铵", "硫磺", 0, "硝酸铵为强氧化剂，硫磺为易燃固体，混合可形成爆炸性混合物", ""),
            ("氢氟酸", "氨溶液", 0, "酸碱中和放热，产生有毒氟化铵，严禁混存", ""),
            ("苯乙烯", "过氧化氢", 0, "过氧化物可引发苯乙烯剧烈聚合放热，存在爆炸风险", ""),
            ("硫化氢", "二氧化硫", 1, "同属酸性气体(还原性)，可同库但需有效隔离和通风", ""),
            ("乙炔", "氧气", 0, "易燃气体与助燃气体严禁同库，乙炔遇氧爆炸极限极宽(2.5-82%)", "GB 15603"),
            ("硝酸", "盐酸", 1, "两种强酸可同库分区存放，但需注意硝酸为氧化性需防腐蚀隔离", ""),
            ("氰化钠", "盐酸", 0, "氰化钠遇酸产生剧毒氰化氢(HCN)气体，严禁共库", ""),
            ("铝粉", "硝酸铵", 0, "金属粉末与氧化剂混合可形成爆炸性混合物，严禁混存", ""),
            ("三氯甲烷", "丙酮", 1, "无明确配伍禁忌，可同库分区存放", ""),
        };

        foreach (var r in rules)
        {
            var sql = @"INSERT INTO storage_compatibility_rules (substance_a, substance_b, is_compatible, reason, regulation_ref)
                VALUES (@a, @b, @c, @r, @reg)";
            using var cmd = new SqliteCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("@a", r.a);
            cmd.Parameters.AddWithValue("@b", r.b);
            cmd.Parameters.AddWithValue("@c", r.compat);
            cmd.Parameters.AddWithValue("@r", r.reason);
            cmd.Parameters.AddWithValue("@reg", (object?)r.reg ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task SeedRegulationVersionsAsync(SqliteConnection conn, SqliteTransaction tx)
    {
        var versions = new (string number, string title, string current, string? deprecated, int fullText, string? changes)[]
        {
            ("GB 15603", "常用化学危险品贮存通则", "2022", "1995", 1, "2022版更新了禁忌物料配存表、新增了危险化学品仓库分类储存要求"),
            ("GB 30000", "化学品分类和标签规范", "2013", null, 1, "系列标准共29部分（GB 30000.1-29），另有2024修订GB 30000.1-2024"),
            ("GB 30000.1", "化学品分类和标签规范 第1部分:通则", "2024", "2013", 1, "2024版更新了定义、分类标准，与GHS第8修订版接轨"),
            ("GB 50160", "石油化工企业设计防火标准", "2008（2018局部修订）", null, 0, "包含防火间距、储罐间距等关键安全距离数据"),
            ("GB 18218", "危险化学品重大危险源辨识", "2018", "2009", 0, "2018版调整了部分物质的临界量，扩大了适用范围"),
            ("GB 50016", "建筑设计防火规范", "2014（2018局部修订）", "2006", 0, "包含甲类仓库与建筑的防火间距"),
            ("GB 30871", "危险化学品企业特殊作业安全规范", "2022", "2014", 1, "2022版扩大了适用范围，新增多项特殊作业安全要求"),
        };

        foreach (var v in versions)
        {
            var sql = @"INSERT INTO regulation_versions (regulation_number, title, current_version, deprecated_versions, has_full_text, change_notes)
                VALUES (@rn, @t, @cv, @dv, @hft, @cn)";
            using var cmd = new SqliteCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("@rn", v.number);
            cmd.Parameters.AddWithValue("@t", v.title);
            cmd.Parameters.AddWithValue("@cv", v.current);
            cmd.Parameters.AddWithValue("@dv", (object?)v.deprecated ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@hft", v.fullText);
            cmd.Parameters.AddWithValue("@cn", (object?)v.changes ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
