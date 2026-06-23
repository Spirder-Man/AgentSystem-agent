using System.Collections.Generic;
using System.Linq;
using Agent1.Modules;
using Agent1.Services.Orchestration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agent1.Api.Controllers;

/// <summary>
/// 整改工单 API — P0 业务闭环交付。
/// 提供工单查询 + 状态流转（状态机驱动）。
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "Auditor")]
public class TicketsController : ControllerBase
{
    private readonly InspectionRepository _repo;

    public TicketsController(InspectionRepository repo)
    {
        _repo = repo;
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
}

public record TicketStatusUpdateRequest(string? Action, string? Assignee, string? Reason);
