using Serilog.Context;

namespace Agent1.Api.Middleware;

/// <summary>
/// 请求 ID 透传中间件 (Task 3.3)
/// 读取 X-Request-ID 请求头，无则生成 UUID，注入 Serilog LogContext + HTTP Response Header。
/// </summary>
public class RequestIdMiddleware
{
    private readonly RequestDelegate _next;

    public RequestIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // 读取或生成 Request ID
        var requestId = context.Request.Headers["X-Request-ID"].FirstOrDefault()
                        ?? Guid.NewGuid().ToString("N");

        // 注入到 HTTP Response Header
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Request-ID"] = requestId;
            return Task.CompletedTask;
        });

        // 注入到 Serilog LogContext（所有日志自动携带）
        using (LogContext.PushProperty("RequestId", requestId))
        {
            await _next(context);
        }
    }
}
