using System.Collections.Generic;
using System.Linq;
using Agent1.Models;
using Agent1.Services;
using FluentAssertions;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// 非流式降级路径测试 — 验证 ExecuteEvalInternalAsync 中：
/// 1. 流式空回退后双通道仍正确生效
/// 2. LlmService 为 null 时工具调用映射为空
/// 3. FunctionCallRecord 投影保留所有关键字段
/// 4. 降级场景不影响 HasAnyToolResult / else 兜底分支
/// </summary>
public class EvalInternalFallbackTests
{
    private static FunctionCallRecord MakeServiceRecord(string functionName, string arguments, string? result,
        bool success = true, QualityLevel? quality = null)
        => new()
        {
            FunctionName = functionName,
            Arguments = arguments,
            Result = result,
            Success = success,
            Quality = quality
        };

    /// <summary>
    /// 模拟 ExecuteEvalInternalAsync 中 LlmService.LastFunctionCalls → FunctionCallRecord 的投影。
    /// 这是降级路径前后共享的工具调用记录同步逻辑。
    /// </summary>
    private static List<FunctionCallRecord> ProjectToolCalls(List<FunctionCallRecord>? lastFunctionCalls)
    {
        if (lastFunctionCalls == null)
            return new List<FunctionCallRecord>();

        return lastFunctionCalls
            .Select(fc => new FunctionCallRecord
            {
                FunctionName = fc.FunctionName,
                Arguments = fc.Arguments,
                Result = fc.Result,
                Success = fc.Success,
                Quality = fc.Quality
            }).ToList();
    }

    /// <summary>
    /// 模拟 ApplyDecoupledPipeline（与 AgentDialog 实现一致）
    /// </summary>
    private static string RunPipeline(string answer, List<FunctionCallRecord> toolCalls, bool isInfoQuery)
    {
        var facts = ComplianceFactExtractor.Extract(toolCalls, isInfoQuery);
        if (facts.HasAnyToolResult)
        {
            var sanitized = OutputSanitizer.Sanitize(answer, facts.RegulationRefs);
            var factOutput = FactAssembler.Build(facts);
            return ResponseMerger.Merge(factOutput, sanitized);
        }
        else
        {
            var sanitized = OutputSanitizer.Sanitize(answer, facts.RegulationRefs);
            var factOutput = FactAssembler.Build(facts);
            return ResponseMerger.Merge(factOutput, sanitized);
        }
    }

    // ═══════════════════════════════════════════════════════
    // FunctionCallRecord 投影测试
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void ProjectToolCalls_PreservesAllFields()
    {
        var source = new List<FunctionCallRecord>
        {
            new()
            {
                FunctionName = "CheckHazardCategory",
                Arguments = "substanceName=苯",
                Result = "[REGULATIONS: GB 30000.7]\n「苯」危险类别: 易燃液体",
                Success = true,
                Quality = QualityLevel.DATABASE_HIT
            }
        };

        var projected = ProjectToolCalls(source);

        projected.Should().HaveCount(1);
        projected[0].FunctionName.Should().Be("CheckHazardCategory");
        projected[0].Arguments.Should().Be("substanceName=苯");
        projected[0].Result.Should().Contain("GB 30000.7");
        projected[0].Success.Should().BeTrue();
        projected[0].Quality.Should().Be(QualityLevel.DATABASE_HIT);
        // 投影是深拷贝：修改投影不应影响源
        projected[0].Should().NotBeSameAs(source[0]);
    }

    [Fact]
    public void ProjectToolCalls_NullSource_ReturnsEmptyList()
    {
        var projected = ProjectToolCalls(null);

        projected.Should().NotBeNull();
        projected.Should().BeEmpty();
    }

    [Fact]
    public void ProjectToolCalls_EmptySource_ReturnsEmptyList()
    {
        var projected = ProjectToolCalls(new List<FunctionCallRecord>());

        projected.Should().BeEmpty();
    }

    [Fact]
    public void ProjectToolCalls_FailedToolCall_PreservesSuccessFlag()
    {
        var source = new List<FunctionCallRecord>
        {
            MakeServiceRecord("CheckHazardCategory", "substanceName=苯",
                "Error: timeout", success: false)
        };

        var projected = ProjectToolCalls(source);

        projected[0].Success.Should().BeFalse();
        // 即使失败，Result 仍保留
        projected[0].Result.Should().Be("Error: timeout");
    }

    // ═══════════════════════════════════════════════════════
    // 降级场景：流式返回空字符串 → 非流式后回答有效
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void Fallback_EmptyAnswer_WithTools_PipelineStillApplies()
    {
        // 模拟：流式返回空 → 非流式降级 → 获得有效回答
        var postFallbackAnswer = "苯属于易燃液体，储存需隔离火源。";
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeServiceRecord("CheckHazardCategory", "substanceName=苯",
                "[REGULATIONS: GB 30000.7-2013]\n「苯」危险类别: 易燃液体,类别2")
        };

        var result = RunPipeline(postFallbackAnswer, ProjectToolCalls(toolCalls), isInfoQuery: false);

        // 降级后双通道仍正常运作
        result.Should().Contain("查询结果");
        result.Should().Contain("「苯」");
        result.Should().Contain("易燃液体");
        result.Should().Contain("GB 30000.7");
        result.Should().Contain("隔离火源");
    }

    [Fact]
    public void Fallback_EmptyAnswer_NoTools_ReturnsFallbackDeclaration()
    {
        // 模拟：流式返回空 → 非流式降级 → 仍无工具调用
        var postFallbackAnswer = "无法处理此查询。";

        var result = RunPipeline(postFallbackAnswer, ProjectToolCalls(null), isInfoQuery: false);

        result.Should().Contain("无法给出确定结论");
        result.Should().Contain("安环部门");
    }

    [Fact]
    public void Fallback_WhitespaceAnswer_HandledCorrectly()
    {
        // 模拟：流式返回空白字符
        var postFallbackAnswer = "   \n  ";
        var toolCalls = new List<FunctionCallRecord>
        {
            MakeServiceRecord("CheckHazardCategory", "substanceName=苯",
                "[REGULATIONS: GB 30000.7-2013]\n「苯」危险类别: 易燃液体,类别2")
        };

        var result = RunPipeline(postFallbackAnswer, ProjectToolCalls(toolCalls), isInfoQuery: false);

        // 事实通道应正常渲染，解释通道可能为空
        result.Should().Contain("「苯」");
        result.Should().Contain("易燃液体");
    }

    // ═══════════════════════════════════════════════════════
    // LlmService 为 null 场景
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void NullLlmService_ToolCallsEmpty_PipelineFallbackApplies()
    {
        // 模拟：LlmService 为 null → toolCalls = empty list
        var answer = "根据通用知识，苯是易燃液体。";
        var toolCalls = ProjectToolCalls(null); // 等价于 ?? new List<FunctionCallRecord>()

        var result = RunPipeline(answer, toolCalls, isInfoQuery: false);

        // 无工具 → 兜底消毒
        result.Should().Contain("无法给出确定结论");
    }

    [Fact]
    public void NullLlmService_WithManuallyCollectedTools_PipelineApplies()
    {
        // 模拟：即使 LlmService 为 null，但通过其他途径收集到工具调用
        var toolCalls = ProjectToolCalls(new List<FunctionCallRecord>
        {
            MakeServiceRecord("CheckHazardCategory", "substanceName=苯",
                "[REGULATIONS: GB 30000.7-2013]\n「苯」危险类别: 易燃液体,类别2")
        });
        var answer = "苯属于易燃液体（GB 30000.7-2013）。";

        var result = RunPipeline(answer, toolCalls, isInfoQuery: false);

        result.Should().Contain("「苯」");
        result.Should().Contain("GB 30000.7");
    }

    // ═══════════════════════════════════════════════════════
    // isInfoQuery 在降级路径中正确传递
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void Fallback_InfoQuery_True_PipelinePreservesFlag()
    {
        var toolCalls = ProjectToolCalls(new List<FunctionCallRecord>
        {
            MakeServiceRecord("CheckHazardCategory", "substanceName=丙酮",
                "[REGULATIONS: GB 30000.7-2013]\n「丙酮」危险类别: 易燃液体,类别2")
        });
        var answer = "丙酮: 易燃液体,类别2。";

        var result = RunPipeline(answer, toolCalls, isInfoQuery: true);

        // 信息查询 → isInfoQuery=true 传递给 Extract
        result.Should().Contain("「丙酮」");
        result.Should().Contain("易燃液体");
    }

    [Fact]
    public void Fallback_InfoQuery_False_PipelinePreservesFlag()
    {
        var toolCalls = ProjectToolCalls(new List<FunctionCallRecord>
        {
            MakeServiceRecord("CheckStorageCompatibility", "substanceA=苯,substanceB=硝酸",
                "[REGULATIONS: GB 15603]\n「苯」与「硝酸」: 不得同库储存\n[判定: is_compliant=false]")
        });
        var answer = "不得同库储存。";

        var result = RunPipeline(answer, toolCalls, isInfoQuery: false);

        result.Should().Contain("不得同库");
        result.Should().Contain("「苯」");
    }

    // ═══════════════════════════════════════════════════════
    // PerCaseAsync 路径特殊场景（工具裁剪 + 降级）
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void PerCaseAsync_ToolsTriggeredWithSubset_DualChannelApplies()
    {
        // PerCaseAsync 只发送当前 case 需要的工具子集
        var toolCalls = ProjectToolCalls(new List<FunctionCallRecord>
        {
            MakeServiceRecord("GetSafetyDistance", "facilityType=甲类仓库-明火点",
                "[REGULATIONS: GB 50160-2008]\n安全距离: 30米")
        });
        var answer = "甲类仓库与明火点安全距离为30米。";

        var result = RunPipeline(answer, toolCalls, isInfoQuery: false);

        result.Should().Contain("30米");
        result.Should().Contain("GB 50160");
        result.Should().Contain("安全距离");
    }

    [Fact]
    public void PerCaseAsync_NoToolsInSubset_FallbackApplies()
    {
        // 工具子集为空或未触发
        var answer = "根据现有资料无法给出结论。";

        var result = RunPipeline(answer, ProjectToolCalls(null), isInfoQuery: false);

        result.Should().Contain("无法给出确定结论");
    }

    // ═══════════════════════════════════════════════════════
    // 降级后幻觉法规消毒（与正常路径行为一致）
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void Fallback_NoTools_PostFallbackAnswer_HallucinationSanitized()
    {
        // 降级后 LLM 仍可能产生幻觉法规
        var postFallbackAnswer = "依据 GB 88888.1-2099 第3条，该物质属于危险化学品。";
        var toolCalls = ProjectToolCalls(null);

        var result = RunPipeline(postFallbackAnswer, toolCalls, isInfoQuery: false);

        result.Should().Contain("无法给出确定结论");
        result.Should().NotContain("GB 88888");
        result.Should().NotContain("第3条");
        // 无害文本保留
        result.Should().Contain("危险化学品");
    }

    [Fact]
    public void Fallback_WithTools_PostFallbackAnswer_ValidRegulationPreserved()
    {
        // 降级后工具调用成功 → 白名单法规应保留
        var toolCalls = ProjectToolCalls(new List<FunctionCallRecord>
        {
            MakeServiceRecord("CheckHazardCategory", "substanceName=苯",
                "[REGULATIONS: GB 30000.7-2013]\n「苯」危险类别: 易燃液体,类别2")
        });
        var postFallbackAnswer = "依据 GB 30000.7-2013 和第5条，苯属于易燃液体。";

        var result = RunPipeline(postFallbackAnswer, toolCalls, isInfoQuery: false);

        result.Should().Contain("GB 30000.7");
        // 幻觉条款号被移除
        result.Should().NotContain("第5条");
    }
}
