using System;
using System.Collections.Generic;
using System.Linq;

namespace Agent1.Services.DriftMonitor;

/// <summary>
/// 认知漂移监测·断言抽取器 —— 从 AI 输出文本中提取"认知断言"。
///
/// 锚点驱动策略：以 drift_anchors 的 EntityKey 为字典——
/// 文本提到某个锚点实体（如"API监听端口"），就产生一条该实体的断言。
/// 只抽取可证伪锚点（含强标记），避免无法验证的提及污染测量。
/// </summary>
public class DriftClaimExtractor
{
    /// <summary>
    /// 抽取断言：遍历锚点，EntityKey 在文本中出现（规范化后包含匹配）即产生断言。
    /// </summary>
    public List<DriftClaim> Extract(string text, List<DriftAnchor> anchors)
    {
        var claims = new List<DriftClaim>();
        if (string.IsNullOrWhiteSpace(text) || anchors == null || anchors.Count == 0)
            return claims;

        var normalizedText = DriftMatcher.Normalize(text);
        foreach (var anchor in anchors)
        {
            // 不可证伪的锚点不产生断言（测量仪只测能测的）
            if (!DriftMatcher.IsFalsifiable(anchor))
                continue;

            var normalizedKey = DriftMatcher.Normalize(anchor.EntityKey);
            if (normalizedKey.Length == 0)
                continue;

            if (normalizedText.Contains(normalizedKey, System.StringComparison.Ordinal))
            {
                var mentioned = DriftMatcher.ExtractMentionedTokens(text, anchor.CanonicalValue);
                claims.Add(new DriftClaim
                {
                    Anchor = anchor,
                    MentionedTokens = mentioned
                });
            }
        }
        return claims;
    }

    /// <summary>
    /// 探针断言构建（Phase 3）：黄金问题回答 → 断言集。纯函数，可单测。
    ///
    /// 与 Extract 的本质区别：
    ///   · 强制断言——无论回答是否提及锚点键名，都生成期望锚点的一条断言。
    ///     这是区分"答错"与"未作答"的关键：未作答 = 零强标记命中 = 全漂移，
    ///     不能像被动抽取那样静默跳过（跳过等于把盲区当满分）。
    ///   · 全量去重——回答中顺带提到的其他锚点照常抽取，但排除与强制断言
    ///     重复的键名，避免同一条锚点被计两次。
    /// </summary>
    public List<DriftClaim> BuildProbeClaims(
        string answerText, DriftAnchor? targetAnchor, List<DriftAnchor>? allAnchors)
    {
        var claims = new List<DriftClaim>();

        // 1. 强制断言期望锚点（targetAnchor 解析失败时降级为纯抽取，不阻断测量）
        if (targetAnchor != null)
        {
            var mentioned = DriftMatcher.ExtractMentionedTokens(answerText, targetAnchor.CanonicalValue);
            claims.Add(new DriftClaim { Anchor = targetAnchor, MentionedTokens = mentioned });
        }

        // 2. 全量抽取去重：回答中其他提及的锚点照常产生断言
        if (allAnchors != null && allAnchors.Count > 0)
        {
            var coveredKeys = new HashSet<string>(
                claims.Select(c => c.Anchor.EntityKey), StringComparer.OrdinalIgnoreCase);
            foreach (var claim in Extract(answerText, allAnchors))
            {
                if (coveredKeys.Add(claim.Anchor.EntityKey))
                    claims.Add(claim);
            }
        }
        return claims;
    }
}

/// <summary>一条认知断言：某个锚点实体被 AI 文本谈论 + 文本中实际出现的强标记</summary>
public class DriftClaim
{
    /// <summary>被谈论的锚点（含基准值/严重度/分域）</summary>
    public DriftAnchor Anchor { get; set; } = new();

    /// <summary>文本中实际出现的强标记（如端口/路径/配置键）</summary>
    public List<string> MentionedTokens { get; set; } = new();
}
