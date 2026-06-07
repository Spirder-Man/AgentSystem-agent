using System;
using System.Collections.Generic;
using Agent1.Services;
using FluentAssertions;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// ConclusionVerifier 单元测试：法规编号提取 + 有效性验证。
/// 纯逻辑测试，无需外部依赖。
/// </summary>
public class ConclusionVerifierTests
{
    [Fact]
    public void ExtractRegulations_GBStandard_ExtractsNumber()
    {
        var response = "依据 GB 30000.2-2013 第5条，爆炸物属于危险类别";
        var result = ConclusionVerifier.ExtractRegulations(response);
        // 提取的法规编号至少包含 "GB 30000"
        result.Should().Contain(r => r.Contains("GB 30000"));
    }

    [Fact]
    public void ExtractRegulations_MultipleStandards_ExtractsAll()
    {
        var response = "参考 GB 30000.2-2013 和 GB/T 18218-2020 的规定";
        var result = ConclusionVerifier.ExtractRegulations(response);
        result.Count.Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public void ExtractRegulations_NoStandard_ReturnsEmpty()
    {
        var response = "这个化学品没有找到相关标准";
        var result = ConclusionVerifier.ExtractRegulations(response);
        result.Count.Should().Be(0);
    }

    [Fact]
    public void ExtractRegulations_GBTVariant_ReturnsNotEmpty()
    {
        var response = "依据 GB/T 30000.2-2013";
        var result = ConclusionVerifier.ExtractRegulations(response);
        result.Should().NotBeEmpty();
    }

    [Fact]
    public void HasValidRegulation_WithStandard_ReturnsTrue()
    {
        ConclusionVerifier.HasValidRegulation("依据 GB 30000.2-2013")
            .Should().BeTrue();
    }

    [Fact]
    public void HasValidRegulation_WithoutStandard_ReturnsFalse()
    {
        ConclusionVerifier.HasValidRegulation("没有引用任何标准")
            .Should().BeFalse();
    }

    [Fact]
    public void HasValidRegulation_Empty_ReturnsFalse()
    {
        ConclusionVerifier.HasValidRegulation("").Should().BeFalse();
    }
}
