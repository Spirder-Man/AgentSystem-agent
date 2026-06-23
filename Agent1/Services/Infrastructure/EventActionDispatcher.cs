using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Agent1.Models;

namespace Agent1.Services
{
    /// <summary>
    /// 事件动作订阅器 — 范式 3 的核心基础设施。
    /// 
    /// 对标 Dependency-Track 的 Notification 机制:
    ///   事件发生 → 订阅者自动执行对应动作（发邮件/生成工单/写审计日志）
    /// 
    /// 化工安全场景的典型订阅:
    ///   "FindingCreated" → 自动通知区域安全员
    ///   "FindingVerified" → 自动归档审计日志
    ///   "InspectionCompleted" → 自动生成巡检报告
    /// </summary>
    public class EventActionDispatcher
    {
        private readonly Dictionary<string, List<Func<PipelineEvent, Task>>> _subscriptions = new();

        /// <summary>订阅事件</summary>
        public void Subscribe(string eventType, Func<PipelineEvent, Task> handler)
        {
            if (!_subscriptions.ContainsKey(eventType))
                _subscriptions[eventType] = new List<Func<PipelineEvent, Task>>();
            _subscriptions[eventType].Add(handler);
            Serilog.Log.Information("[EventAction] 订阅事件: {EventType}", eventType);
        }

        /// <summary>发布事件 → 触发所有订阅者（fire-and-forget）</summary>
        public void Publish(PipelineEvent evt)
        {
            if (!_subscriptions.TryGetValue(evt.EventType, out var handlers))
                return;

            foreach (var handler in handlers)
            {
                _ = Task.Run(async () =>
                {
                    try { await handler(evt); }
                    catch (Exception ex)
                    {
                        Serilog.Log.Warning("[EventAction] 处理器异常: {EventType} | {Error}",
                            evt.EventType, ex.Message);
                    }
                });
            }

            Serilog.Log.Information("[EventAction] 发布事件: {EventType} | {Desc} | 订阅者={Count}",
                evt.EventType, evt.Description, handlers.Count);
        }

        /// <summary>获取所有订阅（诊断用）</summary>
        public IReadOnlyDictionary<string, int> GetSubscriptionCounts()
        {
            var result = new Dictionary<string, int>();
            foreach (var kv in _subscriptions)
                result[kv.Key] = kv.Value.Count;
            return result;
        }
    }
}
