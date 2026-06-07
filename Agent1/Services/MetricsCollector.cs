using System.Collections.Concurrent;

namespace Agent1.Services;

/// <summary>
/// Task 9: 轻量级应用指标收集器。
/// 提供 LLM 调用延迟、RAG 检索延迟、请求计数等基础指标，
/// 支持通过 /metrics 端点暴露（Prometheus text 格式）。
/// 线程安全，适合 ASP.NET Core 多线程环境。
/// </summary>
public static class MetricsCollector
{
    // ── LLM 指标 ──
    private static long _llmCallCount;
    private static long _llmTotalDurationMs;
    private static long _llmErrorCount;
    private static long _llmRetryCount;

    // ── RAG 指标 ──
    private static long _ragSearchCount;
    private static long _ragTotalDurationMs;
    private static long _ragCacheHitCount;

    // ── 请求指标 ──
    private static long _apiRequestCount;
    private static long _apiErrorCount;

    // 慢请求阈值 (毫秒)
    private const long SlowRequestThresholdMs = 60_000;

    // ── LLM 调用记录 ──

    public static void RecordLlmCall(long durationMs, bool success, int retries = 0)
    {
        Interlocked.Increment(ref _llmCallCount);
        Interlocked.Add(ref _llmTotalDurationMs, durationMs);
        if (!success) Interlocked.Increment(ref _llmErrorCount);
        Interlocked.Add(ref _llmRetryCount, retries);

        if (durationMs > SlowRequestThresholdMs)
        {
            Console.WriteLine($"   ⚠️ [慢请求告警] LLM 调用耗时 {durationMs / 1000.0:F1}s (阈值 60s)");
        }
    }

    // ── RAG 检索记录 ──

    public static void RecordRagSearch(long durationMs, bool cacheHit = false)
    {
        Interlocked.Increment(ref _ragSearchCount);
        Interlocked.Add(ref _ragTotalDurationMs, durationMs);
        if (cacheHit) Interlocked.Increment(ref _ragCacheHitCount);
    }

    // ── API 请求记录 ──

    public static void RecordApiRequest(bool success)
    {
        Interlocked.Increment(ref _apiRequestCount);
        if (!success) Interlocked.Increment(ref _apiErrorCount);
    }

    // ── 指标快照 ──

    public static MetricsSnapshot GetSnapshot()
    {
        return new MetricsSnapshot
        {
            LlmCallCount = Interlocked.Read(ref _llmCallCount),
            LlmAvgDurationMs = Interlocked.Read(ref _llmCallCount) > 0
                ? Interlocked.Read(ref _llmTotalDurationMs) / (double)Interlocked.Read(ref _llmCallCount)
                : 0,
            LlmErrorCount = Interlocked.Read(ref _llmErrorCount),
            LlmRetryCount = Interlocked.Read(ref _llmRetryCount),
            RagSearchCount = Interlocked.Read(ref _ragSearchCount),
            RagAvgDurationMs = Interlocked.Read(ref _ragSearchCount) > 0
                ? Interlocked.Read(ref _ragTotalDurationMs) / (double)Interlocked.Read(ref _ragSearchCount)
                : 0,
            RagCacheHitCount = Interlocked.Read(ref _ragCacheHitCount),
            ApiRequestCount = Interlocked.Read(ref _apiRequestCount),
            ApiErrorCount = Interlocked.Read(ref _apiErrorCount),
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 返回 Prometheus text 格式的指标字符串，供 /metrics 端点使用。
    /// </summary>
    public static string ToPrometheusFormat()
    {
        var snap = GetSnapshot();
        return string.Join("\n",
            "# HELP agent1_llm_calls_total Total LLM call count",
            "# TYPE agent1_llm_calls_total counter",
            $"agent1_llm_calls_total {snap.LlmCallCount}",
            "",
            "# HELP agent1_llm_duration_ms_avg Average LLM call duration in ms",
            "# TYPE agent1_llm_duration_ms_avg gauge",
            $"agent1_llm_duration_ms_avg {snap.LlmAvgDurationMs:F1}",
            "",
            "# HELP agent1_llm_errors_total Total LLM error count",
            "# TYPE agent1_llm_errors_total counter",
            $"agent1_llm_errors_total {snap.LlmErrorCount}",
            "",
            "# HELP agent1_rag_searches_total Total RAG search count",
            "# TYPE agent1_rag_searches_total counter",
            $"agent1_rag_searches_total {snap.RagSearchCount}",
            "",
            "# HELP agent1_rag_duration_ms_avg Average RAG search duration in ms",
            "# TYPE agent1_rag_duration_ms_avg gauge",
            $"agent1_rag_duration_ms_avg {snap.RagAvgDurationMs:F1}",
            "",
            "# HELP agent1_rag_cache_hits_total Total RAG cache hits",
            "# TYPE agent1_rag_cache_hits_total counter",
            $"agent1_rag_cache_hits_total {snap.RagCacheHitCount}",
            "",
            "# HELP agent1_api_requests_total Total API request count",
            "# TYPE agent1_api_requests_total counter",
            $"agent1_api_requests_total {snap.ApiRequestCount}",
            "",
            "# HELP agent1_api_errors_total Total API error count",
            "# TYPE agent1_api_errors_total counter",
            $"agent1_api_errors_total {snap.ApiErrorCount}",
            ""
        );
    }
}

/// <summary>
/// 指标快照 — 线程安全的瞬时读数
/// </summary>
public class MetricsSnapshot
{
    public long LlmCallCount { get; init; }
    public double LlmAvgDurationMs { get; init; }
    public long LlmErrorCount { get; init; }
    public long LlmRetryCount { get; init; }
    public long RagSearchCount { get; init; }
    public double RagAvgDurationMs { get; init; }
    public long RagCacheHitCount { get; init; }
    public long ApiRequestCount { get; init; }
    public long ApiErrorCount { get; init; }
    public DateTime Timestamp { get; init; }
}
