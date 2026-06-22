using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Agent1.Services.Logging.Enrichers;

/// <summary>
/// 会话 Enricher：为每条日志注入默认 SessionId（"none"）。
/// 实际会话 ID 由 SessionService 通过 LogContext.PushProperty("SessionId", id) 动态覆盖。
/// 这样即使未开始会话也有基准值，避免查询时出现空字段。
/// 
/// 使用方式（在 SessionService 中）：
///   using (LogContext.PushProperty("SessionId", sessionId))
///   {
///       // 此 scope 内的所有日志自动携带此 SessionId
///   }
/// </summary>
public class SessionEnricher : ILogEventEnricher
{
    private const string SessionIdKey = "SessionId";

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        // 仅当 LogContext 未设置时才写入默认值
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(SessionIdKey, "none"));
    }
}
