using Serilog.Core;
using Serilog.Events;

namespace Agent1.Services.Logging.Enrichers;

/// <summary>
/// 运行批次 Enricher：自动注入 RunId + StartTime，
/// 支持按程序启动批次聚合/隔离日志。
/// </summary>
public class RunIdEnricher : ILogEventEnricher
{
    private const string RunIdKey = "RunId";
    private const string StartTimeKey = "StartTime";

    private static readonly string _runId = RunIdGenerator.Current;
    private static readonly string _startTime = RunIdGenerator.StartTime.ToString("O");

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(RunIdKey, _runId));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(StartTimeKey, _startTime));
    }
}
