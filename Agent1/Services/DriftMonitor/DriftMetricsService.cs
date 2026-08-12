using System.Collections.Generic;
using System.Linq;

namespace Agent1.Services.DriftMonitor;

/// <summary>
/// 认知漂移监测·度量服务 —— 漂移量计算（纯函数，可单测）。
///
/// 核心公式（加权漂移率）：
///   D = Σ(severityᵢ × errᵢ) / Σ(severityᵢ)
///   errᵢ = 1 - matchScoreᵢ（0=一致, 0.5=部分漂移, 1=全漂移）
///   结构级锚点（severity=2）权重翻倍——架构认知崩坏比细节记错更严重。
/// </summary>
public class DriftMetricsService
{
    /// <summary>由断言比对结果计算一次测量的漂移量（含分域分解）</summary>
    public DriftProbeResult Compute(List<ClaimMatch> matches)
    {
        var result = new DriftProbeResult();

        if (matches == null || matches.Count == 0)
        {
            result.ClaimCount = 0;
            result.MatchCount = 0;
            result.DriftScore = 0.0;
            result.DomainBreakdown = new List<DomainDrift>();
            return result;
        }

        result.ClaimCount = matches.Count;
        result.MatchCount = matches.Count(m => m.Score >= 1.0);

        // 加权漂移率
        double weightedErr = 0;
        double weightSum = 0;
        foreach (var m in matches)
        {
            var w = m.Anchor.Severity;
            if (w <= 0) w = 1; // 参考级按 1 计，避免零权重掩盖漂移
            weightedErr += w * (1.0 - m.Score);
            weightSum += w;
        }
        result.DriftScore = weightSum > 0 ? weightedErr / weightSum : 0.0;

        // 分域分解
        result.DomainBreakdown = matches
            .GroupBy(m => m.Anchor.Domain)
            .Select(g =>
            {
                var errs = g.Count(m => m.Score < 1.0);
                return new DomainDrift
                {
                    Domain = g.Key,
                    Total = g.Count(),
                    Errs = errs,
                    Score = g.Count() > 0 ? (double)errs / g.Count() : 0.0
                };
            })
            .OrderByDescending(d => d.Score)
            .ToList();

        return result;
    }
}

/// <summary>一条断言的比对结果</summary>
public class ClaimMatch
{
    /// <summary>被谈论的锚点</summary>
    public DriftAnchor Anchor { get; set; } = new();

    /// <summary>匹配分：0（全漂移）/ 0.5（部分）/ 1（一致）</summary>
    public double Score { get; set; }

    /// <summary>文本中实际出现的强标记（明细展示用）</summary>
    public List<string> ActualTokens { get; set; } = new();
}

/// <summary>一次测量的漂移量汇总</summary>
public class DriftProbeResult
{
    /// <summary>抽取断言数</summary>
    public int ClaimCount { get; set; }

    /// <summary>完全匹配断言数</summary>
    public int MatchCount { get; set; }

    /// <summary>加权漂移率 0~1（Σ(sev·err)/Σsev）</summary>
    public double DriftScore { get; set; }

    /// <summary>分域漂移明细（按漂移率降序）</summary>
    public List<DomainDrift> DomainBreakdown { get; set; } = new();
}

/// <summary>分域漂移统计</summary>
public class DomainDrift
{
    /// <summary>分域：architecture / port / config / data / constraint</summary>
    public string Domain { get; set; } = "";

    /// <summary>该分域断言总数</summary>
    public int Total { get; set; }

    /// <summary>漂移断言数（match < 1）</summary>
    public int Errs { get; set; }

    /// <summary>分域漂移率（Errs/Total）</summary>
    public double Score { get; set; }
}
