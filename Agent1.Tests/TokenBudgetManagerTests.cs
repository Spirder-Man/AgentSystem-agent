// ============================================================
// TokenBudgetManager 纯逻辑测试 — Phase 4 覆盖率冲刺
//
// 测试范围：所有 public 方法（全纯逻辑，无外部依赖）
//   - EstimateTokens — 中英文 Token 估算
//   - EstimateToolTokens — 工具定义 Token 估算
//   - EstimateFullPromptTokens — 完整 Prompt Token 估算
//   - WouldExceedBudget — 预算溢出检查
//   - TrimPrompt — Prompt 按优先级裁剪
//   - GenerateBudgetReport — Token 预算诊断报告
//   - 属性: MaxBudget, SafetyMargin, EffectiveBudget
// ============================================================

using System.Collections.Generic;
using Agent1.Services.AI;
using FluentAssertions;
using Xunit;

namespace Agent1.Tests;

public class TokenBudgetManagerTests
{
    private readonly TokenBudgetManager _manager = new();

    // ═══════════════════════════════
    // EstimateTokens
    // ═══════════════════════════════

    [Fact]
    public void EstimateTokens_NullOrEmpty_ShouldReturnZero()
    {
        _manager.EstimateTokens(null).Should().Be(0);
        _manager.EstimateTokens("").Should().Be(0);
    }

    [Fact]
    public void EstimateTokens_PureChinese_ShouldEstimateCorrectly()
    {
        // 15个中文字符 ÷ 1.5 = 10 tokens
        var result = _manager.EstimateTokens("这是十五个中文字符用于测试估算");
        result.Should().BeGreaterThan(5).And.BeLessThan(15);
    }

    [Fact]
    public void EstimateTokens_PureEnglish_ShouldEstimateCorrectly()
    {
        // "HelloWorld" = 10 chars ÷ 3.5 ≈ 2-3 tokens
        var result = _manager.EstimateTokens("HelloWorld");
        result.Should().BeGreaterThan(0).And.BeLessThan(5);
    }

    [Fact]
    public void EstimateTokens_MixedChineseEnglish_ShouldEstimate()
    {
        var result = _manager.EstimateTokens("苯Benzene的CAS号是71-43-2");
        result.Should().BeGreaterThan(0);
    }

    [Fact]
    public void EstimateTokens_OnlyWhitespace_ShouldReturnZero()
    {
        _manager.EstimateTokens("   \t\n  ").Should().Be(0);
    }

    // ═══════════════════════════════
    // EstimateToolTokens
    // ═══════════════════════════════

    [Fact]
    public void EstimateToolTokens_WithDescription_ShouldIncludeDescriptionTokens()
    {
        var result = _manager.EstimateToolTokens("CheckHazard", "查询危化品危险类别", paramCount: 1);
        // base(50) + desc tokens + 1*150
        result.Should().BeGreaterThan(150);
    }

    [Fact]
    public void EstimateToolTokens_NoDescription_ShouldReturnMinimum()
    {
        var result = _manager.EstimateToolTokens("GetTime", "", paramCount: 0);
        // base(50) + 0 + 0*150 = 50
        result.Should().Be(50);
    }

    [Fact]
    public void EstimateToolTokens_MultipleParams_ShouldAddParamTokens()
    {
        var single = _manager.EstimateToolTokens("Tool", "desc", paramCount: 1);
        var multi = _manager.EstimateToolTokens("Tool", "desc", paramCount: 3);
        // multi should be larger by 2*150 = 300
        (multi - single).Should().BeGreaterThan(200);
    }

    // ═══════════════════════════════
    // EstimateFullPromptTokens
    // ═══════════════════════════════

    [Fact]
    public void EstimateFullPromptTokens_EmptyInputs_ShouldReturnEstimatedResultTokens()
    {
        var tools = new List<(string, string, int)>();
        var result = _manager.EstimateFullPromptTokens("", "", "", tools, expectedToolResultTokens: 500);
        result.Should().Be(500);
    }

    [Fact]
    public void EstimateFullPromptTokens_WithTools_ShouldSumCorrectly()
    {
        var tools = new List<(string, string, int)>
        {
            ("CheckStorageCompatibility", "查询两种危化品是否可以同库储存", 2),
            ("GetCurrentTime", "获取当前时间", 0),
        };
        var result = _manager.EstimateFullPromptTokens(
            "你是化工安全助手", "请回答以下问题", "苯和丙酮可以同库吗", tools);

        result.Should().BeGreaterThan(700); // system + prompt + query + 2 tools + 500
    }

    // ═══════════════════════════════
    // WouldExceedBudget
    // ═══════════════════════════════

    [Fact]
    public void WouldExceedBudget_UnderBudget_ShouldReturnFalse()
    {
        _manager.WouldExceedBudget(100).Should().BeFalse();
    }

    [Fact]
    public void WouldExceedBudget_OverEffectiveBudget_ShouldReturnTrue()
    {
        var overBudget = _manager.EffectiveBudget + 1;
        _manager.WouldExceedBudget(overBudget).Should().BeTrue();
    }

    [Fact]
    public void WouldExceedBudget_ExactlyAtEffectiveBudget_ShouldReturnFalse()
    {
        _manager.WouldExceedBudget(_manager.EffectiveBudget).Should().BeFalse();
    }

    // ═══════════════════════════════
    // TrimPrompt
    // ═══════════════════════════════

    [Fact]
    public void TrimPrompt_UnderBudget_ShouldReturnOriginal()
    {
        var prompt = "简短提示";
        var result = _manager.TrimPrompt(prompt, targetTokens: 1000);
        result.Should().Be(prompt);
    }

    [Fact]
    public void TrimPrompt_OverBudget_ShouldRemoveOutputFormatSection()
    {
        var prompt = "System role text.\n【输出格式】请按JSON格式输出\n【反幻觉指令】不要虚构法规\n核心指令: 查询化学品";
        var result = _manager.TrimPrompt(prompt, targetTokens: 5);
        result.Should().NotContain("【输出格式】");
    }

    [Fact]
    public void TrimPrompt_OverBudget_ShouldRemoveAntiHallucinationSection()
    {
        var prompt = "System text.\n【反幻觉指令】不要虚构任何法规\n核心指令: 查询";
        var result = _manager.TrimPrompt(prompt, targetTokens: 3);
        result.Should().NotContain("【反幻觉指令】");
    }

    [Fact]
    public void TrimPrompt_AllSectionsTrimmed_ShouldStillReturnContent()
    {
        // 核心内容前置（如 system role），后面是可裁减的指令段落
        // TrimPrompt 移除【输出格式】后保留前置内容
        var prompt = "核心内容在这里\n【输出格式】JSON格式输出要求";
        var result = _manager.TrimPrompt(prompt, targetTokens: 1);
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("核心内容在这里");
        result.Should().NotContain("【输出格式】");
    }

    // ═══════════════════════════════
    // GenerateBudgetReport
    // ═══════════════════════════════

    [Fact]
    public void GenerateBudgetReport_Safe_ShouldIndicateSafe()
    {
        var tools = new List<(string, string, int)>();
        var report = _manager.GenerateBudgetReport("助手", "回答", "你好", tools);

        report.Should().Contain("Token预算");
        report.Should().Contain("✅ 安全");
    }

    [Fact]
    public void GenerateBudgetReport_OverBudget_ShouldIndicateWarning()
    {
        // 构造超预算场景：大量工具
        var tools = new List<(string, string, int)>();
        for (int i = 0; i < 200; i++)
            tools.Add(($"Tool{i}", "这是一个很长的工具描述用于消耗大量token预算", 5));

        var report = _manager.GenerateBudgetReport(
            new string('中', 10000), new string('文', 10000), new string('测', 10000), tools);

        report.Should().Contain("⚠️ 超预算");
    }

    // ═══════════════════════════════
    // Properties
    // ═══════════════════════════════

    [Fact]
    public void DefaultMaxBudget_ShouldBeReasonable()
    {
        _manager.MaxBudget.Should().Be(32768);
    }

    [Fact]
    public void DefaultSafetyMargin_ShouldBeEightyPercent()
    {
        _manager.SafetyMargin.Should().Be(0.8);
    }

    [Fact]
    public void EffectiveBudget_ShouldBeMaxBudgetTimesSafetyMargin()
    {
        _manager.EffectiveBudget.Should().Be((int)(32768 * 0.8));
    }

    // ═══════════════════════════════
    // Static Tool Definitions
    // ═══════════════════════════════

    [Fact]
    public void InfoQueryTools_ShouldContainExpectedTools()
    {
        TokenBudgetManager.InfoQueryTools.Should().NotBeEmpty();
        TokenBudgetManager.InfoQueryTools.Should().Contain(t => t.Name == "CheckHazardCategory");
        TokenBudgetManager.InfoQueryTools.Should().Contain(t => t.Name == "GetCurrentTime");
    }

    [Fact]
    public void ComplianceJudgeTools_ShouldBeSupersetOfInfoQueryTools()
    {
        TokenBudgetManager.ComplianceJudgeTools.Length.Should()
            .BeGreaterThan(TokenBudgetManager.InfoQueryTools.Length);
        TokenBudgetManager.ComplianceJudgeTools.Should()
            .Contain(t => t.Name == "CheckStorageCompatibility");
    }

    [Fact]
    public void AllTools_ShouldBeLargestSet()
    {
        TokenBudgetManager.AllTools.Length.Should()
            .BeGreaterOrEqualTo(TokenBudgetManager.ComplianceJudgeTools.Length);
    }
}
