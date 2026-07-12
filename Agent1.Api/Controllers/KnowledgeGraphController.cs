using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Agent1.Models;
using Agent1.Modules;
using Agent1.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agent1.Api.Controllers;

/// <summary>
/// 知识图谱 API — 化学品-法规-事故关联网查询。
/// 覆盖控制台功能：#19 知识图谱 [化学品-法规-事故关联网]。
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "Viewer")]
public class KnowledgeGraphController : ControllerBase
{
    private readonly IModuleFactory _moduleFactory;
    private readonly ILogger<KnowledgeGraphController> _logger;

    public KnowledgeGraphController(IModuleFactory moduleFactory, ILogger<KnowledgeGraphController> logger)
    {
        _moduleFactory = moduleFactory;
        _logger = logger;
    }

    /// <summary>
    /// 知识图谱查询 — 输入化学品或法规关键词，返回关联法规、事故案例与物质属性。
    /// </summary>
    [HttpPost("query")]
    public async Task<IActionResult> Query([FromBody] KnowledgeGraphRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return BadRequest(new
            {
                error = "请输入化学品或法规关键词",
                examples = new[]
                {
                    new { query = "苯 相关法规 事故", description = "查询苯相关的法规与历史事故" },
                    new { query = "甲类仓库 储存条件", description = "查询甲类仓库相关法规要求" },
                    new { query = "硝化反应 爆炸", description = "查询硝化反应相关爆炸案例" }
                }
            });
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var module = _moduleFactory.CreateModule(ModuleType.KnowledgeGraph);
            var result = await module.RunWithResultAsync(request.Query);
            sw.Stop();

            _logger.LogInformation("知识图谱查询完成: 查询={Query}, 耗时={Elapsed}ms",
                request.Query, sw.ElapsedMilliseconds);

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
            _logger.LogError(ex, "知识图谱查询失败: {Query}", request.Query);
            return StatusCode(500, new { error = $"查询失败: {ex.Message}", elapsedMs = sw.ElapsedMilliseconds });
        }
    }
}

public record KnowledgeGraphRequest(
    [Required] string Query
);
