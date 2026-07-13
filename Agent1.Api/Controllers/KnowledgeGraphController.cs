using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Agent1.Models;
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
    private readonly IKnowledgeBaseService _kbService;
    private readonly ILogger<KnowledgeGraphController> _logger;

    public KnowledgeGraphController(IKnowledgeBaseService kbService, ILogger<KnowledgeGraphController> logger)
    {
        _kbService = kbService;
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
            return BadRequest(new { error = "请输入化学品或法规关键词" });
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var graph = KnowledgeGraphFactory.GetOrBuild(_kbService);
            var output = await graph.QueryAsync(request.Query);
            sw.Stop();

            _logger.LogInformation("知识图谱查询完成: 查询={Query}, 耗时={Elapsed}ms",
                request.Query, sw.ElapsedMilliseconds);

            return Ok(new
            {
                query = request.Query,
                success = true,
                elapsedMs = sw.ElapsedMilliseconds,
                entityCount = graph.EntityCount,
                relationCount = graph.RelationCount,
                output
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
