using System.Collections.Concurrent;
using System.Text;

namespace Agent1.Services.Observability;

/// <summary>
/// [QF-2026-001] L4 运行时缓存质量监控。
/// 提供三个核心指标用于 Prometheus/Grafana 仪表盘：
///   - agent1_cache_quality_ratio: 有效缓存占比 (Gauge)
///   - agent1_fallback_reject_total: 被拦截的兜底写入次数 (Counter)
///   - agent1_stale_cache_served_total: 低质量缓存返回次数 (Counter)
///
/// 暂以内存方式实现，可通过 /metrics 端点暴露为 Prometheus 文本格式。
/// </summary>
public class MetricsCollectorService
{
    private readonly ConcurrentDictionary<string, double> _gauges = new();
    private readonly ConcurrentDictionary<string, long> _counters = new();

    // ── Gauge 指标 ──

    /// <summary>有效缓存 / 总缓存比率 (0.0 ~ 1.0)</summary>
    public double CacheQualityRatio
    {
        get => _gauges.GetValueOrDefault("agent1_cache_quality_ratio", 1.0);
        set => _gauges["agent1_cache_quality_ratio"] = Math.Clamp(value, 0.0, 1.0);
    }

    // ── Counter 指标 ──

    /// <summary>被规则引擎拦截的兜底写入次数</summary>
    public long FallbackRejectTotal
    {
        get => _counters.GetValueOrDefault("agent1_fallback_reject_total", 0);
    }

    /// <summary>低质量缓存被返回的次数</summary>
    public long StaleCacheServedTotal
    {
        get => _counters.GetValueOrDefault("agent1_stale_cache_served_total", 0);
    }

    /// <summary>总缓存写入次数 (用于计算比率)</summary>
    public long CacheWriteTotal
    {
        get => _counters.GetValueOrDefault("agent1_cache_write_total", 0);
    }

    // ── 操作方法 ──

    /// <summary>记录一次兜底文本被拦截</summary>
    public void IncrementFallbackReject()
    {
        _counters.AddOrUpdate("agent1_fallback_reject_total", 1, (_, v) => v + 1);
    }

    /// <summary>记录一次低质量缓存被返回给用户</summary>
    public void IncrementStaleCacheServed()
    {
        _counters.AddOrUpdate("agent1_stale_cache_served_total", 1, (_, v) => v + 1);
    }

    /// <summary>记录一次缓存写入（带质量等级）</summary>
    public void RecordCacheWrite(bool isValid)
    {
        _counters.AddOrUpdate("agent1_cache_write_total", 1, (_, v) => v + 1);
        if (!isValid)
            IncrementFallbackReject();

        // 更新质量比率
        var total = CacheWriteTotal;
        var rejected = FallbackRejectTotal;
        CacheQualityRatio = total > 0 ? (double)(total - rejected) / total : 1.0;
    }

    /// <summary>记录一次缓存命中（带质量标记）</summary>
    public void RecordCacheHit(bool isLowQuality)
    {
        if (isLowQuality)
            IncrementStaleCacheServed();
    }

    /// <summary>获取所有指标的快照</summary>
    public MetricsSnapshot GetSnapshot()
    {
        return new MetricsSnapshot
        {
            CacheQualityRatio = CacheQualityRatio,
            FallbackRejectTotal = FallbackRejectTotal,
            StaleCacheServedTotal = StaleCacheServedTotal,
            CacheWriteTotal = CacheWriteTotal
        };
    }

    /// <summary>
    /// 导出为 Prometheus 文本格式 (OpenMetrics exposition format)。
    /// 可通过 HTTP /metrics 端点直接暴露。
    /// </summary>
    public string ExportPrometheusText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# HELP agent1_cache_quality_ratio Ratio of non-fallback cache entries to total");
        sb.AppendLine("# TYPE agent1_cache_quality_ratio gauge");
        sb.AppendLine($"agent1_cache_quality_ratio {CacheQualityRatio:F4}");

        sb.AppendLine("# HELP agent1_fallback_reject_total Total number of fallback cache writes rejected");
        sb.AppendLine("# TYPE agent1_fallback_reject_total counter");
        sb.AppendLine($"agent1_fallback_reject_total {FallbackRejectTotal}");

        sb.AppendLine("# HELP agent1_stale_cache_served_total Total number of low-quality cache entries served to users");
        sb.AppendLine("# TYPE agent1_stale_cache_served_total counter");
        sb.AppendLine($"agent1_stale_cache_served_total {StaleCacheServedTotal}");

        sb.AppendLine("# HELP agent1_cache_write_total Total number of cache write attempts");
        sb.AppendLine("# TYPE agent1_cache_write_total counter");
        sb.AppendLine($"agent1_cache_write_total {CacheWriteTotal}");

        return sb.ToString();
    }

    /// <summary>重置所有指标（主要用于测试）</summary>
    public void Reset()
    {
        _gauges.Clear();
        _counters.Clear();
    }
}

/// <summary>指标快照 DTO</summary>
public class MetricsSnapshot
{
    public double CacheQualityRatio { get; set; }
    public long FallbackRejectTotal { get; set; }
    public long StaleCacheServedTotal { get; set; }
    public long CacheWriteTotal { get; set; }
}
