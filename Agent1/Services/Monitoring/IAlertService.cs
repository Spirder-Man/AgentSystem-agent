namespace Agent1.Services.Monitoring;

/// <summary>
/// 告警级别
/// </summary>
public enum AlertLevel
{
    /// <summary>信息通知（如日报摘要）</summary>
    Info,
    /// <summary>警告（如慢请求、资源水位偏高）</summary>
    Warning,
    /// <summary>严重（如 LLM 熔断、数据库连接断开）</summary>
    Critical
}

/// <summary>
/// 告警通道接口 — 可插拔设计，支持多通道扇出。
/// 实现类：EmailAlertService（邮件）、ConsoleAlertService（控制台）、WebhookAlertService（未来扩展）。
/// </summary>
public interface IAlertService
{
    /// <summary>
    /// 发送告警。
    /// </summary>
    /// <param name="title">告警标题</param>
    /// <param name="message">告警详情</param>
    /// <param name="level">告警级别</param>
    Task SendAlertAsync(string title, string message, AlertLevel level);

    /// <summary>当前通道是否已启用（配置驱动）</summary>
    bool IsEnabled { get; }
}
