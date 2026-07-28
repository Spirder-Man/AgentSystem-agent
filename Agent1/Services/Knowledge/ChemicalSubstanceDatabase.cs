using Agent1.Models;

namespace Agent1.Services;

/// <summary>
/// 化工危化品基础属性数据库 — 门面类
/// 
/// 原为硬编码 50+ 物质的静态类。已重构为 ChemicalKnowledgeGraph 的门面：
/// - 数据持久化在 PostgreSQL 中（db/migrations/002_chemical_knowledge_graph.sql）
/// - 启动时加载到 ChemicalKnowledgeGraph 内存图
/// - 本类所有方法委托给底层图服务，保持调用方兼容性
/// 
/// [Obsolete] 新代码请直接使用 IChemicalKnowledgeGraph (DI 注入)。
/// </summary>
public static class ChemicalSubstanceDatabase
{
    private static IChemicalKnowledgeGraph? _graph;

    /// <summary>由 DI 容器在启动时注入图服务实例</summary>
    public static void SetGraph(IChemicalKnowledgeGraph graph)
    {
        _graph = graph;
        _graph.EnsureInitialized();
    }

    /// <summary>按名称查询化学品属性，支持别名自动还原</summary>
    public static ChemicalSubstance? Lookup(string name)
    {
        return _graph?.Lookup(name);
    }

    /// <summary>按 CAS 号查询</summary>
    public static ChemicalSubstance? LookupByCas(string casNumber)
    {
        return _graph?.LookupByCas(casNumber);
    }

    /// <summary>模糊搜索化学品名称</summary>
    public static List<ChemicalSubstance> Search(string keyword, int maxResults = 5)
    {
        return _graph?.Search(keyword, maxResults) ?? new List<ChemicalSubstance>();
    }

    /// <summary>获取所有化学品列表</summary>
    public static IReadOnlyList<ChemicalSubstance> GetAll() => _graph?.GetAll() ?? new List<ChemicalSubstance>();

    /// <summary>获取化学品总数</summary>
    public static int Count => _graph?.Count ?? 0;

    // ════════════════════════════════════════
    // 储存兼容性查询
    // ════════════════════════════════════════

    /// <summary>查询两种化学品是否兼容</summary>
    public static StorageIncompatibilityRule? CheckCompatibility(string substanceA, string substanceB)
    {
        return _graph?.CheckCompatibility(substanceA, substanceB);
    }

    // ════════════════════════════════════════
    // 安全距离查询
    // ════════════════════════════════════════

    public static SafetyDistanceRule? GetSafetyDistance(string facilityPair)
    {
        return _graph?.GetSafetyDistance(facilityPair);
    }

    public static IReadOnlyList<SafetyDistanceRule> GetAllSafetyDistances() => _graph?.GetAllSafetyDistances() ?? new List<SafetyDistanceRule>();

    // ════════════════════════════════════════
    // 法规版本查询
    // ════════════════════════════════════════

    public static RegulationVersion? GetRegulationVersion(string number)
    {
        return _graph?.GetRegulationVersion(number);
    }

    public static IReadOnlyList<RegulationVersion> GetAllRegulationVersions() => _graph?.GetAllRegulationVersions() ?? new List<RegulationVersion>();
}
