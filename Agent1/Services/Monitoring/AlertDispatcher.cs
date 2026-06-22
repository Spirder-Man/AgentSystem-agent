using System.Collections.Concurrent;

namespace Agent1.Services.Monitoring;

/// <summary>
/// 告警分发器：组合多个 IAlertService，扇出时任一失败不影响其他。
/// 内置 60s 防抖（相同 title 不重复发送），防止告警风暴。
/// 
/// 使用方式：
///   var dispatcher = new AlertDispatcher();
///   dispatcher.Register(new EmailAlertService(config));
///   dispatcher.Register(new ConsoleAlertService());
///   await dispatcher.SendAlertAsync("LLM熔断", "连续3次失败", AlertLevel.Critical);
/// </summary>
public class AlertDispatcher
{
    private readonly List<IAlertService> _services = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastAlertTimes = new();
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromSeconds(60);

    /// <summary>注册告警通道</summary>
    public void Register(IAlertService service)
    {
        _services.Add(service);
    }

    /// <summary>
    /// 向所有已注册且启用的通道扇出告警。
    /// 内置防抖：相同 title 在 60s 内不重复发送。
    /// </summary>
    public async Task SendAlertAsync(string title, string message, AlertLevel level)
    {
        // 防抖检查
        var now = DateTime.UtcNow;
        if (_lastAlertTimes.TryGetValue(title, out var lastTime) &&
            now - lastTime < DebounceInterval)
        {
            return; // 冷却期内，跳过
        }
        _lastAlertTimes[title] = now;

        // 扇出到所有启用的通道
        var tasks = _services
            .Where(s => s.IsEnabled)
            .Select(s => SendToChannelSafeAsync(s, title, message, level));

        await Task.WhenAll(tasks);
    }

    private static async Task SendToChannelSafeAsync(
        IAlertService service, string title, string message, AlertLevel level)
    {
        try
        {
            await service.SendAlertAsync(title, message, level);
        }
        catch (Exception ex)
        {
            // 任一通道失败不影响其他通道
            Console.Error.WriteLine(
                $"[AlertDispatcher] 告警通道 {service.GetType().Name} 发送失败: {ex.Message}");
        }
    }
}
