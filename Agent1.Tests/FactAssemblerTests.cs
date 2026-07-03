using System.Collections.Generic;
using Agent1.Models;
using Agent1.Services;
using FluentAssertions;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// FactAssembler 单元测试：确定性事实渲染器。
/// 纯 C# 模板引擎，不走 LLM，验证输出格式正确性。
/// </summary>
public class FactAssemblerTests
{
    [Fact]
    public void Build_HazardCategory_RendersCorrectFormat()
    {
        var facts = new ExtractedFacts
        {
            HazardCategories = new Dictionary<string, string>
            {
                ["苯"] = "易燃液体,类别2"
            },
            RegulationRefs = new List<string> { "GB 30000.7-2013" }
        };

        var output = FactAssembler.Build(facts);

        output.Should().Contain("「苯」");
        output.Should().Contain("易燃液体,类别2");
        output.Should().Contain("GB 30000.7");
        output.Should().Contain("法规依据");
    }

    [Fact]
    public void Build_StorageCompatibility_RendersVerdictAndRegulation()
    {
        var facts = new ExtractedFacts
        {
            ComplianceVerdicts = new Dictionary<string, string>
            {
                ["硝酸|乙酸"] = "不得同库储存"
            },
            RegulationRefs = new List<string> { "GB 15603" }
        };

        var output = FactAssembler.Build(facts);

        output.Should().Contain("「硝酸」");
        output.Should().Contain("「乙酸」");
        output.Should().Contain("不得同库储存");
        output.Should().Contain("GB 15603");
    }

    [Fact]
    public void Build_NoFacts_RendersStandardRefusal()
    {
        var facts = new ExtractedFacts();

        var output = FactAssembler.Build(facts);

        output.Should().Contain("无法给出确定结论");
        output.Should().Contain("安环部门");
    }

    [Fact]
    public void Build_NullFacts_RendersStandardRefusal()
    {
        var output = FactAssembler.Build(null);

        output.Should().Contain("无法给出确定结论");
    }

    [Fact]
    public void Build_SafetyDistance_RendersWithRegulation()
    {
        var facts = new ExtractedFacts
        {
            SafetyDistances = new Dictionary<string, string>
            {
                ["甲类仓库-明火点"] = "30米"
            },
            RegulationRefs = new List<string> { "GB 50016-2014" }
        };

        var output = FactAssembler.Build(facts);

        output.Should().Contain("30米");
        output.Should().Contain("GB 50016");
    }

    [Fact]
    public void Build_Threshold_RendersWithRegulation()
    {
        var facts = new ExtractedFacts
        {
            Thresholds = new Dictionary<string, string>
            {
                ["苯"] = "50吨"
            },
            RegulationRefs = new List<string> { "GB 18218-2018" }
        };

        var output = FactAssembler.Build(facts);

        output.Should().Contain("50吨");
        output.Should().Contain("重大危险源临界量");
        output.Should().Contain("GB 18218");
    }

    [Fact]
    public void Build_MultipleCategories_RendersAll()
    {
        var facts = new ExtractedFacts
        {
            HazardCategories = new Dictionary<string, string>
            {
                ["苯"] = "易燃液体,类别2",
                ["硝酸"] = "氧化性液体,类别2"
            },
            SafetyDistances = new Dictionary<string, string>
            {
                ["储罐-建筑"] = "25米"
            },
            RegulationRefs = new List<string> { "GB 30000.7-2013", "GB 30000.14-2013", "GB 50160-2008" }
        };

        var output = FactAssembler.Build(facts);

        output.Should().Contain("「苯」");
        output.Should().Contain("「硝酸」");
        output.Should().Contain("25米");
        // 法规引用应该去重并列出
        output.Should().Contain("GB 30000");
    }
}
