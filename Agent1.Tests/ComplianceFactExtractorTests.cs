using System;
using System.Collections.Generic;
using Agent1.Models;
using Agent1.Services;
using FluentAssertions;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// ComplianceFactExtractor 单元测试：从工具调用记录中提取结构化事实。
/// 纯逻辑测试，无需外部依赖。
/// </summary>
public class ComplianceFactExtractorTests
{
    private static FunctionCallRecord MakeRecord(string functionName, string arguments, string result)
        => new()
        {
            FunctionName = functionName,
            Arguments = arguments,
            Result = result,
            Success = true
        };

    [Fact]
    public void Extract_CheckHazardCategory_ReturnsCorrectRegulations()
    {
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeRecord("CheckHazardCategory",
                "substanceName=苯",
                "[REGULATIONS: GB 30000.7-2013]\n「苯」危险类别: 易燃液体,类别2 [判定:is_compliant=unknown]")
        };

        var facts = ComplianceFactExtractor.Extract(toolCalls, isInfoQuery: false);

        facts.RegulationRefs.Should().Contain(r => r.Contains("GB 30000.7"));
        facts.HazardCategories.Should().ContainKey("苯");
        facts.HazardCategories["苯"].Should().Be("易燃液体,类别2");
    }

    [Fact]
    public void Extract_MultipleTools_MergesAllRegulations()
    {
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeRecord("CheckHazardCategory",
                "substanceName=苯",
                "[REGULATIONS: GB 30000.7-2013]\n「苯」危险类别: 易燃液体,类别2"),
            MakeRecord("CheckStorageCompatibility",
                "substanceA=苯,substanceB=硝酸",
                "[REGULATIONS: GB 15603]\n⚠️ 禁用：「苯」与「硝酸」存在配伍禁忌——氧化剂类不可与之同库贮存 [判定:is_compliant=false]")
        };

        var facts = ComplianceFactExtractor.Extract(toolCalls, isInfoQuery: false);

        facts.RegulationRefs.Should().HaveCountGreaterOrEqualTo(2);
        facts.RegulationRefs.Should().Contain(r => r.Contains("GB 30000.7"));
        facts.RegulationRefs.Should().Contain(r => r.Contains("GB 15603"));
        facts.HazardCategories.Should().ContainKey("苯");
        facts.ComplianceVerdicts.Should().HaveCountGreaterOrEqualTo(1);
    }

    [Fact]
    public void Extract_NoTools_ReturnsEmptyFacts()
    {
        var facts = ComplianceFactExtractor.Extract(new List<FunctionCallRecord>(), isInfoQuery: false);

        facts.HasAnyToolResult.Should().BeFalse();
        facts.RegulationRefs.Should().BeEmpty();
        facts.HazardCategories.Should().BeEmpty();
    }

    [Fact]
    public void Extract_CheckStorageCompatibility_ExtractsVerdict()
    {
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeRecord("CheckStorageCompatibility",
                "substanceA=硝酸,substanceB=乙酸",
                "[REGULATIONS: GB 15603]\n⚠️ 禁用：「硝酸」与「乙酸」不得同库储存 [判定:is_compliant=false]")
        };

        var facts = ComplianceFactExtractor.Extract(toolCalls, isInfoQuery: false);

        facts.ComplianceVerdicts.Should().NotBeEmpty();
        facts.OverallComplianceVerdict.Should().NotBeNull();
    }

    [Fact]
    public void Extract_GetMajorHazardThreshold_ExtractsThreshold()
    {
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeRecord("GetMajorHazardThreshold",
                "substanceName=苯",
                "[REGULATIONS: GB 18218-2018]\n📋 「苯」(CAS: 71-43-2)\n   重大危险源临界量: **50 吨**")
        };

        var facts = ComplianceFactExtractor.Extract(toolCalls, isInfoQuery: false);

        facts.Thresholds.Should().ContainKey("苯");
        facts.RegulationRefs.Should().Contain(r => r.Contains("GB 18218"));
    }

    [Fact]
    public void Extract_CheckRegulationVersion_ExtractsVersion()
    {
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeRecord("CheckRegulationVersion",
                "regulationName=GB 15603",
                "现行版本为 GB 15603-2013")
        };

        var facts = ComplianceFactExtractor.Extract(toolCalls, isInfoQuery: false);

        facts.RegulationVersions.Should().NotBeEmpty();
        facts.RegulationRefs.Should().HaveCountGreaterOrEqualTo(1);
    }

    [Fact]
    public void Extract_ToolWithoutRegulationTags_FallbackExtractsGB()
    {
        // 工具结果不含 [REGULATIONS:] 标签，但包含自由文本 GB 编号
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeRecord("LookupChemicalProperties",
                "substanceName=苯",
                "苯的闪点为-11°C，沸点80.1°C。参考标准：GB 30000.7-2013")
        };

        var facts = ComplianceFactExtractor.Extract(toolCalls, isInfoQuery: false);

        facts.ChemicalProperties.Should().ContainKey("苯");
        facts.RegulationRefs.Should().Contain(r => r.Contains("GB 30000.7"));
    }
}
