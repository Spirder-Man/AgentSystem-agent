using Agent1.Services;
using FluentAssertions;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// PromptSanitizer 单元测试：剥离传给 LLM 的工具结果中的法规编号。
/// 纯逻辑测试，无需外部依赖。
/// </summary>
public class PromptSanitizerTests
{
    [Fact]
    public void SanitizeToolResult_RegulationsTag_Removed()
    {
        var toolResult = "[REGULATIONS: GB 30000.7-2013, GB 30000.8-2013]\n「苯」危险类别: 易燃液体,类别2 [判定:is_compliant=unknown]";

        var result = PromptSanitizer.SanitizeToolResult(toolResult);

        result.Should().NotContain("[REGULATIONS:");
        result.Should().NotContain("GB 30000.7");
        result.Should().NotContain("GB 30000.8");
        result.Should().NotContain("[判定:");
        // 语义信息保留
        result.Should().Contain("易燃液体");
        result.Should().Contain("类别2");
    }

    [Fact]
    public void SanitizeToolResult_PlainText_Unchanged()
    {
        var toolResult = "该化学品属于易燃液体类别，闪点为-11°C。";

        var result = PromptSanitizer.SanitizeToolResult(toolResult);

        result.Should().Be(toolResult);
    }

    [Fact]
    public void SanitizeToolResult_EmptyOrNull_ReturnsAsIs()
    {
        PromptSanitizer.SanitizeToolResult("").Should().Be("");
        PromptSanitizer.SanitizeToolResult(null!).Should().Be("");
    }

    [Fact]
    public void SanitizeToolResult_ClauseNumber_Removed()
    {
        var toolResult = "依据 GB15603 第5.3.2条规定";

        var result = PromptSanitizer.SanitizeToolResult(toolResult);

        result.Should().NotContain("GB15603");
        result.Should().NotContain("第5.3.2条");
    }

    [Fact]
    public void SanitizeToolResult_GBWithT_Removed()
    {
        var toolResult = "参考标准 GB/T 18218-2020，该物质临界量为50吨";

        var result = PromptSanitizer.SanitizeToolResult(toolResult);

        result.Should().NotContain("GB/T 18218");
        result.Should().Contain("50吨");
    }

    [Fact]
    public void SanitizeSystemPrompt_RegulationDirective_Replaced()
    {
        var systemPrompt = "【法规依据】引用具体标准编号+条款\n【违规点】若无违规写「无」";

        var result = PromptSanitizer.SanitizeSystemPrompt(systemPrompt);

        // 【法规依据】行被整个移除或替换，最终不应残留法规引用指令
        result.Should().NotContain("【法规依据】");
        result.Should().NotContain("引用具体标准编号+条款");
        result.Should().Contain("【违规点】"); // 其他字段保留
    }

    [Fact]
    public void SanitizeSystemPrompt_RegulationRefPattern_Replaced()
    {
        var systemPrompt = "请引用具体标准编号+条款作为依据";

        var result = PromptSanitizer.SanitizeSystemPrompt(systemPrompt);

        result.Should().NotContain("引用具体标准编号+条款");
        result.Should().Contain("已知事实");
    }

    [Fact]
    public void SanitizeSystemPrompt_NoRegulationDirective_Unchanged()
    {
        var systemPrompt = "你是化工园区危化品合规审核专家。请给出专业分析。";

        var result = PromptSanitizer.SanitizeSystemPrompt(systemPrompt);

        // SanitizeSystemPrompt 会追加合规约束指令，原始内容应保留
        result.Should().Contain(systemPrompt);
    }

    [Fact]
    public void SanitizeSystemPrompt_EmptyOrNull_ReturnsAsIs()
    {
        PromptSanitizer.SanitizeSystemPrompt("").Should().Be("");
        PromptSanitizer.SanitizeSystemPrompt(null!).Should().Be("");
    }
}
