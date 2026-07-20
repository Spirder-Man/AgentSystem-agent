using System.Collections.Generic;
using Agent1.Models;
using Agent1.Services;
using FluentAssertions;
using Xunit;
using static Agent1.Services.OutputValidator;

namespace Agent1.Tests;

/// <summary>
/// P0-1: OutputValidator 集成测试 — 端到端"事实通道"校验链路。
/// 模拟完整 LLM 输出 + 工具返回，验证:
///   1. 法规编号幻觉检测 (Hallucination Detection)
///   2. 数值一致性校验 (安全距离/临界量)
///   3. 置信度定级 (HIGH/MEDIUM/LOW/UNKNOWN)
///   4. 引用锁定 (LockCitations + 降级声明)
///   5. 标准化拒绝模板 (GetRefusalTemplate)
///   6. 置信度标签映射 (GetConfidenceTag)
/// </summary>
public class OutputValidatorIntegrationTests
{
    // ═══════════════════════════════════════
    // 幻觉检测: LLM 引用了工具未返回的法规
    // ═══════════════════════════════════════

    [Fact]
    public void Validate_LlmUsesUnlistedRegulation_ShouldDetectHallucination()
    {
        var llmOutput = "根据 GB 99999-2099 的规定，苯的安全距离为 50 米。";
        var toolOutput = "[REGULATIONS: GB30000.7-2013, GB50160-2008]";

        var result = OutputValidator.Validate(
            llmOutput, toolOutput, QualityLevel.DATABASE_HIT);

        result.HasHallucination.Should().BeTrue();
        result.HallucinatedRegulations.Should().NotBeEmpty();
        result.HallucinatedRegulations.Should().Contain(r => r.Contains("99999"));
    }

    [Fact]
    public void Validate_LlmUsesOnlyAllowedRegulations_ShouldNotFlagHallucination()
    {
        var llmOutput = "根据 GB 30000.7-2013 的规定，甲类仓库与明火点间距不少于 30 米。";
        var toolOutput = "[REGULATIONS: GB30000.7-2013, GB50160-2008]";

        var result = OutputValidator.Validate(
            llmOutput, toolOutput, QualityLevel.DATABASE_HIT);

        result.HasHallucination.Should().BeFalse();
        result.HallucinatedRegulations.Should().BeEmpty();
    }

    [Fact]
    public void Validate_EmptyAllowedList_ShouldNotFlagHallucination()
    {
        var llmOutput = "根据 GB 99999-2099 的规定...";
        var toolOutput = ""; // 无工具返回 → 无允许列表

        var result = OutputValidator.Validate(
            llmOutput, toolOutput, null);

        // 空允许列表时不拦截（无工具调用的场景）
        result.HasHallucination.Should().BeFalse();
    }

    // ═══════════════════════════════════════
    // 数值一致性: 安全距离矛盾
    // ═══════════════════════════════════════

    [Fact]
    public void Validate_DistanceMismatch_ShouldDetectContradiction()
    {
        var llmOutput = "根据国标，安全距离应为 100 米。";
        var toolOutput = "[DISTANCE: 50m] [REGULATIONS: GB30000.7]";

        var result = OutputValidator.Validate(
            llmOutput, toolOutput, QualityLevel.DATABASE_HIT);

        result.HasContradiction.Should().BeTrue();
        result.Contradictions.Should().NotBeEmpty();
        result.Contradictions.Should().Contain(c => c.Contains("50m") && c.Contains("100m"));
    }

    [Fact]
    public void Validate_DistanceMatchWithinTolerance_ShouldNotDetectContradiction()
    {
        // 5% 容忍度内 (50m * 1.05 = 52.5) → 51m 在容忍范围内
        var llmOutput = "安全距离为 51 米。";
        var toolOutput = "[DISTANCE: 50m] [REGULATIONS: GB30000.7]";

        var result = OutputValidator.Validate(
            llmOutput, toolOutput, QualityLevel.DATABASE_HIT);

        result.HasContradiction.Should().BeFalse();
    }

    // ═══════════════════════════════════════
    // 置信度定级
    // ═══════════════════════════════════════

    [Fact]
    public void Validate_DatabaseHit_NoHallucination_ShouldBeHighConfidence()
    {
        var llmOutput = "根据 GB 30000.7-2013，甲类仓库安全距离为 50 米。";
        var toolOutput = "[DISTANCE: 50m] [REGULATIONS: GB30000.7-2013]";

        var result = OutputValidator.Validate(
            llmOutput, toolOutput, QualityLevel.DATABASE_HIT);

        result.Confidence.Should().Be(ConfidenceLevel.HIGH_CONFIDENCE);
    }

    [Fact]
    public void Validate_RagHit_NoHallucination_ShouldBeMediumConfidence()
    {
        var llmOutput = "根据 GB 30000.7-2013，建议储存间距为 30 米。";
        var toolOutput = "[REGULATIONS: GB30000.7-2013]";

        var result = OutputValidator.Validate(
            llmOutput, toolOutput, QualityLevel.RAG_HIT);

        result.Confidence.Should().Be(ConfidenceLevel.MEDIUM_CONFIDENCE);
    }

    [Fact]
    public void Validate_FallbackQuality_ShouldBeUnknown()
    {
        var llmOutput = "该化学品在 GB 99999-2099 中可能有规定。";
        var toolOutput = "";

        var result = OutputValidator.Validate(
            llmOutput, toolOutput, QualityLevel.FALLBACK);

        result.Confidence.Should().Be(ConfidenceLevel.UNKNOWN);
    }

    [Fact]
    public void Validate_HallucinationDetected_ShouldDowngradeToLowConfidence()
    {
        // 即使数据库命中，出现幻觉也应该降级
        var llmOutput = "根据 GB 99999-2099，苯的安全距离为 50 米。";
        var toolOutput = "[DISTANCE: 50m] [REGULATIONS: GB30000.7-2013]";

        var result = OutputValidator.Validate(
            llmOutput, toolOutput, QualityLevel.DATABASE_HIT);

        result.Confidence.Should().Be(ConfidenceLevel.LOW_CONFIDENCE,
            "出现幻觉/矛盾时应降级为 LOW_CONFIDENCE");
    }

    // ═══════════════════════════════════════
    // 引用锁定
    // ═══════════════════════════════════════

    [Fact]
    public void Validate_Hallucination_ShouldLockCitations()
    {
        var llmOutput = "根据 GB 99999-2099，需要安装防爆装置。";
        var toolOutput = "[REGULATIONS: GB30000.7-2013]";

        var result = OutputValidator.Validate(
            llmOutput, toolOutput, QualityLevel.DATABASE_HIT);

        result.HasHallucination.Should().BeTrue();
        result.SanitizedOutput.Should().Contain("[未验证引用]",
            "未验证的法规引用应被标注");
        result.SanitizedOutput.Should().Contain("引用校验",
            "应追加降级声明");
    }

    // ═══════════════════════════════════════
    // 标准化拒绝模板
    // ═══════════════════════════════════════

    [Fact]
    public void GetRefusalTemplate_ShouldContainAllRequiredSections()
    {
        var template = OutputValidator.GetRefusalTemplate(
            "未知化合物X",
            new List<string> { "GB 18218-2020", "GB 30000.7-2013" });

        template.Should().Contain("无法给出确定结论");
        template.Should().Contain("已检索法规");
        template.Should().Contain("GB 18218-2020");
        template.Should().Contain("未知化合物X");
        template.Should().Contain("is_compliant=无法判定");
        template.Should().Contain("UNKNOWN");
    }

    [Fact]
    public void GetRefusalTemplate_EmptyRegulations_ShouldHandleGracefully()
    {
        var template = OutputValidator.GetRefusalTemplate("物质A", new List<string>());

        template.Should().Contain("未检索到相关法规");
    }

    // ═══════════════════════════════════════
    // 置信度标签
    // ═══════════════════════════════════════

    [Theory]
    [InlineData(QualityLevel.DATABASE_HIT, "HIGH")]
    [InlineData(QualityLevel.RAG_HIT, "MEDIUM")]
    [InlineData(QualityLevel.DICTIONARY_HIT, "LOW")]
    [InlineData(QualityLevel.FALLBACK, "UNKNOWN")]
    [InlineData(QualityLevel.ERROR, "UNKNOWN")]
    public void GetConfidenceTag_ShouldMapCorrectly(QualityLevel quality, string expectedTag)
    {
        var tag = OutputValidator.GetConfidenceTag(quality);

        tag.Should().Contain(expectedTag);
    }

    [Fact]
    public void GetConfidenceTag_NullQuality_ShouldReturnUnknown()
    {
        var tag = OutputValidator.GetConfidenceTag(null);

        tag.Should().Contain("UNKNOWN");
    }

    // ═══════════════════════════════════════
    // 端到端: 完整合规输出校验
    // ═══════════════════════════════════════

    [Fact]
    public void Validate_FullCompliancePipeline_NormalCase()
    {
        // 模拟一次完整的合规查询: 用户问 → 工具返回 → LLM 输出 → 校验
        var llmOutput = @"【合规性判定】
根据 GB 30000.7-2013《危险化学品目录》，苯属于危险化学品。
依据 GB 50160-2008《石油化工企业设计防火规范》，甲类仓库与明火点的安全距离应不少于 50 米。

【结论】: 该储存方案不符合 GB 50160-2008 第 5.2.3 条规定，建议调整安全距离至 50 米以上。";

        var toolOutput = @"[REGULATIONS: GB30000.7-2013, GB50160-2008]
[DISTANCE: 50m]
苯 (Benzene): CAS 71-43-2，危险类别: 易燃液体，UN 编号: 1114";

        var result = OutputValidator.Validate(
            llmOutput, toolOutput, QualityLevel.DATABASE_HIT);

        // 完整性断言
        result.Should().NotBeNull();
        result.OriginalOutput.Should().Be(llmOutput);
        result.SanitizedOutput.Should().NotBeNullOrEmpty();

        // 无幻觉（所有引用的 GB 都在工具返回中）
        result.HasHallucination.Should().BeFalse("所有引用法规都经过了工具确认");
        result.HallucinatedRegulations.Should().BeEmpty();

        // 无矛盾（距离一致）
        result.HasContradiction.Should().BeFalse("LLM 输出的距离与工具返回一致");

        // 高置信度（数据库命中 + 无幻觉 + 数值一致）
        result.Confidence.Should().Be(ConfidenceLevel.HIGH_CONFIDENCE);
    }

    [Fact]
    public void Validate_FullCompliancePipeline_HallucinationCase()
    {
        // LLM 幻觉: 编造了一个法规编号
        var llmOutput = "根据 GB 30871-2022《危险化学品企业特殊作业安全规范》，作业间距应不少于 30 米。";
        var toolOutput = "[REGULATIONS: GB30000.7-2013, GB50160-2008]"; // GB 30871-2022 不在工具返回中

        var result = OutputValidator.Validate(
            llmOutput, toolOutput, QualityLevel.RAG_HIT);

        result.HasHallucination.Should().BeTrue("GB 30871-2022 是 LLM 幻觉编造的");
        result.Confidence.Should().Be(ConfidenceLevel.LOW_CONFIDENCE,
            "幻觉检测应触发置信度降级");
        result.SanitizedOutput.Should().NotBe(llmOutput,
            "输出应被修改（标注未验证引用）");
    }

    [Fact]
    public void Validate_FullCompliancePipeline_TotalUnknownGivesRefusal()
    {
        // 空 LLM 输出 + FALLBACK → 触发拒绝模板
        var result = OutputValidator.Validate("", null, QualityLevel.FALLBACK);

        result.Confidence.Should().Be(ConfidenceLevel.UNKNOWN);
        result.SanitizedOutput.Should().Contain("无法给出确定结论",
            "空输出时应触发标准拒绝模板");
    }
}
