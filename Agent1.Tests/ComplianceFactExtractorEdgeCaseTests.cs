using System.Collections.Generic;
using Agent1.Models;
using Agent1.Services;
using FluentAssertions;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// ComplianceFactExtractor 边界场景测试。
/// 覆盖 null/空输入、异常工具结果、未知工具类型、去重等边缘情况。
/// </summary>
public class ComplianceFactExtractorEdgeCaseTests
{
    private static FunctionCallRecord MakeRecord(string functionName, string arguments, string? result, bool success = true)
        => new()
        {
            FunctionName = functionName,
            Arguments = arguments,
            Result = result,
            Success = success
        };

    // ── null / 空输入 ──

    [Fact]
    public void Extract_NullToolCalls_ReturnsEmptyFacts()
    {
        var facts = ComplianceFactExtractor.Extract(null!, isInfoQuery: false);

        facts.HasAnyToolResult.Should().BeFalse();
        facts.RegulationRefs.Should().BeEmpty();
        facts.HazardCategories.Should().BeEmpty();
        facts.RawToolOutputs.Should().BeEmpty();
    }

    [Fact]
    public void Extract_EmptyToolCalls_ReturnsEmptyFacts()
    {
        var facts = ComplianceFactExtractor.Extract(new List<FunctionCallRecord>(), isInfoQuery: false);

        facts.HasAnyToolResult.Should().BeFalse();
        facts.RegulationRefs.Should().BeEmpty();
    }

    [Fact]
    public void Extract_NullResult_SkipToolGracefully()
    {
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeRecord("CheckHazardCategory", "substanceName=苯", null) // Result is null
        };

        var facts = ComplianceFactExtractor.Extract(toolCalls, isInfoQuery: false);

        // Should not crash; null result is skipped
        facts.HasAnyToolResult.Should().BeFalse();
        facts.RegulationRefs.Should().BeEmpty();
    }

    [Fact]
    public void Extract_EmptyResultText_NoRegulationsExtracted()
    {
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeRecord("CheckHazardCategory", "substanceName=苯", "")
        };

        var facts = ComplianceFactExtractor.Extract(toolCalls, isInfoQuery: false);

        // Empty result: no regex match, no HazardCategory extracted (substance exists but no match in result)
        facts.RegulationRefs.Should().BeEmpty();
    }

    [Fact]
    public void Extract_MixedNullAndValidResults_ProcessesValidOnly()
    {
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeRecord("CheckHazardCategory", "substanceName=苯", null),
            MakeRecord("CheckHazardCategory", "substanceName=丙酮",
                "[REGULATIONS: GB 30000.7-2013]\n「丙酮」危险类别: 易燃液体,类别2")
        };

        var facts = ComplianceFactExtractor.Extract(toolCalls, isInfoQuery: false);

        facts.HasAnyToolResult.Should().BeTrue();
        facts.HazardCategories.Should().ContainKey("丙酮");
        facts.RegulationRefs.Should().Contain(r => r.Contains("30000.7"));
        // Null result tool should not contribute
        facts.RawToolOutputs.Should().HaveCount(1); // only valid result added
    }

    // ── 未知工具类型 ──

    [Fact]
    public void Extract_UnknownTool_StillExtractsRegulations()
    {
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeRecord("SomeUnknownTool", "param=value",
                "[REGULATIONS: GB 15603-2022]\nSome result text")
        };

        var facts = ComplianceFactExtractor.Extract(toolCalls, isInfoQuery: false);

        // Unknown tool → falls to default case, but generic regulation extraction still runs
        facts.RegulationRefs.Should().Contain(r => r.Contains("15603"));
        // No structured facts extracted (unknown tool has no parser)
        facts.HasAnyToolResult.Should().BeFalse();
    }

    [Fact]
    public void Extract_UnknownTool_NoRegulationTag_NoExtraction()
    {
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeRecord("GetCurrentTime", "", "2024-01-15T10:30:00")
        };

        var facts = ComplianceFactExtractor.Extract(toolCalls, isInfoQuery: false);

        facts.RegulationRefs.Should().BeEmpty();
        facts.HasAnyToolResult.Should().BeFalse();
    }

    // ── 失败的工具调用 ──

    [Fact]
    public void Extract_FailedTool_StillProcessesResult()
    {
        // Even if Success=false, the Result may still contain partial data
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeRecord("CheckHazardCategory", "substanceName=苯",
                "[REGULATIONS: GB 30000.7-2013]\nError: database timeout",
                success: false)
        };

        var facts = ComplianceFactExtractor.Extract(toolCalls, isInfoQuery: false);

        // Extract doesn't check Success flag; it processes the Result text
        facts.RegulationRefs.Should().Contain(r => r.Contains("30000.7"));
    }

    // ── 无 [REGULATIONS:] 标签的降级提取 ──

    [Fact]
    public void Extract_NoRegulationTag_FreeTextGbExtraction()
    {
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeRecord("CheckHazardCategory", "substanceName=苯",
                "「苯」危险类别: 易燃液体,类别2\n依据标准 GB 30000.7-2013 判定")
        };

        var facts = ComplianceFactExtractor.Extract(toolCalls, isInfoQuery: false);

        // Free text path: GbNumberRegex picks up GB number
        facts.RegulationRefs.Should().Contain(r => r.Contains("30000.7"));
        facts.HazardCategories.Should().ContainKey("苯");
    }

    [Fact]
    public void Extract_NoRegulationTag_NoGbInText_NoExtraction()
    {
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeRecord("CheckHazardCategory", "substanceName=苯",
                "「苯」危险类别: 易燃液体,类别2")
        };

        var facts = ComplianceFactExtractor.Extract(toolCalls, isInfoQuery: false);

        // HazardCategory parsed from Regex, but no GB number in text
        facts.HazardCategories.Should().ContainKey("苯");
        facts.RegulationRefs.Should().BeEmpty();
    }

    // ── 多法规引用 ──

    [Fact]
    public void Extract_MultipleRegulationsInSingleResult()
    {
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeRecord("CheckHazardCategory", "substanceName=苯",
                "[REGULATIONS: GB 30000.7-2013; GB 13690-2009]\n「苯」危险类别: 易燃液体,类别2")
        };

        var facts = ComplianceFactExtractor.Extract(toolCalls, isInfoQuery: false);

        facts.RegulationRefs.Should().HaveCountGreaterOrEqualTo(2);
        facts.RegulationRefs.Should().Contain(r => r.Contains("30000.7"));
        facts.RegulationRefs.Should().Contain(r => r.Contains("13690"));
    }

    [Fact]
    public void Extract_DuplicateRegulations_PreservedInRawList()
    {
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeRecord("CheckHazardCategory", "substanceName=苯",
                "[REGULATIONS: GB 30000.7-2013]\n「苯」危险类别: 易燃液体,类别2"),
            MakeRecord("CheckHazardCategory", "substanceName=甲苯",
                "[REGULATIONS: GB 30000.7-2013]\n「甲苯」危险类别: 易燃液体,类别2")
        };

        var facts = ComplianceFactExtractor.Extract(toolCalls, isInfoQuery: false);

        // Raw list may contain duplicates
        facts.RegulationRefs.Should().HaveCount(2);
        // GetUniqueRegulations deduplicates
        var unique = facts.GetUniqueRegulations();
        unique.Should().HaveCount(1);
        unique[0].Should().Contain("30000.7");
    }

    // ── isInfoQuery 标记 ──

    [Fact]
    public void Extract_IsInfoQuery_FlagPropagates()
    {
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeRecord("CheckHazardCategory", "substanceName=苯",
                "[REGULATIONS: GB 30000.7-2013]\n「苯」危险类别: 易燃液体,类别2")
        };

        var facts = ComplianceFactExtractor.Extract(toolCalls, isInfoQuery: true);

        facts.IsInfoQuery.Should().BeTrue();
    }

    [Fact]
    public void Extract_IsNotInfoQuery_FlagReflectsFalse()
    {
        var facts = ComplianceFactExtractor.Extract(
            new List<FunctionCallRecord>(), isInfoQuery: false);

        facts.IsInfoQuery.Should().BeFalse();
    }

    // ── 畸形参数处理 ──

    [Fact]
    public void Extract_MalformedArguments_NoSubstanceName_HazardSkipped()
    {
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeRecord("CheckHazardCategory", "foo=bar",
                "[REGULATIONS: GB 30000.7-2013]\nSome text")
        };

        var facts = ComplianceFactExtractor.Extract(toolCalls, isInfoQuery: false);

        // Regulation extracted generically, but no HazardCategory (no substance name)
        facts.RegulationRefs.Should().Contain(r => r.Contains("30000.7"));
        facts.HazardCategories.Should().BeEmpty();
    }

    [Fact]
    public void Extract_StorageCompatibility_MissingSubstanceB_Skipped()
    {
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeRecord("CheckStorageCompatibility", "substanceA=苯",
                "[REGULATIONS: GB 15603]\n「苯」与「?」: 不得同库储存")
        };

        var facts = ComplianceFactExtractor.Extract(toolCalls, isInfoQuery: false);

        // substanceB is empty → ParseStorageCompatibility returns early
        facts.ComplianceVerdicts.Should().BeEmpty();
        facts.RegulationRefs.Should().Contain(r => r.Contains("15603"));
    }

    // ── 合规判定汇总 ──

    [Fact]
    public void Extract_ComplianceVerdictNonCompliant_OverallVerdictSet()
    {
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeRecord("CheckStorageCompatibility", "substanceA=苯,substanceB=硝酸",
                "[REGULATIONS: GB 15603]\n「苯」与「硝酸」: 不得同库储存\n[判定: is_compliant=false]")
        };

        var facts = ComplianceFactExtractor.Extract(toolCalls, isInfoQuery: false);

        facts.ComplianceVerdicts.Should().ContainKey("苯|硝酸");
        facts.OverallComplianceVerdict.Should().Be("不得同库");
    }

    [Fact]
    public void Extract_ComplianceVerdictCompliant_OverallVerdictSet()
    {
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeRecord("CheckStorageCompatibility", "substanceA=乙醇,substanceB=水",
                "[REGULATIONS: GB 15603]\n「乙醇」与「水」: 可同库储存\n[判定: is_compliant=true]")
        };

        var facts = ComplianceFactExtractor.Extract(toolCalls, isInfoQuery: false);

        facts.OverallComplianceVerdict.Should().Be("可同库");
    }

    // ── 多工具组合 ──

    [Fact]
    public void Extract_MultipleToolTypes_AllParsed()
    {
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeRecord("CheckHazardCategory", "substanceName=苯",
                "[REGULATIONS: GB 30000.7-2013]\n「苯」危险类别: 易燃液体,类别2"),
            MakeRecord("GetSafetyDistance", "facilityType=甲类仓库-明火点",
                "[REGULATIONS: GB 50160-2008]\n安全距离: 30米"),
            MakeRecord("GetMajorHazardThreshold", "substanceName=苯",
                "[REGULATIONS: GB 18218-2018]\n临界量: 50吨")
        };

        var facts = ComplianceFactExtractor.Extract(toolCalls, isInfoQuery: false);

        facts.HasAnyToolResult.Should().BeTrue();
        facts.HazardCategories.Should().ContainKey("苯");
        facts.SafetyDistances.Should().ContainKey("甲类仓库-明火点");
        facts.Thresholds.Should().ContainKey("苯");
        facts.RegulationRefs.Should().HaveCountGreaterOrEqualTo(3);
    }

    // ── RawToolOutputs 追踪 ──

    [Fact]
    public void Extract_RawToolOutputs_TracksAllResults()
    {
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeRecord("CheckHazardCategory", "substanceName=苯", "result1"),
            MakeRecord("CheckStorageCompatibility", "substanceA=苯,substanceB=硝酸", "result2")
        };

        var facts = ComplianceFactExtractor.Extract(toolCalls, isInfoQuery: false);

        facts.RawToolOutputs.Should().HaveCount(2);
        facts.RawToolOutputs[0].Should().Be("result1");
        facts.RawToolOutputs[1].Should().Be("result2");
    }

    // ── 法规版本 ──

    [Fact]
    public void Extract_RegulationVersion_ParsesCorrectly()
    {
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeRecord("CheckRegulationVersion", "standardName=GB 15603",
                "[REGULATIONS: GB 15603-2022]\n现行版本为 GB 15603-2022")
        };

        var facts = ComplianceFactExtractor.Extract(toolCalls, isInfoQuery: false);

        facts.RegulationVersions.Should().NotBeEmpty();
    }

    // ── 化学品属性 ──

    [Fact]
    public void Extract_ChemicalProperties_ParsesCorrectly()
    {
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeRecord("LookupChemicalProperties", "substanceName=苯",
                "[REGULATIONS: GB 30000.7-2013]\n危险特性: 高度易燃液体和蒸气")
        };

        var facts = ComplianceFactExtractor.Extract(toolCalls, isInfoQuery: false);

        facts.ChemicalProperties.Should().ContainKey("苯");
        facts.ChemicalProperties["苯"].Should().Contain("高度易燃");
    }

    // ── 降级路径：HazardCategory 无结构化匹配 ──

    [Fact]
    public void Extract_HazardCategory_FallbackToFirstLine()
    {
        // Result doesn't match 「X」危险类别: Y pattern → falls back to first line
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeRecord("CheckHazardCategory", "substanceName=苯",
                "易燃液体,类别2\n更多信息...")
        };

        var facts = ComplianceFactExtractor.Extract(toolCalls, isInfoQuery: false);

        facts.HazardCategories.Should().ContainKey("苯");
        facts.HazardCategories["苯"].Should().Be("易燃液体,类别2");
    }

    // ── 空物质名 ──

    [Fact]
    public void Extract_EmptySubstanceName_Argument_Skipped()
    {
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeRecord("CheckHazardCategory", "substanceName=",
                "[REGULATIONS: GB 30000.7-2013]\n「」危险类别: 易燃液体")
        };

        var facts = ComplianceFactExtractor.Extract(toolCalls, isInfoQuery: false);

        // Empty substance name → ParseHazardCategory returns early
        facts.HazardCategories.Should().BeEmpty();
    }

    // ── ToSanitizedSummary ──

    [Fact]
    public void ToSanitizedSummary_WithFacts_FormatsCorrectly()
    {
        var facts = new ExtractedFacts
        {
            HazardCategories = new Dictionary<string, string> { ["苯"] = "易燃液体,类别2" },
            SafetyDistances = new Dictionary<string, string> { ["甲类仓库-明火点"] = "30米" }
        };

        var summary = facts.ToSanitizedSummary();

        summary.Should().Contain("苯");
        summary.Should().Contain("易燃液体");
        summary.Should().Contain("30米");
    }

    [Fact]
    public void ToSanitizedSummary_Empty_ReturnsDefaultMessage()
    {
        var facts = new ExtractedFacts();
        var summary = facts.ToSanitizedSummary();

        summary.Should().Contain("未返回");
    }
}
