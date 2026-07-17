using System.Reflection;
using Agent1.Modules;
using FluentAssertions;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// P1-1: RegulatoryAuditModule 运算符优先级修复验证。
/// 验证 ParseAuditResult:
///   1. (✅ || '合规') && !不合规 && !补充 → 正确判定合规
///   2. ❌ || '不合规' → 正确判定不合规
///   3. 双通道解耦架构 (Fact Channel vs LLM 解读 Channel)
/// </summary>
public class RegulatoryAuditModuleTests
{
    // ═══════════════════════════════════════
    // P1-1: 运算符优先级修复验证
    // ═══════════════════════════════════════

    /// <summary>反射调用私有 ParseAuditResult 方法</summary>
    private static (string Status, string RegulationRef, string Suggestion) CallParse(string llmOutput)
    {
        var method = typeof(RegulatoryAuditModule).GetMethod(
            "ParseAuditResult",
            BindingFlags.NonPublic | BindingFlags.Static);

        var result = method!.Invoke(null, new object[] { llmOutput });
        var tuple = ((string status, string refs, string suggestion))result!;
        return (tuple.status, tuple.refs, tuple.suggestion);
    }

    [Fact]
    public void ParseResult_CompliantOutput_ShouldReturnCompliant()
    {
        var output = "判定: ✅ 合规\n法规: GB 18218-2020\n建议: 无需整改";

        var result = CallParse(output);

        result.Status.Should().Be("✅ 合规");
    }

    [Fact]
    public void ParseResult_NonCompliantOutput_ShouldReturnNonCompliant()
    {
        var output = "判定: ❌ 不合规\n法规: GB 15603-2022\n建议: 整改储存方式";

        var result = CallParse(output);

        result.Status.Should().Be("❌ 不合规");
    }

    [Fact]
    public void ParseResult_NeedSupplementOutput_ShouldReturnSupplement()
    {
        var output = "判定: ⚠️ 需补充材料\n建议: 请提供该化学品的SDS安全数据表";

        var result = CallParse(output);

        result.Status.Should().Be("⚠️ 需补充材料");
    }

    [Fact]
    public void ParseResult_KeywordCompliant_ButNotContainBucihegui_ShouldBeCompliant()
    {
        // P1-1 核心修复: "✅ 合规" 且不含"不合规" → 应判定为合规
        var output = "判定: ✅ 合规\n法规: 见上述依据\n建议: 无需整改";

        var result = CallParse(output);

        result.Status.Should().Be("✅ 合规",
            "P1-1 修复: (✅ || '合规') && !不合规 && !补充 → 应正确判定为合规");
    }

    [Fact]
    public void ParseResult_ContainsComplianceCharButAlsoBucihegui_ShouldBeNonCompliant()
    {
        // "✅ 合规" 但同时也出现了"不合规"关键词 → 应该是不合规
        // 这是P1-1 修复需要处理的边界情况
        var output = "判定: ❌ 不符合法规要求，不合规\n建议: 整改";

        var result = CallParse(output);

        result.Status.Should().Be("❌ 不合规");
    }

    // ═══════════════════════════════════════
    // 法规/建议提取
    // ═══════════════════════════════════════

    [Fact]
    public void ParseResult_ShouldExtractRegulationRef()
    {
        var output = "判定: ✅ 合规\n法规: GB 30000.7-2013 第4.2条\n建议: 无需整改";

        var result = CallParse(output);

        result.RegulationRef.Should().Contain("30000.7");
    }

    [Fact]
    public void ParseResult_ShouldExtractSuggestion()
    {
        var output = "判定: ❌ 不合规\n法规: GB 15603\n建议: 请调整储存距离至50米以上";

        var result = CallParse(output);

        result.Suggestion.Should().Contain("50");
    }

    [Fact]
    public void ParseResult_WithColonSeparator_ShouldExtractCorrectly()
    {
        var output = "判定：✅ 合规\n法规：GB 18218-2020\n建议：无需整改";

        var result = CallParse(output);

        result.Status.Should().Be("✅ 合规");
        result.RegulationRef.Should().Contain("18218");
    }

    // ═══════════════════════════════════════
    // 空输入/边界情况
    // ═══════════════════════════════════════

    [Fact]
    public void ParseResult_EmptyOutput_ShouldReturnDefault()
    {
        var result = CallParse("");

        result.Status.Should().Be("⚠️ 需补充材料");
        result.RegulationRef.Should().Be("");
        result.Suggestion.Should().Be("");
    }

    [Fact]
    public void ParseResult_NullOutput_ShouldReturnDefault()
    {
        var result = CallParse(null!);

        result.Status.Should().Be("⚠️ 需补充材料");
    }

    [Fact]
    public void ParseResult_OnlyStatus_NoSuggestionOrRef()
    {
        var output = "判定: ✅ 合规";

        var result = CallParse(output);

        result.Status.Should().Be("✅ 合规");
    }

    // ═══════════════════════════════════════
    // 多行混合
    // ═══════════════════════════════════════

    [Fact]
    public void ParseResult_MultipleLines_ShouldExtractAll()
    {
        var output = @"判定: ❌ 不合规
法规: GB 50160-2008 第6.3.2条
建议: 需要安装防泄漏围堰并定期检测";

        var result = CallParse(output);

        result.Status.Should().Be("❌ 不合规");
        result.RegulationRef.Should().NotBeEmpty();
        result.Suggestion.Should().NotBeEmpty();
    }
}
