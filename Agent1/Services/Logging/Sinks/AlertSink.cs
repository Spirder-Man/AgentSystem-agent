using Agent1.Services.Monitoring;
using Serilog.Core;
using Serilog.Events;

namespace Agent1.Services.Logging.Sinks;

/// <summary>
/// 告警 Sink：监听 Critical 级别日志并转发到 AlertDispatcher。
/// 作为 Serilog Pipeline 最后一环，不阻塞主流程。
/// </summary>
public class AlertSink : ILogEventSink
{
    private readonly AlertDispatcher _dispatcher;

    public AlertSink(AlertDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public void Emit(LogEvent logEvent)
    {
        // 仅 Critical 级别触发告警
        if (logEvent.Level < LogEventLevel.Fatal)
            return;

        var title = $"Agent1 Critical: {logEvent.MessageTemplate.Text}";
        var message = $"[{logEvent.Timestamp:O}] [{logEvent.Level}] {logEvent.RenderMessage()}";

        // 附加异常信息
        if (logEvent.Exception != null)
            message += $"\n异常: {logEvent.Exception}";

        // 火后不管（fire-and-forget），避免阻塞日志管道
        _ = _dispatcher.SendAlertAsync(title, message, AlertLevel.Critical);
    }
}
