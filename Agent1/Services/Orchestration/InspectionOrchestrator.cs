using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Agent1.Config;
using Agent1.Models;
using Agent1.Modules;

namespace Agent1.Services.Orchestration
{
    /// <summary>
    /// 巡检编排器 — 化工安全巡检业务的核心协调者。
    /// 
    /// 编排流程（对标真实化工园区巡检场景）：
    ///   1. 加载 InspectionPlan → 遍历 InspectionItem
    ///   2. 每个检查项 → AgentDialog.ExecuteAsync（自动享受 SafetyGuardService + AuditService + PipelineMetrics）
    ///   3. 收集 CliExecutionResult → 提取合规判定 → 不合规则自动生成工单
    ///   4. 汇总 InspectionRound → 生成 InspectionReport（含 SHA256 审计哈希）
    /// 
    /// 不改动原则：所有原子能力（AgentDialog / TicketFollowupModule / SafetyGuardService）
    /// 保持独立可用，编排器只做协调。
    /// </summary>
    public class InspectionOrchestrator
    {
        private readonly AgentDialog _agentDialog;
        private readonly IKnowledgeBaseService _kb;
        private readonly IAuditService _audit;
        private readonly ILlmService _llm;
        private readonly ISessionService _session;

        private readonly InspectionRepository _repo;
        private readonly DeterministicRuleEngine _ruleEngine;

        public InspectionOrchestrator(
            AgentDialog agentDialog, IKnowledgeBaseService kb,
            IAuditService audit, ILlmService llm, ISessionService session,
            InspectionRepository repo, DeterministicRuleEngine ruleEngine)
        {
            _agentDialog = agentDialog; _kb = kb; _audit = audit;
            _llm = llm; _session = session; _repo = repo;
            _ruleEngine = ruleEngine;
        }

        // ═══════════════════════════════════════
        // Phase 1: 计划管理
        // ═══════════════════════════════════════

        /// <summary>创建巡检计划</summary>
        public InspectionPlan CreatePlan(string name, InspectionType type, string area,
            string inspector, List<InspectionItem> items, string? notes = null)
        {
            var plan = new InspectionPlan
            {
                Name = name,
                Type = type,
                Area = area,
                Inspector = inspector,
                Items = items,
                Notes = notes ?? "",
                Status = InspectionStatus.Draft
            };

            lock (_repo) { _repo.SavePlan(plan); }

            Serilog.Log.Information("[InspectionOrchestrator] 创建计划 {PlanId}: {Name} | {Count}项",
                plan.PlanId, plan.Name, plan.Items.Count);

            return plan;
        }

        /// <summary>获取所有计划</summary>
        public List<InspectionPlan> GetAllPlans() => _repo.GetAllPlans();

        /// <summary>获取计划详情</summary>
        public InspectionPlan? GetPlan(string planId) => _repo.GetPlan(planId);

        /// <summary>删除巡检计划</summary>
        public bool DeletePlan(string planId)
        {
            var plan = GetPlan(planId);
            if (plan == null) return false;
            lock (_repo) { return _repo.DeletePlan(planId); }
        }

        /// <summary>更新巡检计划</summary>
        public InspectionPlan? UpdatePlan(string planId, string? name, string? area,
            string? inspector, string? notes, List<InspectionItem>? items)
        {
            var plan = GetPlan(planId);
            if (plan == null) return null;

            if (name != null) plan.Name = name;
            if (area != null) plan.Area = area;
            if (inspector != null) plan.Inspector = inspector;
            if (notes != null) plan.Notes = notes;
            if (items != null && items.Count > 0)
            {
                plan.Items = items.Select((it, i) =>
                {
                    it.ItemId = i + 1;
                    return it;
                }).ToList();
            }

            lock (_repo) { _repo.SavePlan(plan); }

            Serilog.Log.Information("[InspectionOrchestrator] 更新计划 {PlanId}: {Name}", planId, plan.Name);
            return plan;
        }

        // ═══════════════════════════════════════
        // Phase 2: 巡检执行
        // ═══════════════════════════════════════

        /// <summary>
        /// 执行完整巡检计划。
        /// 遍历每个 InspectionItem → 调 AgentDialog.ExecuteAsync → 收集结果 → 生成工单。
        /// </summary>
        public async Task<InspectionRound> ExecutePlanAsync(string planId, string executedBy)
        {
            var plan = GetPlan(planId)
                ?? throw new InvalidOperationException($"计划不存在: {planId}");

            if (plan.Items.Count == 0)
                throw new InvalidOperationException("巡检计划没有任何检查项");

            var round = new InspectionRound
            {
                PlanId = planId,
                StartedAt = DateTime.Now,
                ExecutedBy = executedBy
            };

            plan.Status = InspectionStatus.InProgress;

            Console.WriteLine($"\n🔍 开始执行巡检: {plan.Name}");
            Console.WriteLine($"   检查项: {plan.Items.Count} 项 | 检查人: {executedBy}");
            Console.WriteLine(new string('─', 50));

            for (int i = 0; i < plan.Items.Count; i++)
            {
                var item = plan.Items[i];
                Console.Write($"   [{i + 1}/{plan.Items.Count}] {item.Query.Truncate(50)} ... ");

                var result = await ExecuteInspectionItemAsync(item);
                item.Result = result;
                round.Results.Add(result);

                // 不合规项自动生成工单
                if (result.IsCompliant == false)
                {
                    Console.Write($"❌ 不合规 → 生成工单 ... ");
                    result.Tickets = await GenerateRectificationTicketAsync(item, result);
                    Console.WriteLine($"({result.Tickets?.Count ?? 0}个工单)");
                }
                else if (result.IsCompliant == true)
                {
                    Console.WriteLine($"✅ 合规 ({(result.Metrics?.TotalMs ?? 0)}ms)");
                }
                else
                {
                    Console.WriteLine($"⚠️ 无法判定");
                }

                // 安全检查项间延迟（避免 LLM 过载）
                if (i < plan.Items.Count - 1)
                    await Task.Delay(500);
            }

            round.CompletedAt = DateTime.Now;
            plan.Status = InspectionStatus.Completed;

            _repo.SaveRound(round);
            _repo.SavePlan(plan);

            Console.WriteLine(new string('─', 50));
            Console.WriteLine($"✅ 巡检完成: {round.CompliantCount}/{round.TotalItems}合规 | " +
                $"{round.NonCompliantCount}不合规 | {round.WarningCount}条警告 | " +
                $"工单={round.TicketCount}个 | 总耗时={round.TotalElapsedMs}ms");

            await _audit.LogOperationAsync(executedBy, "InspectionComplete",
                $"巡检完成: {plan.Name} | {round.CompliantCount}/{round.TotalItems}合规 | " +
                $"工单={round.TicketCount}个 | 耗时={round.TotalElapsedMs}ms",
                isSensitive: true);

            return round;
        }

        /// <summary>
        /// 快速执行单条检查（不创建计划）。
        /// 适用于"临时查一下某化学品是否合规"的场景。
        /// </summary>
        public async Task<InspectionItemResult> ExecuteQuickCheckAsync(string query, string executedBy)
        {
            var plan = InspectionPlan.QuickCheck(query, executedBy);
            var item = plan.Items[0];

            Console.WriteLine($"\n🔍 快速检查: {query.Truncate(50)}");
            var result = await ExecuteInspectionItemAsync(item);

            if (result.IsCompliant == false)
                result.Tickets = await GenerateRectificationTicketAsync(item, result);

            return result;
        }

        /// <summary>
        /// 执行单条检查项的核心逻辑。
        /// 每个检查项独立走一次 AgentDialog.ExecuteAsync 的 6 步流水线，
        /// 自动获得 SafetyGuardService 安全检测 + AuditService 审计 + PipelineMetrics 指标。
        /// </summary>
        private async Task<InspectionItemResult> ExecuteInspectionItemAsync(InspectionItem item)
        {
            // [铁律核心] 确定性规则引擎优先 — NumericCheck/BooleanCheck不调LLM
            var (directResult, needsLLM) = _ruleEngine.TryDetermine(item, item.Query);
            if (!needsLLM && directResult != null)
            {
                var verdict = directResult.IsCompliant == true ? "合规" : "不合规";
                Serilog.Log.Information("[RuleEngine] 规则引擎直接判定: Item={Id} → {Verdict}", item.ItemId, verdict);
                return directResult;
            }

            // 规则引擎未命中 → 走 LLM 推理
            var session = _agentDialog.CreateSession(SessionType.ChemicalCompliance);
            var execResult = await _agentDialog.ExecuteAsync(item.Query, session);

            // 转换为业务结果
            var result = InspectionItemResult.From(item.ItemId, execResult);

            // 如果不合规且没有安全警告，补充一条业务级警告
            if (result.IsCompliant == false && result.Warnings.Count == 0)
            {
                result.Warnings.Add($"检查项 [{item.ItemId}] 判定为不合规，请核实并生成整改工单");
            }

            return result;
        }

        // ═══════════════════════════════════════
        // Phase 3: 工单生成
        // ═══════════════════════════════════════

        /// <summary>
        /// 为不合规的检查项生成整改工单。
        /// 复用现有的 TicketFollowupModule.ProcessFollowupAsync。
        /// </summary>
        private async Task<List<TicketItem>> GenerateRectificationTicketAsync(
            InspectionItem item, InspectionItemResult result)
        {
            try
            {
                var ticketModule = new TicketFollowupModule(_llm, _kb, _audit);
                var tickets = await ticketModule.ProcessFollowupAsync(result.Conclusion);

                // 为每个工单补充上下文
                foreach (var ticket in tickets)
                {
                    ticket.RegulationRef ??= result.RegulationRef;
                }

                return tickets;
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning("[InspectionOrchestrator] 工单生成失败: {Error}", ex.Message);
                // 降级：返回一个手动创建的工单
                return new List<TicketItem>
                {
                    new TicketItem
                    {
                        Id = item.ItemId,
                        Issue = item.Query,
                        Action = $"请参照 {result.RegulationRef} 进行整改",
                        Priority = "高",
                        RegulationRef = result.RegulationRef
                    }
                };
            }
        }

        // ═══════════════════════════════════════
        // Phase 4: 报告生成
        // ═══════════════════════════════════════

        /// <summary>
        /// 生成巡检报告 — 含 SHA256 审计哈希（等保三级要求）。
        /// </summary>
        public InspectionReport GenerateReport(string roundId, string generatedBy)
        {
            var round = _repo.GetRound(roundId)
                ?? throw new InvalidOperationException($"巡检轮次不存在: {roundId}");
            var plan = _repo.GetPlan(round.PlanId)
                ?? throw new InvalidOperationException($"巡检计划不存在: {round.PlanId}");

                var report = new InspectionReport
                {
                    RoundId = roundId,
                    Plan = plan,
                    Round = round,
                    GeneratedBy = generatedBy,
                    ComplianceRate = round.ComplianceRate,
                    Summary = GenerateSummary(plan, round),
                    CriticalFindings = round.Results
                        .Where(r => r.IsCompliant == false &&
                               r.Warnings.Count > 0)
                        .Select(r => $"[{r.ItemId}] {plan.Items.Find(i => i.ItemId == r.ItemId)?.Query}: {r.Conclusion.Truncate(100)}")
                        .ToList(),
                    AllTickets = round.Results
                        .Where(r => r.Tickets != null)
                        .SelectMany(r => r.Tickets!)
                        .ToList(),
                    AuditHash = ComputeAuditHash(round)
            };

            return report;
        }

        /// <summary>
        /// 获取或生成巡检报告（按 roundId 缓存）
        /// </summary>
        public InspectionReport GetOrGenerateReport(string roundId, string generatedBy)
        {
            return GenerateReport(roundId, generatedBy);
        }

        // ═══════════════════════════════════════
        // 辅助方法
        // ═══════════════════════════════════════

        private static string GenerateSummary(InspectionPlan plan, InspectionRound round)
        {
            var verdict = round.ComplianceRate >= 0.9 ? "整体合规状况良好" :
                          round.ComplianceRate >= 0.7 ? "存在若干不合规项需整改" :
                          "不合规项较多，建议立即安排专项整改";

            return $"巡检「{plan.Name}」({plan.Area}) 于 {round.CompletedAt:yyyy-MM-dd HH:mm} 完成。" +
                   $"共检查 {round.TotalItems} 项，合规 {round.CompliantCount} 项，" +
                   $"不合规 {round.NonCompliantCount} 项，合规率 {round.ComplianceRate:P1}。" +
                   $"{verdict}。生成整改工单 {round.TicketCount} 个。";
        }

        /// <summary>
        /// 计算巡检轮次的 SHA256 审计哈希。
        /// 对 Results 列表序列化后取哈希，确保报告不可篡改。
        /// </summary>
        private static string ComputeAuditHash(InspectionRound round)
        {
            try
            {
                // 只对合规判定结果做哈希（不包含可变的时间戳）
                var payload = new
                {
                    round.RoundId,
                    round.PlanId,
                    round.ExecutedBy,
                    Results = round.Results.Select(r => new
                    {
                        r.ItemId,
                        r.IsCompliant,
                        r.RegulationRef,
                        WarningsCount = r.Warnings.Count,
                        ToolCallNames = r.ToolCalls.Select(tc => tc.FunctionName).ToList()
                    }).ToList()
                };

                var json = JsonSerializer.Serialize(payload);
                var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
                return Convert.ToHexString(hash);
            }
            catch
            {
                return "HASH_ERROR";
            }
        }
    }
}
