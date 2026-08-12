using System.Collections.Generic;
using System.Linq;
using Agent1.Services.DriftMonitor;
using FluentAssertions;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// 认知漂移监测 Phase 3 纯逻辑单元测试（不依赖数据库）：
/// BuildProbeClaims 强制断言 + 全量去重，以及探针完整链路（回答 → 漂移量）。
/// </summary>
public class DriftPhase3ProbeLogicTests
{
    // ═══════════════════════════════════════════
    // BuildProbeClaims — 强制断言 + 去重
    // ═══════════════════════════════════════════

    [Fact]
    public void BuildProbeClaims_Unanswered_StillForcesTargetClaim()
    {
        // 黄金问题"分块存在哪两张表"→ AI 回答完全没提 → 仍必须产生强制断言
        var target = Anchor("动脉A双表", "knowledge_documents + knowledge_chunks", 2, "architecture");
        var claims = new DriftClaimExtractor().BuildProbeClaims("我不知道，让我查一下", target, null);

        claims.Should().ContainSingle();
        claims[0].Anchor.EntityKey.Should().Be("动脉A双表");
        claims[0].MentionedTokens.Should().BeEmpty(); // 未作答 = 零命中
    }

    [Fact]
    public void BuildProbeClaims_CorrectAnswer_ForcedClaimHits()
    {
        var target = Anchor("动脉A双表", "knowledge_documents + knowledge_chunks", 2, "architecture");
        var extractor = new DriftClaimExtractor();

        var claims = extractor.BuildProbeClaims(
            "存在 knowledge_documents 和 knowledge_chunks 两张表", target, null);

        claims.Should().ContainSingle();
        claims[0].MentionedTokens.Should().Contain("knowledge_documents");
        claims[0].MentionedTokens.Should().Contain("knowledge_chunks");
    }

    [Fact]
    public void BuildProbeClaims_ExtraAnchorMentioned_ClaimAddedAndDeduplicated()
    {
        var target = Anchor("动脉A双表", "knowledge_documents + knowledge_chunks", 2, "architecture");
        var other = Anchor("API监听端口", "ASPNETCORE_URLS 控制, 默认 http://0.0.0.0:5000", 2, "port");
        var extractor = new DriftClaimExtractor();

        // 回答提到期望表名 + 额外提到端口锚点键名（键名重复提及应去重）
        var claims = extractor.BuildProbeClaims(
            "表是 knowledge_documents 和 knowledge_chunks；动脉A双表没错。端口是 ASPNETCORE_URLS 控制的 5000（API监听端口）",
            target, new List<DriftAnchor> { target, other });

        // 强制断言 1 + 额外断言 1（键名重复提及不产生第二条）
        claims.Should().HaveCount(2);
        claims.Select(c => c.Anchor.EntityKey).Should().Contain("API监听端口");
        claims.Count(c => c.Anchor.EntityKey == "动脉A双表").Should().Be(1);
    }

    [Fact]
    public void BuildProbeClaims_TargetNotFound_FallsBackToExtractOnly()
    {
        // anchor_key 解析失败（锚点不存在）→ 降级为纯抽取，不阻断测量
        var other = Anchor("API监听端口", "ASPNETCORE_URLS 控制, 默认 http://0.0.0.0:5000", 2, "port");
        var extractor = new DriftClaimExtractor();

        var claims = extractor.BuildProbeClaims(
            "API监听端口是 ASPNETCORE_URLS 控制的 5000", null, new List<DriftAnchor> { other });

        claims.Should().ContainSingle();
        claims[0].Anchor.EntityKey.Should().Be("API监听端口");
    }

    [Fact]
    public void BuildProbeClaims_WrongAnswer_ForcedClaimNoHit()
    {
        // 回答说了错误信息（表名完全不对）→ 强制断言零命中
        var target = Anchor("动脉A双表", "knowledge_documents + knowledge_chunks", 2, "architecture");

        var claims = new DriftClaimExtractor().BuildProbeClaims(
            "存在 audit_logs 和 drift_probes 两张表", target, null);

        claims.Should().ContainSingle();
        claims[0].MentionedTokens.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════
    // 探针完整链路：回答 → 强制断言 → 比对 → 度量
    // ═══════════════════════════════════════════

    [Fact]
    public void ProbePipeline_Unanswered_ScoreIsFullDrift()
    {
        // 关键语义：未作答 ≠ 满分——跳过盲区等于把测量做假
        var target = Anchor("动脉A双表", "knowledge_documents + knowledge_chunks", 2, "architecture");

        var result = Measure(target, "这个问题我不太清楚", null);

        result.ClaimCount.Should().Be(1);   // 强制断言仍在
        result.MatchCount.Should().Be(0);   // 零命中
        result.DriftScore.Should().Be(1.0); // 全漂移
    }

    [Fact]
    public void ProbePipeline_CorrectAnswer_ScoreZero()
    {
        var target = Anchor("动脉A双表", "knowledge_documents + knowledge_chunks", 2, "architecture");

        var result = Measure(target, "存在 knowledge_documents 和 knowledge_chunks 两张表", null);

        result.ClaimCount.Should().Be(1);
        result.MatchCount.Should().Be(1);
        result.DriftScore.Should().Be(0.0);
    }

    [Fact]
    public void ProbePipeline_WrongAnswer_ScoreFullDrift()
    {
        // 答错 = 全漂移；且因为强制断言，答错与未作答都能被测量到
        var target = Anchor("动脉A双表", "knowledge_documents + knowledge_chunks", 2, "architecture");

        var result = Measure(target, "存在 audit_logs 表", null);

        result.ClaimCount.Should().Be(1);
        result.MatchCount.Should().Be(0);
        result.DriftScore.Should().Be(1.0);
    }

    [Fact]
    public void ProbePipeline_PartialAnswer_Score0_5()
    {
        // 只答对一张表名 → 部分漂移
        var target = Anchor("动脉A双表", "knowledge_documents + knowledge_chunks", 2, "architecture");

        var result = Measure(target, "存在 knowledge_documents 表", null);

        result.ClaimCount.Should().Be(1);
        result.MatchCount.Should().Be(0);
        result.DriftScore.Should().Be(0.5);
    }

    [Fact]
    public void ProbePipeline_WeightedByTemplateSeverity()
    {
        // 模板权重 sev=1 覆盖锚点 sev=2 → 单条全漂移 D = 1.0（权重只影响多断言加权）
        var target = Anchor("动脉A双表", "knowledge_documents + knowledge_chunks", 2, "architecture");
        var extractor = new DriftClaimExtractor();

        var claims = extractor.BuildProbeClaims("我不知道", target, null);
        claims[0].Anchor.Severity = 1; // 模拟 RecordProbeAsync 的模板权重覆盖
        var matches = claims
            .Select(c => new ClaimMatch
            {
                Anchor = c.Anchor,
                Score = DriftMatcher.MatchScore("我不知道", c.Anchor.CanonicalValue),
                ActualTokens = c.MentionedTokens
            })
            .ToList();
        var result = new DriftMetricsService().Compute(matches);

        result.DriftScore.Should().Be(1.0);
        result.ClaimCount.Should().Be(1);
    }

    // ═══════════════════════════════════════════
    // 私有
    // ═══════════════════════════════════════════

    /// <summary>走完整探针测量链路（BuildProbeClaims → MatchScore → Compute）</summary>
    private static DriftProbeResult Measure(DriftAnchor? target, string answer, List<DriftAnchor>? allAnchors)
    {
        var extractor = new DriftClaimExtractor();
        var claims = extractor.BuildProbeClaims(answer, target, allAnchors);
        var matches = claims
            .Select(c => new ClaimMatch
            {
                Anchor = c.Anchor,
                Score = DriftMatcher.MatchScore(answer, c.Anchor.CanonicalValue),
                ActualTokens = c.MentionedTokens
            })
            .ToList();
        return new DriftMetricsService().Compute(matches);
    }

    private static DriftAnchor Anchor(string key, string value, int severity, string domain = "port")
        => new() { EntityKey = key, CanonicalValue = value, Severity = severity, Domain = domain };
}
