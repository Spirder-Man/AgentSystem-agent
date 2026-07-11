using System;
using System.Collections.Generic;
using Agent1.Models;
using Agent1.Services;
using FluentAssertions;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// EvalEngine 辅助方法测试: CheckParams, CheckConclusion, CheckSafetyDistanceMatch, CheckRegulationMatch。
/// 纯逻辑测试，无需外部依赖。
/// </summary>
public class EvalEngineTests
{
    // ── CheckParams ──

    [Fact]
    public void CheckParams_ExactMatch_ReturnsTrue()
    {
        var args = "substanceName=苯";
        var expected = new Dictionary<string, string> { ["substance"] = "苯" };
        EvalEngine.CheckParams(args, expected).Should().BeTrue();
    }

    [Fact]
    public void CheckParams_PartialMatch_ReturnsTrue()
    {
        var args = "substanceA=爆炸物, substanceB=易燃气体";
        var expected = new Dictionary<string, string> { ["substance_a"] = "爆炸物" };
        EvalEngine.CheckParams(args, expected).Should().BeTrue();
    }

    [Fact]
    public void CheckParams_NoMatch_ReturnsFalse()
    {
        var args = "substanceName=苯";
        var expected = new Dictionary<string, string> { ["substance"] = "甲苯" };
        EvalEngine.CheckParams(args, expected).Should().BeFalse();
    }

    [Fact]
    public void CheckParams_NullArgs_ReturnsFalse()
    {
        EvalEngine.CheckParams(null, new Dictionary<string, string> { ["key"] = "val" })
            .Should().BeFalse();
    }

    [Fact]
    public void CheckParams_EmptyExpected_ReturnsFalse()
    {
        EvalEngine.CheckParams("args", new Dictionary<string, string>())
            .Should().BeFalse();
    }

    // ── CheckRegulationMatch ──

    [Fact]
    public void CheckRegulationMatch_ExactMatch_ReturnsTrue()
    {
        EvalEngine.CheckRegulationMatch("依据 GB 30000.2-2013", "GB 30000.2-2013")
            .Should().BeTrue();
    }

    [Fact]
    public void CheckRegulationMatch_GBTVariant_ReturnsTrue()
    {
        EvalEngine.CheckRegulationMatch("依据 GB/T 30000.2-2013", "GB 30000.2-2013")
            .Should().BeTrue();
    }

    [Fact]
    public void CheckRegulationMatch_NoMatch_ReturnsFalse()
    {
        EvalEngine.CheckRegulationMatch("依据 GB 30000.2-2013", "GB 50016-2014")
            .Should().BeFalse();
    }

    // ── CheckSafetyDistanceMatch ──

    [Fact]
    public void CheckSafetyDistanceMatch_WithinTolerance_ReturnsTrue()
    {
        EvalEngine.CheckSafetyDistanceMatch("安全距离: 30米", 30).Should().BeTrue();
        EvalEngine.CheckSafetyDistanceMatch("安全距离: 31米", 30).Should().BeTrue(); // ±1m 容差
    }

    [Fact]
    public void CheckSafetyDistanceMatch_OutsideTolerance_ReturnsFalse()
    {
        EvalEngine.CheckSafetyDistanceMatch("安全距离: 50米", 30).Should().BeFalse();
    }

    [Fact]
    public void CheckSafetyDistanceMatch_NoNumber_ReturnsFalse()
    {
        EvalEngine.CheckSafetyDistanceMatch("安全距离: 未指定", 30).Should().BeFalse();
    }

    // ── CheckConclusion ──

    [Fact]
    public void CheckConclusion_ComplianceTagMatch_ReturnsTrue()
    {
        var response = "【合规判断】是\n[判定:is_compliant=true]\n【法规依据】GB 30000.2-2013";
        var expected = new EvalConclusion { IsCompliant = true };
        EvalEngine.CheckConclusion(response, expected, toolTriggered: true).Should().BeTrue();
    }

    [Fact]
    public void CheckConclusion_NotToolTriggered_ReturnsFalse()
    {
        var response = "【合规判断】是\n[判定:is_compliant=true]";
        var expected = new EvalConclusion { IsCompliant = true };
        EvalEngine.CheckConclusion(response, expected, toolTriggered: false).Should().BeFalse();
    }

    [Fact]
    public void CheckConclusion_InfoQuery_DistanceMatch_ReturnsTrue()
    {
        var response = "安全距离为 30米";
        var expected = new EvalConclusion { ExpectedDistance = 30 };
        EvalEngine.CheckConclusion(response, expected, toolTriggered: true, category: "安全距离", intent: "info_query")
            .Should().BeTrue();
    }

    [Fact]
    public void CheckConclusion_InfoQuery_RegulationMatch_ReturnsTrue()
    {
        var response = "适用标准 GB 30000.2-2013，该物质属于易燃液体";
        var expected = new EvalConclusion { ExpectedRegulationNumbers = new List<string> { "GB 30000.2-2013" } };
        EvalEngine.CheckConclusion(response, expected, toolTriggered: true, intent: "info_query")
            .Should().BeTrue();
    }

    [Fact]
    public void CheckConclusion_ComplianceNotCompliantKeyword_ReturnsTrue()
    {
        var response = "不合规，该物质与存储条件冲突";
        var expected = new EvalConclusion { IsCompliant = false };
        EvalEngine.CheckConclusion(response, expected, toolTriggered: true).Should().BeTrue();
    }

    [Fact]
    public void CheckConclusion_NullResponse_ReturnsFalse()
    {
        EvalEngine.CheckConclusion(null, new EvalConclusion(), toolTriggered: true)
            .Should().BeFalse();
    }

    [Fact]
    public void CheckConclusion_NullExpected_ReturnsFalse()
    {
        EvalEngine.CheckConclusion("合规", null, toolTriggered: true)
            .Should().BeFalse();
    }
}
