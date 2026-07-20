using System;
using System.Collections.Generic;
using System.Reflection;
using Agent1.Models;
using Agent1.Modules;
using Agent1.Services;
using FluentAssertions;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// P5b: RegulatoryAudit / ComplianceCheck / EmergencyResponse 纯逻辑测试
///
/// 聚焦无 LLM 依赖的 private static 方法（通过反射调用）：
///   - RegulatoryAuditModule.ParseAuditResult: LLM 输出解析 → (status, refs, suggestion)
///   - EmergencyResponseService.CalculateEvacuationZones: ERG 疏散距离
///   - EmergencyResponseService.RecommendPpe: PPE 等级推荐
///   - EmergencyResponseService.SelectFireMedia: 灭火介质选择
///   - EmergencyResponseService.HasHazard: 危险类别关键词匹配
/// </summary>
public class RegulatoryEmergencyModuleTests
{
    // ═══════════════════════════════════════
    // ParseAuditResult: LLM 输出解析
    // ═══════════════════════════════════════

    [Fact]
    public void ParseAuditResult_Compliant()
    {
        var (status, refs, suggestion) = CallParseAuditResult(
            "判定: ✅ 合规\n法规: GB 50016-2014\n建议: 无需整改");

        status.Should().Be("✅ 合规");
        refs.Should().Contain("GB 50016");
        suggestion.Should().Contain("无需整改");
    }

    [Fact]
    public void ParseAuditResult_NonCompliant()
    {
        var (status, refs, suggestion) = CallParseAuditResult(
            "判定: ❌ 不合规\n法规: GB 15603-1995 §4.2.2\n建议: 立即清理消防通道");

        status.Should().Be("❌ 不合规");
        refs.Should().Contain("GB 15603");
        suggestion.Should().Contain("清理消防通道");
    }

    [Fact]
    public void ParseAuditResult_NeedsReview()
    {
        var (status, refs, _) = CallParseAuditResult(
            "判定: ⚠️ 需补充材料\n法规: 缺少应急处置预案");

        status.Should().Be("⚠️ 需补充材料");
    }

    [Fact]
    public void ParseAuditResult_EmptyOrNull_DefaultsToNeedsReview()
    {
        var (s1, _, _) = CallParseAuditResult("");
        var (s2, _, _) = CallParseAuditResult(null!);

        s1.Should().Be("⚠️ 需补充材料");
        s2.Should().Be("⚠️ 需补充材料");
    }

    [Fact]
    public void ParseAuditResult_EnglishColon_Works()
    {
        var (status, refs, suggestion) = CallParseAuditResult(
            "判定: ✅ 合规\n法规: GB 50016-2014\n建议: 无需整改");

        status.Should().Be("✅ 合规"); // Chinese colon
    }

    // ═══════════════════════════════════════
    // CalculateEvacuationZones: ERG 疏散距离
    // ═══════════════════════════════════════

    [Fact]
    public void EvacuationZones_ToxicGas_LargeLeak()
    {
        var scenario = new EmergencyScenario { QuantityKg = 500 }; // >200 = large
        var substance = CreateSubstance("氯气", flashPt: null, "毒性气体");

        var (isolation, protective) = CallCalculateEvacuationZones(scenario, substance);

        isolation.Should().Be(500);
        protective.Should().Be(3000);
    }

    [Fact]
    public void EvacuationZones_ToxicGas_SmallLeak()
    {
        var scenario = new EmergencyScenario { QuantityKg = 50 }; // <=200 = small
        var substance = CreateSubstance("氨气", flashPt: null, "有毒气体");

        var (isolation, protective) = CallCalculateEvacuationZones(scenario, substance);

        isolation.Should().Be(100);
        protective.Should().Be(500);
    }

    [Fact]
    public void EvacuationZones_ToxicLiquid_LargeLeak()
    {
        var scenario = new EmergencyScenario { QuantityKg = 300 };
        var substance = CreateSubstance("苯", flashPt: -11, "毒性液体"); // flashPt <= -10 → gas

        var (isolation, protective) = CallCalculateEvacuationZones(scenario, substance);

        // flashPt -11 → isGas=true, isToxic=true → toxic gas path
        isolation.Should().Be(500);
        protective.Should().Be(3000);
    }

    [Fact]
    public void EvacuationZones_ToxicLiquid_SmallLeak()
    {
        var scenario = new EmergencyScenario { QuantityKg = 100 };
        var substance = CreateSubstance("苯", flashPt: 25, "毒性液体"); // flashPt 25 → NOT gas

        var (isolation, protective) = CallCalculateEvacuationZones(scenario, substance);

        // isToxic && !isGas → toxic liquid
        isolation.Should().Be(50);
        protective.Should().Be(200);
    }

    [Fact]
    public void EvacuationZones_FlammableGas_LargeLeak()
    {
        var scenario = new EmergencyScenario { QuantityKg = 300 };
        var substance = CreateSubstance("液化石油气", flashPt: null, "易燃气体");

        var (isolation, protective) = CallCalculateEvacuationZones(scenario, substance);

        isolation.Should().Be(300);
        protective.Should().Be(1500);
    }

    [Fact]
    public void EvacuationZones_FlammableLiquid_SmallLeak()
    {
        var scenario = new EmergencyScenario { QuantityKg = 50 };
        var substance = CreateSubstance("乙醇", flashPt: 13, "易燃液体");

        var (isolation, protective) = CallCalculateEvacuationZones(scenario, substance);

        isolation.Should().Be(30);
        protective.Should().Be(100);
    }

    // ═══════════════════════════════════════
    // SelectFireMedia: 灭火介质
    // ═══════════════════════════════════════

    [Fact]
    public void FireMedia_WaterReactive_NoWater()
    {
        var sub = CreateSubstance("钠", flashPt: null, "遇水放出易燃气体");

        var media = CallSelectFireMedia(sub);

        media.Should().Contain("严禁用水");
        media.Should().Contain("干粉");
    }

    [Fact]
    public void FireMedia_FlammableLiquid_FoamPowder()
    {
        var sub = CreateSubstance("汽油", flashPt: -43, "易燃液体");

        var media = CallSelectFireMedia(sub);

        media.Should().Contain("泡沫");
        media.Should().Contain("干粉");
    }

    [Fact]
    public void FireMedia_Oxidizer_WaterCooling()
    {
        var sub = CreateSubstance("过氧化氢", flashPt: null, "氧化性液体");

        var media = CallSelectFireMedia(sub);

        media.Should().Contain("大量水");
        media.Should().Contain("氧化剂");
    }

    [Fact]
    public void FireMedia_Corrosive_DryPowderPreferred()
    {
        var sub = CreateSubstance("硫酸", flashPt: null, "腐蚀性液体");

        var media = CallSelectFireMedia(sub);

        media.Should().Contain("干粉");
        media.Should().Contain("防飞溅");
    }

    // ═══════════════════════════════════════
    // RecommendPpe: PPE 等级
    // ═══════════════════════════════════════

    [Fact]
    public void Ppe_ToxicGas_LevelB()
    {
        var scenario = new EmergencyScenario();
        var sub = CreateSubstance("氯气", flashPt: null, "剧毒气体");

        var ppe = CallRecommendPpe(scenario, sub);

        ppe.Should().Contain("B级");
        ppe.Should().Contain("SCBA");
    }

    [Fact]
    public void Ppe_FlammableFire_LevelB()
    {
        var scenario = new EmergencyScenario { IncidentType = "火灾" };
        var sub = CreateSubstance("汽油", flashPt: -43, "易燃液体");

        var ppe = CallRecommendPpe(scenario, sub);

        ppe.Should().Contain("B级");
        ppe.Should().Contain("防火服");
    }

    [Fact]
    public void Ppe_ToxicLiquid_LevelC()
    {
        var scenario = new EmergencyScenario();
        var sub = CreateSubstance("苯", flashPt: 25, "致癌物");

        var ppe = CallRecommendPpe(scenario, sub);

        ppe.Should().Contain("C级");
    }

    [Fact]
    public void Ppe_Default_LevelD()
    {
        var scenario = new EmergencyScenario();
        var sub = CreateSubstance("乙醇", flashPt: 13, "易燃液体");

        var ppe = CallRecommendPpe(scenario, sub);

        ppe.Should().Contain("D级");
    }

    // ═══════════════════════════════════════
    // HasHazard: 危险类别关键词
    // ═══════════════════════════════════════

    [Fact]
    public void HasHazard_MatchesKeyword()
    {
        var sub = CreateSubstance("X", flashPt: null, "毒性气体");

        CallHasHazard(sub, "毒性").Should().BeTrue();
        CallHasHazard(sub, "腐蚀").Should().BeFalse();
    }

    [Fact]
    public void HasHazard_MultipleCategories()
    {
        var sub = new ChemicalSubstance
        {
            Name = "Y",
            HazardCategories = new List<HazardCategoryRef>
            {
                new() { Category = "易燃液体" },
                new() { Category = "剧毒物质" }
            }
        };

        CallHasHazard(sub, "易燃").Should().BeTrue();
        CallHasHazard(sub, "剧毒").Should().BeTrue();
        CallHasHazard(sub, "腐蚀").Should().BeFalse();
    }

    // ═══════════════════════════════════════
    // BuildAuditPrompt: Prompt 模板
    // ═══════════════════════════════════════

    [Fact]
    public void BuildAuditPrompt_ContainsCheckItem()
    {
        var prompt = CallBuildAuditPrompt("消防通道宽度检查", new List<RetrievedChunk>());

        prompt.Should().Contain("消防通道宽度检查");
        prompt.Should().Contain("判定");
        prompt.Should().Contain("法规");
        prompt.Should().Contain("建议");
    }

    [Fact]
    public void BuildAuditPrompt_NoReferences_ShowsFallback()
    {
        var prompt = CallBuildAuditPrompt("test", new List<RetrievedChunk>());

        prompt.Should().Contain("未检索到");
    }

    [Fact]
    public void BuildAuditPrompt_WithReferences_Truncates()
    {
        var chunks = new List<RetrievedChunk>
        {
            new() { Content = new string('A', 400) } // >300 chars
        };

        var prompt = CallBuildAuditPrompt("test", chunks);

        prompt.Should().Contain("..."); // truncated
        prompt.Should().NotContain(new string('A', 350)); // shouldn't show full content
    }

    // ═══════════════════════════════════════
    // Reflection Helpers
    // ═══════════════════════════════════════

    private static (string status, string refs, string suggestion) CallParseAuditResult(string llmOutput)
    {
        var method = typeof(RegulatoryAuditModule).GetMethod("ParseAuditResult",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var args = new object[] { llmOutput };
        var result = method.Invoke(null, args)!;
        // ValueTuple
        var tupleType = result.GetType();
        return (
            (string)tupleType.GetField("Item1")!.GetValue(result)!,
            (string)tupleType.GetField("Item2")!.GetValue(result)!,
            (string)tupleType.GetField("Item3")!.GetValue(result)!
        );
    }

    private static (int isolation, int protective) CallCalculateEvacuationZones(
        EmergencyScenario scenario, ChemicalSubstance substance)
    {
        var method = typeof(EmergencyResponseService).GetMethod("CalculateEvacuationZones",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = method.Invoke(null, new object[] { scenario, substance })!;
        var tupleType = result.GetType();
        return (
            (int)tupleType.GetField("Item1")!.GetValue(result)!,
            (int)tupleType.GetField("Item2")!.GetValue(result)!
        );
    }

    private static string CallSelectFireMedia(ChemicalSubstance substance)
    {
        var method = typeof(EmergencyResponseService).GetMethod("SelectFireMedia",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string)method.Invoke(null, new object[] { substance })!;
    }

    private static string CallRecommendPpe(EmergencyScenario scenario, ChemicalSubstance substance)
    {
        var method = typeof(EmergencyResponseService).GetMethod("RecommendPpe",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string)method.Invoke(null, new object[] { scenario, substance })!;
    }

    private static bool CallHasHazard(ChemicalSubstance sub, params string[] keywords)
    {
        var method = typeof(EmergencyResponseService).GetMethod("HasHazard",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (bool)method.Invoke(null, new object[] { sub, keywords })!;
    }

    private static string CallBuildAuditPrompt(string item, List<RetrievedChunk> chunks)
    {
        var method = typeof(RegulatoryAuditModule).GetMethod("BuildAuditPrompt",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string)method.Invoke(null, new object[] { item, chunks })!;
    }

    /// <summary>创建测试用 ChemicalSubstance</summary>
    private static ChemicalSubstance CreateSubstance(string name, double? flashPt, params string[] hazardCategories)
    {
        var cats = new List<HazardCategoryRef>();
        foreach (var cat in hazardCategories)
            cats.Add(new HazardCategoryRef { Category = cat });

        return new ChemicalSubstance
        {
            Name = name,
            FlashPointC = flashPt,
            HazardCategories = cats
        };
    }
}
