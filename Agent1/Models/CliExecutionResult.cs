using System.Collections.Generic;
using Agent1.Services;

namespace Agent1.Models
{
    /// <summary>
    /// CLI 功能统一输出契约 — 所有模块的 RunAsync / ExecuteAsync 输出均封装为此类型。
    /// 
    /// 化工安全系统要求：
    ///   1. 每次合规判断必须有可追溯的结构化结果
    ///   2. 高危断言必须在 Warnings 中标记
    ///   3. 操作必须记录审计日志
    /// 
    /// 引入原因（工程价值，非技术洁癖）：
    ///   - 散落的 Console.Write 无法被审计、无法被 API 复用、无法在评测中做结构化校验
    ///   - AgentDialog.LastToolResults 是跨请求共享的可变状态，存在竞态风险
    /// </summary>
    public class CliExecutionResult
    {
        /// <summary>执行是否成功（被安全拦截 = false）</summary>
        public bool Success { get; set; }

        /// <summary>终端展示文本（原有 Console.Write 的内容）</summary>
        public string DisplayOutput { get; set; } = "";

        /// <summary>结构化结果（供 API 层/评测器/下游模块消费）</summary>
        public object? StructuredResult { get; set; }

        /// <summary>安全警告列表（SafetyGuardService.ValidateOutput 产出）</summary>
        public List<string> Warnings { get; set; } = new();

        /// <summary>审计记录摘要（供 AuditService 持久化）</summary>
        public string? AuditRecord { get; set; }

        /// <summary>意图归类结果（供评测器验证路由正确性）</summary>
        public IntentType Intent { get; set; }

        /// <summary>触发路由的具体关键词（供审计追溯）</summary>
        public string? MatchedRouteKeyword { get; set; }

        /// <summary>本轮工具调用记录（替代 AgentDialog.LastToolResults 可变状态）</summary>
        public List<FunctionCallRecord> ToolCalls { get; set; } = new();

        /// <summary>
        /// 事件溯源链 — 本次请求从输入到输出的完整事件列表。
        /// 每个 PipelineEvent 有不可变的 EventId，按时间顺序排列。
        /// 审计追溯时按 EventId 排序即可还原完整执行历史。
        /// </summary>
        public List<PipelineEvent> Events { get; set; } = new();

        // ── 工厂方法 ──

        public static CliExecutionResult Blocked(string reason) => new()
        {
            Success = false,
            DisplayOutput = $"❌ 输入被安全拦截: {reason}",
            Warnings = new() { reason },
            AuditRecord = $"安全拦截: {reason}"
        };

        public static CliExecutionResult Ok(string output, object? structured = null) => new()
        {
            Success = true,
            DisplayOutput = output,
            StructuredResult = structured
        };
    }
}
