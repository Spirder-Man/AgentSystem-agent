using Serilog.Core;
using Serilog.Events;

namespace Agent1.Services.Logging.Enrichers;

/// <summary>
/// 环境信息 Enricher：自动注入 MachineName / ProcessId / OSVersion，
/// 多机部署时可快速区分日志来源。
/// </summary>
public class EnvironmentEnricher : ILogEventEnricher
{
    private const string MachineNameKey = "MachineName";
    private const string ProcessIdKey = "ProcessId";
    private const string OSVersionKey = "OSVersion";

    // 缓存静态值，避免每次 Enrich 重复调用 Environment.*
    private static readonly string _machineName = Environment.MachineName;
    private static readonly int _processId = Environment.ProcessId;
    private static readonly string _osVersion = Environment.OSVersion.VersionString;

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(MachineNameKey, _machineName));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(ProcessIdKey, _processId));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(OSVersionKey, _osVersion));
    }
}
