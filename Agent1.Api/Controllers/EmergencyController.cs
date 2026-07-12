using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
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
    private readonly IModuleFactory _moduleFactory;
    private readonly ILogger<EmergencyController> _logger;

    public EmergencyController(IModuleFactory moduleFactory, ILogger<EmergencyController> logger)
    {
        _moduleFactory = moduleFactory;
        _logger = logger;
    }

    /// <summary>
    /// 应急响应方案 — 输入化学品名称和事故类型，AI 生成标准化应急响应方案。
    /// </summary>
    [HttpPost("response")]
    public async Task<IActionResult> GenerateResponse([FromBody] EmergencyResponseRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Scenario))
        {
            return BadRequest(new
            {
                error = "请输入事故场景描述",
                examples = new[]
                {
                    new { scenario = "苯泄漏", description = "液体化学品泄漏事故" },
                    new { scenario = "甲醇火灾", description = "可燃液体火灾事故" },
                    new { scenario = "氯气爆炸", description = "有毒气体爆炸事故" },
                    new { scenario = "氰化钠中毒", description = "剧毒化学品中毒事故" }
                }
            });
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var module = _moduleFactory.CreateModule(ModuleType.EmergencyResponse);
            var result = await module.RunWithResultAsync(request.Scenario);
            sw.Stop();

            _logger.LogInformation("应急响应方案生成完成: 场景={Scenario}, 耗时={Elapsed}ms",
                request.Scenario, sw.ElapsedMilliseconds);

            return Ok(new
            {
                scenario = request.Scenario,
                success = result.Success,
                warnings = result.Warnings,
                intent = result.Intent.ToString(),
                elapsedMs = sw.ElapsedMilliseconds,
                output = result.DisplayOutput,
                auditRecord = result.AuditRecord
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "应急响应方案生成失败: {Scenario}", request.Scenario);
            return StatusCode(500, new { error = $"生成失败: {ex.Message}", elapsedMs = sw.ElapsedMilliseconds });
        }
    }
}

public record EmergencyResponseRequest(
    [Required] string Scenario
);
