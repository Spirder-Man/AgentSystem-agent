using System.Collections.Generic;
using System.Threading.Tasks;
using Agent1.Config;
using Agent1.Services;
using Moq;
using Xunit;
using FluentAssertions;

public class ToolServiceTests
{
    private static ILlmService CreateLlmStub()
    {
        var mock = new Mock<ILlmService>();
        return mock.Object;
    }

    private static IKnowledgeBaseService CreateKbStub()
    {
        var mock = new Mock<IKnowledgeBaseService>();
        return mock.Object;
    }

    private static List<ToolDefinition> CreateChemicalToolDefinitions()
    {
        return new List<ToolDefinition>
        {
            new() { Name = "CheckHazardCategory", Description = "查询危化品危险类别及适用国标", KeywordTriggers = new() { "类别", "分类", "属于", "国标", "GB" } },
            new() { Name = "CheckStorageCompatibility", Description = "检查两种危化品是否可同库储存", KeywordTriggers = new() { "同库", "共存", "混合", "禁忌", "配伍" } },
            new() { Name = "GetSafetyDistance", Description = "查询设施间安全间距要求", KeywordTriggers = new() { "安全距离", "间距", "消防通道", "储罐间距" } },
            new() { Name = "GetCurrentTime", Description = "获取当前时间", KeywordTriggers = new() { "时间", "几点", "日期" } },
            new() { Name = "Calculate", Description = "数学计算", KeywordTriggers = new() { "计算", "等于" } },
        };
    }

    [Fact]
    public async Task AnalyzeAndPlanToolsAsync_DetectsTools()
    {
        var llm = CreateLlmStub();
        var kb = CreateKbStub();
        var tools = CreateChemicalToolDefinitions();
        var service = new ToolService(llm, kb, tools);

        var hazardPlan = await service.AnalyzeAndPlanToolsAsync("苯属于什么危险类别", "");
        hazardPlan.NeedsTools.Should().BeTrue();
        hazardPlan.ToolNames.Should().Contain("CheckHazardCategory");

        var timePlan = await service.AnalyzeAndPlanToolsAsync("请问当前时间是几点了", "");
        timePlan.NeedsTools.Should().BeTrue();
        timePlan.ToolNames.Should().Contain("GetCurrentTime");

        var nonePlan = await service.AnalyzeAndPlanToolsAsync("你好，今天天气怎么样？", "");
        nonePlan.NeedsTools.Should().BeFalse();
    }
}