using System;
using Agent1.Services;
using FluentAssertions;
using Xunit;

namespace Agent1.Tests;

public class SensitiveDataMaskerTests
{
    [Fact]
    public void Mask_PhoneNumber_MasksMiddleDigits()
    {
        SensitiveDataMasker.Mask("联系手机: 13812345678").Should().Be("联系手机: 138****5678");
    }

    [Fact]
    public void Mask_PhoneNumber_MultipleNumbers()
    {
        SensitiveDataMasker.Mask("电话: 13987654321 或 15811112222")
            .Should().Be("电话: 139****4321 或 158****2222");
    }

    [Fact]
    public void Mask_Email_MasksName()
    {
        SensitiveDataMasker.Mask("邮箱: zhangsan@example.com")
            .Should().Be("邮箱: z***n@example.com");
    }

    [Fact]
    public void Mask_Email_ShortName()
    {
        // 短邮箱名 (atIndex <= 2) 仅返回 "***" + domain
        SensitiveDataMasker.Mask("邮箱: ab@test.com")
            .Should().Be("邮箱: ***@test.com");
    }

    [Fact]
    public void Mask_IdCard_MasksMiddle()
    {
        // 18位身份证: 前6位 + **** + 后4位
        var result = SensitiveDataMasker.Mask("身份证号: 110101199001011234");
        result.Should().Contain("****");
        result.Should().StartWith("身份证号: 110101");
        result.Should().EndWith("1234");
    }

    [Fact]
    public void Mask_ApiKey_Redacts()
    {
        SensitiveDataMasker.Mask("Authorization: api_key=sk-this-is-a-very-long-secret-key-abcdefg")
            .Should().Contain("***API_KEY_REDACTED***");
    }

    [Fact]
    public void Mask_ApiKey_TokenFormat()
    {
        SensitiveDataMasker.Mask("token: very-long-secret-token-value-here")
            .Should().Contain("***API_KEY_REDACTED***");
    }

    [Fact]
    public void Mask_LongQuery_Truncates()
    {
        var longText = new string('A', 600);
        var result = SensitiveDataMasker.Mask(longText);
        result.Length.Should().BeLessThan(600);
        result.Should().Contain("[截断/原始长度600]");
    }

    [Fact]
    public void Mask_ShortQuery_NotTruncated()
    {
        var shortText = "苯和丙酮能同库储存吗";
        SensitiveDataMasker.Mask(shortText).Should().Be(shortText);
    }

    [Fact]
    public void Mask_Null_ReturnsEmpty()
    {
        SensitiveDataMasker.Mask(null).Should().Be("");
    }

    [Fact]
    public void Mask_Whitespace_ReturnsSame()
    {
        SensitiveDataMasker.Mask("   ").Should().Be("   ");
    }

    [Fact]
    public void MaskChemicalQuery_DelegatesToMask()
    {
        var result = SensitiveDataMasker.MaskChemicalQuery("联系13800001111查询苯的危化品分类");
        result.Should().Contain("138****1111");
    }
}
