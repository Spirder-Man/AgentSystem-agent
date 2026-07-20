using System.Collections.Generic;
using Agent1.Models;
using Agent1.Services;
using FluentAssertions;
using Xunit;
using static Agent1.Services.OutputValidator;

namespace Agent1.Tests;

/// <summary>
/// OutputValidator 单元测试 — 化工合规输出校验器，零失误架构安全组件。
/// 覆盖: 法规编号幻觉检测、数值一致性校验、置信度定级、引用锁定、拒绝模板。
/// </summary>
public class OutputValidatorTests
{
    // ═══════════════════════════════════════════════════════
    // Validate: 空输入
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void Validate_EmptyOutput_ShouldReturnUnknown()
    {
        var result = OutputValidator.Validate("", null, null);

        result.Confidence.Should().Be(ConfidenceLevel.UNKNOWN);
        result.SanitizedOutput.Should().Contain("无法给出确定结论");
    }

    [Fact]
    public void Validate_NullOutput_ShouldReturnUnknown()
    {
        var result = OutputValidator.Validate(null!, null, null);

        result.Confidence.Should().Be(ConfidenceLevel.UNKNOWN);
    }

    [Fact]
    public void Validate_WhitespaceOutput_ShouldReturnUnknown()
    {
        var result = OutputValidator.Validate("   ", null, null);

        result.Confidence.Should().Be(ConfidenceLevel.UNKNOWN);
    }

    // ═══════════════════════════════════════════════════════
    // Validate: 合规输出（无幻觉）
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void Validate_RegulationInAllowedList_ShouldNotFlagHallucination()
    {
        var llmOutput = "依据 GB 15603-2022 §4.2.2，苯和丙酮不得同库储存。";
        var toolOutput = "[REGULATIONS: GB 15603-2022]\n判定: is_compliant=false";

        var result = OutputValidator.Validate(llmOutput, toolOutput, QualityLevel.DATABASE_HIT);

        result.HasHallucination.Should().BeFalse();
        result.Confidence.Should().Be(ConfidenceLevel.HIGH_CONFIDENCE);
    }

    [Fact]
    public void Validate_NoToolOutput_NoAllowedRegs_ShouldNotFlag()
    {
        // 无工具输出时，allowedRegs 为空 → IsRegulationAllowed 返回 true
        var llmOutput = "依据 GB 15603-2022，合规。";

        var result = OutputValidator.Validate(llmOutput, null, null);

        result.HasHallucination.Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════
    // Validate: 幻觉检测
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void Validate_HallucinatedRegulation_ShouldDetect()
    {
        var llmOutput = "依据 GB 88888-2022 第5条规定，该化学品安全。";
        var toolOutput = "[REGULATIONS: GB 15603-2022]";

        var result = OutputValidator.Validate(llmOutput, toolOutput, QualityLevel.DATABASE_HIT);

        result.HasHallucination.Should().BeTrue();
        result.HallucinatedRegulations.Should().Contain(r => r.Contains("88888"));
        result.Confidence.Should().Be(ConfidenceLevel.LOW_CONFIDENCE);
    }

    [Fact]
    public void Validate_MultipleHallucinatedRegulations_ShouldDetectAll()
    {
        var llmOutput = "依据 GB 88888-2022 和 GB 99999-2023。参考 GB 15603-2022。";
        var toolOutput = "[REGULATIONS: GB 15603-2022]";

        var result = OutputValidator.Validate(llmOutput, toolOutput, QualityLevel.DATABASE_HIT);

        result.HasHallucination.Should().BeTrue();
        result.HallucinatedRegulations.Should().HaveCount(2);
    }

    [Fact]
    public void Validate_Hallucination_ShouldLockCitations()
    {
        var llmOutput = "依据 GB 88888-2022，合规。参考 GB 15603-2022。";
        var toolOutput = "[REGULATIONS: GB 15603-2022]";

        var result = OutputValidator.Validate(llmOutput, toolOutput, QualityLevel.DATABASE_HIT);

        result.SanitizedOutput.Should().Contain("[未验证引用]");
        result.SanitizedOutput.Should().Contain("GB 15603-2022"); // 合规的应保留
    }

    // ═══════════════════════════════════════════════════════
    // Validate: 数值一致性
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void Validate_DistanceMismatch_ShouldDetectContradiction()
    {
        var llmOutput = "甲类仓库安全距离为50米。";
        var toolOutput = "[DISTANCE: 30m]\n[REGULATIONS: GB 50016]";

        var result = OutputValidator.Validate(llmOutput, toolOutput, QualityLevel.DATABASE_HIT);

        result.HasContradiction.Should().BeTrue();
        result.Contradictions.Should().Contain(c => c.Contains("30m") && c.Contains("50"));
    }

    [Fact]
    public void Validate_DistanceWithinTolerance_ShouldNotFlag()
    {
        var llmOutput = "甲类仓库安全距离为31米。";
        var toolOutput = "[DISTANCE: 30m]\n[REGULATIONS: GB 50016]";

        var result = OutputValidator.Validate(llmOutput, toolOutput, QualityLevel.DATABASE_HIT);
        // 31m vs 30m: (31-30)/30 = 3.3% ≤ 5% tolerance
        result.HasContradiction.Should().BeFalse();
    }

    [Fact]
    public void Validate_ThresholdMismatch_ShouldDetectContradiction()
    {
        var llmOutput = "苯的临界量为100吨。";
        var toolOutput = "临界量: 50吨\n[REGULATIONS: GB 18218-2018]";

        var result = OutputValidator.Validate(llmOutput, toolOutput, QualityLevel.DATABASE_HIT);

        result.HasContradiction.Should().BeTrue();
    }

    [Fact]
    public void Validate_NoToolOutput_ShouldNotCheckConsistency()
    {
        var llmOutput = "安全距离约为45米。";

        var result = OutputValidator.Validate(llmOutput, null, null);

        result.HasContradiction.Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════
    // Validate: 置信度定级
    // ═══════════════════════════════════════════════════════

    [Theory]
    [InlineData(QualityLevel.DATABASE_HIT, ConfidenceLevel.HIGH_CONFIDENCE)]
    [InlineData(QualityLevel.RAG_HIT, ConfidenceLevel.MEDIUM_CONFIDENCE)]
    [InlineData(QualityLevel.DICTIONARY_HIT, ConfidenceLevel.LOW_CONFIDENCE)]
    [InlineData(QualityLevel.FALLBACK, ConfidenceLevel.UNKNOWN)]
    public void Validate_QualityBasedConfidence(QualityLevel quality, ConfidenceLevel expected)
    {
        var llmOutput = "依据 GB 15603-2022。";
        var toolOutput = "[REGULATIONS: GB 15603-2022]";

        var result = OutputValidator.Validate(llmOutput, toolOutput, quality);

        result.Confidence.Should().Be(expected);
    }

    [Fact]
    public void Validate_HallucinationOverridesQualityConfidence()
    {
        var llmOutput = "依据 GB 88888。";
        var toolOutput = "[REGULATIONS: GB 15603-2022]";

        var result = OutputValidator.Validate(llmOutput, toolOutput, QualityLevel.DATABASE_HIT);

        // Even with DATABASE_HIT quality, hallucination downgrades to LOW
        result.Confidence.Should().Be(ConfidenceLevel.LOW_CONFIDENCE);
    }

    // ═══════════════════════════════════════════════════════
    // GetRefusalTemplate
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void GetRefusalTemplate_ShouldContainSubstanceName()
    {
        var template = OutputValidator.GetRefusalTemplate("苯并芘", new List<string>());

        template.Should().Contain("苯并芘");
        template.Should().Contain("无法给出确定结论");
        template.Should().Contain("[判定:is_compliant=无法判定]");
    }

    [Fact]
    public void GetRefusalTemplate_WithRegulations_ShouldListThem()
    {
        var regs = new List<string> { "GB 15603-2022", "GB 30000.7-2013" };

        var template = OutputValidator.GetRefusalTemplate("X物质", regs);

        template.Should().Contain("GB 15603-2022");
        template.Should().Contain("GB 30000.7-2013");
    }

    [Fact]
    public void GetRefusalTemplate_EmptyRegulations_ShouldShowNoMatch()
    {
        var template = OutputValidator.GetRefusalTemplate("Y物质", new List<string>());

        template.Should().Contain("未检索到相关法规");
    }

    // ═══════════════════════════════════════════════════════
    // GetConfidenceTag
    // ═══════════════════════════════════════════════════════

    [Theory]
    [InlineData(QualityLevel.DATABASE_HIT, "HIGH")]
    [InlineData(QualityLevel.RAG_HIT, "MEDIUM")]
    [InlineData(QualityLevel.DICTIONARY_HIT, "LOW")]
    [InlineData(null, "UNKNOWN")]
    public void GetConfidenceTag_ShouldMapCorrectly(QualityLevel? quality, string expectedTag)
    {
        var tag = OutputValidator.GetConfidenceTag(quality);
        tag.Should().Contain(expectedTag);
    }

    // ═══════════════════════════════════════════════════════
    // Citation Locking: edge cases
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void Validate_LockCitations_PreservesNonHallucinatedRegs()
    {
        var llmOutput = "依据 GB 15603-2022，合规。另参考 GB 88888。";
        var toolOutput = "[REGULATIONS: GB 15603-2022]";

        var result = OutputValidator.Validate(llmOutput, toolOutput, QualityLevel.DATABASE_HIT);

        // 合规引用应保留
        result.SanitizedOutput.Should().Contain("GB 15603-2022");
        // 幻觉引用应有标注
        result.SanitizedOutput.Should().Contain("未验证引用");
        result.SanitizedOutput.Should().Contain("引用校验");
    }

    [Fact]
    public void Validate_NoHallucination_OriginalEqualsSanitized()
    {
        var llmOutput = "依据 GB 15603-2022，合规。";
        var toolOutput = "[REGULATIONS: GB 15603-2022]";

        var result = OutputValidator.Validate(llmOutput, toolOutput, QualityLevel.DATABASE_HIT);

        result.SanitizedOutput.Should().Be(llmOutput);
    }

    // ═══════════════════════════════════════════════════════
    // Regulation extraction from tool output
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void Validate_ExtractsRegulationsFromToolOutput()
    {
        var llmOutput = "依据 GB 15603-2022 和 GB 50160，合规。";
        var toolOutput = "[REGULATIONS: GB 15603-2022, GB 50160]";

        var result = OutputValidator.Validate(llmOutput, toolOutput, QualityLevel.DATABASE_HIT);

        result.HasHallucination.Should().BeFalse();
        result.Confidence.Should().Be(ConfidenceLevel.HIGH_CONFIDENCE);
    }

    [Fact]
    public void Validate_MultipleRegsInToolOutput_AllAgainstLLM()
    {
        var llmOutput = "依据 GB 15603-2022, GB 30000.7-2013, GB 18218-2018，进行判定。";
        var toolOutput = "[REGULATIONS: GB 15603-2022, GB 30000.7-2013, GB 18218-2018]";

        var result = OutputValidator.Validate(llmOutput, toolOutput, QualityLevel.DATABASE_HIT);

        result.HasHallucination.Should().BeFalse();
    }
}
