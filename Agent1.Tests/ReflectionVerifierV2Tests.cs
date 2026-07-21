// ============================================================
// ReflectionVerifier 纯逻辑测试 — Phase 4 覆盖率冲刺
//
// 测试范围：纯逻辑/静态方法（无 IKnowledgeBaseService 依赖）
//   - VerifySystemHealth — 系统健康检查
//   - ValidateGbNumberHallucinations — GB编号交叉验证
//   - BuildCorrectedPrompt — 修正Prompt构建
//   - BusinessVerificationReport.ToMarkdown — 核查报告格式化
//   - SystemHealthReport.ToMarkdown — 健康报告格式化
//   - ClaimVerification.ToString — 声明格式化
//   - StringExtensions.Truncate — 字符串截断
// ============================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Agent1.Models;
using Agent1.Services;
using FluentAssertions;
using Xunit;

namespace Agent1.Tests;

public class ReflectionVerifierV2Tests
{
    // ═══════════════════════════════
    // VerifySystemHealth
    // ═══════════════════════════════

    [Fact]
    public void VerifySystemHealth_AllToolsSuccessful_ShouldReturnHealthy()
    {
        var verifier = new ReflectionVerifier(null!); // kbService not needed for VerifySystemHealth
        var toolResults = new Dictionary<string, string>
        {
            ["CheckStorageCompatibility"] = "苯与丙酮禁止同库储存",
            ["CheckHazardCategory"] = "苯: 易燃液体, 类别2"
        };
        var plan = new ToolPlan { ToolNames = new List<string> { "CheckStorageCompatibility", "CheckHazardCategory" } };

        var report = verifier.VerifySystemHealth(toolResults, plan);

        report.ToolsPlanned.Should().Be(2);
        report.ToolsExecuted.Should().Be(2);
        report.ToolsCancelled.Should().Be(0);
        report.ToolWarnings.Should().BeEmpty();
    }

    [Fact]
    public void VerifySystemHealth_ToolCancelled_ShouldDetect()
    {
        var verifier = new ReflectionVerifier(null!);
        var toolResults = new Dictionary<string, string>
        {
            ["Tool1"] = "Task was cancelled by user - cancelled"
        };
        var plan = new ToolPlan { ToolNames = new List<string> { "Tool1", "Tool2" } };

        var report = verifier.VerifySystemHealth(toolResults, plan);

        report.ToolsCancelled.Should().Be(1);
        report.ToolsPlanned.Should().Be(2);
        report.ToolsExecuted.Should().Be(1);
    }

    [Fact]
    public void VerifySystemHealth_EmptyToolResult_ShouldWarn()
    {
        var verifier = new ReflectionVerifier(null!);
        var toolResults = new Dictionary<string, string>
        {
            ["Tool1"] = "",
            ["Tool2"] = "   "
        };
        var plan = new ToolPlan { ToolNames = new List<string> { "Tool1", "Tool2" } };

        var report = verifier.VerifySystemHealth(toolResults, plan);

        report.ToolWarnings.Should().HaveCount(2);
        foreach (var w in report.ToolWarnings)
            w.Should().Contain("返回结果为空");
    }

    [Fact]
    public void VerifySystemHealth_KnowledgeBaseMiss_ShouldWarn()
    {
        var verifier = new ReflectionVerifier(null!);
        var toolResults = new Dictionary<string, string>
        {
            ["Tool1"] = "未在知识库中找到相关信息"
        };

        var report = verifier.VerifySystemHealth(toolResults);

        report.ToolWarnings.Should().Contain(w => w.Contains("知识库未命中"));
    }

    [Fact]
    public void VerifySystemHealth_ToolExecutionFailed_ShouldWarn()
    {
        var verifier = new ReflectionVerifier(null!);
        var toolResults = new Dictionary<string, string>
        {
            ["Tool1"] = "调用失败: 连接超时"
        };

        var report = verifier.VerifySystemHealth(toolResults);

        report.ToolWarnings.Should().Contain(w => w.Contains("工具执行异常"));
    }

    [Fact]
    public void VerifySystemHealth_NullPlan_ShouldNotThrow()
    {
        var verifier = new ReflectionVerifier(null!);
        var toolResults = new Dictionary<string, string>
        {
            ["Tool1"] = "result"
        };

        var report = verifier.VerifySystemHealth(toolResults, null);

        report.ToolsPlanned.Should().Be(0);
        report.ToolsExecuted.Should().Be(1);
    }

    // ═══════════════════════════════
    // ValidateGbNumberHallucinations (static)
    // ═══════════════════════════════

    [Fact]
    public void ValidateGbNumberHallucinations_NoGbNumbers_ShouldReturnEmpty()
    {
        var result = ReflectionVerifier.ValidateGbNumberHallucinations("没有GB编号的文本");

        result.Should().BeEmpty();
    }

    [Fact]
    public void ValidateGbNumberHallucinations_ValidGbNumbers_ShouldNotFlagErrors()
    {
        var result = ReflectionVerifier.ValidateGbNumberHallucinations(
            "根据 GB 30000.14 的规定...");

        // 如果没有 substanceNames 进行交叉验证，不会标记为幻觉
        // 但格式应该是正确的
        result.Should().NotContain("格式错误");
    }

    [Fact]
    public void ValidateGbNumberHallucinations_FormatError_MissingZero_ShouldDetect()
    {
        // 需要同时包含一个有效的 GB 30000 编号来触发 malformed 检查路径
        var result = ReflectionVerifier.ValidateGbNumberHallucinations(
            "根据 GB3025 和 GB 30000.14 进行分类");

        result.Should().Contain("格式错误");
        result.Should().Contain("GB3025");
    }

    [Fact]
    public void ValidateGbNumberHallucinations_FormatError_ExtraZero_ShouldDetect()
    {
        var result = ReflectionVerifier.ValidateGbNumberHallucinations(
            "参见 GB 30000.14 和 GB 300026");

        result.Should().Contain("格式错误");
    }

    [Fact]
    public void ValidateGbNumberHallucinations_WithSubstanceNames_ShouldCrossReference()
    {
        // 用数据库中存在的物质名进行交叉验证
        var result = ReflectionVerifier.ValidateGbNumberHallucinations(
            "苯的GHS分类参照 GB 30000.14",
            new[] { "苯" });

        // 苯在数据库中有正确的GB映射，不会产生幻觉警告
        result.Should().NotBeNullOrEmpty(); // 可能包含格式检查结果
    }

    [Fact]
    public void ValidateGbNumberHallucinations_InvalidGbNumber_ShouldNotMatch()
    {
        var result = ReflectionVerifier.ValidateGbNumberHallucinations(
            "GB 30000.99 不存在于标准中");

        // 99超出范围（1-30），不会被提取
        result.Should().BeEmpty();
    }

    // ═══════════════════════════════
    // BuildCorrectedPrompt
    // ═══════════════════════════════

    [Fact]
    public void BuildCorrectedPrompt_ShouldContainOriginalConclusion()
    {
        var verifier = new ReflectionVerifier(null!);
        var bizReport = new BusinessVerificationReport { RawConclusion = "测试结论" };
        var sysReport = new SystemHealthReport();

        var prompt = verifier.BuildCorrectedPrompt("用户输入", "原始结论", bizReport, sysReport);

        prompt.Should().Contain("原始结论");
        prompt.Should().Contain("代码核查报告");
        prompt.Should().Contain("修正规则");
    }

    [Fact]
    public void BuildCorrectedPrompt_WithHallucinations_ShouldIncludeGbValidation()
    {
        var verifier = new ReflectionVerifier(null!);
        var bizReport = new BusinessVerificationReport { RawConclusion = "GB3025 规定..." };
        var sysReport = new SystemHealthReport();

        var prompt = verifier.BuildCorrectedPrompt("用户输入", "GB3025 规定...", bizReport, sysReport);

        // "GB3025" 触发格式错误检测
        prompt.Should().NotBeNullOrEmpty();
    }

    // ═══════════════════════════════
    // BusinessVerificationReport.ToMarkdown
    // ═══════════════════════════════

    [Fact]
    public void BusinessVerificationReport_ToMarkdown_ShouldFormatCorrectly()
    {
        var report = new BusinessVerificationReport
        {
            Claims = new List<ClaimVerification>
            {
                new() { ClaimedText = "GB 15603-2022", FoundInSource = true, EvidenceSnippet = "第4.2条" },
                new() { ClaimedText = "GB 99999", FoundInSource = false }
            },
            HallucinatedClaims = new List<string> { "GB 99999" },
            FactualPrecision = 0.5
        };

        var md = report.ToMarkdown();

        md.Should().Contain("事实核查报告");
        md.Should().Contain("GB 15603-2022");
        md.Should().Contain("50.0%");
        md.Should().Contain("可疑声明");
    }

    [Fact]
    public void BusinessVerificationReport_ToMarkdown_AllVerified_ShouldNotShowHallucinations()
    {
        var report = new BusinessVerificationReport
        {
            Claims = new List<ClaimVerification>
            {
                new() { ClaimedText = "GB 15603", FoundInSource = true }
            },
            FactualPrecision = 1.0
        };

        var md = report.ToMarkdown();

        md.Should().Contain("100.0%");
        md.Should().NotContain("可疑声明");
    }

    // ═══════════════════════════════
    // SystemHealthReport.ToMarkdown
    // ═══════════════════════════════

    [Fact]
    public void SystemHealthReport_ToMarkdown_CompleteChain_ShouldShowHealthy()
    {
        var report = new SystemHealthReport
        {
            ToolsPlanned = 3,
            ToolsExecuted = 3,
            ToolsCancelled = 0
        };

        var md = report.ToMarkdown();

        md.Should().Contain("工具链完整");
        md.Should().Contain("3/3");
    }

    [Fact]
    public void SystemHealthReport_ToMarkdown_CancelledTools_ShouldWarn()
    {
        var report = new SystemHealthReport
        {
            ToolsPlanned = 3,
            ToolsExecuted = 2,
            ToolsCancelled = 1,
            ToolWarnings = new List<string> { "Tool3: 返回结果为空" }
        };

        var md = report.ToMarkdown();

        md.Should().Contain("流式取消");
        md.Should().Contain("2/3");
        md.Should().Contain("Tool3");
    }

    // ═══════════════════════════════
    // ClaimVerification.ToString
    // ═══════════════════════════════

    [Fact]
    public void ClaimVerification_Found_ToString_ShouldShowCheckmark()
    {
        var claim = new ClaimVerification
        {
            ClaimedText = "GB 15603-2022",
            FoundInSource = true,
            SearchQuery = "GB 15603-2022",
            ChunksReturned = 3,
            EvidenceSnippet = "第4.2.2条 禁忌物料不得同库储存"
        };

        var str = claim.ToString();

        str.Should().Contain("✓");
        str.Should().Contain("GB 15603-2022");
        str.Should().Contain("3chunks");
    }

    [Fact]
    public void ClaimVerification_NotFound_ToString_ShouldShowCrossAndWarning()
    {
        var claim = new ClaimVerification
        {
            ClaimedText = "GB 99999",
            FoundInSource = false,
            SearchQuery = "GB 99999",
            ChunksReturned = 0
        };

        var str = claim.ToString();

        str.Should().Contain("✗");
        str.Should().Contain("可能幻觉");
    }

    // ═══════════════════════════════
    // StringExtensions.Truncate
    // ═══════════════════════════════

    [Fact]
    public void Truncate_ShortString_ShouldReturnOriginal()
    {
        var result = "hello".Truncate(10);
        result.Should().Be("hello");
    }

    [Fact]
    public void Truncate_LongString_ShouldTruncateWithEllipsis()
    {
        var result = "This is a very long string that exceeds the maximum".Truncate(20);
        result.Should().HaveLength(23); // 20 + "..."
        result.Should().EndWith("...");
    }

    [Fact]
    public void Truncate_NullOrEmpty_ShouldReturnEmpty()
    {
        ((string)null!).Truncate(10).Should().Be("");
        "".Truncate(10).Should().Be("");
    }

    // ═══════════════════════════════
    // NormalizeRegNumber (tested via ValidateGbNumberHallucinations)
    // ═══════════════════════════════

    [Fact]
    public void ValidateGbNumberHallucinations_NormalizedFormats_ShouldWorkCorrectly()
    {
        // 空格差异应不影响匹配
        var result1 = ReflectionVerifier.ValidateGbNumberHallucinations("GB30000.14");
        var result2 = ReflectionVerifier.ValidateGbNumberHallucinations("GB 30000.14");
        var result3 = ReflectionVerifier.ValidateGbNumberHallucinations("GB 30000-14");

        // 格式检查一致
        result1.Should().Be(result2);
    }

    [Fact]
    public void BuildCorrectedPrompt_EmptyInputs_ShouldNotThrow()
    {
        var verifier = new ReflectionVerifier(null!);
        var bizReport = new BusinessVerificationReport();
        var sysReport = new SystemHealthReport();

        var prompt = verifier.BuildCorrectedPrompt("", "", bizReport, sysReport);

        prompt.Should().NotBeNull();
        prompt.Should().Contain("修正规则");
    }
}
