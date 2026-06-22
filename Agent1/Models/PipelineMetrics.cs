using System.Collections.Generic;

namespace Agent1.Models
{
    /// <summary>
    /// 6 步流水线每步的性能指标 — 从 Console.Write 迁移到结构化度量的核心数据载体。
    /// 
    /// 设计对标 RetailPulse 的 LogEvent.Properties：Message 归人读，Metrics 归机器读。
    /// 终端仍然能看到 [1/6]～[6/6] 的进度文字，但每条附带的 JSON 中有精确毫秒数。
    /// </summary>
    public class PipelineMetrics
    {
        /// <summary>本次请求的唯一标识，关联全链路日志</summary>
        public string TraceId { get; set; } = "";

        /// <summary>输入字符数（不含空白修剪）</summary>
        public int InputLength { get; set; }

        // ── 每步耗时（毫秒） ──

        /// <summary>步骤 1: 预处理耗时</summary>
        public long PreprocessMs { get; set; }

        /// <summary>安全检测（输入）耗时</summary>
        public long SafetyCheckInputMs { get; set; }

        /// <summary>步骤 2: 意图路由耗时</summary>
        public long RouteMs { get; set; }

        /// <summary>步骤 3: 上下文加载耗时</summary>
        public long LoadContextMs { get; set; }

        /// <summary>步骤 4: 业务执行耗时（含 LLM 推理 + RAG 检索 + 工具调用）</summary>
        public long ExecuteBusinessMs { get; set; }

        /// <summary>安全检测（输出）耗时</summary>
        public long SafetyCheckOutputMs { get; set; }

        /// <summary>步骤 5: 会话保存耗时</summary>
        public long SaveSessionMs { get; set; }

        /// <summary>步骤 6: 输出格式化耗时</summary>
        public long FormatOutputMs { get; set; }

        /// <summary>端到端总耗时</summary>
        public long TotalMs { get; set; }

        // ── 关键业务指标 ──

        /// <summary>RAG 检索召回条数</summary>
        public int RagRecallCount { get; set; }

        /// <summary>LLM 工具调用次数</summary>
        public int ToolCallCount { get; set; }

        /// <summary>LLM 输出长度（字符数）</summary>
        public int OutputLength { get; set; }

        /// <summary>意图类型</summary>
        public string Intent { get; set; } = "";

        /// <summary>匹配到的路由关键词</summary>
        public string? MatchedKeyword { get; set; }

        /// <summary>安全警告条数</summary>
        public int WarningCount { get; set; }

        // ── 序列化为结构化属性（对标 RetailPulse 的 Properties 字典） ──

        public Dictionary<string, object> ToProperties()
        {
            return new Dictionary<string, object>
            {
                ["TraceId"] = TraceId,
                ["InputLength"] = InputLength,
                ["PreprocessMs"] = PreprocessMs,
                ["SafetyCheckInputMs"] = SafetyCheckInputMs,
                ["RouteMs"] = RouteMs,
                ["LoadContextMs"] = LoadContextMs,
                ["ExecuteBusinessMs"] = ExecuteBusinessMs,
                ["SafetyCheckOutputMs"] = SafetyCheckOutputMs,
                ["SaveSessionMs"] = SaveSessionMs,
                ["FormatOutputMs"] = FormatOutputMs,
                ["TotalMs"] = TotalMs,
                ["RagRecallCount"] = RagRecallCount,
                ["ToolCallCount"] = ToolCallCount,
                ["OutputLength"] = OutputLength,
                ["Intent"] = Intent,
                ["MatchedKeyword"] = MatchedKeyword ?? "",
                ["WarningCount"] = WarningCount
            };
        }
    }
}
