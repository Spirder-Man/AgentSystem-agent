using System.Collections.Generic;
using Agent1.Models;

namespace Agent1.Services
{
    /// <summary>
    /// 事件存储接口 — 事件溯源的核心抽象。
    /// 
    /// CQRS 三层映射：
    ///   Command（写）→ 产生事件 → 存入 IEventStore
    ///   Query（读） → 从 IEventStore 读取历史事件做审计/重放
    /// </summary>
    public interface IEventStore
    {
        /// <summary>追加一条不可变事件</summary>
        void Append(PipelineEvent evt);

        /// <summary>按 TraceId 查询同一请求的所有事件（审计追溯）</summary>
        IReadOnlyList<PipelineEvent> GetByTraceId(string traceId);

        /// <summary>获取所有事件（重放验证）</summary>
        IReadOnlyList<PipelineEvent> GetAll();
    }

    /// <summary>
    /// 内存事件存储 — 演示实现。
    /// 生产环境应替换为 PostgreSQL / EventStoreDB 等持久化存储。
    /// </summary>
    public class InMemoryEventStore : IEventStore
    {
        private readonly List<PipelineEvent> _events = new();
        private readonly object _lock = new();

        public void Append(PipelineEvent evt)
        {
            lock (_lock)
            {
                _events.Add(evt);
            }
        }

        public IReadOnlyList<PipelineEvent> GetByTraceId(string traceId)
        {
            lock (_lock)
            {
                return _events.FindAll(e => e.TraceId == traceId).AsReadOnly();
            }
        }

        public IReadOnlyList<PipelineEvent> GetAll()
        {
            lock (_lock)
            {
                return _events.AsReadOnly();
            }
        }
    }
}
