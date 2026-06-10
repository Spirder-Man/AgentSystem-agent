using System.Collections.Concurrent;

namespace Agent1.Api.Middleware;

/// <summary>
/// 简易速率限制中间件 — 基于滑动窗口的 IP 级别限流。
/// 生产环境建议使用 AspNetCoreRateLimit 或网关层限流（nginx/kong）。
/// 当前实现适用于单机部署场景。
/// </summary>
public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly int _maxRequests;
    private readonly TimeSpan _window;
    private readonly ConcurrentDictionary<string, SlidingWindow> _clients = new();

    public RateLimitingMiddleware(RequestDelegate next, int maxRequests = 100, int windowSeconds = 60)
    {
        _next = next;
        _maxRequests = maxRequests;
        _window = TimeSpan.FromSeconds(windowSeconds);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var clientKey = GetClientKey(context);
        var now = DateTime.UtcNow;

        var window = _clients.GetOrAdd(clientKey, _ => new SlidingWindow());
        lock (window)
        {
            // 清理过期记录
            window.Timestamps.RemoveAll(t => now - t > _window);

            if (window.Timestamps.Count >= _maxRequests)
            {
                context.Response.StatusCode = 429;
                context.Response.ContentType = "application/json; charset=utf-8";
                var resetAfter = (int)(window.Timestamps[0] + _window - now).TotalSeconds;
                context.Response.Headers["Retry-After"] = Math.Max(resetAfter, 1).ToString();
                // 同步等待写入完成，防止响应体截断
                context.Response.WriteAsync(
                    $"{{\"error\":\"请求过于频繁，请 {Math.Max(resetAfter, 1)} 秒后重试\",\"retry_after_seconds\":{Math.Max(resetAfter, 1)}}}")
                    .GetAwaiter().GetResult();
                return;
            }

            window.Timestamps.Add(now);
        }

        await _next(context);
    }

    private static string GetClientKey(HttpContext context)
    {
        // 优先使用 X-Forwarded-For（反向代理场景），其次用直接 IP
        var ip = context.Request.Headers["X-Forwarded-For"].FirstOrDefault()
                 ?? context.Connection.RemoteIpAddress?.ToString()
                 ?? "unknown";

        // 按 IP + 端点路径限流（更细粒度）
        var path = context.Request.Path.Value ?? "/";
        return $"{ip}:{path}";
    }

    private class SlidingWindow
    {
        public List<DateTime> Timestamps { get; } = new();
    }
}
