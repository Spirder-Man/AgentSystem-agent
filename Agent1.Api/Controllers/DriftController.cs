using System;
using System.Linq;
using Agent1.Services.DriftMonitor;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agent1.Api.Controllers;

/// <summary>
/// 认知漂移监测 API —— AI 对项目认知漂移量的实时测量数据。
///
/// 只读端点（Viewer 及以上角色可看）：
///   GET /api/drift/current?session=xxx   最近一次测量的漂移率 + 断言明细
///   GET /api/drift/trend?session=xxx     全部测量批次（画漂移曲线）
///   GET /api/drift/anchors/stats         锚点基线分域统计
///   GET /api/drift/probes                探针模板清单（黄金问题，按血管分组）
///
/// 写端点（仅 Admin）：
///   POST /api/drift/measure              手动喂一段 AI 输出触发测量（验证/演示用）
///   POST /api/drift/probes/{key}/answer  手动提交某探针的回答（不经过 LLM）
///   POST /api/drift/probes/run           立即执行一轮探针测量（问 LLM → 落库）
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DriftController : ControllerBase
{
    private readonly DriftMonitor _monitor;
    private readonly DriftAnchorRegistry _registry;

    public DriftController(DriftMonitor monitor, DriftAnchorRegistry registry)
    {
        _monitor = monitor;
        _registry = registry;
    }

    /// <summary>最近一次测量的漂移率 + 断言明细（含分域分解）</summary>
    [HttpGet("current")]
    [Authorize(Policy = "Viewer")]
    public async Task<IActionResult> GetCurrent([FromQuery] string session = "default")
    {
        var latest = await _monitor.GetLatestAsync(session);
        if (latest == null)
            return NotFound(new
            {
                session,
                message = "该会话尚无测量记录——先调用 POST /api/drift/measure 或接入被动采集"
            });
        return Ok(new
        {
            session,
            latest.TurnNo,
            latest.TriggerType,
            latest.ContextTokens,
            latest.ClaimCount,
            latest.MatchCount,
            DriftScore = Math.Round(latest.DriftScore, 4),
            latest.AnchorVersion,
            latest.CreatedAt,
            DomainBreakdown = latest.DomainBreakdown,
            Details = latest.Details
        });
    }

    /// <summary>某会话全部测量批次（按轮次升序，漂移曲线数据源）</summary>
    [HttpGet("trend")]
    [Authorize(Policy = "Viewer")]
    public async Task<IActionResult> GetTrend([FromQuery] string session = "default")
    {
        var probes = await _monitor.GetTrendAsync(session);
        return Ok(new
        {
            session,
            count = probes.Count,
            probes = probes.Select(p => new
            {
                p.TurnNo,
                p.TriggerType,
                p.ContextTokens,
                p.ClaimCount,
                p.MatchCount,
                DriftScore = Math.Round(p.DriftScore, 4),
                p.AnchorVersion,
                p.CreatedAt,
                p.DomainBreakdown
            })
        });
    }

    /// <summary>锚点基线分域统计（测量仪"基准源"的健康状态）</summary>
    [HttpGet("anchors/stats")]
    [Authorize(Policy = "Viewer")]
    public async Task<IActionResult> GetAnchorStats()
    {
        var stats = await _registry.GetStatsAsync();
        var version = await _registry.GetCurrentVersionAsync();
        return Ok(new { anchorVersion = version, domains = stats });
    }

    /// <summary>
    /// 探针模板清单（黄金问题，按血管分组，附最近测量时间）。
    /// 探针测量记录落在 session="probe"，turn_no=模板id 槽位——反查最近测量时间。
    /// </summary>
    [HttpGet("probes")]
    [Authorize(Policy = "Viewer")]
    public async Task<IActionResult> GetProbes()
    {
        var templates = await _monitor.GetTemplatesAsync();
        var history = await _monitor.GetTrendAsync("probe");
        var lastByTurn = history
            .GroupBy(p => p.TurnNo)
            .ToDictionary(g => g.Key, g => g.Max(p => p.CreatedAt));

        return Ok(new
        {
            total = templates.Count,
            vessels = templates
                .GroupBy(t => t.Vessel)
                .Select(g => new
                {
                    vessel = g.Key,
                    probes = g.Select(t => new
                    {
                        t.ProbeKey,
                        t.Question,
                        t.AnchorKey,
                        t.Severity,
                        t.Enabled,
                        t.Source,
                        LastMeasuredAt = lastByTurn.TryGetValue((int)t.Id, out var ts)
                            ? (DateTime?)ts
                            : null
                    })
                })
        });
    }

    /// <summary>
    /// 手动提交某探针的回答（不经过 LLM，直接强制断言测量）——
    /// 用于验证单条探针的测量逻辑 / 演示"答错 vs 未作答"的区别。
    /// </summary>
    [HttpPost("probes/{probeKey}/answer")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> AnswerProbe(string probeKey, [FromBody] ProbeAnswerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Answer))
            return BadRequest(new { error = "answer 不能为空" });

        try
        {
            var result = await _monitor.RecordProbeAsync(probeKey, request.Answer);
            return Ok(new
            {
                probeKey,
                result.ClaimCount,
                result.MatchCount,
                DriftScore = Math.Round(result.DriftScore, 4),
                result.DomainBreakdown
            });
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// 立即执行一轮探针测量：调度器取最久未测的启用模板 → 问被测 LLM → 强制断言落库。
    /// 与定时轮询并发安全（幂等覆盖，不堆积重复行）。
    /// </summary>
    [HttpPost("probes/run")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> RunProbes([FromServices] DriftProbeScheduler scheduler)
    {
        var ran = await scheduler.RunRoundAsync(HttpContext.RequestAborted);
        return Ok(new { measured = ran, message = ran > 0 ? "探针测量完成" : "本轮没有可测的探针（模板为空或全部失败）" });
    }

    /// <summary>
    /// 手动触发一次测量：喂一段 AI 输出文本，返回该轮漂移率 + 明细。
    /// 用于验证测量链路 / 演示漂移曲线（生产环境应接入被动采集自动调用）。
    /// </summary>
    [HttpPost("measure")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> Measure([FromBody] MeasureRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { error = "text 不能为空" });

        var result = await _monitor.RecordTurnAsync(
            request.Session ?? "default",
            request.TurnNo,
            request.ContextTokens ?? 0,
            request.Text);

        return Ok(new
        {
            session = request.Session ?? "default",
            turnNo = request.TurnNo,
            result.ClaimCount,
            result.MatchCount,
            DriftScore = Math.Round(result.DriftScore, 4),
            result.DomainBreakdown
        });
    }
}

/// <summary>手动测量请求 DTO</summary>
public record MeasureRequest(string Text, int TurnNo = 1, string? Session = null, int? ContextTokens = null);

/// <summary>手动提交探针回答请求 DTO</summary>
public record ProbeAnswerRequest(string Answer);
