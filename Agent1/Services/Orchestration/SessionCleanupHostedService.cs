using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Agent1.Services.Orchestration
{
    /// <summary>
    /// [P0-3] 会话清理后台服务 — 周期性清理过期会话，防止 API 长期运行后内存泄漏。
    /// 
    /// 清理策略：
    ///   - 每 10 分钟执行一次
    ///   - 清理超过 30 分钟未活动的会话
    ///   - 与 SessionManager.CreateSession 的内联自清洁互补（双保险）
    /// </summary>
    public class SessionCleanupHostedService : BackgroundService
    {
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan SessionTimeout = TimeSpan.FromMinutes(30);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Serilog.Log.Information("[SessionCleanup] 后台清理已启动 | 间隔={Interval}min | 超时={Timeout}min",
                CleanupInterval.TotalMinutes, SessionTimeout.TotalMinutes);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(CleanupInterval, stoppingToken);
                    SessionManager.CleanupExpiredSessions(SessionTimeout);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error("[SessionCleanup] 清理异常: {Error}", ex.Message);
                }
            }

            Serilog.Log.Information("[SessionCleanup] 后台清理已停止");
        }
    }
}
