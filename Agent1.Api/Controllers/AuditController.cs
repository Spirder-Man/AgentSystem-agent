using Agent1.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agent1.Api.Controllers;

/// <summary>
/// 审计日志 API — 等保三级操作审计。
/// 仅 admin 角色可访问，提供日志查询 + SHA256 哈希链完整性验证。
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "Admin")]
public class AuditController : ControllerBase
{
    private readonly IAuditService _auditService;

    public AuditController(IAuditService auditService)
    {
        _auditService = auditService;
    }

    /// <summary>
    /// 查询审计日志列表 — 支持时间范围 + 用户筛选 + 分页。
    /// </summary>
    [HttpGet("logs")]
    public async Task<IActionResult> GetLogs(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? user,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var logs = await _auditService.GetAuditLogsAsync(from, to, user);
        var total = logs.Count;

        var paged = logs
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new
            {
                l.Id,
                User = l.UserId,
                Operation = l.Operation,
                Details = l.Details,
                l.IsSensitive,
                Timestamp = l.CreateTime.ToString("yyyy-MM-dd HH:mm:ss"),
                ChainHash = l.ChainHash != null ? l.ChainHash[..Math.Min(l.ChainHash.Length, 16)] : null
            })
            .ToList();

        return Ok(new
        {
            total,
            page,
            pageSize,
            logs = paged
        });
    }

    /// <summary>
    /// 验证 SHA256 哈希链完整性 — 逐条重算检测任何篡改。
    /// </summary>
    [HttpGet("integrity")]
    public async Task<IActionResult> VerifyIntegrity()
    {
        var (intact, brokenAtId, detail) = await _auditService.VerifyIntegrityAsync();
        return Ok(new
        {
            intact,
            brokenAtId,
            detail,
            verifiedAt = DateTime.UtcNow.ToString("o")
        });
    }

    /// <summary>
    /// 导出审计报告 — 指定时间范围的纯文本报告。
    /// </summary>
    [HttpGet("export")]
    public async Task<IActionResult> ExportReport(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to)
    {
        if (from >= to)
            return BadRequest(new { error = "起始时间必须早于结束时间" });

        var report = await _auditService.ExportAuditReportAsync(from, to);
        return Ok(new { report, generatedAt = DateTime.UtcNow.ToString("o") });
    }

    /// <summary>
    /// 审计统计摘要 — 总记录数 + 按操作类型分布。
    /// </summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var allLogs = await _auditService.GetAuditLogsAsync(null, null);
        var totalCount = allLogs.Count;

        var byOperation = allLogs
            .GroupBy(l => l.Operation)
            .ToDictionary(g => g.Key, g => g.Count());

        var byUser = allLogs
            .GroupBy(l => l.UserId)
            .ToDictionary(g => g.Key, g => g.Count());

        return Ok(new
        {
            totalCount,
            byOperation,
            byUser,
            lastLogAt = allLogs.FirstOrDefault()?.CreateTime.ToString("yyyy-MM-dd HH:mm:ss")
        });
    }
}
