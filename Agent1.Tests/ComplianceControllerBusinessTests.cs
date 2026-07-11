using System.Collections.Generic;
using Agent1.Models;
using Agent1.Services;
using FluentAssertions;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// API 业务逻辑测试 — 验证双通道在 Controller 三条业务路径上的核心行为。
/// 直接测试组件链（Extract → Sanitize → Assemble → Merge），
/// 覆盖 API 三个端点的典型输入场景和开关行为。
/// </summary>
public class ComplianceControllerBusinessTests
{
    private static FunctionCallRecord MakeRecord(string functionName, string arguments, string? result)
        => new()
        {
            FunctionName = functionName,
            Arguments = arguments,
            Result = result,
            Success = true
        };

    /// <summary>
    /// 模拟 ApplyDecoupledPipeline 的完整逻辑（与 AgentDialog 实现一致）。
    /// enableSwitch=false 模拟 UseDecoupledArchitecture=false 时的透传行为。
    /// </summary>
    private static string RunPipeline(string llmAnswer, List<FunctionCallRecord> toolCalls,
        bool isInfoQuery, bool enableSwitch = true)
    {
        if (!enableSwitch)
            return llmAnswer; // 开关关闭 → 原样返回

        var facts = ComplianceFactExtractor.Extract(toolCalls, isInfoQuery);
        if (facts.HasAnyToolResult)
        {
            var sanitized = OutputSanitizer.Sanitize(llmAnswer, facts.RegulationRefs);
            var factOutput = FactAssembler.Build(facts);
            return ResponseMerger.Merge(factOutput, sanitized);
        }
        else
        {
            var sanitized = OutputSanitizer.Sanitize(llmAnswer, facts.RegulationRefs);
            var factOutput = FactAssembler.Build(facts);
            return ResponseMerger.Merge(factOutput, sanitized);
        }
    }

    // ═══════════════════════════════════════════════════════
    // 场景 1: POST /api/compliance/check — 合规审核
    //   对应 Controller.CheckCompliance → ExecuteEvalFastAsync
    //   isInfoQuery=false，LLM 需进行合规判定
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void CheckCompliance_ToolsTriggered_MergesFactAndExplanation()
    {
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeRecord("CheckHazardCategory", "substanceName=苯",
                "[REGULATIONS: GB 30000.7-2013]\n「苯」危险类别: 易燃液体,类别2"),
            MakeRecord("CheckStorageCompatibility", "substanceA=苯,substanceB=硝酸",
                "[REGULATIONS: GB 15603]\n「苯」与「硝酸」: 不得同库储存\n[判定: is_compliant=false]")
        };
        var llmOutput = "苯属于易燃液体（GB 30000.7-2013），与硝酸不得同库储存（GB 15603）。建议分库存放。";

        var result = RunPipeline(llmOutput, toolCalls, isInfoQuery: false);

        // 事实通道应包含工具返回值
        result.Should().Contain("「苯」");
        result.Should().Contain("不得同库");
        result.Should().Contain("GB 30000.7");
        result.Should().Contain("GB 15603");
        // 解释通道应包含 LLM 建议
        result.Should().Contain("分库存放");
        // 事实在前，解释在后
        result.IndexOf("查询结果").Should().BeLessThan(result.IndexOf("分库存放"));
    }

    [Fact]
    public void CheckCompliance_NoTools_ReturnsFallbackWithSanitizedAdvice()
    {
        // LLM 拒绝调用工具，直接输出（含幻觉法规引用）
        var llmOutput = "根据 GB 30000.7-2013 第5条和 GB 88888 的规定，该物质安全。";

        var result = RunPipeline(llmOutput, new List<FunctionCallRecord>(), isInfoQuery: false);

        // 兜底声明
        result.Should().Contain("无法给出确定结论");
        // 幻觉法规被消毒
        result.Should().NotContain("GB 30000.7");
        result.Should().NotContain("GB 88888");
        result.Should().NotContain("第5条");
        // 无害内容保留
        result.Should().Contain("安全");
    }

    [Fact]
    public void CheckCompliance_SwitchOff_PassesThroughUnchanged()
    {
        var llmOutput = "根据 GB 30000.7-2013，苯属于易燃液体。";
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeRecord("CheckHazardCategory", "substanceName=苯",
                "[REGULATIONS: GB 30000.7-2013]\n「苯」危险类别: 易燃液体,类别2")
        };

        var result = RunPipeline(llmOutput, toolCalls, isInfoQuery: false, enableSwitch: false);

        result.Should().Be(llmOutput);
    }

    // ═══════════════════════════════════════════════════════
    // 场景 2: POST /api/compliance/hazard/query — 危化品信息查询
    //   对应 Controller.QueryHazard → ExecuteEvalFastQueryAsync
    //   isInfoQuery=true，只提取事实不做合规判定
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void HazardQuery_ToolsTriggered_RendersFactWithoutVerdict()
    {
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeRecord("CheckHazardCategory", "substanceName=丙酮",
                "[REGULATIONS: GB 30000.7-2013]\n「丙酮」危险类别: 易燃液体,类别2")
        };
        var llmOutput = "丙酮属于GB 30000.7-2013规定的易燃液体，类别2，闪点-20°C。";

        var result = RunPipeline(llmOutput, toolCalls, isInfoQuery: true);

        // 信息查询：应渲染事实
        result.Should().Contain("「丙酮」");
        result.Should().Contain("易燃液体");
        result.Should().Contain("GB 30000.7");
        // 信息查询：LLM 的技术解读应保留
        result.Should().Contain("闪点");
    }

    [Fact]
    public void HazardQuery_NoTools_FallbackPreservesTechnicalInfo()
    {
        var llmOutput = "根据GB 30000.7-2013，丙酮属于第3类易燃液体。";

        var result = RunPipeline(llmOutput, new List<FunctionCallRecord>(), isInfoQuery: true);

        // 兜底声明 + 幻觉法规被消毒
        result.Should().Contain("无法给出确定结论");
        result.Should().NotContain("GB 30000.7");
    }

    // ═══════════════════════════════════════════════════════
    // 场景 3: POST /api/compliance/storage/compatibility — 储存兼容性
    //   对应 Controller.CheckStorageCompatibility → ExecuteEvalFastAsync
    //   isInfoQuery=false
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void StorageCompatibility_Compatible_RendersVerdict()
    {
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeRecord("CheckStorageCompatibility", "substanceA=乙醇,substanceB=水",
                "[REGULATIONS: GB 15603]\n「乙醇」与「水」: 可同库储存\n[判定: is_compliant=true]")
        };
        var llmOutput = "乙醇与水可同库储存，但仍需注意乙醇的易燃特性。";

        var result = RunPipeline(llmOutput, toolCalls, isInfoQuery: false);

        result.Should().Contain("「乙醇」");
        result.Should().Contain("「水」");
        result.Should().Contain("可同库");
        result.Should().Contain("GB 15603");
    }

    [Fact]
    public void StorageCompatibility_Incompatible_RendersWarning()
    {
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeRecord("CheckStorageCompatibility", "substanceA=硫酸,substanceB=氢氧化钠",
                "[REGULATIONS: GB 15603]\n「硫酸」与「氢氧化钠」: 不得同库储存\n[判定: is_compliant=false]")
        };
        var llmOutput = "硫酸与氢氧化钠严禁同库储存，酸碱反应会引发危险。";

        var result = RunPipeline(llmOutput, toolCalls, isInfoQuery: false);

        result.Should().Contain("「硫酸」");
        result.Should().Contain("「氢氧化钠」");
        result.Should().Contain("不得同库");
        result.Should().Contain("GB 15603");
        // LLM 警告也应保留
        result.Should().Contain("严禁");
    }

    // ═══════════════════════════════════════════════════════
    // 跨场景：幻觉法规消毒（三个端点共有）
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void HallucinatedRegulations_RemovedFromAllChannels()
    {
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeRecord("CheckHazardCategory", "substanceName=苯",
                "[REGULATIONS: GB 30000.7-2013]\n「苯」危险类别: 易燃液体,类别2")
        };
        // LLM 幻觉生成了假的法规编号
        var llmOutput = "依据 GB 30000.7-2013、GB 88888.1-2099 第3.2条和 GB 99999，苯属于易燃液体。";

        var result = RunPipeline(llmOutput, toolCalls, isInfoQuery: false);

        // 白名单法规保留
        result.Should().Contain("30000.7");
        // 幻觉法规移除
        result.Should().NotContain("GB 88888");
        result.Should().NotContain("GB 99999");
        result.Should().NotContain("第3.2条");
    }

    // ═══════════════════════════════════════════════════════
    // 跨场景：开关行为（原有通道兼容性）
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void SwitchOff_EvenWithHallucinatedRegulations_PassesThrough()
    {
        var llmOutput = "幻觉法规 GB 99999.1-2099 声称该物质安全。";
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeRecord("CheckHazardCategory", "substanceName=苯",
                "[REGULATIONS: GB 30000.7-2013]\n「苯」危险类别: 易燃液体,类别2")
        };

        var result = RunPipeline(llmOutput, toolCalls, isInfoQuery: false, enableSwitch: false);

        // 开关关闭 → 幻觉法规照样通过（回归原通道行为）
        result.Should().Be(llmOutput);
        result.Should().Contain("GB 99999");
    }

    [Fact]
    public void SwitchOn_NoTools_EmptyAnswer_ReturnsFallback()
    {
        var result = RunPipeline("", new List<FunctionCallRecord>(), isInfoQuery: false);

        result.Should().Contain("无法给出确定结论");
        result.Should().Contain("安环部门");
    }

    // ═══════════════════════════════════════════════════════
    // 安全距离场景
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void SafetyDistance_ToolsTriggered_RendersDistanceWithRegulation()
    {
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeRecord("GetSafetyDistance", "facilityType=甲类仓库-明火点",
                "[REGULATIONS: GB 50160-2008]\n安全距离: 30米")
        };
        var llmOutput = "甲类仓库与明火点的安全距离为30米，符合GB 50160-2008要求。";

        var result = RunPipeline(llmOutput, toolCalls, isInfoQuery: false);

        result.Should().Contain("30米");
        result.Should().Contain("GB 50160");
        result.Should().Contain("安全距离");
    }
}
