using Serilog.Core;
using Serilog.Events;

namespace Agent1.Services.Logging.Enrichers;

/// <summary>
/// 线程 Enricher：自动注入当前托管线程 ID，
/// 并发场景下用于区分不同线程产生的日志。
/// </summary>
public class ThreadEnricher : ILogEventEnricher
{
    private const string ThreadIdKey = "ThreadId";

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var threadId = Environment.CurrentManagedThreadId;
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(ThreadIdKey, threadId));
    }
}
