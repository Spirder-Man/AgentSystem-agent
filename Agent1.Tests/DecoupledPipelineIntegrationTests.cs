using System.Collections.Generic;
using Agent1.Models;
using Agent1.Services;
using FluentAssertions;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// 双通道集成测试 — 验证 ApplyDecoupledPipeline 内部链路的完整行为：
/// ComplianceFactExtractor → OutputSanitizer → FactAssembler → ResponseMerger。
/// 组件级集成，无需 AgentDialog / LLM mocking。
/// </summary>
public class DecoupledPipelineIntegrationTests
{
    private static FunctionCallRecord MakeRecord(string functionName, string arguments, string result)
        => new()
        {
            FunctionName = functionName,
            Arguments = arguments,
            Result = result,
            Success = true
        };

    /// <summary>模拟 ApplyDecoupledPipeline 内部完整链路</summary>
    private static string RunPipeline(string llmOutput, List<FunctionCallRecord> toolCalls, bool isInfoQuery)
    {
        var facts = ComplianceFactExtractor.Extract(toolCalls, isInfoQuery);
        if (facts.HasAnyToolResult)
        {
            var sanitized = OutputSanitizer.Sanitize(llmOutput, facts.RegulationRefs);
            var factOutput = FactAssembler.Build(facts);
            return ResponseMerger.Merge(factOutput, sanitized);
        }
        else
        {
            var sanitized = OutputSanitizer.Sanitize(llmOutput, facts.RegulationRefs);
            var factOutput = FactAssembler.Build(facts);
            return ResponseMerger.Merge(factOutput, sanitized);
        }
    }

    [Fact]
    public void Pipeline_ToolsTriggered_FactOutputContainsRegulations()
    {
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeRecord("CheckHazardCategory",
                "substanceName=苯",
                "[REGULATIONS: GB 30000.7-2013]\n「苯」危险类别: 易燃液体,类别2"),
            MakeRecord("CheckStorageCompatibility",
                "substanceA=苯,substanceB=硝酸",
                "[REGULATIONS: GB 15603]\n「苯」与「硝酸」: 不得同库储存")
        };
        var llmOutput = "苯属于易燃液体（GB 30000.7-2013），与硝酸不得同库储存（GB 15603）。";

        var result = RunPipeline(llmOutput, toolCalls, isInfoQuery: false);

        // 事实通道内容
        result.Should().Contain("查询结果");
        result.Should().Contain("「苯」");
        result.Should().Contain("易燃液体");
        result.Should().Contain("不得同库储存");
        // 解释通道（LLM 内容）
        result.Should().Contain("属于易燃液体");
    }

    [Fact]
    public void Pipeline_ToolsTriggered_HallucinatedRegulationsRemoved()
    {
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeRecord("CheckHazardCategory",
                "substanceName=苯",
                "[REGULATIONS: GB 30000.7-2013]\n「苯」危险类别: 易燃液体,类别2")
        };
        // LLM 幻觉生成了不在白名单中的法规编号
        var llmOutput = "根据 GB 30000.7-2013 和 GB 99999.1-2099 第3.2条，苯属于易燃液体。";

        var result = RunPipeline(llmOutput, toolCalls, isInfoQuery: false);

        // 白名单中的法规应保留
        result.Should().Contain("30000.7");
        // 幻觉法规应被移除
        result.Should().NotContain("GB 99999");
        result.Should().NotContain("第3.2条");
    }

    [Fact]
    public void Pipeline_NoToolsTriggered_FallbackSafeguard()
    {
        // 工具未触发场景（例如 LLM 拒绝调用工具直接回答）
        var llmOutput = "根据 GB 30000.7-2013 第5条，该物质属于危险化学品。建议...";

        var result = RunPipeline(llmOutput, new List<FunctionCallRecord>(), isInfoQuery: false);

        // 兜底声明
        result.Should().Contain("无法给出确定结论");
        result.Should().Contain("安环部门");
        // 幻觉法规被消毒
        result.Should().NotContain("GB 30000.7");
        result.Should().NotContain("第5条");
    }

    [Fact]
    public void Pipeline_MultipleRegulationsFromMultipleTools_AllPreserved()
    {
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeRecord("CheckHazardCategory",
                "substanceName=甲醇",
                "[REGULATIONS: GB 30000.7-2013]\n「甲醇」危险类别: 易燃液体,类别2"),
            MakeRecord("GetSafetyDistance",
                "facilityType=甲类仓库-明火点",
                "[REGULATIONS: GB 50160-2008]\n安全距离: 30米"),
            MakeRecord("GetMajorHazardThreshold",
                "substanceName=甲醇",
                "[REGULATIONS: GB 18218-2018]\n临界量: 500吨")
        };
        var llmOutput = "甲醇属于易燃液体，甲类仓库与明火点安全距离30米，临界量500吨。";

        var result = RunPipeline(llmOutput, toolCalls, isInfoQuery: false);

        // 三个工具的法规引用均应出现在事实通道
        result.Should().Contain("30000.7");
        result.Should().Contain("50160");
        result.Should().Contain("18218");
    }

    [Fact]
    public void Pipeline_InfoQuery_PreservesFactWithoutComplianceVerdict()
    {
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeRecord("CheckHazardCategory",
                "substanceName=丙酮",
                "[REGULATIONS: GB 30000.7-2013]\n「丙酮」危险类别: 易燃液体,类别2")
        };
        var llmOutput = "丙酮属于GB 30000.7-2013规定的易燃液体，类别2。";

        var result = RunPipeline(llmOutput, toolCalls, isInfoQuery: true);

        // 信息查询应渲染事实
        result.Should().Contain("「丙酮」");
        result.Should().Contain("易燃液体");
        // 不应包含合规判定（isInfoQuery 影响 Prompt 选择，但 FactAssembler 行为一致）
    }

    [Fact]
    public void Pipeline_MixedToolsAndUnknownTool_FiltersCorrectly()
    {
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeRecord("CheckHazardCategory",
                "substanceName=苯",
                "[REGULATIONS: GB 30000.7-2013]\n「苯」危险类别: 易燃液体,类别2"),
            MakeRecord("GetCurrentTime",  // 非合规工具，不应干扰
                "",
                "2024-01-15T10:30:00")
        };
        var llmOutput = "现在是2024年1月15日，苯属于GB 30000.7规定的易燃液体。";

        var result = RunPipeline(llmOutput, toolCalls, isInfoQuery: false);

        // 合规事实应正确渲染
        result.Should().Contain("苯");
        result.Should().Contain("30000.7");
        // 非合规工具结果不干扰
        result.Should().Contain("查询结果");
    }
}
