using System.Net;
using System.Text.Json;
using Agent1.Services;

namespace Agent1.Api.Middleware;

/// <summary>
/// 全局异常处理中间件 — 确保任何未处理异常不泄露内部细节到客户端。
/// </summary>
public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (CircuitBreakerOpenException ex)
        {
            // 熔断器打开 — 返回 503 并提示稍后重试
            _logger.LogWarning("熔断器拒绝请求: {Path} — {Message}", context.Request.Path, ex.Message);
            context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
            context.Response.ContentType = "application/json";
            var result = JsonSerializer.Serialize(new
            {
                error = ex.Message,
                traceId = context.TraceIdentifier,
                retryAfter = 30
            });
            await context.Response.WriteAsync(result);
        }
        catch (OperationCanceledException)
        {
            // 客户端断开连接，不记录错误
            context.Response.StatusCode = 499; // Client Closed Request
        }
        catch (TimeoutException ex)
        {
            _logger.LogError(ex, "请求超时: {Path}", context.Request.Path);
            context.Response.StatusCode = (int)HttpStatusCode.GatewayTimeout;
            context.Response.ContentType = "application/json";
            var result = JsonSerializer.Serialize(new { error = "请求处理超时，请稍后重试" });
            await context.Response.WriteAsync(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "未处理异常: {Path} {Method}", context.Request.Path, context.Request.Method);
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.ContentType = "application/json";

            var errorResponse = new
            {
                error = "系统内部错误，请稍后重试",
                traceId = context.TraceIdentifier
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(errorResponse));
        }
    }
}
