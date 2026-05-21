using System.Threading.Tasks;
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

    [Fact]
    public async Task AnalyzeAndPlanToolsAsync_DetectsTools()
    {
        var llm = CreateLlmStub();
        var service = new ToolService(llm);

        var tempPlan = await service.AnalyzeAndPlanToolsAsync("主轴 温度 是否 超过 阈值", "");
        tempPlan.NeedsTools.Should().BeTrue();
        tempPlan.ToolNames.Should().Contain("GetSpindleTemperature");

        var timePlan = await service.AnalyzeAndPlanToolsAsync("请问 当前 时间 是 多少", "");
        timePlan.NeedsTools.Should().BeTrue();
        timePlan.ToolNames.Should().Contain("GetCurrentTime");

        var nonePlan = await service.AnalyzeAndPlanToolsAsync("你好，今天天气怎么样？", "");
        nonePlan.NeedsTools.Should().BeFalse();
    }
}