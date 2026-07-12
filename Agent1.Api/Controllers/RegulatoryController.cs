using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Agent1.Models;
using Agent1.Modules;
using Agent1.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agent1.Api.Controllers;

/// <summary>
/// 监管核查辅助 API — 安监核查清单逐条评估。
/// 覆盖控制台功能：#17 监管核查辅助 [安监核查清单逐条评估]。
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "Auditor")]
public class RegulatoryController : ControllerBase
{
    private readonly IModuleFactory _moduleFactory;
    private readonly ILogger<RegulatoryController> _logger;

    public RegulatoryController(IModuleFactory moduleFactory, ILogger<RegulatoryController> logger)
    {
        _moduleFactory = moduleFactory;
        _logger = logger;
    }

    /// <summary>
    /// 监管核查辅助 — 输入核查要求或场景描述，AI 逐条对照法规进行评估。
    /// </summary>
    [HttpPost("audit")]
    public async Task<IActionResult> Audit([FromBody] RegulatoryAuditRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return BadRequest(new { error = "请输入核查要求或场景描述" });
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var module = _moduleFactory.CreateModule(ModuleType.RegulatoryAudit);
            var result = await module.RunWithResultAsync(request.Query);
            sw.Stop();

            _logger.LogInformation("监管核查完成: 耗时={Elapsed}ms, 成功={Success}",
                sw.ElapsedMilliseconds, result.Success);

            return Ok(new
            {
                query = request.Query,
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
            _logger.LogError(ex, "监管核查失败: {Query}", request.Query);
            return StatusCode(500, new { error = $"核查失败: {ex.Message}", elapsedMs = sw.ElapsedMilliseconds });
        }
    }
}

public record RegulatoryAuditRequest(
    [Required] string Query
);
