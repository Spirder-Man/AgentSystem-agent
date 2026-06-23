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
    /// 巡检工作台 CLI 命令 — Phase 1 业务能力编排层入口。
    /// 
    /// 将原有的分散菜单（8/14/15/17）收敛为一个统一的巡检工作台，
    /// 内部按业务场景组织子选项：
    ///   1. 新建巡检计划
    ///   2. 从已有计划执行巡检
    ///   3. 快速单次检查
    ///   4. 查看巡检报告
    ///   5. 整改工单管理
    /// </summary>
    public class InspectionWorkbenchCommand : IMenuCommand
    {
        private readonly InspectionOrchestrator _orchestrator;
        private readonly CapabilityRegistry _capabilityRegistry;
        private readonly ModuleDispatcher _dispatcher;

        public string Key => "21";
        public string Label => "🔍 巡检工作台 [计划·执行·报告·工单]";

        public InspectionWorkbenchCommand(InspectionOrchestrator orchestrator,
            CapabilityRegistry capabilityRegistry, ModuleDispatcher dispatcher)
        {
            _orchestrator = orchestrator;
            _capabilityRegistry = capabilityRegistry;
            _dispatcher = dispatcher;
        }

        public async Task ExecuteAsync()
        {
            while (true)
            {
                Console.WriteLine("\n══════════ 巡检工作台 ══════════");
                Console.WriteLine("  1. 新建巡检计划");
                Console.WriteLine("  2. 从已有计划执行巡检");
                Console.WriteLine("  3. 快速单次检查（不创建计划）");
                Console.WriteLine("  4. 查看巡检报告");
                Console.WriteLine("  5. 整改工单管理");
                Console.WriteLine("  0. 返回主菜单");
                Console.Write("\n请选择: ");

                var choice = Console.ReadLine() ?? "0";
                switch (choice)
                {
                    case "1": await CreatePlanAsync(); break;
                    case "2": await ExecuteExistingPlanAsync(); break;
                    case "3": await QuickCheckAsync(); break;
                    case "4": await ViewReportAsync(); break;
                    case "5": await ManageTicketsAsync(); break;
                    case "0": return;
                }
            }
        }

        // ── 子功能 1: 新建巡检计划 ──

        private async Task CreatePlanAsync()
        {
            Console.WriteLine("\n── 新建巡检计划 ──");
            Console.Write("计划名称 (如 甲类仓库周检): ");
            var name = Console.ReadLine() ?? "";
            if (string.IsNullOrWhiteSpace(name)) { Console.WriteLine("计划名称不能为空"); return; }

            Console.WriteLine("巡检类型: 1.日常周检 2.月度专项 3.节前大检查 4.安监局迎检");
            Console.Write("选择 (默认1): ");
            var typeChoice = Console.ReadLine() ?? "1";
            var type = typeChoice switch
            {
                "2" => InspectionType.Monthly,
                "3" => InspectionType.PreHoliday,
                "4" => InspectionType.Regulatory,
                _ => InspectionType.DailyWeekly
            };

            Console.Write("巡检区域 (如 甲类仓库A区): ");
            var area = Console.ReadLine() ?? "";

            Console.Write("检查人: ");
            var inspector = Console.ReadLine() ?? "system";

            Console.WriteLine("\n输入检查项（每行一项，输入空行结束）:");
            Console.WriteLine("示例: 苯和丙酮能否同库储存");
            Console.WriteLine("      甲类仓库消防通道宽度是否合规");
            Console.WriteLine();

            var items = new List<InspectionItem>();
            int itemId = 1;
            while (true)
            {
                Console.Write($"  检查项{itemId}: ");
                var line = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(line)) break;

                items.Add(new InspectionItem
                {
                    ItemId = itemId,
                    Query = line.Trim(),
                    CapabilityName = DetectCapability(line, _capabilityRegistry)
                });
                itemId++;
            }

            if (items.Count == 0) { Console.WriteLine("至少需要一项检查"); return; }

            var plan = _orchestrator.CreatePlan(name, type, area, inspector, items);
            Console.WriteLine($"\n✅ 计划已创建: {plan.PlanId} | {plan.Items.Count}项检查");
            Console.Write("是否立即执行? (y/n): ");
            if ((Console.ReadLine() ?? "").Equals("y", StringComparison.OrdinalIgnoreCase))
            {
                await _orchestrator.ExecutePlanAsync(plan.PlanId, inspector);
            }
        }

        // ── 子功能 2: 执行已有计划 ──

        private async Task ExecuteExistingPlanAsync()
        {
            var plans = _orchestrator.GetAllPlans();
            if (plans.Count == 0)
            {
                Console.WriteLine("\n⚠️ 暂无巡检计划，请先新建计划");
                return;
            }

            Console.WriteLine("\n── 已有巡检计划 ──");
            foreach (var p in plans)
                Console.WriteLine($"  [{p.PlanId}] {p.Name} | {p.Area} | {p.Items.Count}项 | {p.Status}");

            Console.Write("\n输入计划ID执行 (或 0 返回): ");
            var planId = Console.ReadLine() ?? "";
            if (planId == "0") return;

            var plan = _orchestrator.GetPlan(planId);
            if (plan == null) { Console.WriteLine("计划不存在"); return; }

            Console.Write("执行人: ");
            var executor = Console.ReadLine() ?? "system";

            var round = await _orchestrator.ExecutePlanAsync(planId, executor);

            Console.Write("\n是否查看报告? (y/n): ");
            if ((Console.ReadLine() ?? "").Equals("y", StringComparison.OrdinalIgnoreCase))
            {
                var report = _orchestrator.GenerateReport(round.RoundId, executor);
                Console.WriteLine(report.ToMarkdown());
            }
        }

        // ── 子功能 3: 快速单次检查 ──

        private async Task QuickCheckAsync()
        {
            Console.WriteLine("\n── 快速单次检查 ──");
            Console.Write("输入检查内容: ");
            var query = Console.ReadLine() ?? "";
            if (string.IsNullOrWhiteSpace(query)) return;

            Console.Write("检查人: ");
            var inspector = Console.ReadLine() ?? "system";

            var result = await _orchestrator.ExecuteQuickCheckAsync(query, inspector);

            Console.WriteLine($"\n📋 检查结果:");
            Console.WriteLine($"   判定: {(result.IsCompliant == true ? "✅ 合规" :
                                       result.IsCompliant == false ? "❌ 不合规" : "⚠️ 无法判定")}");
            Console.WriteLine($"   法规: {result.RegulationRef}");
            Console.WriteLine($"   耗时: {result.Metrics?.TotalMs ?? 0}ms");
            Console.WriteLine($"   工具: {string.Join(", ", result.ToolCalls.Select(tc => tc.FunctionName))}");

            if (result.Warnings.Count > 0)
            {
                Console.WriteLine($"   ⚠️ 警告: {result.Warnings.Count}条");
                foreach (var w in result.Warnings.Take(3))
                    Console.WriteLine($"      - {w.Truncate(80)}");
            }

            if (result.Tickets != null && result.Tickets.Count > 0)
                Console.WriteLine($"   📋 工单: {result.Tickets.Count}个");
        }

        // ── 子功能 4: 查看报告 ──

        private async Task ViewReportAsync()
        {
            var plans = _orchestrator.GetAllPlans();
            if (plans.Count == 0)
            {
                Console.WriteLine("\n⚠️ 暂无巡检记录");
                return;
            }

            Console.WriteLine("\n── 已完成巡检 ──");
            foreach (var p in plans.Where(p => p.Status == InspectionStatus.Completed))
                Console.WriteLine($"  [{p.PlanId}] {p.Name} | {p.Area} | {p.Status}");

            Console.Write("\n输入计划ID查看报告 (或 0 返回): ");
            var planId = Console.ReadLine() ?? "";
            if (planId == "0") return;

            // 查找该计划的最新轮次
            var plan = _orchestrator.GetPlan(planId);
            if (plan == null) { Console.WriteLine("计划不存在"); return; }

            try
            {
                // 通过 GenerateReport 会自动查找 round
                // 简化处理：重新执行一次 GenerateReport 并显示
                Console.WriteLine("⚠️ 报告需在巡检执行后立即生成。请重新执行巡检计划。");
            }
            catch
            {
                Console.WriteLine("未找到该计划的巡检记录");
            }
        }

        // ── 子功能 5: 整改工单管理 ──

        private async Task ManageTicketsAsync()
        {
            Console.WriteLine("\n── 整改工单管理 ──");
            Console.WriteLine("  1. 查看待整改清单");
            Console.WriteLine("  2. 输入合规检查结果提取工单");
            Console.Write("选择: ");
            var choice = Console.ReadLine() ?? "1";

            if (choice == "2")
            {
                await _dispatcher.ExecuteModuleAsync(ModuleType.TicketFollowup);
            }
            else
            {
                Console.WriteLine("\n⚠️ 待整改清单需基于巡检报告生成，请先执行巡检计划");
            }
        }

        // ── 辅助 ──

        /// <summary>通过 CapabilityRegistry 动态匹配能力（范式 4）</summary>
        private static string DetectCapability(string query, CapabilityRegistry registry)
        {
            var matches = registry.MatchByInput(query);
            return matches.Count > 0 ? matches[0].Name : "storage-compliance";
        }
    }
}
