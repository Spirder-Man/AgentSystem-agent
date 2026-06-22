namespace Agent1.Services.Monitoring;

/// <summary>
/// 控制台告警通道 — 最低保障通道，将告警输出到控制台（红色高亮）。
/// 始终启用，作为邮件通道故障时的兜底。
/// </summary>
public class ConsoleAlertService : IAlertService
{
    public bool IsEnabled => true;

    public Task SendAlertAsync(string title, string message, AlertLevel level)
    {
        var originalColor = Console.ForegroundColor;

        try
        {
            Console.ForegroundColor = level switch
            {
                AlertLevel.Critical => ConsoleColor.Red,
                AlertLevel.Warning => ConsoleColor.Yellow,
                _ => ConsoleColor.Cyan
            };

            Console.WriteLine();
            Console.WriteLine($"╔══════════════════════════════════════════╗");
            Console.WriteLine($"║  [{level.ToString().ToUpper()}] {title}");
            Console.WriteLine($"╠══════════════════════════════════════════╣");
            foreach (var line in message.Split('\n'))
                Console.WriteLine($"║  {line.TrimEnd()}");
            Console.WriteLine($"╚══════════════════════════════════════════╝");
            Console.WriteLine();
        }
        finally
        {
            Console.ForegroundColor = originalColor;
        }

        return Task.CompletedTask;
    }
}
