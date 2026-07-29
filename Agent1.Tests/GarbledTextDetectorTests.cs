using Agent1.Services;
using FluentAssertions;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// [#5 FIX] GarbledTextDetector 单元测试 — 知识库乱码块过滤器。
/// 正例来自真实运行日志（api-20260728_104206.log）中国标 PDF 自定义字体编码
/// 提取产出的乱码块；反例为正常法规文本，确保不误杀。
/// </summary>
public class GarbledTextDetectorTests
{
    // ── 正例：真实日志乱码样本必须被拒收 ──

    [Fact]
    public void IsGarbled_RealLogSample1_ReturnsTrue()
    {
        var sample = "书书书!!!!!\"#$%&'\"(&'#)*+,-./$!\"!!\"#$0123456\"7\"89\":;<";

        var garbled = GarbledTextDetector.IsGarbled(sample, out var reason);

        garbled.Should().BeTrue();
        reason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void IsGarbled_RealLogSample2_ReturnsTrue()
    {
        var sample = "书书书!!\"!!!#$%!\"\"&'()*+,-.!!!\"#$%&'()*+!\"##/012,-./0";

        var garbled = GarbledTextDetector.IsGarbled(sample, out var reason);

        garbled.Should().BeTrue();
        reason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void IsGarbled_RepeatedChineseChar_Rule1_ReturnsTrue()
    {
        // 规则①：同字符连续重复 ≥4（"书书书书"形态）
        var garbled = GarbledTextDetector.IsGarbled("书书书书某某内容", out var reason);

        garbled.Should().BeTrue();
        reason.Should().Contain("连续重复");
    }

    [Fact]
    public void IsGarbled_MostlyInvalidChars_Rule2_ReturnsTrue()
    {
        // 规则②：非中文/字母/数字/常用标点占比 >40%（无连续重复，避开规则①）
        var garbled = GarbledTextDetector.IsGarbled("仓库␀¶Ω␀¶Ω␀¶Ω␀¶Ω距离", out var reason);

        garbled.Should().BeTrue();
        reason.Should().Contain("异常字符");
    }

    [Fact]
    public void IsGarbled_PureEnglishBlock_Rule3_ReturnsTrue()
    {
        // 规则③：中文占比 <20%（本知识库全部为中文法规文本）
        var sample = "The quick brown fox jumps over the lazy dog and runs far away.";

        var garbled = GarbledTextDetector.IsGarbled(sample, out var reason);

        garbled.Should().BeTrue();
        reason.Should().Contain("中文占比");
    }

    [Fact]
    public void IsGarbled_EmptyOrWhitespace_ReturnsTrue()
    {
        GarbledTextDetector.IsGarbled("", out _).Should().BeTrue();
        GarbledTextDetector.IsGarbled("   \n\t  ", out _).Should().BeTrue();
    }

    // ── 反例：正常法规文本不得误杀 ──

    [Fact]
    public void IsGarbled_NormalRegulationText_ReturnsFalse()
    {
        var text = "甲类仓库与明火作业点的防火间距不应小于30m，详见GB 50016-2014表3.4.1。";

        var garbled = GarbledTextDetector.IsGarbled(text, out var reason);

        garbled.Should().BeFalse();
        reason.Should().BeEmpty();
    }

    [Fact]
    public void IsGarbled_TextWithLongDigitRun_DigitExemption_ReturnsFalse()
    {
        // 规则①数字豁免：连续重复数字（如容积 10000m³）不触发误杀
        var text = "液化烃储罐总容积大于10000m³时，应设置独立的消防给水系统，储罐间距按GB 50160-2008第4.2.12条执行。";

        var garbled = GarbledTextDetector.IsGarbled(text, out var reason);

        garbled.Should().BeFalse();
        reason.Should().BeEmpty();
    }

    [Fact]
    public void IsGarbled_TextWithClauseNumbersAndPunctuation_ReturnsFalse()
    {
        // 中英标点混排 + 条款号 + 百分比/温度符号均在常用标点白名单内
        var text = "危险化学品储存温度不应超过37℃，相对湿度宜为45%～75%；出入库应执行双人收发、双人记账制度（GB 15603-2022第6.2条）。";

        var garbled = GarbledTextDetector.IsGarbled(text, out var reason);

        garbled.Should().BeFalse();
        reason.Should().BeEmpty();
    }

    [Fact]
    public void IsGarbled_ShortNormalChineseText_ReturnsFalse()
    {
        var garbled = GarbledTextDetector.IsGarbled("氢气储罐与办公楼的安全距离要求", out _);

        garbled.Should().BeFalse();
    }
}
