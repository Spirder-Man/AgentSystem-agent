using System.Collections.Generic;
using Agent1.Services;
using FluentAssertions;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// P1-2: GB 法规编号正则统一验证。
/// 验证 GbCodeHelper.ExtractGbCodes / NormalizeGbCode / IsInWhitelist
/// 在全链路 6 处引用中的一致性。
/// </summary>
public class GbCodeHelperTests
{
    // ═══════════════════════════════════════
    // ExtractGbCodes: 标准格式提取
    // ═══════════════════════════════════════

    [Theory]
    [InlineData("GB/T 18218-2020")]
    [InlineData("GB 30000.7-2013")]
    [InlineData("GB30000.7")]
    [InlineData("GB 15603")]
    [InlineData("GB 12345.6-2020")]
    [InlineData("GB 50160-2008")]
    [InlineData("GB/T 16483-2008")]
    public void ExtractGbCodes_StandardFormats_ShouldExtract(string input)
    {
        var codes = GbCodeHelper.ExtractGbCodes(input);
        codes.Should().NotBeEmpty("should extract GB code from standard format");
    }

    [Fact]
    public void ExtractGbCodes_CompoundText_ShouldExtractMultiple()
    {
        var text = "依据 GB 30000.7-2013 和 GB/T 18218-2020 以及 GB 15603 的规定";
        var codes = GbCodeHelper.ExtractGbCodes(text);

        codes.Should().HaveCountGreaterThanOrEqualTo(3);
        codes.Should().Contain(c => c.Contains("30000.7"));
        codes.Should().Contain(c => c.Contains("18218"));
        codes.Should().Contain(c => c.Contains("15603"));
    }

    [Fact]
    public void ExtractGbCodes_NoGbCode_ShouldReturnEmpty()
    {
        var codes = GbCodeHelper.ExtractGbCodes("这是一段不含法规编号的文本");

        codes.Should().BeEmpty();
    }

    [Fact]
    public void ExtractGbCodes_EmptyOrNull_ShouldReturnEmpty()
    {
        GbCodeHelper.ExtractGbCodes("").Should().BeEmpty();
        GbCodeHelper.ExtractGbCodes(null!).Should().BeEmpty();
    }

    [Fact]
    public void ExtractGbCodes_DeduplicatesDuplicates()
    {
        var text = "GB 30000.7-2013 GB 30000.7-2013 GB 30000.7-2013";
        var codes = GbCodeHelper.ExtractGbCodes(text);

        codes.Should().HaveCount(1);
    }

    // ═══════════════════════════════════════
    // NormalizeGbCode: 标准化
    // ═══════════════════════════════════════

    [Theory]
    [InlineData("GB30000.7", "GB 30000.7")]
    [InlineData("GB/T 18218-2020", "GB/T 18218-2020")]
    [InlineData("GB  15603", "GB 15603")]
    [InlineData("gb 30000.7-2013", "gb 30000.7-2013")]
    public void NormalizeGbCode_ShouldNormalize(string input, string expected)
    {
        var result = GbCodeHelper.NormalizeGbCode(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void NormalizeGbCode_EmptyOrNull_ShouldReturnEmpty(string input)
    {
        var result = GbCodeHelper.NormalizeGbCode(input);

        result.Should().Be(input ?? "");
    }

    // ═══════════════════════════════════════
    // IsInWhitelist: 白名单匹配
    // ═══════════════════════════════════════

    [Fact]
    public void IsInWhitelist_EmptyWhitelist_ShouldReturnTrue()
    {
        GbCodeHelper.IsInWhitelist("GB 99999-2099", new HashSet<string>())
            .Should().BeTrue("empty whitelist means no restriction");
    }

    [Fact]
    public void IsInWhitelist_ExactMatch_ShouldReturnTrue()
    {
        var whitelist = new HashSet<string> { "GB 30000.7-2013", "GB 15603" };

        GbCodeHelper.IsInWhitelist("GB 30000.7-2013", whitelist).Should().BeTrue();
    }

    [Fact]
    public void IsInWhitelist_PrefixMatch_ShouldReturnTrue()
    {
        var whitelist = new HashSet<string> { "GB 30000.7" };

        // 白名单里有 "GB 30000.7"，输入 "GB 30000.7-2013" 应该通过前缀匹配
        GbCodeHelper.IsInWhitelist("GB 30000.7-2013", whitelist).Should().BeTrue();
    }

    [Fact]
    public void IsInWhitelist_NoSpaceVariant_ShouldMatch()
    {
        var whitelist = new HashSet<string> { "GB 30000.7-2013" };

        GbCodeHelper.IsInWhitelist("GB30000.7-2013", whitelist).Should().BeTrue();
    }

    [Fact]
    public void IsInWhitelist_NotInList_ShouldReturnFalse()
    {
        var whitelist = new HashSet<string> { "GB 15603", "GB 50160-2008" };

        GbCodeHelper.IsInWhitelist("GB 99999-2099", whitelist).Should().BeFalse();
    }

    // ═══════════════════════════════════════
    // GbCodePattern: 正则匹配完整性
    // ═══════════════════════════════════════

    [Theory]
    [InlineData("GB 30000.7-2013")]       // 带年份
    [InlineData("GB/T 18218-2020")]       // 推荐性标准
    [InlineData("GB30000.7")]             // 无空格
    [InlineData("GB 15603")]              // 短编号
    [InlineData("GB 12345.6-2020")]       // 子编号
    [InlineData("GB 50160-2008")]         // 5位编号
    [InlineData("GB/T 16483-2008")]       // 推荐+5位+年份
    [InlineData("GB 30871-2022")]         // 5位编号+年份
    public void GbCodePattern_ShouldMatch_StandardVariants(string input)
    {
        GbCodeHelper.GbCodePattern.IsMatch(input).Should().BeTrue(
            $"pattern should match standard GB code: {input}");
    }

    [Fact]
    public void GbCodePattern_ShouldNotMatch_RandomNumbers()
    {
        GbCodeHelper.GbCodePattern.IsMatch("12345-2020").Should().BeFalse();
        GbCodeHelper.GbCodePattern.IsMatch("ABC 12345").Should().BeFalse();
    }
}
