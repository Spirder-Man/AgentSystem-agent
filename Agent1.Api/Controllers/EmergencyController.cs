using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text;
using Agent1.Models;
using Agent1.Modules;
using Agent1.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agent1.Api.Controllers;

/// <summary>
/// 应急响应方案 API — 泄漏/火灾/爆炸/中毒应急响应方案生成。
/// 覆盖控制台功能：#18 应急响应方案 [泄漏/火灾/爆炸/中毒]。
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "Viewer")]
public class EmergencyController : ControllerBase
{
    private readonly ILlmService _llmService;
    private readonly IKnowledgeBaseService _kbService;
    private readonly IAuditService _auditService;
    private readonly IIntegrationService _integrationService;
    private readonly ILogger<EmergencyController> _logger;

    public EmergencyController(
        ILlmService llmService,
        IKnowledgeBaseService kbService,
        IAuditService auditService,
        IIntegrationService integrationService,
        ILogger<EmergencyController> logger)
    {
        _llmService = llmService;
        _kbService = kbService;
        _auditService = auditService;
        _integrationService = integrationService;
        _logger = logger;
    }

    /// <summary>
    /// 应急响应方案 — 输入化学品名称和事故类型，AI 生成标准化应急响应方案。
    /// </summary>
    [HttpPost("response")]
    public async Task<IActionResult> GenerateResponse([FromBody] EmergencyResponseRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Substance))
        {
            return BadRequest(new { error = "请输入涉及化学品名称" });
        }

        var sw = Stopwatch.StartNew();
        try
        {
            // 将前端英文枚举值映射为中文事故类型
            var incidentTypeMap = new Dictionary<string, string>
            {
                ["leak"] = "泄漏",
                ["fire"] = "火灾",
                ["explosion"] = "爆炸",
                ["poisoning"] = "中毒"
            };
            var incidentType = incidentTypeMap.GetValueOrDefault(request.Scenario ?? "leak", "泄漏");

            var scenario = new EmergencyScenario
            {
                ChemicalName = request.Substance.Trim(),
                IncidentType = incidentType,
                QuantityKg = request.QuantityKg > 0 ? request.QuantityKg : 100 // 默认100kg
            };

            var service = new EmergencyResponseService(_llmService, _kbService, _auditService);
            var plan = await service.GeneratePlanAsync(scenario);
            sw.Stop();

            if (plan.Error != null)
            {
                return Ok(new
                {
                    scenario = request.Scenario,
                    success = false,
                    elapsedMs = sw.ElapsedMilliseconds,
                    output = plan.Error
                });
            }

            var output = FormatEmergencyPlan(plan);

            _logger.LogInformation("应急响应方案生成完成: 化学品={Substance}, 类型={Type}, 耗时={Elapsed}ms",
                request.Substance, incidentType, sw.ElapsedMilliseconds);

            return Ok(new
            {
                scenario = request.Scenario,
                success = true,
                elapsedMs = sw.ElapsedMilliseconds,
                output
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "应急响应方案生成失败: {Scenario}", request.Scenario);
            return StatusCode(500, new { error = $"生成失败: {ex.Message}", elapsedMs = sw.ElapsedMilliseconds });
        }
    }

    private static string FormatEmergencyPlan(EmergencyPlan plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"化学品: {plan.SubstanceName} (CAS {plan.CasNumber} / UN {plan.UnNumber})");
        sb.AppendLine($"危险类别: {string.Join(", ", plan.HazardCategories)}");
        sb.AppendLine($"闪点: {plan.FlashPointC?.ToString() ?? "不适用"}℃");
        sb.AppendLine();
        sb.AppendLine("【疏散与隔离】");
        sb.AppendLine($"  初始隔离半径: {plan.IsolationZoneM}m");
        sb.AppendLine($"  防护行动区: {plan.ProtectiveZoneM}m");
        sb.AppendLine();
        sb.AppendLine("【个人防护装备 (PPE)】");
        sb.AppendLine($"  {plan.PpeLevel}");
        sb.AppendLine();
        sb.AppendLine("【灭火介质】");
        sb.AppendLine($"  {plan.FireMedia}");
        sb.AppendLine();
        sb.AppendLine("【泄漏处置】");
        sb.AppendLine($"  {plan.ContainmentMethod}");
        sb.AppendLine();
        sb.AppendLine("【医疗急救】");
        sb.AppendLine($"  吸入: {plan.FirstAidInhale}");
        sb.AppendLine($"  皮肤: {plan.FirstAidSkin}");
        sb.AppendLine($"  眼睛: {plan.FirstAidEye}");
        sb.AppendLine($"  食入: {plan.FirstAidIngest}");
        sb.AppendLine();
        sb.AppendLine(plan.NotificationTemplate);
        if (plan.RagSupplement != null)
            sb.AppendLine(plan.RagSupplement);
        sb.AppendLine();
        sb.AppendLine("⚠️ 本方案为 AI 辅助生成，仅供参考。实际应急响应应遵循现场指挥和应急预案。");
        return sb.ToString();
    }
}

public record EmergencyResponseRequest(
    string? Scenario,
    [Required] string Substance,
    string? Location,
    double QuantityKg = 100
);
