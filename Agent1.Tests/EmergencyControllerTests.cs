using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Agent1.Api.Controllers;
using Agent1.Models;
using Agent1.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// P5-3a: EmergencyController 格式化与边界测试。
///
/// 覆盖:
///   - FormatEmergencyPlan: private static 格式化方法 (反射)
///   - EmergencyController.GenerateResponse: 空物质名验证
///   - EmergencyResponseRequest 模型默认值
/// </summary>
public class EmergencyControllerTests
{
    // ═══════════════════════════════════════
    // FormatEmergencyPlan: 应急方案格式化 (反射)
    // ═══════════════════════════════════════

    [Fact]
    public void FormatPlan_FullData_ContainsAllSections()
    {
        var plan = new EmergencyPlan
        {
            SubstanceName = "氯气",
            CasNumber = "7782-50-5",
            UnNumber = "1017",
            FlashPointC = null,
            HazardCategories = new List<string> { "毒性气体", "腐蚀性" },
            IsolationZoneM = 500,
            ProtectiveZoneM = 3000,
            PpeLevel = "B级 — SCBA + 全封闭防化服",
            FireMedia = "干粉 / 二氧化碳 — ⛔ 严禁用水",
            ContainmentMethod = "关闭阀门 + 碱液中和",
            FirstAidInhale = "移至新鲜空气处",
            FirstAidSkin = "大量清水冲洗",
            FirstAidEye = "清水冲洗20分钟",
            FirstAidIngest = "禁止催吐",
            NotificationTemplate = "【通报】已通知应急管理局",
            RagSupplement = "【补充】知识库建议",
        };

        var output = CallFormatEmergencyPlan(plan);

        output.Should().Contain("氯气");
        output.Should().Contain("7782-50-5");
        output.Should().Contain("1017");
        output.Should().Contain("不适用");
        output.Should().Contain("毒性气体");
        output.Should().Contain("【疏散与隔离】");
        output.Should().Contain("500m");
        output.Should().Contain("3000m");
        output.Should().Contain("【个人防护装备");
        output.Should().Contain("B级");
        output.Should().Contain("【灭火介质】");
        output.Should().Contain("严禁用水");
        output.Should().Contain("【泄漏处置】");
        output.Should().Contain("【医疗急救】");
        output.Should().Contain("【通报】");
        output.Should().Contain("【补充】");
        output.Should().Contain("仅供参考");
    }

    [Fact]
    public void FormatPlan_MinimalData_StillFormats()
    {
        var plan = new EmergencyPlan
        {
            SubstanceName = "苯",
            CasNumber = "71-43-2",
            UnNumber = "1114",
            HazardCategories = new List<string>(),
            PpeLevel = "D级",
            FireMedia = "水雾",
            ContainmentMethod = "围堰",
            FirstAidInhale = "",
            FirstAidSkin = "",
            FirstAidEye = "",
            FirstAidIngest = "",
            NotificationTemplate = "",
        };

        var output = CallFormatEmergencyPlan(plan);

        output.Should().Contain("苯");
        output.Should().Contain("D级");
        output.Should().Contain("水雾");
        output.Should().Contain("仅供参考");
    }

    [Fact]
    public void FormatPlan_WithFlashPoint_ShowsValue()
    {
        var plan = CreateMinimalPlan();
        plan.FlashPointC = -11;

        var output = CallFormatEmergencyPlan(plan);

        output.Should().Contain("-11℃");
        output.Should().NotContain("不适用");
    }

    [Fact]
    public void FormatPlan_WithoutRagSupplement_NoSupplementSection()
    {
        var plan = CreateMinimalPlan();
        plan.RagSupplement = null;

        var output = CallFormatEmergencyPlan(plan);

        output.Should().NotContain("【补充】");
    }

    // ═══════════════════════════════════════
    // EmergencyController: 空物质名验证
    // ═══════════════════════════════════════

    [Fact]
    public async Task GenerateResponse_EmptySubstance_ReturnsBadRequest()
    {
        var controller = CreateController();
        var result = await controller.GenerateResponse(new EmergencyResponseRequest(
            Scenario: "leak", Substance: "", Location: null));
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GenerateResponse_WhitespaceSubstance_ReturnsBadRequest()
    {
        var controller = CreateController();
        var result = await controller.GenerateResponse(new EmergencyResponseRequest(
            Scenario: "fire", Substance: "   ", Location: null));
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ═══════════════════════════════════════
    // EmergencyResponseRequest: 模型默认值
    // ═══════════════════════════════════════

    [Fact]
    public void EmergencyResponseRequest_DefaultQuantity_Is100()
    {
        var request = new EmergencyResponseRequest(null, "氯气", null);
        request.QuantityKg.Should().Be(100);
    }

    [Fact]
    public void EmergencyResponseRequest_CustomQuantity_OverridesDefault()
    {
        var request = new EmergencyResponseRequest(null, "氯气", null, 500);
        request.QuantityKg.Should().Be(500);
    }

    // ═══════════════════════════════════════
    // Reflection Helpers
    // ═══════════════════════════════════════

    private static string CallFormatEmergencyPlan(EmergencyPlan plan)
    {
        var method = typeof(EmergencyController).GetMethod("FormatEmergencyPlan",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string)method.Invoke(null, new object[] { plan })!;
    }

    private static EmergencyPlan CreateMinimalPlan()
    {
        return new EmergencyPlan
        {
            SubstanceName = "测试化学品",
            CasNumber = "000-00-0",
            UnNumber = "0000",
            HazardCategories = new List<string>(),
            PpeLevel = "D级",
            FireMedia = "通用",
            ContainmentMethod = "通用方法",
            FirstAidInhale = "",
            FirstAidSkin = "",
            FirstAidEye = "",
            FirstAidIngest = "",
            NotificationTemplate = "",
        };
    }

    private static EmergencyController CreateController()
    {
        return new EmergencyController(
            Mock.Of<ILlmService>(),
            Mock.Of<IKnowledgeBaseService>(),
            Mock.Of<IAuditService>(),
            Mock.Of<IIntegrationService>(),
            Mock.Of<Microsoft.Extensions.Logging.ILogger<EmergencyController>>());
    }
}
