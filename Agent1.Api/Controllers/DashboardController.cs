using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Agent1.Models;
using Agent1.Services.Orchestration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agent1.Api.Controllers;

/// <summary>
/// 合规总览 API — P0 业务闭环交付卡口。
/// 
/// 对标 CLI DashboardCommand，为安监管理部门/外部系统提供：
///   合规态势总览 → 资产台账 → 自动扫描 → 发现列表 → 历史记录 → 隐患报告
///   
/// 原有 CLI 入口：📊 合规总览 [扫描·台账·发现·整改率]（DashboardCommand, Key="4"）
/// 本 Controller 将其 5 个视图完整迁移为 RESTful 端点。
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "Viewer")]
public class DashboardController : ControllerBase
{
    private readonly InspectionRepository _repo;
    private readonly ComplianceRuleEngine _ruleEngine;
    private readonly InspectionOrchestrator _orchestrator;

    public DashboardController(
        InspectionRepository repo,
        ComplianceRuleEngine ruleEngine,
        InspectionOrchestrator orchestrator)
    {
        _repo = repo;
        _ruleEngine = ruleEngine;
        _orchestrator = orchestrator;
    }

    // ═══════════════════════════════════════
    // 1. 合规总览概览
    // ═══════════════════════════════════════

    /// <summary>
    /// 合规总览概览 — 对标 DashboardCommand 主视图。
    /// 返回资产/发现/整改率/最近扫描的聚合指标。
    /// </summary>
    [HttpGet("overview")]
    public IActionResult GetOverview()
    {
        var assets = _repo.GetAllAssets();
        var findings = _repo.GetAllFindings();
        var lastScan = _repo.GetLastScanTime();

        var overview = _ruleEngine.BuildOverview(assets, findings, lastScan);

        return Ok(new
        {
            overview.TotalAssets,
            overview.CheckedAssets,
            overview.CompliantAssets,
            overview.NonCompliantAssets,
            complianceRate = Math.Round(overview.ComplianceRate, 4),
            overview.TotalFindings,
            overview.OpenFindings,
            remediationRate = Math.Round(overview.RemediationRate, 4),
            lastAutoScanAt = overview.LastAutoScanAt,
            overview.HasInventory,
            findingsBySeverity = overview.FindingsBySeverity
                .ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            findingsByStatus = overview.FindingsByStatus
                .ToDictionary(kv => kv.Key.ToString(), kv => kv.Value)
        });
    }

    // ═══════════════════════════════════════
    // 2. 资产台账
    // ═══════════════════════════════════════

    /// <summary>
    /// 化学品资产台账 — 对标 DashboardCommand.ShowInventory。
    /// 返回园区内所有化学资产的清单及合规状态。
    /// </summary>
    [HttpGet("assets")]
    public IActionResult GetAssets()
    {
        var assets = _repo.GetAllAssets();
        var findings = _repo.GetAllFindings();

        var items = assets.Select(a =>
        {
            var assetFindings = findings.Where(f => f.AssetId == a.AssetId).ToList();
            return new
            {
                a.AssetId,
                a.Name,
                a.CasNumber,
                a.Location,
                a.QuantityTons,
                a.StorageCondition,
                a.ResponsiblePerson,
                a.IsMajorHazardSource,
                lastCheckedAt = a.LastCheckedAt,
                lastCheckResult = a.LastCheckResult,
                // 状态中文映射
                status = a.LastCheckResult.HasValue
                    ? (a.LastCheckResult.Value ? "合规" : "不合规")
                    : "未检查",
                openFindings = assetFindings.Count(f => f.IsOpen),
                totalFindings = assetFindings.Count,
                // 关联法规
                a.ApplicableRegulations
            };
        }).ToList();

        var overview = _ruleEngine.BuildOverview(assets, findings, _repo.GetLastScanTime());

        return Ok(new
        {
            items,
            summary = new
            {
                overview.TotalAssets,
                overview.CheckedAssets,
                overview.CompliantAssets,
                overview.NonCompliantAssets,
                uncheckedAssets = overview.TotalAssets - overview.CheckedAssets
            }
        });
    }

    // ═══════════════════════════════════════
    // 3. 自动合规扫描
    // ═══════════════════════════════════════

    /// <summary>
    /// 自动合规扫描 — 对标 DashboardCommand.AutoScanAsync。
    /// 对全部资产执行 AI 合规检查，更新资产状态并生成合规发现。
    /// 需要 Auditor 或更高角色。
    /// </summary>
    [HttpPost("scan")]
    [Authorize(Policy = "Auditor")]
    public async Task<IActionResult> AutoScan()
    {
        var assets = _repo.GetAllAssets();
        if (assets.Count == 0)
            return BadRequest(new { error = "暂无化学资产，请先添加资产台账" });

        var username = GetCurrentUsername();

        var result = await _ruleEngine.ScanAssetsAsync(assets, username);

        // 更新资产状态
        foreach (var f in result.Findings)
        {
            var asset = assets.FirstOrDefault(a => a.AssetId == f.AssetId);
            if (asset != null)
                asset.LastCheckResult = false;
        }

        // 合并新发现
        _repo.SaveFindings(result.Findings);
        _repo.SetLastScanTime(result.ScannedAt);

        var overview = _ruleEngine.BuildOverview(
            _repo.GetAllAssets(), _repo.GetAllFindings(), _repo.GetLastScanTime());

        return Ok(new
        {
            newFindings = result.NewFindings,
            totalFindings = result.Findings.Count,
            scannedAt = result.ScannedAt,
            overview = new
            {
                overview.TotalAssets,
                overview.CheckedAssets,
                complianceRate = Math.Round(overview.ComplianceRate, 4),
                overview.OpenFindings,
                remediationRate = Math.Round(overview.RemediationRate, 4)
            }
        });
    }

    // ═══════════════════════════════════════
    // 4. 合规发现列表
    // ═══════════════════════════════════════

    /// <summary>
    /// 合规发现列表 — 对标 DashboardCommand.ShowFindings。
    /// 支持按严重级别和状态筛选。
    /// </summary>
    /// <param name="severity">可选筛选：Critical / High / Medium / Low / Info</param>
    /// <param name="status">可选筛选：New / Confirmed / InProgress / Remediated / VerifiedClosed / Closed / FalsePositive</param>
    /// <param name="openOnly">仅显示未关闭的发现（默认 true）</param>
    [HttpGet("findings")]
    public IActionResult GetFindings(
        [FromQuery] string? severity = null,
        [FromQuery] string? status = null,
        [FromQuery] bool openOnly = true)
    {
        var findings = _repo.GetAllFindings();
        var assets = _repo.GetAllAssets();

        // 筛选
        var filtered = findings.AsEnumerable();

        if (openOnly)
            filtered = filtered.Where(f => f.IsOpen);

        if (!string.IsNullOrWhiteSpace(severity))
        {
            if (Enum.TryParse<FindingSeverity>(severity, ignoreCase: true, out var sev))
                filtered = filtered.Where(f => f.Severity == sev);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (Enum.TryParse<FindingStatus>(status, ignoreCase: true, out var st))
                filtered = filtered.Where(f => f.Status == st);
        }

        var items = filtered
            .OrderByDescending(f => f.Severity)
            .ThenByDescending(f => f.DiscoveredAt)
            .Select(f =>
            {
                var asset = assets.FirstOrDefault(a => a.AssetId == f.AssetId);
                return new
                {
                    f.FindingId,
                    f.Description,
                    f.RegulationRef,
                    f.AssetId,
                    assetName = asset?.Name ?? "未知",
                    assetLocation = asset?.Location ?? "未知",
                    severity = f.Severity.ToString(),
                    status = f.Status.ToString(),
                    f.IsOpen,
                    f.Assignee,
                    f.RemediationPlan,
                    f.Deadline,
                    f.DiscoveredAt,
                    f.LastStatusChangeAt,
                    f.VerifiedBy,
                    f.VerifiedAt
                };
            }).ToList();

        // 汇总
        var allFindings = _repo.GetAllFindings();
        return Ok(new
        {
            items,
            total = items.Count,
            summary = new
            {
                totalFindings = allFindings.Count,
                openFindings = allFindings.Count(f => f.IsOpen),
                bySeverity = allFindings
                    .GroupBy(f => f.Severity)
                    .ToDictionary(g => g.Key.ToString(), g => g.Count()),
                byStatus = allFindings
                    .GroupBy(f => f.Status)
                    .ToDictionary(g => g.Key.ToString(), g => g.Count())
            },
            appliedFilter = new
            {
                severity = severity ?? "all",
                status = status ?? "all",
                openOnly
            }
        });
    }

    // ═══════════════════════════════════════
    // 5. 历史巡检记录
    // ═══════════════════════════════════════

    /// <summary>
    /// 历史巡检记录 — 对标 DashboardCommand.ShowInspectionHistory。
    /// 返回巡检计划及其关联的轮次记录。
    /// </summary>
    [HttpGet("history")]
    public IActionResult GetInspectionHistory()
    {
        var plans = _orchestrator.GetAllPlans();

        var items = plans
            .OrderByDescending(p => p.CreatedAt)
            .Select(p =>
            {
                var rounds = _repo.GetRoundsByPlan(p.PlanId);
                return new
                {
                    p.PlanId,
                    p.Name,
                    p.Area,
                    type = p.Type.ToString(),
                    p.Inspector,
                    status = p.Status.ToString(),
                    p.ScheduledDate,
                    p.CreatedAt,
                    p.Notes,
                    itemCount = p.Items.Count,
                    roundCount = rounds.Count,
                    rounds = rounds.Select(r => new
                    {
                        r.RoundId,
                        startedAt = r.StartedAt,
                        completedAt = r.CompletedAt,
                        r.TotalItems,
                        r.CompliantCount,
                        r.NonCompliantCount,
                        r.UncertainCount,
                        r.ComplianceRate,
                        duration = r.Duration,
                        r.ExecutedBy
                    }).ToList()
                };
            }).ToList();

        return Ok(new
        {
            items,
            total = items.Count,
            statusBreakdown = plans
                .GroupBy(p => p.Status)
                .ToDictionary(g => g.Key.ToString(), g => g.Count())
        });
    }

    // ═══════════════════════════════════════
    // 6. 安全隐患报告
    // ═══════════════════════════════════════

    /// <summary>
    /// 安全隐患报告 — 对标 DashboardCommand.GenerateHazardReport。
    /// 返回所有未关闭的合规发现，按严重级别排序，可用于安监上报。
    /// </summary>
    [HttpGet("report/hazard")]
    public IActionResult GetHazardReport()
    {
        var findings = _repo.GetAllFindings();
        var assets = _repo.GetAllAssets();
        var openFindings = _repo.GetOpenFindings();

        var items = openFindings
            .OrderByDescending(f => f.Severity)
            .Select(f =>
            {
                var asset = assets.FirstOrDefault(a => a.AssetId == f.AssetId);
                return new
                {
                    f.FindingId,
                    f.Description,
                    f.RegulationRef,
                    severity = f.Severity.ToString(),
                    status = f.Status.ToString(),
                    f.Assignee,
                    f.RemediationPlan,
                    f.Deadline,
                    f.DiscoveredAt,
                    asset = asset == null ? null : new
                    {
                        asset.AssetId,
                        asset.Name,
                        asset.Location,
                        asset.CasNumber,
                        asset.IsMajorHazardSource
                    }
                };
            }).ToList();

        return Ok(new
        {
            generatedAt = DateTime.Now,
            disclaimer = "本报告为 AI 辅助生成，建议人工复核后提交安全管理部。",
            summary = new
            {
                totalAssets = assets.Count,
                totalFindings = findings.Count,
                openFindings = openFindings.Count,
                closedFindings = findings.Count - openFindings.Count,
                bySeverity = openFindings
                    .GroupBy(f => f.Severity)
                    .ToDictionary(g => g.Key.ToString(), g => g.Count())
            },
            items
        });
    }

    // ═══════════════════════════════════════
    // 辅助方法
    // ═══════════════════════════════════════

    private string GetCurrentUsername()
    {
        return User?.Identity?.Name ?? "system";
    }
}
