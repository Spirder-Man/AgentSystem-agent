using Agent1.Services;
using System.Diagnostics;

namespace Agent1.Api.Middleware;

/// <summary>
/// Task 9e: 请求指标中间件 — 记录 API 请求计数、耗时和慢请求告警。
/// 在 GlobalExceptionMiddleware 之后注册，仅记录成功通过异常处理的请求。
/// </summary>
public class RequestMetricsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestMetricsMiddleware> _logger;
    private const long SlowRequestThresholdMs = 60_000; // 60s

    public RequestMetricsMiddleware(RequestDelegate next, ILogger<RequestMetricsMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        bool success = true;

        try
        {
            await _next(context);
        }
        catch
        {
            success = false;
            throw; // 重新抛出，由 GlobalExceptionMiddleware 处理
        }
        finally
        {
            sw.Stop();

            // 跳过健康检查和指标端点（避免噪声）
            var path = context.Request.Path.Value ?? "";
            if (!path.StartsWith("/health") && path != "/metrics")
            {
                MetricsCollector.RecordApiRequest(success);

                if (sw.ElapsedMilliseconds > SlowRequestThresholdMs)
                {
                    _logger.LogWarning(
                        "慢请求告警: {Method} {Path} 耗时 {Duration:F1}s (阈值 {Threshold}s)",
                        context.Request.Method,
                        path,
                        sw.Elapsed.TotalSeconds,
                        SlowRequestThresholdMs / 1000.0);
                }
            }
        }
    }
}
