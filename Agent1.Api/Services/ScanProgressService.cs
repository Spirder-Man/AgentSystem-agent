using System;

namespace Agent1.Api.Services;

/// <summary>
/// [#4 FIX] 后台扫描进度跟踪 — 单例服务。
///
/// Dashboard 自动扫描原为同步阻塞（大资产量下 HTTP 请求可能挂起数分钟），
/// 改造为：POST /scan 原子启动后台任务立即返回 202，前端通过
/// GET /scan/status 轮询本服务暴露的进度快照。
///
/// 线程模型：Controller 请求线程 + 后台 Task.Run 扫描线程并发读写，
/// 全部状态变更经 _lock 保护；快照读取同样加锁，保证字段间一致性。
/// </summary>
public class ScanProgressService
{
    private readonly object _lock = new();

    private bool _running;
    private string? _scanId;
    private int _current;
    private int _total;
    private int _newFindings;
    private DateTime? _startedAt;
    private DateTime? _completedAt;
    private string? _error;

    /// <summary>
    /// 原子启动：已有扫描在跑返回 false（Controller 据此回 409）；
    /// 否则重置进度并返回新 scanId。
    /// </summary>
    public bool TryStart(int totalAssets, out string scanId)
    {
        lock (_lock)
        {
            if (_running)
            {
                scanId = _scanId!;
                return false;
            }

            _running = true;
            _scanId = Guid.NewGuid().ToString("N")[..12];
            _current = 0;
            _total = totalAssets;
            _newFindings = 0;
            _startedAt = DateTime.Now;
            _completedAt = null;
            _error = null;

            scanId = _scanId;
            return true;
        }
    }

    /// <summary>扫描线程进度上报（对接 ScanAssetsAsync 的进度回调）。</summary>
    public void Report(int current, int total, int newFindings)
    {
        lock (_lock)
        {
            _current = current;
            _total = total;
            _newFindings = newFindings;
        }
    }

    /// <summary>扫描成功收尾。</summary>
    public void Complete(int newFindings)
    {
        lock (_lock)
        {
            _running = false;
            _newFindings = newFindings;
            _current = _total;
            _completedAt = DateTime.Now;
        }
    }

    /// <summary>扫描异常收尾（错误信息透出到 status 供前端展示）。</summary>
    public void Fail(string error)
    {
        lock (_lock)
        {
            _running = false;
            _completedAt = DateTime.Now;
            _error = error;
        }
    }

    /// <summary>进度快照（加锁读取，保证字段一致性）。</summary>
    public ScanStatusSnapshot GetStatus()
    {
        lock (_lock)
        {
            return new ScanStatusSnapshot(
                _running, _scanId, _current, _total, _newFindings,
                _startedAt, _completedAt, _error);
        }
    }
}

/// <summary>GET /api/Dashboard/scan/status 响应快照。</summary>
public record ScanStatusSnapshot(
    bool Running,
    string? ScanId,
    int Current,
    int Total,
    int NewFindings,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? Error);
