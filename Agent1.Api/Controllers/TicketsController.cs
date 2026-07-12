using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Agent1.Models;
using Agent1.Modules;
using Agent1.Services;
using Agent1.Services.Orchestration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agent1.Api.Controllers;

/// <summary>
/// 整改工单 API — P0 业务闭环交付。
/// 提供工单查询 + 状态流转（状态机驱动）+ 整改工单跟进。
/// 覆盖控制台功能：#14 整改工单跟进。
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "Viewer")]
public class TicketsController : ControllerBase
{
    private readonly InspectionRepository _repo;
    private readonly IModuleFactory _moduleFactory;
    private readonly ILogger<TicketsController> _logger;

    public TicketsController(InspectionRepository repo, IModuleFactory moduleFactory, ILogger<TicketsController> logger)
    {
        _repo = repo;
        _moduleFactory = moduleFactory;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult ListTickets()
    {
        // 从所有巡检轮次中提取工单
        var rounds = _repo.GetAllRounds();
        var allTickets = rounds
            .SelectMany(r => r.Results)
            .Where(r => r.Tickets != null)
            .SelectMany(r => r.Tickets!)
            .Select(t => new
            {
                t.Id, t.Issue, t.Action, t.Priority,
                t.Status, t.Assignee, t.RegulationRef,
                t.SuggestedDeadline,
                IsOpen = t.IsOpen,
                LogCount = t.StatusLog.Count
            })
            .ToList();

        return Ok(new
        {
            total = allTickets.Count,
            open = allTickets.Count(t => t.IsOpen),
            tickets = allTickets
        });
    }

    [HttpPut("{id}/status")]
    [Authorize(Policy = "Auditor")]
    public IActionResult UpdateStatus(int id, [FromBody] TicketStatusUpdateRequest request)
    {
        // 查找工单
        var rounds = _repo.GetAllRounds();
        TicketItem? ticket = null;
        foreach (var round in rounds)
        {
            foreach (var result in round.Results)
            {
                ticket = result.Tickets?.FirstOrDefault(t => t.Id == id);
                if (ticket != null) break;
            }
            if (ticket != null) break;
        }

        if (ticket == null) return NotFound(new { error = $"工单 #{id} 不存在" });

        var operator_ = User.Identity?.Name ?? "api-user";
        switch (request.Action?.ToLower())
        {
            case "accept": ticket.Accept(request.Assignee ?? operator_); break;
            case "start": ticket.StartWork(operator_); break;
            case "complete": ticket.Complete(operator_); break;
            case "verify": ticket.Verify(operator_); break;
            case "close": ticket.Close(); break;
            case "reject": ticket.Reject(request.Reason ?? "未说明", operator_); break;
            default: return BadRequest(new { error = $"无效操作: {request.Action}. 支持: accept/start/complete/verify/close/reject" });
        }

        _repo.SaveRound(rounds.First()); // 触发持久化
        return Ok(new { ticketId = id, newStatus = ticket.Status.ToString(), logCount = ticket.StatusLog.Count });
    }

    // ═══════════════════════════════════════════════
    // #14 整改工单跟进
    // ═══════════════════════════════════════════════

    /// <summary>
    /// 整改工单跟进 — 输入合规检查结果文本，LLM 自动提取整改项并生成结构化工单。
    /// 也可对已有工单进行状态跟进分析。
    /// </summary>
    [HttpPost("followup")]
    [Authorize(Policy = "Auditor")]
    public async Task<IActionResult> Followup([FromBody] TicketFollowupRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ComplianceResult))
        {
            return BadRequest(new { error = "请输入合规检查结果或巡检记录" });
        }

        try
        {
            var module = (TicketFollowupModule)_moduleFactory.CreateModule(ModuleType.TicketFollowup);
            var tickets = await module.ProcessFollowupAsync(request.ComplianceResult);

            _logger.LogInformation("工单跟进完成: 从合规结果中提取 {Count} 个整改项", tickets.Count);

            return Ok(new
            {
                total = tickets.Count,
                tickets = tickets.Select(t => new
                {
                    t.Id,
                    t.Issue,
                    t.Action,
                    t.Priority,
                    t.Status,
                    t.Assignee,
                    t.RegulationRef,
                    t.SuggestedDeadline,
                    IsOpen = t.IsOpen
                }),
                message = $"LLM 从合规结果中提取了 {tickets.Count} 个整改项"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "工单跟进执行失败");
            return StatusCode(500, new { error = $"工单跟进失败: {ex.Message}" });
        }
    }
}

public record TicketStatusUpdateRequest(string? Action, string? Assignee, string? Reason);

public record TicketFollowupRequest(
    [Required] string ComplianceResult
);
