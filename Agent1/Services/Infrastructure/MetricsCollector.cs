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

    // ── 双通道解耦管道指标 ──
    private static long _decoupledPipelineInvocations;
    private static long _decoupledPipelineCharsRemoved;

    // ── S3-3: 生产监控扩展指标 ──
    private static long _llmActiveRequests;           // LLM 并发活跃请求数 (gauge)
    private static long _ragCacheMissCount;            // RAG 缓存未命中次数 (counter)
    private static long _circuitBreakerOpen;           // 熔断器状态: 1=打开/拒绝, 0=闭合 (gauge)
    private static long _apiStatus5xxCount;            // API 5xx 响应数 (counter)
    private static long _llmFallbackToRuleEngineCount; // LLM 降级到规则引擎次数 (counter)

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

    // ── 双通道解耦管道记录 ──

    /// <summary>记录一次双通道解耦管道调用</summary>
    public static void RecordDecoupledPipelineInvocation()
    {
        Interlocked.Increment(ref _decoupledPipelineInvocations);
    }

    /// <summary>记录双通道解耦管道中移除的字符数（幻觉法规消毒）</summary>
    public static void RecordDecoupledPipelineCharsRemoved(long count)
    {
        if (count > 0)
            Interlocked.Add(ref _decoupledPipelineCharsRemoved, count);
    }

    // ── S3-3: 生产监控扩展记录 ──

    /// <summary>LLM 活跃请求 +1 (调用开始)</summary>
    public static void IncrementLlmActiveRequests()
    {
        Interlocked.Increment(ref _llmActiveRequests);
    }

    /// <summary>LLM 活跃请求 -1 (调用结束)</summary>
    public static void DecrementLlmActiveRequests()
    {
        Interlocked.Decrement(ref _llmActiveRequests);
    }

    /// <summary>RAG 缓存未命中计数</summary>
    public static void RecordRagCacheMiss()
    {
        Interlocked.Increment(ref _ragCacheMissCount);
    }

    /// <summary>设置熔断器状态</summary>
    public static void SetCircuitBreakerOpen(bool isOpen)
    {
        Interlocked.Exchange(ref _circuitBreakerOpen, isOpen ? 1 : 0);
    }

    /// <summary>API 5xx 响应计数</summary>
    public static void RecordApi5xx()
    {
        Interlocked.Increment(ref _apiStatus5xxCount);
    }

    /// <summary>LLM 降级到规则引擎计数</summary>
    public static void RecordLlmFallbackToRuleEngine()
    {
        Interlocked.Increment(ref _llmFallbackToRuleEngineCount);
    }

    // ── 指标快照 ──

    // [P2-14 FIX] 单次原子读取所有计数器, 避免多次 Interlocked.Read 之间状态变化导致快照不一致
    public static MetricsSnapshot GetSnapshot()
    {
        var llmCalls = Interlocked.Read(ref _llmCallCount);
        var llmDuration = Interlocked.Read(ref _llmTotalDurationMs);
        var llmErrors = Interlocked.Read(ref _llmErrorCount);
        var llmRetries = Interlocked.Read(ref _llmRetryCount);
        var ragSearches = Interlocked.Read(ref _ragSearchCount);
        var ragDuration = Interlocked.Read(ref _ragTotalDurationMs);
        var ragHits = Interlocked.Read(ref _ragCacheHitCount);
        var apiReqs = Interlocked.Read(ref _apiRequestCount);
        var apiErrors = Interlocked.Read(ref _apiErrorCount);
        var pipelineInvocations = Interlocked.Read(ref _decoupledPipelineInvocations);
        var pipelineCharsRemoved = Interlocked.Read(ref _decoupledPipelineCharsRemoved);
        var llmActive = Interlocked.Read(ref _llmActiveRequests);
        var ragMisses = Interlocked.Read(ref _ragCacheMissCount);
        var cbOpen = Interlocked.Read(ref _circuitBreakerOpen);
        var api5xx = Interlocked.Read(ref _apiStatus5xxCount);
        var fallbackCount = Interlocked.Read(ref _llmFallbackToRuleEngineCount);

        return new MetricsSnapshot
        {
            LlmCallCount = llmCalls,
            LlmAvgDurationMs = llmCalls > 0 ? llmDuration / (double)llmCalls : 0,
            LlmErrorCount = llmErrors,
            LlmRetryCount = llmRetries,
            RagSearchCount = ragSearches,
            RagAvgDurationMs = ragSearches > 0 ? ragDuration / (double)ragSearches : 0,
            RagCacheHitCount = ragHits,
            ApiRequestCount = apiReqs,
            ApiErrorCount = apiErrors,
            DecoupledPipelineInvocations = pipelineInvocations,
            DecoupledPipelineCharsRemoved = pipelineCharsRemoved,
            LlmActiveRequests = llmActive,
            RagCacheMissCount = ragMisses,
            CircuitBreakerOpen = cbOpen,
            ApiStatus5xxCount = api5xx,
            LlmFallbackToRuleEngineCount = fallbackCount,
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
            "",
            "# HELP agent1_decoupled_pipeline_invocations_total Total decoupled pipeline invocations",
            "# TYPE agent1_decoupled_pipeline_invocations_total counter",
            $"agent1_decoupled_pipeline_invocations_total {snap.DecoupledPipelineInvocations}",
            "",
            "# HELP agent1_decoupled_pipeline_chars_removed_total Total characters removed by decoupled pipeline sanitization",
            "# TYPE agent1_decoupled_pipeline_chars_removed_total counter",
            $"agent1_decoupled_pipeline_chars_removed_total {snap.DecoupledPipelineCharsRemoved}",
            "",
            "# HELP agent1_llm_active_requests Current active LLM requests (concurrency)",
            "# TYPE agent1_llm_active_requests gauge",
            $"agent1_llm_active_requests {snap.LlmActiveRequests}",
            "",
            "# HELP agent1_rag_cache_misses_total Total RAG cache misses",
            "# TYPE agent1_rag_cache_misses_total counter",
            $"agent1_rag_cache_misses_total {snap.RagCacheMissCount}",
            "",
            "# HELP agent1_circuit_breaker_open Circuit breaker status: 1=open/blocked, 0=closed/ok",
            "# TYPE agent1_circuit_breaker_open gauge",
            $"agent1_circuit_breaker_open {snap.CircuitBreakerOpen}",
            "",
            "# HELP agent1_api_5xx_total Total API 5xx responses",
            "# TYPE agent1_api_5xx_total counter",
            $"agent1_api_5xx_total {snap.ApiStatus5xxCount}",
            "",
            "# HELP agent1_llm_fallback_rule_engine_total Total LLM-to-rule-engine fallback invocations",
            "# TYPE agent1_llm_fallback_rule_engine_total counter",
            $"agent1_llm_fallback_rule_engine_total {snap.LlmFallbackToRuleEngineCount}",
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
    public long DecoupledPipelineInvocations { get; init; }
    public long DecoupledPipelineCharsRemoved { get; init; }
    public long LlmActiveRequests { get; init; }
    public long RagCacheMissCount { get; init; }
    public long CircuitBreakerOpen { get; init; }
    public long ApiStatus5xxCount { get; init; }
    public long LlmFallbackToRuleEngineCount { get; init; }
    public DateTime Timestamp { get; init; }
}
