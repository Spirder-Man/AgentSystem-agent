using System.Collections.Generic;
using Agent1.Services;
using FluentAssertions;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// OutputSanitizer 单元测试：LLM 输出硬拦截，白名单比对。
/// 纯逻辑测试，无需外部依赖。
/// </summary>
public class OutputSanitizerTests
{
    [Fact]
    public void Sanitize_LegitRegulation_Preserved()
    {
        var llmOutput = "苯属于易燃液体，依据 GB 30000.7-2013 分类。";
        var whitelist = new List<string> { "GB 30000.7-2013" };

        var result = OutputSanitizer.Sanitize(llmOutput, whitelist);

        result.Should().Contain("GB 30000.7");
    }

    [Fact]
    public void Sanitize_HallucinatedRegulation_Removed()
    {
        var llmOutput = "依据 GB 99999.1-2099 第3.2条规定，该化学品不可储存。";
        var whitelist = new List<string> { "GB 15603" };

        var result = OutputSanitizer.Sanitize(llmOutput, whitelist);

        result.Should().NotContain("GB 99999");
        result.Should().NotContain("第3.2条");
    }

    [Fact]
    public void Sanitize_MultipleHallucinations_AllRemoved()
    {
        var llmOutput = "参考 GB 30000.3-2020、GB 30000.6-2020 和 GB 15603-2023第5.3.2条。";
        var whitelist = new List<string> { "GB 15603" }; // 只信任 GB 15603

        var result = OutputSanitizer.Sanitize(llmOutput, whitelist);

        result.Should().NotContain("GB 30000.3");
        result.Should().NotContain("GB 30000.6");
        result.Should().Contain("GB 15603"); // 白名单中的保留
        result.Should().NotContain("5.3.2条"); // 条款号被剥离
    }

    [Fact]
    public void Sanitize_FormattedRegulation_Corrected()
    {
        var llmOutput = "依据 GB30000.7 规定";
        var whitelist = new List<string> { "GB 30000.7-2013" };

        var result = OutputSanitizer.Sanitize(llmOutput, whitelist);

        // 在白名单中 → 标准化格式
        result.Should().Contain("GB 30000.7");
    }

    [Fact]
    public void Sanitize_EmptyWhitelist_RemovesAllRegulations()
    {
        var llmOutput = "根据 GB 30000.7-2013 和 GB 15603 的规定";
        var whitelist = new List<string>();

        var result = OutputSanitizer.Sanitize(llmOutput, whitelist);

        result.Should().NotContain("GB 30000.7");
        result.Should().NotContain("GB 15603");
    }

    [Fact]
    public void Sanitize_NullWhitelist_RemovesAllRegulations()
    {
        var llmOutput = "根据 GB 30000.7-2013 的规定";

        var result = OutputSanitizer.Sanitize(llmOutput, null);

        result.Should().NotContain("GB 30000.7");
    }

    [Fact]
    public void Sanitize_EmptyInput_ReturnsEmpty()
    {
        var result = OutputSanitizer.Sanitize("", new List<string> { "GB 30000" });

        result.Should().BeEmpty();
    }

    [Fact]
    public void Sanitize_ClauseWithoutGB_Removed()
    {
        var llmOutput = "根据第5.3.2条规定，禁止同库储存。";
        var whitelist = new List<string>();

        var result = OutputSanitizer.Sanitize(llmOutput, whitelist);

        result.Should().NotContain("第5.3.2条");
    }

    [Fact]
    public void Sanitize_PrefixMatch_Preserved()
    {
        // 工具返回 "GB 30000.7"，LLM 写了 "GB 30000.7-2013" → 前缀匹配应保留
        var llmOutput = "依据 GB 30000.7-2013";
        var whitelist = new List<string> { "GB 30000.7" };

        var result = OutputSanitizer.Sanitize(llmOutput, whitelist);

        result.Should().Contain("30000.7");
    }

    [Fact]
    public void NormalizeGbNumber_VariousFormats_Corrected()
    {
        OutputSanitizer.NormalizeGbNumber("GB30000.7").Should().Be("GB 30000.7");
        OutputSanitizer.NormalizeGbNumber("GB 30000.7-2013").Should().Be("GB 30000.7-2013");
        OutputSanitizer.NormalizeGbNumber("GB/T 18218-2020").Should().Contain("GB/T 18218-2020");
    }
}
