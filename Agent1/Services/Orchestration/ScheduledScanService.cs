using System;
using System.Threading;
using System.Threading.Tasks;
using Agent1.Models;

namespace Agent1.Services.Orchestration
{
    /// <summary>
    /// 定时自动扫描服务 — P1 可持续合规监控。
    /// 
    /// 对标 Dependency-Track 的定期重新分析机制:
    ///   每天固定时间自动扫描所有化学资产，发现新不合规项自动记录。
    /// 
    /// 使用 Timer 实现，不依赖外部调度系统（生产环境可替换为 cron/计划任务）。
    /// </summary>
    public class ScheduledScanService : IDisposable
    {
        private readonly ComplianceRuleEngine _ruleEngine;
        private readonly InspectionRepository _repo;
        private readonly EventActionDispatcher _eventDispatcher;
        private Timer? _timer;
        private bool _disposed;

        /// <summary>扫描间隔（默认24小时 = 86400000ms）</summary>
        private static readonly TimeSpan DefaultInterval = TimeSpan.FromDays(1);

        /// <summary>首次扫描延迟（启动后60秒，避免阻塞启动流程）</summary>
        private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(60);

        public ScheduledScanService(
            ComplianceRuleEngine ruleEngine,
            InspectionRepository repo,
            EventActionDispatcher eventDispatcher)
        {
            _ruleEngine = ruleEngine;
            _repo = repo;
            _eventDispatcher = eventDispatcher;
        }

        /// <summary>启动定时扫描</summary>
        public void Start()
        {
            _timer = new Timer(async _ => await ExecuteScanAsync(),
                null, InitialDelay, DefaultInterval);

            Serilog.Log.Information("[ScheduledScan] 定时扫描已启动 | 间隔={Interval}h | 首次延迟={Delay}s",
                DefaultInterval.TotalHours, InitialDelay.TotalSeconds);
        }

        /// <summary>停止定时扫描</summary>
        public void Stop()
        {
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            Serilog.Log.Information("[ScheduledScan] 定时扫描已停止");
        }

        /// <summary>立即执行一次扫描（手动触发）</summary>
        public async Task<ComplianceScanResult> ScanNowAsync()
        {
            Serilog.Log.Information("[ScheduledScan] 手动触发扫描");
            return await ExecuteScanAsync();
        }

        private async Task<ComplianceScanResult> ExecuteScanAsync()
        {
            try
            {
                var assets = _repo.GetAllAssets();
                if (assets.Count == 0)
                {
                    Serilog.Log.Information("[ScheduledScan] 无资产，跳过扫描");
                    return new ComplianceScanResult { ScannedAt = DateTime.Now };
                }

                var result = await _ruleEngine.ScanAssetsAsync(assets, "scheduler");

                // 持久化新发现
                if (result.Findings.Count > 0)
                {
                    _repo.SaveFindings(result.Findings);
                    _repo.SetLastScanTime(result.ScannedAt);
                }

                // 发布事件 — 范式 3
                if (result.NewFindings > 0)
                {
                    _eventDispatcher.Publish(PipelineEvent.Create(
                        eventId: 0,
                        traceId: $"scheduled-{DateTime.Now:yyyyMMdd}",
                        eventType: "ScheduledScanCompleted",
                        description: $"定时扫描完成: {result.NewFindings}个新发现",
                        data: new System.Collections.Generic.Dictionary<string, object>
                        {
                            ["TotalAssets"] = result.TotalAssets,
                            ["NewFindings"] = result.NewFindings,
                            ["TotalFindings"] = result.TotalFindings
                        }));
                }

                return result;
            }
            catch (Exception ex)
            {
                Serilog.Log.Error("[ScheduledScan] 扫描异常: {Error}", ex.Message);
                return new ComplianceScanResult { ScannedAt = DateTime.Now };
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _timer?.Dispose();
            _disposed = true;
        }
    }
}
