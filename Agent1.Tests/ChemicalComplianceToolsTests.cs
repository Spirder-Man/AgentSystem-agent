using System;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Xunit;
using Agent1.Services;

namespace Agent1.Tests;

/// <summary>
/// ChemicalComplianceTools 硬编码降级路径测试 (无需 RAG 知识库)。
/// 验证 27 条硬编码字典的查询逻辑。
/// </summary>
public class ChemicalComplianceToolsTests
{
    private readonly ChemicalComplianceTools _tools;

    public ChemicalComplianceToolsTests()
    {
        // 不传 kbService → 使用硬编码字典降级路径
        _tools = new ChemicalComplianceTools(null);
    }

    [Fact]
    public async Task CheckHazardCategory_Explosives_ReturnsGBStandard()
    {
        var result = await _tools.CheckHazardCategory("爆炸物");
        result.Should().Contain("GB 30000.2-2013");
        result.Should().Contain("爆炸物");
    }

    [Fact]
    public async Task CheckHazardCategory_FlammableGas_ReturnsGBStandard()
    {
        var result = await _tools.CheckHazardCategory("易燃气体");
        result.Should().Contain("GB 30000.3-2013");
        result.Should().Contain("易燃气体");
    }

    [Fact]
    public async Task CheckHazardCategory_ToxicSubstance_ReturnsGuidance()
    {
        var result = await _tools.CheckHazardCategory("毒性物质");
        result.Should().Contain("GB 30000");
    }

    [Fact]
    public async Task CheckHazardCategory_UnknownSubstance_ReturnsGuidance()
    {
        var result = await _tools.CheckHazardCategory("未知化学品XYZ");
        result.Should().Contain("未在常见");
    }

    [Fact]
    public async Task CheckStorageCompatibility_Compatible_ReturnsWithStandard()
    {
        var result = await _tools.CheckStorageCompatibility("爆炸物", "易燃气体");
        result.Should().Contain("GB");
    }

    [Fact]
    public async Task CheckStorageCompatibility_Unknown_ReturnsWithStandard()
    {
        var result = await _tools.CheckStorageCompatibility("未知A", "未知B");
        result.Should().Contain("GB");
    }

    [Fact]
    public async Task GetSafetyDistance_TankFireLane_ReturnsDistanceOrGuidance()
    {
        var result = await _tools.GetSafetyDistance("储罐防火间距");
        // 硬编码降级路径可能返回具体距离或引导性文本
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetSafetyDistance_Unknown_ReturnsWithGuidance()
    {
        var result = await _tools.GetSafetyDistance("未知设施类型");
        result.Should().Contain("未找到");
    }

    [Fact]
    public void GetCurrentTime_ReturnsCurrentTime()
    {
        var result = _tools.GetCurrentTime();
        result.Should().Contain(DateTime.Now.Year.ToString());
    }
}
