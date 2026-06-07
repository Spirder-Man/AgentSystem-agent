using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Agent1.Api.Controllers;

/// <summary>
/// 认证 API — JWT Token 签发
/// </summary>
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class AuthController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IConfiguration configuration, ILogger<AuthController> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// 用户登录 — 返回 JWT Token
    /// 当前为简化实现（用户名密码硬编码 admin/admin123），
    /// 生产环境应替换为数据库验证或 SSO 对接。
    /// </summary>
    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        // 简化的用户验证（生产环境替换为数据库 / LDAP / SSO）
        if (!IsValidUser(request.Username, request.Password))
        {
            _logger.LogWarning("登录失败: {Username}", request.Username);
            return Unauthorized(new { error = "用户名或密码错误" });
        }

        var token = GenerateJwtToken(request.Username, GetUserRole(request.Username));
        _logger.LogInformation("用户登录成功: {Username}, 角色: {Role}", request.Username, GetUserRole(request.Username));

        return Ok(new LoginResponse
        {
            Token = token,
            Username = request.Username,
            Role = GetUserRole(request.Username),
            ExpiresAt = DateTime.UtcNow.AddHours(8)
        });
    }

    private bool IsValidUser(string username, string password)
    {
        // 生产级：从数据库/AD/LDAP 验证
        // 当前开发版：admin/admin123, auditor/auditor123, viewer/viewer123
        return (username, password) switch
        {
            ("admin", "admin123") => true,
            ("auditor", "auditor123") => true,
            ("viewer", "viewer123") => true,
            _ => false
        };
    }

    private string GetUserRole(string username)
    {
        return username switch
        {
            "admin" => "admin",
            "auditor" => "auditor",
            "viewer" => "viewer",
            _ => "viewer"
        };
    }

    private string GenerateJwtToken(string username, string role)
    {
        var jwtSection = _configuration.GetSection("Jwt");
        var key = jwtSection["Key"] ?? Environment.GetEnvironmentVariable("JWT_KEY") ?? "Agent1-Dev-Key-Change-In-Production-2026";
        var issuer = jwtSection["Issuer"] ?? "Agent1";
        var audience = jwtSection["Audience"] ?? "Agent1.Api";

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Sub, username),
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public record LoginRequest(string Username, string Password);

public record LoginResponse
{
    public string Token { get; init; } = "";
    public string Username { get; init; } = "";
    public string Role { get; init; } = "";
    public DateTime ExpiresAt { get; init; }
}
