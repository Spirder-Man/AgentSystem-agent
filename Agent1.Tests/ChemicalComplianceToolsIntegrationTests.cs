// Phase 5: ChemicalComplianceTools integration tests with StubKnowledgeBaseService
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Agent1.Services;
using FluentAssertions;
using Xunit;

namespace Agent1.Tests;

public class ChemicalComplianceToolsIntegrationTests
{
    [Fact]
    public async Task CheckHazardCategory_Benzene_ReturnsResult()
    {
        var tools = new ChemicalComplianceTools(lazyKb: null, chemDb: null);
        var result = await tools.CheckHazardCategory("苯");
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CheckHazardCategory_Unknown_ReturnsFallback()
    {
        var tools = new ChemicalComplianceTools(lazyKb: null, chemDb: null);
        var result = await tools.CheckHazardCategory("XYZNotExist123");
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CheckStorageCompatibility_ReturnsResult()
    {
        var tools = new ChemicalComplianceTools(lazyKb: null, chemDb: null);
        var result = await tools.CheckStorageCompatibility("硝酸", "甲醇");
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetSafetyDistance_Known_ReturnsResult()
    {
        var tools = new ChemicalComplianceTools(lazyKb: null, chemDb: null);
        var result = await tools.GetSafetyDistance("储罐-储罐");
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetSafetyDistance_Unknown_ReturnsFallback()
    {
        var tools = new ChemicalComplianceTools(lazyKb: null, chemDb: null);
        var result = await tools.GetSafetyDistance("XYZ-Unknown-Facility");
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void CheckRegulationVersion_Known_ReturnsResult()
    {
        var tools = new ChemicalComplianceTools(lazyKb: null, chemDb: null);
        var result = tools.CheckRegulationVersion("GB 15603");
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void CheckRegulationVersion_Unknown_ReturnsResult()
    {
        var tools = new ChemicalComplianceTools(lazyKb: null, chemDb: null);
        var result = tools.CheckRegulationVersion("GB 99999");
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task LookupChemicalProperties_Benzene_ReturnsResult()
    {
        var tools = new ChemicalComplianceTools(lazyKb: null, chemDb: null);
        var result = await tools.LookupChemicalProperties("苯");
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task LookupChemicalProperties_Unknown_ReturnsResult()
    {
        var tools = new ChemicalComplianceTools(lazyKb: null, chemDb: null);
        var result = await tools.LookupChemicalProperties("XYZ-999");
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetMajorHazardThreshold_Benzene_ReturnsResult()
    {
        var tools = new ChemicalComplianceTools(lazyKb: null, chemDb: null);
        var result = await tools.GetMajorHazardThreshold("苯");
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetMajorHazardThreshold_Unknown_ReturnsResult()
    {
        var tools = new ChemicalComplianceTools(lazyKb: null, chemDb: null);
        var result = await tools.GetMajorHazardThreshold("XYZ-Not-Found");
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetCurrentTime_ReturnsResult()
    {
        var tools = new ChemicalComplianceTools(lazyKb: null, chemDb: null);
        var result = tools.GetCurrentTime();
        result.Should().Contain(DateTime.Now.Year.ToString());
    }

    [Fact]
    public void Calculate_SimpleAdd_ReturnsResult()
    {
        var tools = new ChemicalComplianceTools(lazyKb: null, chemDb: null);
        var result = tools.Calculate("10+5");
        result.Should().Contain("15");
    }

    [Fact]
    public void Calculate_InvalidExpression_ReturnsResult()
    {
        var tools = new ChemicalComplianceTools(lazyKb: null, chemDb: null);
        var result = tools.Calculate("xyz");
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task MultipleCalls_DifferentSubstances_AllSucceed()
    {
        var tools = new ChemicalComplianceTools(lazyKb: null, chemDb: null);
        var r1 = await tools.CheckHazardCategory("苯");
        var r2 = await tools.CheckHazardCategory("甲醇");
        r1.Should().NotBeNullOrEmpty();
        r2.Should().NotBeNullOrEmpty();
    }
}
