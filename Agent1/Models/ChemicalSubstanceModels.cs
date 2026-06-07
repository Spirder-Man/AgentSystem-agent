namespace Agent1.Models;

/// <summary>
/// 危化品基础属性数据模型 — Task 10: 化工知识库专业覆盖增强
/// 支持 CAS号/UN号/闪点/爆炸极限/危险类别等结构化查询
/// </summary>
public class ChemicalSubstance
{
    /// <summary>化学品中文标准名称</summary>
    public string Name { get; set; } = "";

    /// <summary>化学品英文名称</summary>
    public string NameEn { get; set; } = "";

    /// <summary>CAS 号</summary>
    public string CasNumber { get; set; } = "";

    /// <summary>UN 编号（危险货物运输编号）</summary>
    public string UnNumber { get; set; } = "";

    /// <summary>分子式</summary>
    public string Formula { get; set; } = "";

    /// <summary>闪点 (°C)，null 表示不适用</summary>
    public double? FlashPointC { get; set; }

    /// <summary>沸点 (°C)</summary>
    public double? BoilingPointC { get; set; }

    /// <summary>爆炸下限 (LEL, %体积)</summary>
    public double? ExplosiveLowerLimit { get; set; }

    /// <summary>爆炸上限 (UEL, %体积)</summary>
    public double? ExplosiveUpperLimit { get; set; }

    /// <summary>自燃温度 (°C)</summary>
    public double? AutoIgnitionTempC { get; set; }

    /// <summary>相对密度 (水=1)</summary>
    public double? RelativeDensity { get; set; }

    /// <summary>蒸气相对密度 (空气=1)</summary>
    public double? VaporDensity { get; set; }

    /// <summary>危险类别列表（GB 30000 系列）</summary>
    public List<HazardCategoryRef> HazardCategories { get; set; } = new();

    /// <summary>储存禁忌类别列表</summary>
    public List<string> IncompatibleWith { get; set; } = new();

    /// <summary>别名（非标准名称列表）</summary>
    public List<string> Aliases { get; set; } = new();

    /// <summary>GB 18218 重大危险源临界量 (吨)，0 表示不适用</summary>
    public double MajorHazardThresholdTons { get; set; }

    /// <summary>理化状态: 气体/液体/固体</summary>
    public string PhysicalState { get; set; } = "";
}

/// <summary>
/// 危险类别引用 — 将类别名与对应 GB 标准号绑定
/// </summary>
public class HazardCategoryRef
{
    /// <summary>危险类别中文名</summary>
    public string Category { get; set; } = "";

    /// <summary>适用 GB 标准号</summary>
    public string GbStandard { get; set; } = "";

    /// <summary>子类别或备注</summary>
    public string? SubCategory { get; set; }
}

/// <summary>
/// 法规版本追踪条目 — Task 10f
/// </summary>
public class RegulationVersion
{
    /// <summary>法规编号 (如 GB 15603)</summary>
    public string RegulationNumber { get; set; } = "";

    /// <summary>法规中文名称</summary>
    public string Title { get; set; } = "";

    /// <summary>当前有效版本年份</summary>
    public string CurrentVersion { get; set; } = "";

    /// <summary>是否已收录知识库全文</summary>
    public bool HasFullText { get; set; }

    /// <summary>旧版本年份（已废止）</summary>
    public List<string> DeprecatedVersions { get; set; } = new();

    /// <summary>关键变更说明</summary>
    public string? ChangeNotes { get; set; }
}

/// <summary>
/// 储存禁忌规则 — Task 10e 扩展
/// </summary>
public class StorageIncompatibilityRule
{
    /// <summary>化学品A名称</summary>
    public string SubstanceA { get; set; } = "";

    /// <summary>化学品B名称</summary>
    public string SubstanceB { get; set; } = "";

    /// <summary>是否兼容</summary>
    public bool IsCompatible { get; set; }

    /// <summary>不兼容原因说明</summary>
    public string? Reason { get; set; }

    /// <summary>依据法规编号</summary>
    public string? RegulationRef { get; set; }
}

/// <summary>
/// 安全距离规则 — Task 10e 扩展
/// </summary>
public class SafetyDistanceRule
{
    /// <summary>设施类型对</summary>
    public string FacilityPair { get; set; } = "";

    /// <summary>最小安全距离 (米)</summary>
    public double MinDistanceMeters { get; set; }

    /// <summary>依据法规编号</summary>
    public string? RegulationRef { get; set; }
}
