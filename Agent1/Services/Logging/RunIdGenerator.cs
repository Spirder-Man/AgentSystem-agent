namespace Agent1.Services.Logging;

/// <summary>
/// 每次程序启动生成唯一 RunId，用于按启动批次聚合日志。
/// 线程安全，首次访问时自动初始化。
/// </summary>
public static class RunIdGenerator
{
    private static readonly Lazy<string> _runId = new(() =>
    {
        // 取 Guid 前 8 位，兼顾唯一性与可读性
        var guid = Guid.NewGuid().ToString("N");
        return guid[..8];
    });

    private static readonly Lazy<DateTime> _startTime = new(() => DateTime.UtcNow);

    /// <summary>当前运行批次的唯一标识（8 位十六进制）</summary>
    public static string Current => _runId.Value;

    /// <summary>程序启动时间（UTC）</summary>
    public static DateTime StartTime => _startTime.Value;

    /// <summary>
    /// 生成新的 SessionId（每次业务会话调用）
    /// </summary>
    public static string NewSessionId() => Guid.NewGuid().ToString("N")[..12];
}
