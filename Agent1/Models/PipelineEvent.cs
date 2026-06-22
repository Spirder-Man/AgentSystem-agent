using System;
using System.Collections.Generic;

namespace Agent1.Models
{
    /// <summary>
    /// 流水线事件 — 事件溯源的最小单元。
    /// 每个 EventId 不可变，按发生顺序追加到 EventStore。
    /// 审计追溯时按 EventId 排序即可还原完整执行历史。
    /// </summary>
    public record PipelineEvent
    {
        /// <summary>事件序号（同一请求内自增，从1开始）</summary>
        public int EventId { get; init; }

        /// <summary>请求级 TraceId，关联所有同一次请求的事件</summary>
        public string TraceId { get; init; } = "";

        /// <summary>事件类型名（如 InputReceived, IntentRouted, SafetyChecked）</summary>
        public string EventType { get; init; } = "";

        /// <summary>事件发生时间戳</summary>
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;

        /// <summary>事件携带的结构化数据（键值对）</summary>
        public Dictionary<string, object> Data { get; init; } = new();

        /// <summary>事件描述（人读）</summary>
        public string Description { get; init; } = "";

        // ── 工厂方法 ──

        public static PipelineEvent Create(int eventId, string traceId, string eventType,
            string description, Dictionary<string, object>? data = null)
        {
            return new PipelineEvent
            {
                EventId = eventId,
                TraceId = traceId,
                EventType = eventType,
                Timestamp = DateTime.UtcNow,
                Description = description,
                Data = data ?? new Dictionary<string, object>()
            };
        }
    }
}
