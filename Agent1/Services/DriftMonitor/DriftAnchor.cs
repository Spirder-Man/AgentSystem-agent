using System;

namespace Agent1.Services.DriftMonitor;

/// <summary>
/// 认知漂移监测·锚点模型（对应 drift_anchors 表，见 db/migrations/004_drift_anchor_baseline.sql）。
/// 锚点 = 项目事实基准：AI 输出中抽出的认知断言，将与锚点比对得出漂移量。
/// </summary>
public class DriftAnchor
{
    /// <summary>主键</summary>
    public long Id { get; set; }

    /// <summary>分域: architecture / port / config / data / constraint</summary>
    public string Domain { get; set; } = "";

    /// <summary>实体类型: component / table / port / path / key / rule / sequence</summary>
    public string EntityType { get; set; } = "";

    /// <summary>实体名（AI 输出中可被断言匹配的键）</summary>
    public string EntityKey { get; set; } = "";

    /// <summary>基准值（敏感锚点只写语义描述，不写真实值）</summary>
    public string CanonicalValue { get; set; } = "";

    /// <summary>敏感锚点的 SHA-256（预留）</summary>
    public string? ValueHash { get; set; }

    /// <summary>严重度: 0=参考 1=重要 2=结构级（错则架构认知崩坏）</summary>
    public int Severity { get; set; } = 1;

    /// <summary>锚点版本（血谱校准后递增，历史版本保留供复算）</summary>
    public int Version { get; set; } = 1;

    /// <summary>出处（文档/代码 + 行号），证据链</summary>
    public string Source { get; set; } = "";

    /// <summary>入库时间</summary>
    public DateTime CreatedAt { get; set; }
}
