using System;
using FluentAssertions;
using Xunit;

namespace Agent1.Tests;

public class IntentRouterTests
{
    [Theory]
    [InlineData("苯属于什么危险类别", IntentType.ChemicalCompliance)]
    [InlineData("甲类仓库与明火点的安全距离是多少", IntentType.ChemicalCompliance)]
    [InlineData("苯和丙酮能同库储存吗", IntentType.ChemicalCompliance)]
    [InlineData("GB 18218的重大危险源标准是什么", IntentType.ChemicalCompliance)]
    [InlineData("这个化学品有毒性和腐蚀性", IntentType.ChemicalCompliance)]
    [InlineData("储罐间距有什么要求", IntentType.ChemicalCompliance)]
    [InlineData("易燃气体储存禁忌配伍", IntentType.ChemicalCompliance)]
    [InlineData("危险品分类标准", IntentType.ChemicalCompliance)]
    [InlineData("合规审核流程是什么", IntentType.ChemicalCompliance)]
    public void Route_ComplianceKeywords_ReturnsChemicalCompliance(string input, IntentType expected)
    {
        IntentRouter.Route(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("你好", IntentType.SimpleChat)]
    [InlineData("hello, how are you", IntentType.SimpleChat)]
    [InlineData("谢谢你的帮助", IntentType.SimpleChat)]
    [InlineData("我叫张三", IntentType.SimpleChat)]
    [InlineData("刚才那个问题再问一遍", IntentType.SimpleChat)]
    [InlineData("好的，明白了", IntentType.SimpleChat)]
    [InlineData("在吗？", IntentType.SimpleChat)]
    public void Route_ChatKeywords_ReturnsSimpleChat(string input, IntentType expected)
    {
        IntentRouter.Route(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("今天天气怎么样")]
    [InlineData("帮我写一首诗")]
    [InlineData("推荐一本书")]
    public void Route_NoComplianceKeywords_ReturnsNotCompliance(string input)
    {
        IntentRouter.Route(input).Should().NotBe(IntentType.ChemicalCompliance);
    }

    [Fact]
    public void Route_NullOrEmpty_ReturnsSimpleChat()
    {
        IntentRouter.Route(null!).Should().Be(IntentType.SimpleChat);
        IntentRouter.Route("").Should().Be(IntentType.SimpleChat);
        IntentRouter.Route("   ").Should().Be(IntentType.SimpleChat);
    }

    [Fact]
    public void Route_ComplianceKeywordTakesPriority_OverChatKeyword()
    {
        // "化学品" 是合规关键词，即便包含 "谢谢" 也应该是合规
        IntentRouter.Route("谢谢，请问这个化学品合规吗").Should().Be(IntentType.ChemicalCompliance);
    }
}
