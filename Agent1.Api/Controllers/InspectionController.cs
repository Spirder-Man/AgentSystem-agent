using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Agent1.Models;
using Agent1.Services;
using Agent1.Services.Orchestration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agent1.Api.Controllers;

/// <summary>
/// 巡检业务编排 API — P0 业务闭环交付卡口。
/// 安监部门/园区管理平台通过此 API 集成巡检全流程。
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "Auditor")]
public class InspectionController : ControllerBase
{
    private readonly InspectionOrchestrator _orchestrator;
    private readonly InspectionRepository _repo;
    private readonly ComplianceRuleEngine _ruleEngine;

    public InspectionController(
        InspectionOrchestrator orchestrator,
        InspectionRepository repo,
        ComplianceRuleEngine ruleEngine)
    {
        _orchestrator = orchestrator;
        _repo = repo;
        _ruleEngine = ruleEngine;
    }

    // ═══════════════════════════════════════
    // 巡检计划 CRUD
    // ═══════════════════════════════════════

    [HttpPost("plans")]
    public IActionResult CreatePlan([FromBody] CreatePlanRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "计划名称不能为空" });
        if (request.Items == null || request.Items.Count == 0)
            return BadRequest(new { error = "至少需要一项检查" });

        var items = request.Items.Select((q, i) => new InspectionItem
        {
            ItemId = i + 1,
            Query = q.Query,
            CapabilityName = q.Capability ?? "storage-compliance"
        }).ToList();

        var plan = _orchestrator.CreatePlan(
            request.Name,
            Enum.TryParse<InspectionType>(request.Type, out var t) ? t : InspectionType.DailyWeekly,
            request.Area ?? "",
            GetCurrentUsername(),
            items,
            request.Notes);

        return Ok(new { planId = plan.PlanId, name = plan.Name, items = plan.Items.Count });
    }

    [HttpGet("plans")]
    public IActionResult ListPlans()
    {
        var plans = _orchestrator.GetAllPlans()
            .Select(p => new
            {
                p.PlanId, p.Name, p.Area, p.Inspector,
                p.Status, Items = p.Items.Count,
                p.CreatedAt
            });
        return Ok(plans);
    }

    [HttpGet("plans/{planId}")]
    public IActionResult GetPlan(string planId)
    {
        var plan = _orchestrator.GetPlan(planId);
        if (plan == null) return NotFound(new { error = "计划不存在" });
        return Ok(new
        {
            plan.PlanId, plan.Name, plan.Area, plan.Type,
            plan.Inspector, plan.Status,
            plan.ScheduledDate, plan.CreatedAt, plan.Notes,
            Items = plan.Items.Select(i => new { i.ItemId, i.Query, i.CapabilityName, i.ExpectedRegulation })
        });
    }

    // ═══════════════════════════════════════
    // 巡检执行
    // ═══════════════════════════════════════

    [HttpPost("plans/{planId}/execute")]
    public async Task<IActionResult> ExecutePlan(string planId)
    {
        try
        {
            var round = await _orchestrator.ExecutePlanAsync(planId, GetCurrentUsername());
            return Ok(new
            {
                roundId = round.RoundId,
                planId = round.PlanId,
                complianceRate = round.ComplianceRate,
                compliantCount = round.CompliantCount,
                nonCompliantCount = round.NonCompliantCount,
                warningCount = round.WarningCount,
                ticketCount = round.TicketCount,
                totalElapsedMs = round.TotalElapsedMs,
                executedBy = round.ExecutedBy,
                startedAt = round.StartedAt,
                completedAt = round.CompletedAt
            });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [HttpGet("rounds/{roundId}")]
    public IActionResult GetRound(string roundId)
    {
        var round = _repo.GetRound(roundId);
        if (round == null) return NotFound(new { error = "巡检轮次不存在" });
        return Ok(new
        {
            round.RoundId, round.PlanId, round.ComplianceRate,
            round.CompliantCount, round.NonCompliantCount,
            round.TicketCount, round.WarningCount,
            round.TotalElapsedMs, round.ExecutedBy,
            round.StartedAt, round.CompletedAt,
            Results = round.Results.Select(r => new
            {
                r.ItemId, r.IsCompliant, r.RegulationRef,
                r.Conclusion, Warnings = r.Warnings.Count,
                Tools = r.ToolCalls.Select(tc => tc.FunctionName).ToList(),
                TraceId = r.TraceId,
                ElapsedMs = r.Metrics?.TotalMs
            })
        });
    }

    // ═══════════════════════════════════════
    // 报告
    // ═══════════════════════════════════════

    [HttpGet("reports/{roundId}")]
    public IActionResult GetReport(string roundId)
    {
        try
        {
            var report = _orchestrator.GenerateReport(roundId, GetCurrentUsername());
            return Ok(new
            {
                report.ReportId, report.RoundId, report.ComplianceRate,
                report.Summary, report.CriticalFindings,
                report.AuditHash, report.GeneratedAt, report.GeneratedBy,
                Plan = new { report.Plan.PlanId, report.Plan.Name, report.Plan.Area },
                Markdown = report.ToMarkdown()
            });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>导出巡检报告为 JSON 格式（供下游系统消费）</summary>
    [HttpGet("reports/{roundId}/export")]
    public IActionResult ExportReport(string roundId, [FromQuery] string format = "json")
    {
        try
        {
            var report = _orchestrator.GenerateReport(roundId, GetCurrentUsername());
            return Ok(new
            {
                meta = new { report.ReportId, report.RoundId, format = "json", report.GeneratedAt, report.GeneratedBy },
                plan = new { report.Plan.PlanId, report.Plan.Name, report.Plan.Area, report.Plan.Inspector },
                summary = new { report.ComplianceRate, report.Summary },
                findings = report.CriticalFindings,
                tickets = report.AllTickets.Select(t => new { t.Id, t.Issue, t.Priority, t.Status, t.Assignee, t.RegulationRef }),
                audit = new { report.AuditHash, algorithm = "SHA256" }
            });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    // ═══════════════════════════════════════
    // 资产台账 + 自动扫描
    // ═══════════════════════════════════════

    [HttpGet("assets")]
    public IActionResult GetAssets()
    {
        var assets = _repo.GetAllAssets().Select(a => new
        {
            a.AssetId, a.Name, a.CasNumber, a.Location,
            a.QuantityTons, a.StorageCondition, a.ResponsiblePerson,
            a.IsMajorHazardSource, a.LastCheckResult, a.LastCheckedAt
        });
        return Ok(assets);
    }

    [HttpPost("scan")]
    public async Task<IActionResult> RunAutoScan()
    {
        var result = await _ruleEngine.ScanAssetsAsync(_repo.GetAllAssets(), GetCurrentUsername());
        if (result.Findings.Count > 0)
        {
            _repo.SaveFindings(result.Findings);
            _repo.SetLastScanTime(result.ScannedAt);
        }
        return Ok(new
        {
            result.ScannedAt, result.TotalAssets, result.CheckedAssets,
            result.TotalFindings, result.NewFindings,
            Findings = result.Findings.Take(20).Select(f => new
            {
                f.FindingId, f.AssetId, f.RuleId,
                f.RegulationRef, f.Description, f.Severity, f.Status
            })
        });
    }

    [HttpPost("quick-check")]
    public async Task<IActionResult> QuickCheck([FromBody] QuickCheckRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return BadRequest(new { error = "检查内容不能为空" });

        var result = await _orchestrator.ExecuteQuickCheckAsync(request.Query, GetCurrentUsername());
        return Ok(new
        {
            isCompliant = result.IsCompliant,
            conclusion = result.Conclusion,
            regulationRef = result.RegulationRef,
            warnings = result.Warnings,
            tools = result.ToolCalls.Select(tc => tc.FunctionName).ToList(),
            traceId = result.TraceId,
            elapsedMs = result.Metrics?.TotalMs
        });
    }

    private string GetCurrentUsername()
        => User.Identity?.Name ?? "api-user";
}

// ── 请求模型 ──

public record CreatePlanRequest(
    string Name, string? Type, string? Area,
    List<InspectionItemRequest> Items, string? Notes);

public record InspectionItemRequest(string Query, string? Capability);

public record QuickCheckRequest(string Query);
