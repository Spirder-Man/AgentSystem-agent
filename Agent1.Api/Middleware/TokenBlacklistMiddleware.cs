using System.IdentityModel.Tokens.Jwt;
using Agent1.Services.Security;

namespace Agent1.Api.Middleware;

/// <summary>
/// Token 黑名单中间件 — 在 JWT 认证之后检查 Token 是否已被撤销（登出）。
/// 
/// 必须在 UseAuthentication() 之后、UseAuthorization() 之前注册。
/// 流程：认证 → 黑名单检查 → 授权 → Controller
/// </summary>
public class TokenBlacklistMiddleware
{
    private readonly RequestDelegate _next;
    private readonly TokenBlacklistService _blacklist;

    public TokenBlacklistMiddleware(RequestDelegate next, TokenBlacklistService blacklist)
    {
        _next = next;
        _blacklist = blacklist;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // 仅检查已认证的请求（跳过匿名端点如 /api/auth/login）
        if (context.User.Identity?.IsAuthenticated == true)
        {
            // 从 JWT Claims 中提取 jti
            var jtiClaim = context.User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
            if (!string.IsNullOrEmpty(jtiClaim) && _blacklist.IsRevoked(jtiClaim))
            {
                context.Response.StatusCode = 401;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsync(
                    "{\"error\":\"Token 已被撤销，请重新登录\"}");
                return;
            }
        }

        await _next(context);
    }
}
