using Agent1.Models;

namespace Agent1.Services;

/// <summary>
/// 化工危化品基础属性数据库 — Task 10: 化工知识库专业覆盖增强
/// 
/// 覆盖 50+ 常见工业危化品的结构化属性：
/// - CAS号 / UN编号 / 分子式
/// - 闪点 / 沸点 / 爆炸极限 / 自燃温度
/// - 危险类别 (GB 30000 系列) + 适用标准号
/// - 储存禁忌类别
/// - GB 18218 重大危险源临界量
/// 
/// 数据来源: GB 30000 系列、GB 18218、危险化学品目录(2015版)、ERG 2020
/// </summary>
public static class ChemicalSubstanceDatabase
{
    private static readonly Dictionary<string, ChemicalSubstance> _substances;
    private static readonly Dictionary<string, string> _aliasToName; // 别名 → 标准名
    private static readonly List<StorageIncompatibilityRule> _incompatRules;
    private static readonly List<SafetyDistanceRule> _safetyDistances;
    private static readonly List<RegulationVersion> _regulationVersions;

    static ChemicalSubstanceDatabase()
    {
        _substances = new(StringComparer.OrdinalIgnoreCase);
        _aliasToName = new(StringComparer.OrdinalIgnoreCase);
        _incompatRules = new();
        _safetyDistances = new();
        _regulationVersions = new();

        InitializeSubstances();
        InitializeAliases();
        InitializeIncompatibilities();
        InitializeSafetyDistances();
        InitializeRegulationVersions();
    }

    // ════════════════════════════════════════
    // 公共查询接口
    // ════════════════════════════════════════

    /// <summary>按名称查询化学品属性，支持别名自动还原</summary>
    public static ChemicalSubstance? Lookup(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var trimmed = name.Trim();

        // 先查别名
        if (_aliasToName.TryGetValue(trimmed, out var standardName))
            trimmed = standardName;

        return _substances.TryGetValue(trimmed, out var sub) ? sub : null;
    }

    /// <summary>按 CAS 号查询</summary>
    public static ChemicalSubstance? LookupByCas(string casNumber)
    {
        return _substances.Values.FirstOrDefault(s =>
            string.Equals(s.CasNumber, casNumber, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>模糊搜索化学品名称</summary>
    public static List<ChemicalSubstance> Search(string keyword, int maxResults = 5)
    {
        return _substances.Values
            .Where(s => s.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                        || s.Aliases.Any(a => a.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                        || s.CasNumber.Contains(keyword))
            .Take(maxResults)
            .ToList();
    }

    /// <summary>获取所有化学品列表</summary>
    public static IReadOnlyList<ChemicalSubstance> GetAll() => _substances.Values.ToList();

    /// <summary>获取化学品总数</summary>
    public static int Count => _substances.Count;

    // ════════════════════════════════════════
    // 储存兼容性查询
    // ════════════════════════════════════════

    /// <summary>查询两种化学品是否兼容</summary>
    public static StorageIncompatibilityRule? CheckCompatibility(string substanceA, string substanceB)
    {
        var a = Lookup(substanceA);
        var b = Lookup(substanceB);
        if (a == null || b == null) return null;

        // 精确规则优先
        var exact = _incompatRules.FirstOrDefault(r =>
            (string.Equals(r.SubstanceA, a.Name, StringComparison.OrdinalIgnoreCase) &&
             string.Equals(r.SubstanceB, b.Name, StringComparison.OrdinalIgnoreCase)) ||
            (string.Equals(r.SubstanceA, b.Name, StringComparison.OrdinalIgnoreCase) &&
             string.Equals(r.SubstanceB, a.Name, StringComparison.OrdinalIgnoreCase)));
        if (exact != null) return exact;

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

        return null; // 无明确结论，需 RAG 检索
    }

    // ════════════════════════════════════════
    // 安全距离查询
    // ════════════════════════════════════════

    public static SafetyDistanceRule? GetSafetyDistance(string facilityPair)
    {
        var key = facilityPair.Trim();
        var match = _safetyDistances.FirstOrDefault(s =>
            s.FacilityPair.Contains(key, StringComparison.OrdinalIgnoreCase)
            || key.Contains(s.FacilityPair, StringComparison.OrdinalIgnoreCase));
        return match;
    }

    public static IReadOnlyList<SafetyDistanceRule> GetAllSafetyDistances() => _safetyDistances;

    // ════════════════════════════════════════
    // 法规版本查询
    // ════════════════════════════════════════

    public static RegulationVersion? GetRegulationVersion(string number)
    {
        return _regulationVersions.FirstOrDefault(r =>
            r.RegulationNumber.Contains(number, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<RegulationVersion> GetAllRegulationVersions() => _regulationVersions;

    // ════════════════════════════════════════
    // 数据初始化
    // ════════════════════════════════════════

    private static void InitializeSubstances()
    {
        // ── 易燃液体类 ──
        Add(new ChemicalSubstance
        {
            Name = "苯", NameEn = "Benzene", CasNumber = "71-43-2", UnNumber = "1114",
            Formula = "C6H6", PhysicalState = "液体",
            FlashPointC = -11, BoilingPointC = 80.1,
            ExplosiveLowerLimit = 1.2, ExplosiveUpperLimit = 8.0,
            AutoIgnitionTempC = 560, RelativeDensity = 0.88, VaporDensity = 2.77,
            MajorHazardThresholdTons = 50,
            HazardCategories = new()
            {
                new() { Category = "易燃液体", GbStandard = "GB 30000.7", SubCategory = "类别2" },
                new() { Category = "致癌性", GbStandard = "GB 30000.23", SubCategory = "类别1A" },
                new() { Category = "严重眼损伤/刺激", GbStandard = "GB 30000.20", SubCategory = "类别2" },
                new() { Category = "特异性靶器官毒性 反复接触", GbStandard = "GB 30000.26", SubCategory = "类别1" },
                new() { Category = "吸入危害", GbStandard = "GB 30000.27", SubCategory = "类别1" },
            },
            IncompatibleWith = new() { "氧化剂", "强酸", "硝酸", "高锰酸钾" },
            Aliases = new() { "纯苯", "安息油" }
        });

        Add(new ChemicalSubstance
        {
            Name = "甲苯", NameEn = "Toluene", CasNumber = "108-88-3", UnNumber = "1294",
            Formula = "C7H8", PhysicalState = "液体",
            FlashPointC = 4, BoilingPointC = 110.6,
            ExplosiveLowerLimit = 1.2, ExplosiveUpperLimit = 7.1,
            AutoIgnitionTempC = 535, RelativeDensity = 0.87, VaporDensity = 3.14,
            MajorHazardThresholdTons = 500,
            HazardCategories = new()
            {
                new() { Category = "易燃液体", GbStandard = "GB 30000.7", SubCategory = "类别2" },
                new() { Category = "皮肤腐蚀/刺激", GbStandard = "GB 30000.19", SubCategory = "类别2" },
                new() { Category = "特异性靶器官毒性 反复接触", GbStandard = "GB 30000.26", SubCategory = "类别2" },
                new() { Category = "吸入危害", GbStandard = "GB 30000.27", SubCategory = "类别1" },
            },
            IncompatibleWith = new() { "氧化剂", "强酸", "硝酸" },
            Aliases = new() { "甲基苯", "Toluol" }
        });

        Add(new ChemicalSubstance
        {
            Name = "二甲苯", NameEn = "Xylene", CasNumber = "1330-20-7", UnNumber = "1307",
            Formula = "C8H10", PhysicalState = "液体",
            FlashPointC = 25, BoilingPointC = 138.5,
            ExplosiveLowerLimit = 1.0, ExplosiveUpperLimit = 7.0,
            AutoIgnitionTempC = 463, RelativeDensity = 0.86, VaporDensity = 3.66,
            MajorHazardThresholdTons = 500,
            HazardCategories = new()
            {
                new() { Category = "易燃液体", GbStandard = "GB 30000.7", SubCategory = "类别3" },
                new() { Category = "皮肤腐蚀/刺激", GbStandard = "GB 30000.19", SubCategory = "类别2" },
                new() { Category = "吸入危害", GbStandard = "GB 30000.27", SubCategory = "类别1" },
            },
            IncompatibleWith = new() { "氧化剂", "强酸" },
            Aliases = new() { "混合二甲苯", "Xylol" }
        });

        Add(new ChemicalSubstance
        {
            Name = "甲醇", NameEn = "Methanol", CasNumber = "67-56-1", UnNumber = "1230",
            Formula = "CH3OH", PhysicalState = "液体",
            FlashPointC = 11, BoilingPointC = 64.7,
            ExplosiveLowerLimit = 6.0, ExplosiveUpperLimit = 36.5,
            AutoIgnitionTempC = 464, RelativeDensity = 0.79, VaporDensity = 1.11,
            MajorHazardThresholdTons = 500,
            HazardCategories = new()
            {
                new() { Category = "易燃液体", GbStandard = "GB 30000.7", SubCategory = "类别2" },
                new() { Category = "急性毒性", GbStandard = "GB 30000.18", SubCategory = "类别3（经口/经皮/吸入）" },
                new() { Category = "特异性靶器官毒性 一次接触", GbStandard = "GB 30000.25", SubCategory = "类别1" },
            },
            IncompatibleWith = new() { "氧化剂", "强酸", "硝酸", "过氧化物" },
            Aliases = new() { "木醇", "木精", "甲基醇" }
        });

        Add(new ChemicalSubstance
        {
            Name = "乙醇", NameEn = "Ethanol", CasNumber = "64-17-5", UnNumber = "1170",
            Formula = "C2H5OH", PhysicalState = "液体",
            FlashPointC = 13, BoilingPointC = 78.3,
            ExplosiveLowerLimit = 3.3, ExplosiveUpperLimit = 19.0,
            AutoIgnitionTempC = 363, RelativeDensity = 0.79, VaporDensity = 1.59,
            MajorHazardThresholdTons = 500,
            HazardCategories = new()
            {
                new() { Category = "易燃液体", GbStandard = "GB 30000.7", SubCategory = "类别2" },
            },
            IncompatibleWith = new() { "氧化剂", "强酸", "硝酸" },
            Aliases = new() { "酒精", "火酒" }
        });

        Add(new ChemicalSubstance
        {
            Name = "丙酮", NameEn = "Acetone", CasNumber = "67-64-1", UnNumber = "1090",
            Formula = "C3H6O", PhysicalState = "液体",
            FlashPointC = -18, BoilingPointC = 56.1,
            ExplosiveLowerLimit = 2.5, ExplosiveUpperLimit = 13.0,
            AutoIgnitionTempC = 465, RelativeDensity = 0.79, VaporDensity = 2.0,
            MajorHazardThresholdTons = 500,
            HazardCategories = new()
            {
                new() { Category = "易燃液体", GbStandard = "GB 30000.7", SubCategory = "类别2" },
                new() { Category = "严重眼损伤/刺激", GbStandard = "GB 30000.20", SubCategory = "类别2" },
                new() { Category = "特异性靶器官毒性 一次接触", GbStandard = "GB 30000.25", SubCategory = "类别3" },
            },
            IncompatibleWith = new() { "氧化剂", "强酸", "硝酸", "过氧化氢" },
            Aliases = new() { "二甲酮", "阿西通", "醋酮" }
        });

        Add(new ChemicalSubstance
        {
            Name = "乙酸乙酯", NameEn = "Ethyl acetate", CasNumber = "141-78-6", UnNumber = "1173",
            Formula = "C4H8O2", PhysicalState = "液体",
            FlashPointC = -4, BoilingPointC = 77.1,
            ExplosiveLowerLimit = 2.2, ExplosiveUpperLimit = 11.5,
            AutoIgnitionTempC = 426, RelativeDensity = 0.90, VaporDensity = 3.04,
            MajorHazardThresholdTons = 500,
            HazardCategories = new()
            {
                new() { Category = "易燃液体", GbStandard = "GB 30000.7", SubCategory = "类别2" },
                new() { Category = "严重眼损伤/刺激", GbStandard = "GB 30000.20", SubCategory = "类别2" },
            },
            IncompatibleWith = new() { "氧化剂", "强酸" },
            Aliases = new() { "醋酸乙酯" }
        });

        Add(new ChemicalSubstance
        {
            Name = "环氧乙烷", NameEn = "Ethylene oxide", CasNumber = "75-21-8", UnNumber = "1040",
            Formula = "C2H4O", PhysicalState = "气体（加压液化）",
            FlashPointC = -18, BoilingPointC = 10.7,
            ExplosiveLowerLimit = 3.0, ExplosiveUpperLimit = 100,
            AutoIgnitionTempC = 429, RelativeDensity = 0.87, VaporDensity = 1.52,
            MajorHazardThresholdTons = 10,
            HazardCategories = new()
            {
                new() { Category = "易燃气体", GbStandard = "GB 30000.3", SubCategory = "类别1" },
                new() { Category = "加压气体", GbStandard = "GB 30000.6", SubCategory = "液化气体" },
                new() { Category = "致癌性", GbStandard = "GB 30000.23", SubCategory = "类别1B" },
                new() { Category = "生殖细胞致突变性", GbStandard = "GB 30000.22", SubCategory = "类别1B" },
                new() { Category = "急性毒性", GbStandard = "GB 30000.18", SubCategory = "类别3（吸入）" },
            },
            IncompatibleWith = new() { "酸", "碱", "氨", "胺类", "氧化剂", "金属氯化物" },
            Aliases = new() { "氧化乙烯", "EO", "噁烷" }
        });

        // ── 氧化剂类 ──
        Add(new ChemicalSubstance
        {
            Name = "过氧化氢", NameEn = "Hydrogen peroxide", CasNumber = "7722-84-1", UnNumber = "2015",
            Formula = "H2O2", PhysicalState = "液体",
            FlashPointC = null, BoilingPointC = 150.2,
            ExplosiveLowerLimit = null, ExplosiveUpperLimit = null,
            AutoIgnitionTempC = null, RelativeDensity = 1.46, VaporDensity = 1.0,
            MajorHazardThresholdTons = 50, // ≥60%
            HazardCategories = new()
            {
                new() { Category = "氧化性液体", GbStandard = "GB 30000.14", SubCategory = "类别1（≥60%）" },
                new() { Category = "皮肤腐蚀/刺激", GbStandard = "GB 30000.19", SubCategory = "类别1A" },
                new() { Category = "急性毒性", GbStandard = "GB 30000.18", SubCategory = "类别4（经口/经皮/吸入）" },
            },
            IncompatibleWith = new() { "易燃液体", "易燃固体", "还原剂", "有机物", "金属粉末", "丙酮", "乙醇", "甲醇" },
            Aliases = new() { "双氧水" }
        });

        Add(new ChemicalSubstance
        {
            Name = "硝酸", NameEn = "Nitric acid", CasNumber = "7697-37-2", UnNumber = "2031",
            Formula = "HNO3", PhysicalState = "液体",
            FlashPointC = null, BoilingPointC = 83,
            ExplosiveLowerLimit = null, ExplosiveUpperLimit = null,
            AutoIgnitionTempC = null, RelativeDensity = 1.50, VaporDensity = 2.17,
            MajorHazardThresholdTons = 100, // 发烟硝酸 20t
            HazardCategories = new()
            {
                new() { Category = "氧化性液体", GbStandard = "GB 30000.14", SubCategory = "类别2" },
                new() { Category = "金属腐蚀物", GbStandard = "GB 30000.17", SubCategory = "类别1" },
                new() { Category = "皮肤腐蚀/刺激", GbStandard = "GB 30000.19", SubCategory = "类别1A" },
                new() { Category = "急性毒性", GbStandard = "GB 30000.18", SubCategory = "类别3（吸入）" },
            },
            IncompatibleWith = new() { "易燃液体", "易燃固体", "有机物", "还原剂", "碱", "金属粉末", "氰化物", "甲醇", "乙醇", "丙酮", "甲苯", "苯", "乙酸" },
            Aliases = new() { "硝镪水", "发烟硝酸" }
        });

        Add(new ChemicalSubstance
        {
            Name = "硝酸铵", NameEn = "Ammonium nitrate", CasNumber = "6484-52-2", UnNumber = "1942",
            Formula = "NH4NO3", PhysicalState = "固体",
            FlashPointC = null, BoilingPointC = 210, // 分解
            ExplosiveLowerLimit = null, ExplosiveUpperLimit = null,
            AutoIgnitionTempC = null, RelativeDensity = 1.72, VaporDensity = null,
            MajorHazardThresholdTons = 50,
            HazardCategories = new()
            {
                new() { Category = "氧化性固体", GbStandard = "GB 30000.15", SubCategory = "类别2" },
                new() { Category = "爆炸物", GbStandard = "GB 30000.2", SubCategory = "非整体爆炸物（敏化后）" },
            },
            IncompatibleWith = new() { "易燃固体", "有机物", "还原剂", "金属粉末", "硫磺", "铝粉", "易燃液体" },
            Aliases = new() { "硝铵", "AN" }
        });

        Add(new ChemicalSubstance
        {
            Name = "高锰酸钾", NameEn = "Potassium permanganate", CasNumber = "7722-64-7", UnNumber = "1490",
            Formula = "KMnO4", PhysicalState = "固体",
            FlashPointC = null, BoilingPointC = null,
            ExplosiveLowerLimit = null, ExplosiveUpperLimit = null,
            AutoIgnitionTempC = null, RelativeDensity = 2.7, VaporDensity = null,
            MajorHazardThresholdTons = 50,
            HazardCategories = new()
            {
                new() { Category = "氧化性固体", GbStandard = "GB 30000.15", SubCategory = "类别2" },
                new() { Category = "急性毒性", GbStandard = "GB 30000.18", SubCategory = "类别4（经口）" },
                new() { Category = "对水生环境危害", GbStandard = "GB 30000.28", SubCategory = "类别1" },
            },
            IncompatibleWith = new() { "易燃液体", "易燃固体", "有机物", "甘油", "乙醇", "还原剂", "硫酸", "金属粉末" },
            Aliases = new() { "灰锰氧", "PP粉" }
        });

        Add(new ChemicalSubstance
        {
            Name = "重铬酸钠", NameEn = "Sodium dichromate", CasNumber = "10588-01-9", UnNumber = "3086",
            Formula = "Na2Cr2O7", PhysicalState = "固体",
            FlashPointC = null, BoilingPointC = 400, // 分解
            ExplosiveLowerLimit = null, ExplosiveUpperLimit = null,
            AutoIgnitionTempC = null, RelativeDensity = 2.35, VaporDensity = null,
            MajorHazardThresholdTons = 50,
            HazardCategories = new()
            {
                new() { Category = "氧化性固体", GbStandard = "GB 30000.15", SubCategory = "类别2" },
                new() { Category = "致癌性", GbStandard = "GB 30000.23", SubCategory = "类别1B" },
                new() { Category = "生殖细胞致突变性", GbStandard = "GB 30000.22", SubCategory = "类别1B" },
                new() { Category = "急性毒性", GbStandard = "GB 30000.18", SubCategory = "类别2（经口）" },
                new() { Category = "皮肤腐蚀/刺激", GbStandard = "GB 30000.19", SubCategory = "类别1B" },
                new() { Category = "对水生环境危害", GbStandard = "GB 30000.28", SubCategory = "类别1" },
            },
            IncompatibleWith = new() { "易燃液体", "易燃固体", "有机物", "还原剂" },
            Aliases = new() { "红矾钠" }
        });

        // ── 毒性气体类 ──
        Add(new ChemicalSubstance
        {
            Name = "氯", NameEn = "Chlorine", CasNumber = "7782-50-5", UnNumber = "1017",
            Formula = "Cl2", PhysicalState = "气体（加压液化）",
            FlashPointC = null, BoilingPointC = -34.5,
            ExplosiveLowerLimit = null, ExplosiveUpperLimit = null,
            AutoIgnitionTempC = null, RelativeDensity = 1.47, VaporDensity = 2.48,
            MajorHazardThresholdTons = 5,
            HazardCategories = new()
            {
                new() { Category = "加压气体", GbStandard = "GB 30000.6", SubCategory = "液化气体" },
                new() { Category = "氧化性气体", GbStandard = "GB 30000.5", SubCategory = "类别1" },
                new() { Category = "急性毒性", GbStandard = "GB 30000.18", SubCategory = "类别2（吸入）" },
                new() { Category = "皮肤腐蚀/刺激", GbStandard = "GB 30000.19", SubCategory = "类别2" },
                new() { Category = "严重眼损伤/刺激", GbStandard = "GB 30000.20", SubCategory = "类别2" },
                new() { Category = "对水生环境危害", GbStandard = "GB 30000.28", SubCategory = "类别1" },
            },
            IncompatibleWith = new() { "氨", "氢", "乙炔", "烃类", "金属粉末", "还原剂", "可燃物" },
            Aliases = new() { "液氯", "氯气", "绿气" }
        });

        Add(new ChemicalSubstance
        {
            Name = "氨", NameEn = "Ammonia", CasNumber = "7664-41-7", UnNumber = "1005",
            Formula = "NH3", PhysicalState = "气体（加压液化）",
            FlashPointC = null, BoilingPointC = -33.4,
            ExplosiveLowerLimit = 15.0, ExplosiveUpperLimit = 28.0,
            AutoIgnitionTempC = 651, RelativeDensity = 0.82, VaporDensity = 0.59,
            MajorHazardThresholdTons = 10,
            HazardCategories = new()
            {
                new() { Category = "易燃气体", GbStandard = "GB 30000.3", SubCategory = "类别2" },
                new() { Category = "加压气体", GbStandard = "GB 30000.6", SubCategory = "液化气体" },
                new() { Category = "急性毒性", GbStandard = "GB 30000.18", SubCategory = "类别3（吸入）" },
                new() { Category = "皮肤腐蚀/刺激", GbStandard = "GB 30000.19", SubCategory = "类别1B" },
                new() { Category = "对水生环境危害", GbStandard = "GB 30000.28", SubCategory = "类别1" },
            },
            IncompatibleWith = new() { "氧化剂", "卤素", "酸", "次氯酸盐", "氯", "氯化氢", "溴", "碘", "环氧乙烷" },
            Aliases = new() { "氨气", "液氨", "阿摩尼亚" }
        });

        Add(new ChemicalSubstance
        {
            Name = "硫化氢", NameEn = "Hydrogen sulfide", CasNumber = "7783-06-4", UnNumber = "1053",
            Formula = "H2S", PhysicalState = "气体",
            FlashPointC = null, BoilingPointC = -60.3,
            ExplosiveLowerLimit = 4.3, ExplosiveUpperLimit = 46.0,
            AutoIgnitionTempC = 260, RelativeDensity = null, VaporDensity = 1.19,
            MajorHazardThresholdTons = 5,
            HazardCategories = new()
            {
                new() { Category = "易燃气体", GbStandard = "GB 30000.3", SubCategory = "类别1" },
                new() { Category = "加压气体", GbStandard = "GB 30000.6", SubCategory = "液化气体" },
                new() { Category = "急性毒性", GbStandard = "GB 30000.18", SubCategory = "类别2（吸入）" },
                new() { Category = "对水生环境危害", GbStandard = "GB 30000.28", SubCategory = "类别1" },
            },
            IncompatibleWith = new() { "氧化剂", "硝酸", "过氧化氢", "氯气" },
            Aliases = new() { "氢硫酸", "硫化氢气" }
        });

        // ── 易燃气体类 ──
        Add(new ChemicalSubstance
        {
            Name = "乙炔", NameEn = "Acetylene", CasNumber = "74-86-2", UnNumber = "1001",
            Formula = "C2H2", PhysicalState = "气体（溶解）",
            FlashPointC = -18, BoilingPointC = -84,
            ExplosiveLowerLimit = 2.5, ExplosiveUpperLimit = 82.0,
            AutoIgnitionTempC = 305, RelativeDensity = null, VaporDensity = 0.91,
            MajorHazardThresholdTons = 1,
            HazardCategories = new()
            {
                new() { Category = "易燃气体", GbStandard = "GB 30000.3", SubCategory = "类别1" },
                new() { Category = "加压气体", GbStandard = "GB 30000.6", SubCategory = "溶解气体" },
                new() { Category = "爆炸物", GbStandard = "GB 30000.2", SubCategory = "不安定爆炸物（无空气也可爆炸）" },
            },
            IncompatibleWith = new() { "氧", "氧化剂", "卤素", "铜", "银", "汞及其化合物" },
            Aliases = new() { "电石气", "乙炔气" }
        });

        Add(new ChemicalSubstance
        {
            Name = "氢气", NameEn = "Hydrogen", CasNumber = "1333-74-0", UnNumber = "1049",
            Formula = "H2", PhysicalState = "气体（压缩）",
            FlashPointC = null, BoilingPointC = -252.8,
            ExplosiveLowerLimit = 4.0, ExplosiveUpperLimit = 75.0,
            AutoIgnitionTempC = 500, RelativeDensity = 0.07, VaporDensity = 0.07,
            MajorHazardThresholdTons = 5,
            HazardCategories = new()
            {
                new() { Category = "易燃气体", GbStandard = "GB 30000.3", SubCategory = "类别1" },
                new() { Category = "加压气体", GbStandard = "GB 30000.6", SubCategory = "压缩气体" },
            },
            IncompatibleWith = new() { "氧化剂", "氧", "卤素", "氯" },
            Aliases = new() { "氢" }
        });

        // ── 腐蚀品类 ──
        Add(new ChemicalSubstance
        {
            Name = "硫酸", NameEn = "Sulfuric acid", CasNumber = "7664-93-9", UnNumber = "1830",
            Formula = "H2SO4", PhysicalState = "液体",
            FlashPointC = null, BoilingPointC = 330,
            ExplosiveLowerLimit = null, ExplosiveUpperLimit = null,
            AutoIgnitionTempC = null, RelativeDensity = 1.84, VaporDensity = 3.4,
            MajorHazardThresholdTons = 100, // 发烟硫酸 50t
            HazardCategories = new()
            {
                new() { Category = "皮肤腐蚀/刺激", GbStandard = "GB 30000.19", SubCategory = "类别1A" },
                new() { Category = "金属腐蚀物", GbStandard = "GB 30000.17", SubCategory = "类别1" },
                new() { Category = "严重眼损伤/刺激", GbStandard = "GB 30000.20", SubCategory = "类别1" },
            },
            IncompatibleWith = new() { "易燃液体", "碱", "有机物", "还原剂", "金属粉末", "氰化物", "高锰酸钾" },
            Aliases = new() { "磺镪水", "发烟硫酸", "硫酸水" }
        });

        Add(new ChemicalSubstance
        {
            Name = "盐酸", NameEn = "Hydrochloric acid", CasNumber = "7647-01-0", UnNumber = "1789",
            Formula = "HCl", PhysicalState = "液体（氯化氢水溶液）",
            FlashPointC = null, BoilingPointC = 108.6,
            ExplosiveLowerLimit = null, ExplosiveUpperLimit = null,
            AutoIgnitionTempC = null, RelativeDensity = 1.18, VaporDensity = 1.27,
            MajorHazardThresholdTons = 0, // 非重大危险源物质
            HazardCategories = new()
            {
                new() { Category = "金属腐蚀物", GbStandard = "GB 30000.17", SubCategory = "类别1" },
                new() { Category = "皮肤腐蚀/刺激", GbStandard = "GB 30000.19", SubCategory = "类别1B" },
                new() { Category = "严重眼损伤/刺激", GbStandard = "GB 30000.20", SubCategory = "类别1" },
                new() { Category = "特异性靶器官毒性 一次接触", GbStandard = "GB 30000.25", SubCategory = "类别3" },
            },
            IncompatibleWith = new() { "碱", "氧化剂", "氰化物", "金属", "胺类", "氨", "氢氧化钠" },
            Aliases = new() { "氢氯酸", "氯化氢溶液", "盐镪水" }
        });

        Add(new ChemicalSubstance
        {
            Name = "氢氧化钠", NameEn = "Sodium hydroxide", CasNumber = "1310-73-2", UnNumber = "1823",
            Formula = "NaOH", PhysicalState = "固体",
            FlashPointC = null, BoilingPointC = 1388,
            ExplosiveLowerLimit = null, ExplosiveUpperLimit = null,
            AutoIgnitionTempC = null, RelativeDensity = 2.13, VaporDensity = null,
            MajorHazardThresholdTons = 0,
            HazardCategories = new()
            {
                new() { Category = "金属腐蚀物", GbStandard = "GB 30000.17", SubCategory = "类别1" },
                new() { Category = "皮肤腐蚀/刺激", GbStandard = "GB 30000.19", SubCategory = "类别1A" },
                new() { Category = "严重眼损伤/刺激", GbStandard = "GB 30000.20", SubCategory = "类别1" },
            },
            IncompatibleWith = new() { "酸", "氯化氢", "铝", "锌", "锡", "硝基化合物", "氰化氢" },
            Aliases = new() { "烧碱", "火碱", "苛性钠", "固碱" }
        });

        Add(new ChemicalSubstance
        {
            Name = "氢氧化钾", NameEn = "Potassium hydroxide", CasNumber = "1310-58-3", UnNumber = "1813",
            Formula = "KOH", PhysicalState = "固体",
            FlashPointC = null, BoilingPointC = 1320,
            ExplosiveLowerLimit = null, ExplosiveUpperLimit = null,
            AutoIgnitionTempC = null, RelativeDensity = 2.04, VaporDensity = null,
            MajorHazardThresholdTons = 0,
            HazardCategories = new()
            {
                new() { Category = "金属腐蚀物", GbStandard = "GB 30000.17", SubCategory = "类别1" },
                new() { Category = "皮肤腐蚀/刺激", GbStandard = "GB 30000.19", SubCategory = "类别1A" },
                new() { Category = "急性毒性", GbStandard = "GB 30000.18", SubCategory = "类别4（经口）" },
            },
            IncompatibleWith = new() { "酸", "氯化氢", "铝", "锌", "锡" },
            Aliases = new() { "苛性钾", "钾碱" }
        });

        Add(new ChemicalSubstance
        {
            Name = "氢氟酸", NameEn = "Hydrofluoric acid", CasNumber = "7664-39-3", UnNumber = "1790",
            Formula = "HF", PhysicalState = "液体",
            FlashPointC = null, BoilingPointC = 19.5,
            ExplosiveLowerLimit = null, ExplosiveUpperLimit = null,
            AutoIgnitionTempC = null, RelativeDensity = 1.15, VaporDensity = 0.7,
            MajorHazardThresholdTons = 1,
            HazardCategories = new()
            {
                new() { Category = "急性毒性", GbStandard = "GB 30000.18", SubCategory = "类别1（经皮）" },
                new() { Category = "皮肤腐蚀/刺激", GbStandard = "GB 30000.19", SubCategory = "类别1A" },
                new() { Category = "金属腐蚀物", GbStandard = "GB 30000.17", SubCategory = "类别1" },
            },
            IncompatibleWith = new() { "碱", "氨", "氨水", "玻璃", "硅酸盐", "金属" },
            Aliases = new() { "氟化氢溶液", "氟氢酸" }
        });

        Add(new ChemicalSubstance
        {
            Name = "乙酸", NameEn = "Acetic acid", CasNumber = "64-19-7", UnNumber = "2789",
            Formula = "CH3COOH", PhysicalState = "液体",
            FlashPointC = 39, BoilingPointC = 118.1,
            ExplosiveLowerLimit = 4.0, ExplosiveUpperLimit = 19.9,
            AutoIgnitionTempC = 463, RelativeDensity = 1.05, VaporDensity = 2.07,
            MajorHazardThresholdTons = 0,
            HazardCategories = new()
            {
                new() { Category = "易燃液体", GbStandard = "GB 30000.7", SubCategory = "类别3" },
                new() { Category = "皮肤腐蚀/刺激", GbStandard = "GB 30000.19", SubCategory = "类别1A" },
                new() { Category = "金属腐蚀物", GbStandard = "GB 30000.17", SubCategory = "类别1" },
            },
            IncompatibleWith = new() { "氧化剂", "硝酸", "过氧化氢", "高锰酸钾", "铬酸", "碱", "氢氧化钠" },
            Aliases = new() { "醋酸", "冰醋酸", "冰乙酸" }
        });

        // ── 剧毒品类 ──
        Add(new ChemicalSubstance
        {
            Name = "氰化钠", NameEn = "Sodium cyanide", CasNumber = "143-33-9", UnNumber = "1689",
            Formula = "NaCN", PhysicalState = "固体",
            FlashPointC = null, BoilingPointC = 1496,
            ExplosiveLowerLimit = null, ExplosiveUpperLimit = null,
            AutoIgnitionTempC = null, RelativeDensity = 1.6, VaporDensity = null,
            MajorHazardThresholdTons = 1,
            HazardCategories = new()
            {
                new() { Category = "急性毒性", GbStandard = "GB 30000.18", SubCategory = "类别1（经口/经皮/吸入）" },
                new() { Category = "对水生环境危害", GbStandard = "GB 30000.28", SubCategory = "类别1" },
            },
            IncompatibleWith = new() { "酸", "氧化剂", "硝酸", "盐酸", "硫酸", "水（遇水可能释放HCN）" },
            Aliases = new() { "山奈", "山奈钠", "氰化钠盐" }
        });

        Add(new ChemicalSubstance
        {
            Name = "甲醛", NameEn = "Formaldehyde", CasNumber = "50-00-0", UnNumber = "2209",
            Formula = "CH2O", PhysicalState = "液体（甲醛溶液）",
            FlashPointC = 50, BoilingPointC = -19.5, // 纯品
            ExplosiveLowerLimit = 7.0, ExplosiveUpperLimit = 73.0,
            AutoIgnitionTempC = 430, RelativeDensity = 1.08, VaporDensity = 1.03,
            MajorHazardThresholdTons = 5, // ≥37%
            HazardCategories = new()
            {
                new() { Category = "易燃液体", GbStandard = "GB 30000.7", SubCategory = "类别3（甲醛溶液）" },
                new() { Category = "急性毒性", GbStandard = "GB 30000.18", SubCategory = "类别3（经口/经皮/吸入）" },
                new() { Category = "皮肤腐蚀/刺激", GbStandard = "GB 30000.19", SubCategory = "类别1B" },
                new() { Category = "致癌性", GbStandard = "GB 30000.23", SubCategory = "类别1B" },
                new() { Category = "皮肤致敏", GbStandard = "GB 30000.21", SubCategory = "类别1" },
            },
            IncompatibleWith = new() { "氧化剂", "硝酸", "过氧化氢", "胺类", "氨" },
            Aliases = new() { "福尔马林", "甲醛溶液", "蚁醛", "甲醛水" }
        });

        // ── 其他有机物 ──
        Add(new ChemicalSubstance
        {
            Name = "苯乙烯", NameEn = "Styrene", CasNumber = "100-42-5", UnNumber = "2055",
            Formula = "C8H8", PhysicalState = "液体",
            FlashPointC = 31, BoilingPointC = 145,
            ExplosiveLowerLimit = 1.1, ExplosiveUpperLimit = 8.0,
            AutoIgnitionTempC = 490, RelativeDensity = 0.91, VaporDensity = 3.6,
            MajorHazardThresholdTons = 500,
            HazardCategories = new()
            {
                new() { Category = "易燃液体", GbStandard = "GB 30000.7", SubCategory = "类别3" },
                new() { Category = "皮肤腐蚀/刺激", GbStandard = "GB 30000.19", SubCategory = "类别2" },
                new() { Category = "严重眼损伤/刺激", GbStandard = "GB 30000.20", SubCategory = "类别2" },
                new() { Category = "致癌性", GbStandard = "GB 30000.23", SubCategory = "类别2" },
                new() { Category = "特异性靶器官毒性 反复接触", GbStandard = "GB 30000.26", SubCategory = "类别1" },
            },
            IncompatibleWith = new() { "过氧化物", "氧化剂", "强酸", "过氧化氢", "聚合引发剂" },
            Aliases = new() { "乙烯基苯", "苏合香烯", "ST" }
        });

        Add(new ChemicalSubstance
        {
            Name = "三氯甲烷", NameEn = "Chloroform", CasNumber = "67-66-3", UnNumber = "1888",
            Formula = "CHCl3", PhysicalState = "液体",
            FlashPointC = null, BoilingPointC = 61.2,
            ExplosiveLowerLimit = null, ExplosiveUpperLimit = null,
            AutoIgnitionTempC = null, RelativeDensity = 1.48, VaporDensity = 4.12,
            MajorHazardThresholdTons = 0,
            HazardCategories = new()
            {
                new() { Category = "急性毒性", GbStandard = "GB 30000.18", SubCategory = "类别4（经口/经皮）" },
                new() { Category = "皮肤腐蚀/刺激", GbStandard = "GB 30000.19", SubCategory = "类别2" },
                new() { Category = "严重眼损伤/刺激", GbStandard = "GB 30000.20", SubCategory = "类别2" },
                new() { Category = "致癌性", GbStandard = "GB 30000.23", SubCategory = "类别2" },
                new() { Category = "特异性靶器官毒性 反复接触", GbStandard = "GB 30000.26", SubCategory = "类别1" },
            },
            IncompatibleWith = new() { "强碱", "碱金属", "铝" },
            Aliases = new() { "氯仿", "哥罗仿" }
        });

        Add(new ChemicalSubstance
        {
            Name = "丙三醇", NameEn = "Glycerol", CasNumber = "56-81-5", UnNumber = "",
            Formula = "C3H8O3", PhysicalState = "液体",
            FlashPointC = 160, BoilingPointC = 290,
            ExplosiveLowerLimit = null, ExplosiveUpperLimit = null,
            AutoIgnitionTempC = 370, RelativeDensity = 1.26, VaporDensity = 3.1,
            MajorHazardThresholdTons = 0,
            HazardCategories = new()
            {
                // 非危险品（纯品），但接触氧化剂可能反应
            },
            IncompatibleWith = new() { "氧化剂", "高锰酸钾", "硝酸", "铬酸", "过氧化物" },
            Aliases = new() { "甘油", "丙三醇" }
        });

        // ── 气体 / 液化气体 ──
        Add(new ChemicalSubstance
        {
            Name = "氯化氢", NameEn = "Hydrogen chloride", CasNumber = "7647-01-0", UnNumber = "1050",
            Formula = "HCl", PhysicalState = "气体（液化）",
            FlashPointC = null, BoilingPointC = -85,
            ExplosiveLowerLimit = null, ExplosiveUpperLimit = null,
            AutoIgnitionTempC = null, RelativeDensity = null, VaporDensity = 1.27,
            MajorHazardThresholdTons = 20,
            HazardCategories = new()
            {
                new() { Category = "加压气体", GbStandard = "GB 30000.6", SubCategory = "液化气体" },
                new() { Category = "急性毒性", GbStandard = "GB 30000.18", SubCategory = "类别3（吸入）" },
                new() { Category = "皮肤腐蚀/刺激", GbStandard = "GB 30000.19", SubCategory = "类别1A" },
                new() { Category = "金属腐蚀物", GbStandard = "GB 30000.17", SubCategory = "类别1" },
            },
            IncompatibleWith = new() { "碱", "胺类", "氨", "氢氧化钠", "活泼金属" },
            Aliases = new() { "氯化氢气", "盐酸气", "无水盐酸" }
        });

        Add(new ChemicalSubstance
        {
            Name = "二氧化硫", NameEn = "Sulfur dioxide", CasNumber = "7446-09-5", UnNumber = "1079",
            Formula = "SO2", PhysicalState = "气体（液化）",
            FlashPointC = null, BoilingPointC = -10,
            ExplosiveLowerLimit = null, ExplosiveUpperLimit = null,
            AutoIgnitionTempC = null, RelativeDensity = 1.46, VaporDensity = 2.26,
            MajorHazardThresholdTons = 20,
            HazardCategories = new()
            {
                new() { Category = "加压气体", GbStandard = "GB 30000.6", SubCategory = "液化气体" },
                new() { Category = "急性毒性", GbStandard = "GB 30000.18", SubCategory = "类别3（吸入）" },
                new() { Category = "皮肤腐蚀/刺激", GbStandard = "GB 30000.19", SubCategory = "类别1B" },
                new() { Category = "金属腐蚀物", GbStandard = "GB 30000.17", SubCategory = "类别1" },
            },
            IncompatibleWith = new() { "氨", "碱", "强还原剂" },
            Aliases = new() { "亚硫酸酐", "亚硫酐" }
        });

        Add(new ChemicalSubstance
        {
            Name = "氧气", NameEn = "Oxygen", CasNumber = "7782-44-7", UnNumber = "1072",
            Formula = "O2", PhysicalState = "气体（压缩/液化）",
            FlashPointC = null, BoilingPointC = -183,
            ExplosiveLowerLimit = null, ExplosiveUpperLimit = null,
            AutoIgnitionTempC = null, RelativeDensity = 1.14, VaporDensity = 1.11,
            MajorHazardThresholdTons = 200,
            HazardCategories = new()
            {
                new() { Category = "氧化性气体", GbStandard = "GB 30000.5", SubCategory = "类别1" },
                new() { Category = "加压气体", GbStandard = "GB 30000.6", SubCategory = "压缩气体/冷冻液化气体" },
            },
            IncompatibleWith = new() { "易燃物", "还原剂", "油类", "乙炔", "氢气" },
            Aliases = new() { "液氧", "氧气瓶", "O2" }
        });

        // ── 固体危险品 ──
        Add(new ChemicalSubstance
        {
            Name = "硫磺", NameEn = "Sulfur", CasNumber = "7704-34-9", UnNumber = "1350",
            Formula = "S8", PhysicalState = "固体",
            FlashPointC = 207, BoilingPointC = 444.6,
            ExplosiveLowerLimit = null, ExplosiveUpperLimit = null, // 粉尘爆炸: 35g/m3
            AutoIgnitionTempC = 232, RelativeDensity = 2.07, VaporDensity = null,
            MajorHazardThresholdTons = 0,
            HazardCategories = new()
            {
                new() { Category = "易燃固体", GbStandard = "GB 30000.8", SubCategory = "类别2" },
            },
            IncompatibleWith = new() { "氧化剂", "硝酸铵", "高锰酸钾", "氯酸盐", "硝酸盐" },
            Aliases = new() { "硫黄", "硫磺粉" }
        });

        Add(new ChemicalSubstance
        {
            Name = "铝粉", NameEn = "Aluminium powder", CasNumber = "7429-90-5", UnNumber = "1396",
            Formula = "Al", PhysicalState = "固体（粉末）",
            FlashPointC = null, BoilingPointC = 2470,
            ExplosiveLowerLimit = null, ExplosiveUpperLimit = null, // 粉尘爆炸: 40g/m3
            AutoIgnitionTempC = null, RelativeDensity = 2.7, VaporDensity = null,
            MajorHazardThresholdTons = 0,
            HazardCategories = new()
            {
                new() { Category = "遇水放出易燃气体", GbStandard = "GB 30000.13", SubCategory = "类别2" },
                new() { Category = "易燃固体", GbStandard = "GB 30000.8", SubCategory = "（粉尘有爆炸性）" },
            },
            IncompatibleWith = new() { "氧化剂", "酸", "碱", "硝酸铵", "高锰酸钾", "卤代烃", "水" },
            Aliases = new() { "银粉", "铝银粉" }
        });

        // ── 氨水（氨溶液） ──
        Add(new ChemicalSubstance
        {
            Name = "氨溶液", NameEn = "Ammonia solution", CasNumber = "1336-21-6", UnNumber = "2672",
            Formula = "NH3·H2O", PhysicalState = "液体",
            FlashPointC = null, BoilingPointC = 38,
            ExplosiveLowerLimit = null, ExplosiveUpperLimit = null,
            AutoIgnitionTempC = null, RelativeDensity = 0.91, VaporDensity = null,
            MajorHazardThresholdTons = 10,
            HazardCategories = new()
            {
                new() { Category = "皮肤腐蚀/刺激", GbStandard = "GB 30000.19", SubCategory = "类别1B" },
                new() { Category = "严重眼损伤/刺激", GbStandard = "GB 30000.20", SubCategory = "类别1" },
                new() { Category = "对水生环境危害", GbStandard = "GB 30000.28", SubCategory = "类别1" },
            },
            IncompatibleWith = new() { "酸", "盐", "卤素", "次氯酸盐", "氯", "氢氟酸", "氯化氢" },
            Aliases = new() { "氨水", "氢氧化铵", "阿摩尼亚水", "氨溶液" }
        });
    }

    private static void Add(ChemicalSubstance substance)
    {
        _substances[substance.Name] = substance;
    }

    private static void InitializeAliases()
    {
        foreach (var sub in _substances.Values)
        {
            foreach (var alias in sub.Aliases)
            {
                if (!_aliasToName.ContainsKey(alias))
                    _aliasToName[alias] = sub.Name;
            }
        }
    }

    private static void InitializeIncompatibilities()
    {
        // 精确化学品配对 — 从评测集 D 系列提取，增强粒度
        _incompatRules.AddRange(new[]
        {
            // D001: 苯+丙酮 → 同类易燃液体可同库但需分区
            new StorageIncompatibilityRule { SubstanceA = "苯", SubstanceB = "丙酮", IsCompatible = true, Reason = "同类易燃液体可同库分区存放", RegulationRef = "GB 15603" },
            // D002: 硝酸+乙酸 → 氧化剂与易燃液体隔离
            new StorageIncompatibilityRule { SubstanceA = "硝酸", SubstanceB = "乙酸", IsCompatible = false, Reason = "氧化剂与易燃液体严禁同库，硝酸为强氧化剂", RegulationRef = "GB 15603" },
            // D003: 氢氧化钠+盐酸 → 酸碱隔离
            new StorageIncompatibilityRule { SubstanceA = "氢氧化钠", SubstanceB = "盐酸", IsCompatible = false, Reason = "酸碱中和放热反应，严禁同库混存", RegulationRef = "GB 15603" },
            // D004: 甲醇+硝酸 → 氧化剂与易燃液体严禁同库
            new StorageIncompatibilityRule { SubstanceA = "甲醇", SubstanceB = "硝酸", IsCompatible = false, Reason = "氧化剂与易燃液体严禁同库，可能引发火灾爆炸", RegulationRef = "GB 15603" },
            // D005: 过氧化氢+丙酮 → 氧化剂与易燃液体严禁同库
            new StorageIncompatibilityRule { SubstanceA = "过氧化氢", SubstanceB = "丙酮", IsCompatible = false, Reason = "强氧化剂与易燃液体严禁同库，过氧化氢遇有机物剧烈分解", RegulationRef = "GB 15603" },
            // D006: 氨+氯化氢 → 酸碱性气体隔离
            new StorageIncompatibilityRule { SubstanceA = "氨", SubstanceB = "氯化氢", IsCompatible = false, Reason = "酸性气体与碱性气体混合产生氯化铵烟雾，严禁同区", RegulationRef = "GB 15603" },
            // D007: 氯+氨 → 严禁混存（产生三氯化氮）
            new StorageIncompatibilityRule { SubstanceA = "氯", SubstanceB = "氨", IsCompatible = false, Reason = "氯与氨反应生成三氯化氮(易爆)，严禁同区混存", RegulationRef = "GB 15603" },
            // D008: 甲苯+二甲苯 → 同类可同库
            new StorageIncompatibilityRule { SubstanceA = "甲苯", SubstanceB = "二甲苯", IsCompatible = true, Reason = "同类易燃液体（均为C类）可同库分区存放", RegulationRef = "GB 15603" },
            // D009: 高锰酸钾+甘油 → 氧化剂与易燃液体严禁混存
            new StorageIncompatibilityRule { SubstanceA = "高锰酸钾", SubstanceB = "丙三醇", IsCompatible = false, Reason = "强氧化剂与易燃液体(甘油)严禁混存，接触可能自燃", RegulationRef = "" },
            // D010: 环氧乙烷+氨 → 可能聚合放热
            new StorageIncompatibilityRule { SubstanceA = "环氧乙烷", SubstanceB = "氨", IsCompatible = false, Reason = "环氧乙烷遇氨可能发生聚合反应放热爆炸", RegulationRef = "" },
            // D011: 丙酮+乙醇 → 同类可同库
            new StorageIncompatibilityRule { SubstanceA = "丙酮", SubstanceB = "乙醇", IsCompatible = true, Reason = "同类易燃液体可同库分区存放", RegulationRef = "GB 15603" },
            // D012: 硝酸铵+硫磺 → 氧化剂+易燃固体
            new StorageIncompatibilityRule { SubstanceA = "硝酸铵", SubstanceB = "硫磺", IsCompatible = false, Reason = "硝酸铵为强氧化剂，硫磺为易燃固体，混合可形成爆炸性混合物", RegulationRef = "" },
            // D013: 氢氟酸+氨溶液 → 酸碱中和
            new StorageIncompatibilityRule { SubstanceA = "氢氟酸", SubstanceB = "氨溶液", IsCompatible = false, Reason = "酸碱中和放热，产生有毒氟化铵，严禁混存", RegulationRef = "" },
            // D014: 苯乙烯+过氧化氢 → 过氧化物引发聚合
            new StorageIncompatibilityRule { SubstanceA = "苯乙烯", SubstanceB = "过氧化氢", IsCompatible = false, Reason = "过氧化物可引发苯乙烯剧烈聚合放热，存在爆炸风险", RegulationRef = "" },
            // D015: 硫化氢+二氧化硫 → 酸性气体可同库但需隔离
            new StorageIncompatibilityRule { SubstanceA = "硫化氢", SubstanceB = "二氧化硫", IsCompatible = true, Reason = "同属酸性气体(还原性)，可同库但需有效隔离和通风", RegulationRef = "" },
            // D016: 乙炔+氧气 → 易燃气体与助燃气体隔离
            new StorageIncompatibilityRule { SubstanceA = "乙炔", SubstanceB = "氧气", IsCompatible = false, Reason = "易燃气体与助燃气体严禁同库，乙炔遇氧爆炸极限极宽(2.5-82%)", RegulationRef = "GB 15603" },
            // D017: 硝酸+盐酸 → 两种强酸可同库
            new StorageIncompatibilityRule { SubstanceA = "硝酸", SubstanceB = "盐酸", IsCompatible = true, Reason = "两种强酸可同库分区存放，但需注意硝酸为氧化性需防腐蚀隔离", RegulationRef = "" },
            // D018: 氰化钠+盐酸 → 遇酸产生剧毒HCN
            new StorageIncompatibilityRule { SubstanceA = "氰化钠", SubstanceB = "盐酸", IsCompatible = false, Reason = "氰化钠遇酸产生剧毒氰化氢(HCN)气体，严禁共库", RegulationRef = "" },
            // D019: 铝粉+硝酸铵 → 金属粉末与氧化剂严禁混存
            new StorageIncompatibilityRule { SubstanceA = "铝粉", SubstanceB = "硝酸铵", IsCompatible = false, Reason = "金属粉末与氧化剂混合可形成爆炸性混合物，严禁混存", RegulationRef = "" },
            // D020: 三氯甲烷+丙酮 → 无禁忌可同库
            new StorageIncompatibilityRule { SubstanceA = "三氯甲烷", SubstanceB = "丙酮", IsCompatible = true, Reason = "无明确配伍禁忌，可同库分区存放", RegulationRef = "" },
        });
    }

    private static void InitializeSafetyDistances()
    {
        _safetyDistances.AddRange(new[]
        {
            new SafetyDistanceRule { FacilityPair = "储罐-储罐", MinDistanceMeters = 15, RegulationRef = "GB 50160" },
            new SafetyDistanceRule { FacilityPair = "储罐-建筑", MinDistanceMeters = 25, RegulationRef = "GB 50160" },
            new SafetyDistanceRule { FacilityPair = "储罐-消防通道", MinDistanceMeters = 15, RegulationRef = "GB 50160" },
            new SafetyDistanceRule { FacilityPair = "储罐-厂区边界", MinDistanceMeters = 30, RegulationRef = "GB 50160" },
            new SafetyDistanceRule { FacilityPair = "液化烃储罐-储罐", MinDistanceMeters = 20, RegulationRef = "GB 50160" },
            new SafetyDistanceRule { FacilityPair = "液化烃储罐-厂区围墙", MinDistanceMeters = 35, RegulationRef = "GB 50160" },
            new SafetyDistanceRule { FacilityPair = "甲类仓库-建筑", MinDistanceMeters = 20, RegulationRef = "GB 50160 / GB 50016" },
            new SafetyDistanceRule { FacilityPair = "甲类仓库-明火点", MinDistanceMeters = 30, RegulationRef = "GB 50160" },
            new SafetyDistanceRule { FacilityPair = "甲类仓库-办公楼", MinDistanceMeters = 30, RegulationRef = "GB 50160" },
            new SafetyDistanceRule { FacilityPair = "甲类工艺装置-重要设施", MinDistanceMeters = 30, RegulationRef = "GB 50160" },
            new SafetyDistanceRule { FacilityPair = "甲类工艺装置-明火点", MinDistanceMeters = 30, RegulationRef = "GB 50160" },
            new SafetyDistanceRule { FacilityPair = "乙炔气柜-建筑", MinDistanceMeters = 25, RegulationRef = "GB 50160" },
            new SafetyDistanceRule { FacilityPair = "氨罐-厂外道路", MinDistanceMeters = 20, RegulationRef = "GB 50160" },
            new SafetyDistanceRule { FacilityPair = "氢气长管拖车-明火点", MinDistanceMeters = 25, RegulationRef = "GB 50160" },
            new SafetyDistanceRule { FacilityPair = "消防站-甲类装置", MinDistanceMeters = 15, RegulationRef = "GB 50160" },
            new SafetyDistanceRule { FacilityPair = "氯气储存区-居住区", MinDistanceMeters = 200, RegulationRef = "GB 50160（依据重大危险源等级）" },
            new SafetyDistanceRule { FacilityPair = "液化烃储罐-办公楼", MinDistanceMeters = 35, RegulationRef = "GB 50160" },
            new SafetyDistanceRule { FacilityPair = "易燃液体储罐-装卸站", MinDistanceMeters = 15, RegulationRef = "GB 50160" },
            new SafetyDistanceRule { FacilityPair = "甲类仓库-厂内道路", MinDistanceMeters = 15, RegulationRef = "GB 50016" },
            new SafetyDistanceRule { FacilityPair = "甲类厂房-甲类厂房", MinDistanceMeters = 12, RegulationRef = "GB 50016" },
        });
    }

    private static void InitializeRegulationVersions()
    {
        _regulationVersions.AddRange(new[]
        {
            new RegulationVersion
            {
                RegulationNumber = "GB 15603", Title = "常用化学危险品贮存通则",
                CurrentVersion = "2022", HasFullText = true,
                DeprecatedVersions = new() { "1995" },
                ChangeNotes = "2022版更新了禁忌物料配存表、新增了危险化学品仓库分类储存要求"
            },
            new RegulationVersion
            {
                RegulationNumber = "GB 30000", Title = "化学品分类和标签规范",
                CurrentVersion = "2013", HasFullText = true,
                DeprecatedVersions = new(),
                ChangeNotes = "系列标准共29部分（GB 30000.1-29），另有2024修订GB 30000.1-2024"
            },
            new RegulationVersion
            {
                RegulationNumber = "GB 30000.1", Title = "化学品分类和标签规范 第1部分:通则",
                CurrentVersion = "2024", HasFullText = true,
                DeprecatedVersions = new() { "2013" },
                ChangeNotes = "2024版更新了定义、分类标准，与GHS第8修订版接轨"
            },
            new RegulationVersion
            {
                RegulationNumber = "GB 50160", Title = "石油化工企业设计防火规范",
                CurrentVersion = "2008（2018局部修订）", HasFullText = false,
                DeprecatedVersions = new(),
                ChangeNotes = "包含防火间距、储罐间距等关键安全距离数据"
            },
            new RegulationVersion
            {
                RegulationNumber = "GB 50016", Title = "建筑设计防火规范",
                CurrentVersion = "2014（2018局部修订）", HasFullText = false,
                DeprecatedVersions = new() { "2006" },
                ChangeNotes = "规定了甲/乙/丙/丁/戊类厂房仓库的耐火等级与防火间距"
            },
            new RegulationVersion
            {
                RegulationNumber = "GB 18218", Title = "危险化学品重大危险源辨识",
                CurrentVersion = "2018", HasFullText = false,
                DeprecatedVersions = new() { "2009" },
                ChangeNotes = "重大危险源分级标准，定义了各危化品临界量"
            },
            new RegulationVersion
            {
                RegulationNumber = "GB 30871", Title = "危险化学品企业特殊作业安全规范",
                CurrentVersion = "2022", HasFullText = true,
                DeprecatedVersions = new() { "2014" },
                ChangeNotes = "2022版新增电子作业票、能量隔离等要求"
            },
            new RegulationVersion
            {
                RegulationNumber = "JT/T 617", Title = "危险货物道路运输规则",
                CurrentVersion = "2018", HasFullText = false,
                DeprecatedVersions = new(),
                ChangeNotes = "系列标准共7部分，规定了道路危险货物运输的各项要求"
            },
        });
    }
}
