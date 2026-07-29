using System;
using System.Collections.Generic;
using System.Linq;
using Agent1.Models;
using Agent1.Services;

namespace Agent1.Tests.Stubs;

/// <summary>
/// IChemicalKnowledgeGraph 内存存根 — 用于 DeterministicRuleEngine 等单元测试。
/// 真实 ChemicalKnowledgeGraph 强依赖 PostgreSQL（LoadFromDatabase），单测中不可用。
/// 种子数据覆盖测试断言所需的物质名匹配（Gate）、危险类别、储存兼容性、安全距离场景。
/// </summary>
public class StubChemicalKnowledgeGraph : IChemicalKnowledgeGraph
{
    private readonly List<ChemicalSubstance> _substances;
    private readonly List<SafetyDistanceRule> _safetyDistances;
    private readonly List<RegulationVersion> _regulations;

    public StubChemicalKnowledgeGraph()
    {
        _substances = new List<ChemicalSubstance>
        {
            new()
            {
                Name = "苯", NameEn = "Benzene", CasNumber = "71-43-2",
                FlashPointC = -11, BoilingPointC = 80.1,
                Aliases = { "纯苯" },
                HazardCategories = { new HazardCategoryRef { Category = "易燃液体，类别2", GbStandard = "GB 30000.7" } }
            },
            new()
            {
                Name = "丙酮", NameEn = "Acetone", CasNumber = "67-64-1",
                FlashPointC = -20, BoilingPointC = 56.5,
                HazardCategories = { new HazardCategoryRef { Category = "易燃液体，类别2", GbStandard = "GB 30000.7" } }
            },
            new()
            {
                Name = "甲醇", NameEn = "Methanol", CasNumber = "67-56-1",
                FlashPointC = 11, BoilingPointC = 64.7,
                Aliases = { "木醇" },
                HazardCategories = { new HazardCategoryRef { Category = "易燃液体，类别2", GbStandard = "GB 30000.7" } }
            },
            new()
            {
                Name = "硝酸", NameEn = "Nitric acid", CasNumber = "7697-37-2",
                HazardCategories = { new HazardCategoryRef { Category = "氧化性液体，类别3", GbStandard = "GB 30000.16" } }
            },
            new()
            {
                Name = "硫酸", NameEn = "Sulfuric acid", CasNumber = "7664-93-9",
                HazardCategories = { new HazardCategoryRef { Category = "皮肤腐蚀，类别1A", GbStandard = "GB 30000.19" } }
            },
            new()
            {
                Name = "氢氧化钠", NameEn = "Sodium hydroxide", CasNumber = "1310-73-2",
                Aliases = { "烧碱" },
                HazardCategories = { new HazardCategoryRef { Category = "皮肤腐蚀，类别1A", GbStandard = "GB 30000.19" } }
            },
            new()
            {
                Name = "液氨", NameEn = "Ammonia", CasNumber = "7664-41-7",
                HazardCategories = { new HazardCategoryRef { Category = "易燃气体，类别2", GbStandard = "GB 30000.2" } }
            },
            new()
            {
                Name = "液氯", NameEn = "Chlorine", CasNumber = "7782-50-5",
                HazardCategories = { new HazardCategoryRef { Category = "氧化性气体，类别1", GbStandard = "GB 30000.4" } }
            },
        };

        // [#3 FIX] 与 SQLite 种子/PG migration 002 同步，覆盖评测集 E001-E008 全部设施对
        _safetyDistances = new List<SafetyDistanceRule>
        {
            new() { FacilityPair = "甲类仓库-明火作业点", MinDistanceMeters = 30, RegulationRef = "GB 50016-2014" },
            new() { FacilityPair = "甲类仓库-民用建筑", MinDistanceMeters = 25, RegulationRef = "GB 50016-2014" },
            new() { FacilityPair = "甲类仓库-明火点", MinDistanceMeters = 30, RegulationRef = "GB 50160" },
            new() { FacilityPair = "乙炔气柜-办公楼", MinDistanceMeters = 25, RegulationRef = "GB 50160" },
            new() { FacilityPair = "液化烃储罐-厂区围墙", MinDistanceMeters = 35, RegulationRef = "GB 50160" },
            new() { FacilityPair = "氢气长管拖车-明火点", MinDistanceMeters = 25, RegulationRef = "GB 50160" },
            new() { FacilityPair = "消防站-甲类装置", MinDistanceMeters = 15, RegulationRef = "GB 50160" },
            new() { FacilityPair = "氨罐-厂外道路", MinDistanceMeters = 20, RegulationRef = "GB 50160" },
            new() { FacilityPair = "甲类工艺装置-重要设施", MinDistanceMeters = 30, RegulationRef = "GB 50160" },
            new() { FacilityPair = "氯气储存区-居住区", MinDistanceMeters = 200, RegulationRef = "GB 50160" },
        };

        _regulations = new List<RegulationVersion>
        {
            new() { RegulationNumber = "GB 15603", Title = "危险化学品仓库储存通则", CurrentVersion = "2022", HasFullText = true },
            new() { RegulationNumber = "GB 50016", Title = "建筑设计防火规范", CurrentVersion = "2014", HasFullText = false },
        };
    }

    public ChemicalSubstance? Lookup(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return _substances.FirstOrDefault(s =>
            string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase) ||
            s.Aliases.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase)));
    }

    public ChemicalSubstance? LookupByCas(string casNumber)
        => _substances.FirstOrDefault(s => s.CasNumber == casNumber);

    public List<ChemicalSubstance> Search(string keyword, int maxResults = 5)
        => _substances.Where(s => s.Name.Contains(keyword) || s.Aliases.Any(a => a.Contains(keyword)))
                      .Take(maxResults).ToList();

    public IReadOnlyList<ChemicalSubstance> GetAll() => _substances;

    public int Count => _substances.Count;

    public StorageIncompatibilityRule? CheckCompatibility(string substanceA, string substanceB)
    {
        // 硬编码禁忌对由 DeterministicRuleEngine.StorageCompatibilityRules 优先命中，
        // 存根图侧不再重复维护，未知配对返回 null（走 LLM 兜底路径）。
        return null;
    }

    public SafetyDistanceRule? GetSafetyDistance(string facilityPair)
        => _safetyDistances.FirstOrDefault(d =>
            string.Equals(d.FacilityPair, facilityPair, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<SafetyDistanceRule> GetAllSafetyDistances() => _safetyDistances;

    public RegulationVersion? GetRegulationVersion(string number)
        => _regulations.FirstOrDefault(r => r.RegulationNumber == number);

    public IReadOnlyList<RegulationVersion> GetAllRegulationVersions() => _regulations;

    public void AddSubstance(ChemicalSubstance substance) => _substances.Add(substance);

    public void AddAlias(string substanceName, string alias)
        => Lookup(substanceName)?.Aliases.Add(alias);

    public void EnsureInitialized() { }
}
