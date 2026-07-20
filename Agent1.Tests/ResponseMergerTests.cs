using Agent1.Services;
using FluentAssertions;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// ResponseMerger 单元测试：双通道合并逻辑验证。
/// 纯逻辑测试，无需外部依赖。
/// </summary>
public class ResponseMergerTests
{
    [Fact]
    public void Merge_BothNonEmpty_FactBeforeExplanation()
    {
        var factOutput = "━━━ 查询结果 ━━━\n「苯」危险类别: 易燃液体,类别2 [法规依据: GB 30000.7]";
        var explanation = "根据查询结果，苯属于易燃液体，应储存在阴凉通风处。";

        var result = ResponseMerger.Merge(factOutput, explanation);

        result.Should().Contain("查询结果");
        result.Should().Contain("「苯」");
        result.Should().Contain("阴凉通风处");
        // 事实通道应在解释通道之前
        result.IndexOf("查询结果").Should().BeLessThan(result.IndexOf("阴凉通风处"));
    }

    [Fact]
    public void Merge_EmptyFact_ReturnsExplanationUnchanged()
    {
        var explanation = "根据现有资料，无法给出确定结论。";

        var result = ResponseMerger.Merge("", explanation);

        result.Should().Be(explanation);
    }

    [Fact]
    public void Merge_NullFact_ReturnsExplanation()
    {
        var explanation = "请咨询安环部门。";

        var result = ResponseMerger.Merge(null!, explanation);

        result.Should().Be(explanation);
    }

    [Fact]
    public void Merge_EmptyExplanation_ReturnsFactUnchanged()
    {
        var factOutput = "━━━ 查询结果 ━━━\n「硝酸」与「乙酸」: 不得同库储存";

        var result = ResponseMerger.Merge(factOutput, "");

        result.Should().Be(factOutput);
    }

    [Fact]
    public void Merge_NullExplanation_ReturnsFactUnchanged()
    {
        var factOutput = "━━━ 查询结果 ━━━\n甲类仓库-明火点的安全距离: 30米";

        var result = ResponseMerger.Merge(factOutput, null!);

        result.Should().Be(factOutput);
    }

    [Fact]
    public void Merge_CleansLeadingSeparatorLines()
    {
        // LLM 有时会在输出开头重复生成分隔线
        var factOutput = "━━━ 查询结果 ━━━\n「苯」危险类别: 易燃液体";
        var explanation = "━━━ 分析建议 ━━━\n\n苯属于易燃液体，应注意防火。";

        var result = ResponseMerger.Merge(factOutput, explanation);

        // LLM 输出的分隔线应被清理
        result.Should().NotContain("分析建议");
        result.Should().Contain("苯属于易燃液体");
    }

    [Fact]
    public void Merge_CleansLeadingEmptyLines()
    {
        var factOutput = "━━━ 查询结果 ━━━\n事实文本";
        var explanation = "\n\n\n专业解读内容";

        var result = ResponseMerger.Merge(factOutput, explanation);

        // 前导空行应被清理
        result.Should().NotStartWith("\n");
        result.Should().Contain("专业解读内容");
    }

    [Fact]
    public void Merge_BothWhitespace_ReturnsTrimmedOrEmpty()
    {
        var result = ResponseMerger.Merge("   ", "\n  ");

        result.Should().NotBeNull();
        // 空白事实走空路径 → 返回解释原文（非 null）
        result.Trim().Should().BeEmpty("空白输入的处理结果 trim 后应为空");
    }
}
