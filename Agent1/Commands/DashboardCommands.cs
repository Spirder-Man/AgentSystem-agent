using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Agent1.Models;
using Agent1.Services;
using Agent1.Services.Orchestration;

namespace Agent1.Commands
{
    /// <summary>
    /// 合规总览 CLI 命令 — Phase 1 业务能力编排层管理视图。
    /// 
    /// 对标 Dependency-Track Dashboard：
    ///   资产台账 → 自动合规扫描 → Finding生命周期 → 整改率指标
    /// </summary>
    public class DashboardCommand : IMenuCommand
    {
        private readonly InspectionOrchestrator _orchestrator;
        private readonly ComplianceRuleEngine _ruleEngine;
        private readonly InspectionRepository _repo;
        private readonly ModuleDispatcher _dispatcher;

        public string Key => "4";
        public string Label => "📊 合规总览 [扫描·台账·发现·整改率]";

        public DashboardCommand(InspectionOrchestrator orchestrator,
            ComplianceRuleEngine ruleEngine, InspectionRepository repo,
            ModuleDispatcher dispatcher)
        {
            _orchestrator = orchestrator;
            _ruleEngine = ruleEngine;
            _repo = repo;
            _dispatcher = dispatcher;
        }

        public async Task ExecuteAsync()
        {
            while (true)
            {
                var overview = _ruleEngine.BuildOverview(
                    _repo.GetAllAssets(), _repo.GetAllFindings(), _repo.GetLastScanTime());

                Console.WriteLine("\n══════════ 合规总览 ══════════");
                Console.WriteLine($"  资产: {overview.TotalAssets} | 已检: {overview.CheckedAssets} | " +
                    $"合规率: {overview.ComplianceRate:P1}");
                Console.WriteLine($"  发现: {overview.TotalFindings} | 未关闭: {overview.OpenFindings} | " +
                    $"整改率: {overview.RemediationRate:P0}");
                Console.WriteLine($"  上次扫描: {(_repo.GetLastScanTime()?.ToString("MM-dd HH:mm") ?? "从未")}");
                Console.WriteLine("────────────────────────────");
                Console.WriteLine("  1. 查看资产台账");
                Console.WriteLine("  2. 自动合规扫描 [AI批量检查]");
                Console.WriteLine("  3. 发现列表（按状态）");
                Console.WriteLine("  4. 历史巡检记录");
                Console.WriteLine("  5. 安全隐患报告");
                Console.WriteLine("  0. 返回主菜单");
                Console.Write("\n请选择: ");

                var choice = Console.ReadLine() ?? "0";
                switch (choice)
                {
                    case "1": ShowInventory(overview); break;
                    case "2": await AutoScanAsync(); break;
                    case "3": ShowFindings(); break;
                    case "4": ShowInspectionHistory(); break;
                    case "5": await GenerateHazardReport(); break;
                    case "0": return;
                }
            }
        }

        // ── 视图 1: 资产台账（对标 DT 的 Components 列表）──

        private void ShowInventory(ComplianceOverview overview)
        {
            Console.WriteLine($"\n── 化学品资产台账 ({_repo.GetAllAssets().Count} 个资产) ──");
            Console.WriteLine($"{"名称",-12} {"CAS",-12} {"位置",-18} {"存量(吨)",-8} {"状态"}");
            Console.WriteLine(new string('─', 65));

            foreach (var a in _repo.GetAllAssets())
            {
                var status = a.LastCheckResult == true ? "✅" :
                             a.LastCheckResult == false ? "❌" : "⬜";
                Console.WriteLine($"{a.Name,-12} {a.CasNumber,-12} {a.Location,-18} {a.QuantityTons,-8} {status}");
            }

            Console.WriteLine(new string('─', 65));
            Console.WriteLine($"✅ 合规: {overview.CompliantAssets} | ❌ 不合规: {overview.NonCompliantAssets} | " +
                $"⬜ 未检查: {overview.TotalAssets - overview.CheckedAssets}");
        }

        // ── 视图 2: 自动合规扫描（对标 DT 的 "Analyze"）──

        private async Task AutoScanAsync()
        {
            Console.WriteLine("\n⚠️ 自动扫描将对所有资产执行 AI 合规检查，耗时较长。");
            Console.Write("确认执行? (y/n): ");
            if (!(Console.ReadLine() ?? "").Equals("y", StringComparison.OrdinalIgnoreCase))
                return;

            var result = await _ruleEngine.ScanAssetsAsync(_repo.GetAllAssets(), "admin");

            // 更新资产状态
            foreach (var f in result.Findings)
            {
                var asset = _repo.GetAllAssets().FirstOrDefault(a => a.AssetId == f.AssetId);
                if (asset != null)
                    asset.LastCheckResult = false;
            }

            // 将新发现合并到总列表
            _repo.SaveFindings(result.Findings);
            _repo.SetLastScanTime(result.ScannedAt);

            var overview = _ruleEngine.BuildOverview(
                _repo.GetAllAssets(), _repo.GetAllFindings(), _repo.GetLastScanTime());
            Console.WriteLine($"\n📊 扫描结果: 合规率 {overview.ComplianceRate:P1} | " +
                $"新发现 {result.NewFindings} 个 | 整改率 {overview.RemediationRate:P0}");

            await Task.CompletedTask;
        }

        // ── 视图 3: 发现列表（对标 DT 的 Findings 列表 + 状态机）──

        private void ShowFindings()
        {
            if (_repo.GetAllFindings().Count == 0)
            {
                Console.WriteLine("\n✅ 暂无合规发现。建议先执行自动扫描。");
                return;
            }

            var findings = _repo.GetAllFindings();
            Console.WriteLine($"\n── 合规发现列表 ({findings.Count} 个) ──");

            // 按严重级别分组显示
            var critical = findings.Where(f => f.IsOpen && f.Severity == FindingSeverity.Critical).ToList();
            var high = findings.Where(f => f.IsOpen && f.Severity == FindingSeverity.High).ToList();
            var other = findings.Where(f => f.IsOpen && f.Severity <= FindingSeverity.Medium).ToList();
            var closed = findings.Where(f => !f.IsOpen).ToList();

            void PrintFindings(List<ComplianceFinding> list, string label)
            {
                if (list.Count == 0) return;
                Console.WriteLine($"\n  {label} ({list.Count}):");
                foreach (var f in list.Take(10))
                {
                    var asset = _repo.GetAllAssets().FirstOrDefault(a => a.AssetId == f.AssetId);
                    Console.WriteLine($"    [{f.FindingId}] {f.Description.Truncate(55)}");
                    Console.WriteLine($"         状态: {f.Status} | 法规: {f.RegulationRef} | " +
                        $"负责人: {(string.IsNullOrEmpty(f.Assignee) ? "未分配" : f.Assignee)}");
                }
            }

            PrintFindings(critical, "🔴 严重");
            PrintFindings(high, "🟠 高");
            PrintFindings(other, "🟡 一般");

            if (closed.Count > 0)
                Console.WriteLine($"\n  ✅ 已关闭 ({closed.Count} 个)");

            Console.WriteLine($"\n  💡 使用菜单14(整改工单)管理整改流程");
        }

        // ── 视图 4: 历史巡检记录 ──

        private void ShowInspectionHistory()
        {
            var plans = _orchestrator.GetAllPlans();
            if (plans.Count == 0)
            {
                Console.WriteLine("\n⚠️ 暂无巡检记录");
                return;
            }

            Console.WriteLine($"\n── 历史巡检记录 ({plans.Count} 个计划) ──");
            Console.WriteLine($"{"ID",-10} {"名称",-25} {"状态",-8} {"项",-4} {"时间"}");
            Console.WriteLine(new string('─', 65));

            foreach (var p in plans.OrderByDescending(p => p.CreatedAt))
            {
                var status = p.Status switch
                {
                    InspectionStatus.Completed => "✅",
                    InspectionStatus.InProgress => "🔄",
                    _ => "📝"
                };
                Console.WriteLine($"{p.PlanId,-10} {p.Name.Truncate(25),-25} {status,-8} {p.Items.Count,-4} {p.CreatedAt:MM-dd HH:mm}");
            }
        }

        // ── 视图 5: 安全隐患报告 ──

        private async Task GenerateHazardReport()
        {
            if (_repo.GetAllFindings().Count == 0)
            {
                Console.WriteLine("\n✅ 暂无合规发现，请先执行自动扫描");
                return;
            }

            var openFindings = _repo.GetOpenFindings();
            if (openFindings.Count == 0)
            {
                Console.WriteLine("\n✅ 所有发现均已关闭，当前无安全隐患");
                return;
            }

            Console.WriteLine($"\n══════════ 安全隐患报告 ══════════");
            Console.WriteLine($"生成时间: {DateTime.Now:yyyy-MM-dd HH:mm}");
            Console.WriteLine($"资产总数: {_repo.GetAllAssets().Count} | 发现总数: {_repo.GetAllFindings().Count} | 未关闭: {openFindings.Count}");
            Console.WriteLine($"══════════════════════════════════");

            foreach (var f in openFindings.OrderByDescending(f => f.Severity))
            {
                var asset = _repo.GetAllAssets().FirstOrDefault(a => a.AssetId == f.AssetId);
                var icon = f.Severity switch
                {
                    FindingSeverity.Critical => "🔴",
                    FindingSeverity.High => "🟠",
                    _ => "🟡"
                };
                Console.WriteLine($"\n{icon} [{f.FindingId}] {f.Description.Truncate(60)}");
                Console.WriteLine($"   资产: {asset?.Name ?? "?"} @ {asset?.Location ?? "?"}");
                Console.WriteLine($"   法规: {f.RegulationRef} | 状态: {f.Status} | 严重性: {f.Severity}");
                if (!string.IsNullOrEmpty(f.RemediationPlan))
                    Console.WriteLine($"   建议: {f.RemediationPlan.Truncate(80)}");
            }

            Console.WriteLine($"\n══════════════════════════════════════");
            Console.WriteLine($"⚠️ 本报告为 AI 辅助生成，建议人工复核后提交安全管理部。");

            await Task.CompletedTask;
        }
    }
}
