using System;
using Agent1.Models;
using Agent1.Services;
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

    // ═══════════════════════════════════
    // P2-1: Emergency 应急响应意图
    // ═══════════════════════════════════

    [Theory]
    [InlineData("化学品泄漏应急处理")]
    [InlineData("实验室发生火灾怎么办")]
    [InlineData("苯泄漏了需要疏散吗")]
    [InlineData("危险品爆炸应急预案")]
    [InlineData("需要什么PPE防护装备")]
    [InlineData("化学品泄漏事故处理方法")]
    public void Route_EmergencyKeywords_ReturnsEmergency(string input)
    {
        IntentRouter.Route(input).Should().Be(IntentType.Emergency);
    }

    [Fact]
    public void Route_EmergencyKeywordSetsLastMatchedKeyword()
    {
        // EmergencyKeywords 数组中 "应急" 在 "泄漏" 之前，FirstOrDefault 返回先匹配的
        IntentRouter.Route("氯气泄漏应急预案");
        IntentRouter.LastMatchedKeyword.Should().Be("应急");
    }

    // ═══════════════════════════════════
    // P2-1: RegulatoryAudit 法规审计意图
    // ═══════════════════════════════════

    [Theory]
    [InlineData("合规审计")]
    [InlineData("法规核查")]
    [InlineData("合规检查")]
    [InlineData("监管要求是什么")]
    public void Route_RegulatoryAuditKeywords_ReturnsRegulatoryAudit(string input)
    {
        IntentRouter.Route(input).Should().Be(IntentType.RegulatoryAudit);
    }

    // ═══════════════════════════════════
    // P2-1: KnowledgeGraph 知识图谱意图
    // ═══════════════════════════════════

    [Theory]
    [InlineData("知识图谱查询苯的上下游关系")]
    [InlineData("图谱查询丙酮的关联物质")]
    [InlineData("实体关系查询")]
    public void Route_KnowledgeGraphKeywords_ReturnsKnowledgeGraph(string input)
    {
        IntentRouter.Route(input).Should().Be(IntentType.KnowledgeGraph);
    }

    // ═══════════════════════════════════
    // P2-1: 优先级验证
    // ═══════════════════════════════════

    [Fact]
    public void Route_EmergencyTakesPriorityOverCompliance()
    {
        // "泄漏" + "化学品" → Emergency 优先于 ChemicalCompliance
        IntentRouter.Route("化学品泄漏了怎么办").Should().Be(IntentType.Emergency);
    }

    [Fact]
    public void Route_AuditTakesPriorityOverCompliance()
    {
        // "审计" + "化学品" → RegulatoryAudit 优先于 ChemicalCompliance
        IntentRouter.Route("化学品合规审计").Should().Be(IntentType.RegulatoryAudit);
    }

    [Fact]
    public void Route_KnowledgeGraphTakesPriorityOverCompliance()
    {
        // "知识图谱" + "化学品" → KnowledgeGraph 优先于 ChemicalCompliance
        IntentRouter.Route("化学品知识图谱查询").Should().Be(IntentType.KnowledgeGraph);
    }

    [Fact]
    public void Route_EmergencyTakesTopPriority()
    {
        // "泄漏" + "审计" + "知识图谱" → Emergency 最高优先级
        IntentRouter.Route("泄漏事故的知识图谱审计查询").Should().Be(IntentType.Emergency);
    }

    // ═══════════════════════════════════
    // 完整优先级链: Emergency > Audit > KG > Compliance > Chat
    // ═══════════════════════════════════

    [Fact]
    public void Route_FullPriorityChain()
    {
        IntentRouter.Route("化学品泄漏应急响应").Should().Be(IntentType.Emergency);
        IntentRouter.Route("化学品合规审计报告").Should().Be(IntentType.RegulatoryAudit);
        IntentRouter.Route("化学品知识图谱实体").Should().Be(IntentType.KnowledgeGraph);
        IntentRouter.Route("化学品合规分类查询").Should().Be(IntentType.ChemicalCompliance);
        IntentRouter.Route("你好，今天天气不错").Should().Be(IntentType.SimpleChat);
    }
}
