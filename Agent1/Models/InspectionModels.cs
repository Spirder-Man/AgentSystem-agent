using System;
using System.Collections.Generic;
using Agent1.Services;
using Agent1.Modules;

namespace Agent1.Models
{
    // ═══════════════════════════════════════════════════
    // Phase 1: 业务能力编排层 — 核心业务模型
    // 
    // 设计原则：
    //   1. 不改动现有原子模块（AgentDialog / ComplianceCheck / ...）
    //   2. 编排层只做协调，不做具体推理
    //   3. 每个 InspectionItem 调一次 AgentDialog.ExecuteAsync，
    //      独立获得 PipelineMetrics / SafetyGuard / AuditLog
    // ═══════════════════════════════════════════════════

    // ── 枚举 ──

    /// <summary>巡检类型 — 对应化工园区真实业务场景</summary>
    public enum InspectionType
    {
        DailyWeekly,   // 日常周检
        Monthly,       // 月度专项检
        PreHoliday,    // 节前安全大检查
        Regulatory     // 安监局迎检
    }

    /// <summary>巡检计划状态</summary>
    public enum InspectionStatus
    {
        Draft,         // 草稿（可编辑）
        InProgress,    // 执行中
        Completed,     // 已完成（待归档）
        Archived       // 已归档
    }

    /// <summary>
    /// 检查项类别 — 决定该检查项路由到哪个原子能力。
    /// 每个枚举值映射到一个已有的 Module 或 Service。
    /// </summary>
    public enum ItemCategory
    {
        StorageCompliance,   // → AgentDialog.ExecuteAsync (SK Auto FC)
        SafetyDistance,      // → AgentDialog.ExecuteAsync (SK Auto FC)
        FireEquipment,       // → RegulatoryAuditModule
        EmergencyAccess,     // → RegulatoryAuditModule
        GhsLabel,            // → MultimodalService
        Custom               // → UnifiedDialog (自由对话)
    }

    // ── 核心模型 ──

    /// <summary>
    /// 巡检计划 — 定义一次巡检的范围、检查项列表、责任人、时间。
    /// 
    /// 使用场景：
    ///   "甲类仓库周检" = 1个 InspectionPlan
    ///   "苯储存合规" / "消防通道宽度" = 各1个 InspectionItem
    /// </summary>
    public class InspectionPlan
    {
        /// <summary>计划唯一ID（8位，人可读）</summary>
        public string PlanId { get; set; } = Guid.NewGuid().ToString("N")[..8];

        /// <summary>计划名称，如"甲类仓库A区周检"</summary>
        public string Name { get; set; } = "";

        /// <summary>巡检类型</summary>
        public InspectionType Type { get; set; }

        /// <summary>巡检区域，如"甲类仓库A区"</summary>
        public string Area { get; set; } = "";

        /// <summary>检查人</summary>
        public string Inspector { get; set; } = "";

        /// <summary>计划执行日期</summary>
        public DateTime ScheduledDate { get; set; } = DateTime.Today;

        /// <summary>检查项列表（最少1项）</summary>
        public List<InspectionItem> Items { get; set; } = new();

        /// <summary>计划状态</summary>
        public InspectionStatus Status { get; set; } = InspectionStatus.Draft;

        /// <summary>创建时间</summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>备注</summary>
        public string Notes { get; set; } = "";

        // ── 工厂方法 ──

        /// <summary>创建一个快速单次检查（不关联计划）</summary>
        public static InspectionPlan QuickCheck(string query, string inspector = "system")
        {
            return new InspectionPlan
            {
                Name = $"快速检查: {query.Truncate(30)}",
                Type = InspectionType.DailyWeekly,
                Inspector = inspector,
                Items = new List<InspectionItem>
                {
                    new InspectionItem
                    {
                        ItemId = 1,
                        Query = query,
                        CapabilityName = "storage-compliance"
                    }
                }
            };
        }
    }

    /// <summary>
    /// 巡检项 — 计划中的单条检查项。
    /// 
    /// 范式 1 核心：检查项引用能力名称（如 "storage-compliance"），
    /// 而不是硬编码 ModuleType 或 ItemCategory。
    /// 引擎执行时通过 CapabilityRegistry 动态路由到对应模块。
    /// </summary>
    public class InspectionItem
    {
        public int ItemId { get; set; }
        public string Query { get; set; } = "";

        /// <summary>
        /// 能力名称 — 声明此检查项需要哪种原子能力。
        /// 通过 CapabilityRegistry 动态路由，而非硬编码 ModuleType。
        /// 示例: "storage-compliance" / "safety-distance" / "emergency-plan"
        /// </summary>
        public string CapabilityName { get; set; } = "storage-compliance";

        /// <summary>预期引用的法规（可选）</summary>
        public string? ExpectedRegulation { get; set; }
        public InspectionItemResult? Result { get; set; }

        /// <summary>[向后兼容] 从 ItemCategory 自动推断 CapabilityName</summary>
        public static string CategoryToCapability(ItemCategory category) => category switch
        {
            ItemCategory.StorageCompliance => "storage-compliance",
            ItemCategory.SafetyDistance => "safety-distance",
            ItemCategory.FireEquipment => "regulatory-audit",
            ItemCategory.EmergencyAccess => "regulatory-audit",
            ItemCategory.GhsLabel => "ghs-label-check",
            _ => "storage-compliance"
        };
    }

    /// <summary>
    /// 巡检轮次 — 一次计划的具体执行实例。
    /// 
    /// 一个 InspectionPlan 可以多次执行（同一计划重复使用），
    /// 每次执行产生一个 InspectionRound。
    /// </summary>
    public class InspectionRound
    {
        /// <summary>轮次唯一ID</summary>
        public string RoundId { get; set; } = Guid.NewGuid().ToString("N")[..8];

        /// <summary>关联的计划ID</summary>
        public string PlanId { get; set; } = "";

        /// <summary>执行开始时间</summary>
        public DateTime StartedAt { get; set; }

        /// <summary>执行结束时间</summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>执行人</summary>
        public string ExecutedBy { get; set; } = "";

        /// <summary>所有检查项的执行结果</summary>
        public List<InspectionItemResult> Results { get; set; } = new();

        // ── 汇总统计（计算属性） ──

        public int TotalItems => Results.Count;
        public int CompliantCount => Results.Count(r => r.IsCompliant == true);
        public int NonCompliantCount => Results.Count(r => r.IsCompliant == false);
        public int UncertainCount => Results.Count(r => r.IsCompliant == null);
        public int WarningCount => Results.Sum(r => r.Warnings.Count);
        public int TicketCount => Results.Sum(r => r.Tickets?.Count ?? 0);

        /// <summary>合规率 (0.0 ~ 1.0)</summary>
        public double ComplianceRate =>
            TotalItems > 0 ? (double)CompliantCount / TotalItems : 0;

        /// <summary>总耗时（从 StartedAt 到 CompletedAt）</summary>
        public TimeSpan Duration =>
            CompletedAt.HasValue ? CompletedAt.Value - StartedAt : TimeSpan.Zero;

        /// <summary>端到端总耗时（各检查项 PipelineMetrics.TotalMs 之和）</summary>
        public long TotalElapsedMs =>
            Results.Sum(r => r.Metrics?.TotalMs ?? 0);
    }

    /// <summary>
    /// 巡检项结果 — 单条检查项的执行结果。
    /// 
    /// 数据来源：AgentDialog.ExecuteAsync → CliExecutionResult
    /// 附加字段：业务合规判定 + 生成的工单
    /// </summary>
    public class InspectionItemResult
    {
        /// <summary>对应的检查项ID</summary>
        public int ItemId { get; set; }

        /// <summary>是否合规（null = 无法判定）</summary>
        public bool? IsCompliant { get; set; }

        /// <summary>LLM 输出全文</summary>
        public string Conclusion { get; set; } = "";

        /// <summary>引用的法规编号（如 "GB15603-1995 4.2.2"）</summary>
        public string RegulationRef { get; set; } = "";

        /// <summary>安全警告列表</summary>
        public List<string> Warnings { get; set; } = new();

        /// <summary>本轮工具调用记录</summary>
        public List<FunctionCallRecord> ToolCalls { get; set; } = new();

        /// <summary>7步流水线性能指标</summary>
        public PipelineMetrics? Metrics { get; set; }

        /// <summary>不合规时自动生成的整改工单</summary>
        public List<TicketItem>? Tickets { get; set; }

        /// <summary>TraceId（关联全链路日志）</summary>
        public string? TraceId => Metrics?.TraceId;

        /// <summary>
        /// 从 CliExecutionResult 转换为 InspectionItemResult
        /// </summary>
        public static InspectionItemResult From(int itemId, CliExecutionResult execResult)
        {
            var result = new InspectionItemResult
            {
                ItemId = itemId,
                Conclusion = execResult.DisplayOutput,
                IsCompliant = ParseComplianceFromOutput(execResult.DisplayOutput),
                Warnings = execResult.Warnings,
                ToolCalls = execResult.ToolCalls,
                Metrics = execResult.StructuredResult as PipelineMetrics,
                RegulationRef = ExtractGbNumber(execResult.DisplayOutput)
            };
            return result;
        }

        // ── 辅助解析 ──

        /// <summary>从 LLM 输出中解析合规判定</summary>
        private static bool? ParseComplianceFromOutput(string output)
        {
            if (string.IsNullOrWhiteSpace(output)) return null;
            if (output.Contains("【合规判断】是") || output.Contains("合规判断】是") ||
                output.Contains("合规判断:是")) return true;
            if (output.Contains("【合规判断】否") || output.Contains("合规判断】否") ||
                output.Contains("合规判断:否")) return false;
            return null;
        }

        /// <summary>从 LLM 输出中提取 GB 编号</summary>
        private static string ExtractGbNumber(string output)
        {
            // 简单正则匹配 GB 编号
            var match = System.Text.RegularExpressions.Regex.Match(
                output, @"GB\s*/?T?\s*\d{4,5}[.\-]\d+");
            return match.Success ? match.Value.Trim() : "";
        }
    }

    /// <summary>
    /// 巡检报告 — 一次巡检的正式输出文档。
    /// 
    /// 等保三级要求：报告内容不可篡改 — AuditHash 字段为 SHA256 签名。
    /// </summary>
    public class InspectionReport
    {
        /// <summary>报告唯一ID</summary>
        public string ReportId { get; set; } = Guid.NewGuid().ToString("N")[..8];

        /// <summary>关联的巡检轮次ID</summary>
        public string RoundId { get; set; } = "";

        /// <summary>巡检计划快照</summary>
        public InspectionPlan Plan { get; set; } = null!;

        /// <summary>巡检执行结果</summary>
        public InspectionRound Round { get; set; } = null!;

        /// <summary>报告生成时间</summary>
        public DateTime GeneratedAt { get; set; } = DateTime.Now;

        /// <summary>报告生成人</summary>
        public string GeneratedBy { get; set; } = "";

        // ── 报告内容 ──

        /// <summary>人读摘要（1-2句话）</summary>
        public string Summary { get; set; } = "";

        /// <summary>合规率 (0.0 ~ 1.0)</summary>
        public double ComplianceRate { get; set; }

        /// <summary>严重不合规项列表</summary>
        public List<string> CriticalFindings { get; set; } = new();

        /// <summary>本次巡检产生的所有工单</summary>
        public List<TicketItem> AllTickets { get; set; } = new();

        /// <summary>
        /// SHA256 审计哈希（防篡改）。
        /// 对 Round.Results 序列化后计算哈希。
        /// </summary>
        public string AuditHash { get; set; } = "";

        // ── 导出 ──

        /// <summary>生成 Markdown 格式的巡检报告</summary>
        public string ToMarkdown()
        {
            var lines = new List<string>();
            lines.Add($"# 化工安全巡检报告");
            lines.Add($"");
            lines.Add($"- **报告编号**: {ReportId}");
            lines.Add($"- **巡检计划**: {Plan.Name}");
            lines.Add($"- **巡检区域**: {Plan.Area}");
            lines.Add($"- **检查人**: {Round.ExecutedBy}");
            lines.Add($"- **执行时间**: {Round.StartedAt:yyyy-MM-dd HH:mm} ~ {Round.CompletedAt:yyyy-MM-dd HH:mm}");
            lines.Add($"- **合规率**: {ComplianceRate:P1} ({Round.CompliantCount}/{Round.TotalItems})");
            lines.Add($"- **不合规项**: {Round.NonCompliantCount} | **安全警告**: {Round.WarningCount} | **工单**: {Round.TicketCount}");
            lines.Add($"- **审计哈希**: `{AuditHash}`");
            lines.Add($"");
            lines.Add($"## 检查结果明细");
            lines.Add($"");
            lines.Add($"| # | 检查项 | 判定 | 法规依据 | 警告 |");
            lines.Add($"|---|--------|------|----------|------|");
            foreach (var r in Round.Results)
            {
                var status = r.IsCompliant == true ? "✅ 合规" :
                             r.IsCompliant == false ? "❌ 不合规" : "⚠️ 无法判定";
                lines.Add($"| {r.ItemId} | {Plan.Items.Find(i => i.ItemId == r.ItemId)?.Query.Truncate(40) ?? "-"} | {status} | {r.RegulationRef.Truncate(30)} | {r.Warnings.Count} |");
            }
            lines.Add($"");
            if (CriticalFindings.Count > 0)
            {
                lines.Add($"## ⛔ 严重不合规项");
                foreach (var f in CriticalFindings)
                    lines.Add($"- {f}");
                lines.Add($"");
            }
            if (AllTickets.Count > 0)
            {
                lines.Add($"## 📋 整改工单");
                foreach (var t in AllTickets)
                    lines.Add($"- 工单#{t.Id}: {t.Issue.Truncate(60)} | 优先级: {t.Priority} | 截止: {t.SuggestedDeadline:yyyy-MM-dd}");
                lines.Add($"");
            }
            lines.Add($"---");
            lines.Add($"*报告由 Agent1 化工安全 AI 系统自动生成 | {GeneratedAt:yyyy-MM-dd HH:mm}*");
            return string.Join("\n", lines);
        }
    }
}
