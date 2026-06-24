using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Agent1.Services;
using Agent1.Services.Security;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Agent1.Api.Controllers;

/// <summary>
/// 认证 API — JWT Token 签发
/// 账号配置优先级：环境变量 AUTH_ACCOUNTS_JSON > appsettings.json Auth.Accounts > 开发默认值
/// </summary>
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class AuthController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthController> _logger;
    private readonly IDatabaseService _db;
    private readonly List<AccountEntry> _accounts;

    private readonly TokenBlacklistService _tokenBlacklist;

    public AuthController(IConfiguration configuration, ILogger<AuthController> logger, IDatabaseService db, TokenBlacklistService tokenBlacklist)
    {
        _configuration = configuration;
        _logger = logger;
        _db = db;
        _tokenBlacklist = tokenBlacklist;
        _accounts = LoadAccounts();

        // 自动升级明文密码 → BCrypt 哈希
        foreach (var acc in _accounts)
        {
            if (!IsBcryptHash(acc.Password) && !string.IsNullOrEmpty(acc.Password))
            {
                var hash = UpgradeToBcrypt(acc.Password);
                _logger.LogWarning("⚠️ 账号 {Username} 密码已从明文自动升级为 BCrypt 哈希，请更新配置: {Hash}", acc.Username, hash);
                acc.Password = hash;
            }
        }

        if (_accounts.Count == 0)
        {
            _logger.LogWarning("未配置任何账号，认证将不可用");
        }
        else
        {
            _logger.LogInformation("已加载 {Count} 个账号配置", _accounts.Count);
        }
    }

    /// <summary>
    /// 用户登录 — 返回 JWT Token + Refresh Token
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { error = "用户名和密码不能为空" });
        }

        var account = _accounts.FirstOrDefault(a =>
            a.Username.Equals(request.Username, StringComparison.OrdinalIgnoreCase) &&
            VerifyPassword(request.Password, a.Password));

        if (account == null)
        {
            _logger.LogWarning("登录失败: {Username}", request.Username);
            return Unauthorized(new { error = "用户名或密码错误" });
        }

        var (accessToken, accessExpiry) = GenerateAccessToken(account.Username, account.Role);
        var (refreshToken, _) = await GenerateRefreshTokenAsync(account.Username);

        // [P1 安全加固] 记录设备指纹
        var deviceFingerprint = ComputeDeviceFingerprint();
        _logger.LogInformation("用户登录成功: {Username}, 角色: {Role}, 设备: {DeviceFingerprint}",
            account.Username, account.Role, deviceFingerprint);

        return Ok(new LoginResponse
        {
            Token = accessToken,
            RefreshToken = refreshToken,
            Username = account.Username,
            Role = account.Role,
            ExpiresAt = accessExpiry
        });
    }

    /// <summary>
    /// 刷新 Token — 使用 Refresh Token 换取新的 Access Token
    /// </summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return BadRequest(new { error = "RefreshToken 不能为空" });
        }

        // SHA256 哈希后查询 DB（原子操作：验证 + 删除 + 返回 username）
        var tokenHash = HashRefreshToken(request.RefreshToken);
        var username = await _db.ValidateAndRemoveRefreshTokenAsync(tokenHash);

        if (username == null)
        {
            _logger.LogWarning("Refresh token 无效或已过期");
            return Unauthorized(new { error = "RefreshToken 无效或已过期" });
        }

        // 查找账号确认仍存在
        var account = _accounts.FirstOrDefault(a =>
            a.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
        if (account == null)
        {
            _logger.LogWarning("Refresh token 对应的账号不存在: {Username}", username);
            return Unauthorized(new { error = "账号不存在" });
        }

        // Token Rotation：旧 token 已从 DB 删除，签发新对
        var (accessToken, accessExpiry) = GenerateAccessToken(account.Username, account.Role);
        var (refreshToken, _) = await GenerateRefreshTokenAsync(account.Username);

        _logger.LogInformation("Token 刷新成功: {Username}", account.Username);

        return Ok(new LoginResponse
        {
            Token = accessToken,
            RefreshToken = refreshToken,
            Username = account.Username,
            Role = account.Role,
            ExpiresAt = accessExpiry
        });
    }

    /// <summary>
    /// 三层账号配置加载：环境变量 > appsettings.json > 开发默认值
    /// </summary>
    private List<AccountEntry> LoadAccounts()
    {
        // Layer 1: 环境变量 AUTH_ACCOUNTS_JSON（最高优先级）
        var envJson = Environment.GetEnvironmentVariable("AUTH_ACCOUNTS_JSON");
        if (!string.IsNullOrWhiteSpace(envJson))
        {
            try
            {
                var accounts = System.Text.Json.JsonSerializer.Deserialize<List<AccountEntry>>(envJson);
                if (accounts != null && accounts.Count > 0)
                {
                    // 验证密码不为空
                    var validAccounts = accounts.Where(a => !string.IsNullOrWhiteSpace(a.Password)).ToList();
                    if (validAccounts.Count > 0)
                    {
                        _logger.LogInformation("从环境变量 AUTH_ACCOUNTS_JSON 加载了 {Count} 个账号", validAccounts.Count);
                        return validAccounts;
                    }
                    _logger.LogWarning("环境变量 AUTH_ACCOUNTS_JSON 中的账号密码为空，回退到下一层");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("解析环境变量 AUTH_ACCOUNTS_JSON 失败: {Error}，回退到下一层", ex.Message);
            }
        }

        // Layer 2: appsettings.json Auth.Accounts
        var configAccounts = _configuration.GetSection("Auth:Accounts").Get<List<AccountEntry>>();
        if (configAccounts != null && configAccounts.Count > 0)
        {
            var validAccounts = configAccounts.Where(a => !string.IsNullOrWhiteSpace(a.Password)).ToList();
            if (validAccounts.Count > 0)
            {
                _logger.LogInformation("从 appsettings.json 加载了 {Count} 个账号", validAccounts.Count);
                return validAccounts;
            }
        }

        // Layer 3: 开发默认值（仅当配置和环境变量均未提供时）
        // 生产环境：拒绝启动，强制要求配置真实账号
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if (string.Equals(env, "Production", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogCritical("生产环境未配置任何账号密码！请设置 AUTH_ACCOUNTS_JSON 环境变量");
            throw new InvalidOperationException("生产环境必须通过 AUTH_ACCOUNTS_JSON 环境变量配置账号");
        }
        _logger.LogWarning("⚠️ 使用开发默认账号 — 生产环境请通过环境变量 AUTH_ACCOUNTS_JSON 或 appsettings.json 配置真实密码");

        // 随机生成安全密码，避免硬编码凭证泄露（每次启动不同，仅开发环境使用）
        var adminPwd = GenerateSecureRandomPassword();
        var auditorPwd = GenerateSecureRandomPassword();
        var viewerPwd = GenerateSecureRandomPassword();

        _logger.LogWarning("═══════════════════════════════════════════════════════════");
        _logger.LogWarning("  开发环境默认账号（随机生成，仅本次启动有效）：");
        _logger.LogWarning("  admin   → {AdminPwd}", adminPwd);
        _logger.LogWarning("  auditor → {AuditorPwd}", auditorPwd);
        _logger.LogWarning("  viewer  → {ViewerPwd}", viewerPwd);
        _logger.LogWarning("  请通过 AUTH_ACCOUNTS_JSON 环境变量或 appsettings.json 配置固定密码");
        _logger.LogWarning("═══════════════════════════════════════════════════════════");

        return new List<AccountEntry>
        {
            new() { Username = "admin", Password = adminPwd, Role = "admin" },
            new() { Username = "auditor", Password = auditorPwd, Role = "auditor" },
            new() { Username = "viewer", Password = viewerPwd, Role = "viewer" }
        };
    }

    /// <summary>
    /// 验证密码：支持 BCrypt 哈希和明文（明文仅用于首次加载时的自动升级阶段）
    /// </summary>
    private static bool VerifyPassword(string input, string stored)
    {
        if (string.IsNullOrEmpty(stored)) return false;

        // BCrypt 哈希特征：以 $2a$、$2b$、$2y$ 开头
        if (stored.StartsWith("$2"))
        {
            return BCrypt.Net.BCrypt.Verify(input, stored);
        }

        // 回退：明文比对（仅开发/首次加载时）
        return input == stored;
    }

    /// <summary>
    /// 判断密码是否已是 BCrypt 哈希
    /// </summary>
    private static bool IsBcryptHash(string password)
    {
        return !string.IsNullOrEmpty(password) && password.StartsWith("$2");
    }

    /// <summary>
    /// 将明文密码升级为 BCrypt 哈希（仅在加载配置时调用）
    /// </summary>
    private static string UpgradeToBcrypt(string plainPassword)
    {
        return BCrypt.Net.BCrypt.HashPassword(plainPassword, workFactor: 12);
    }

    /// <summary>
    /// 生成 Access Token（短期，默认 1 小时）
    /// </summary>
    private (string Token, DateTime Expiry) GenerateAccessToken(string username, string role)
    {
        var jwtSection = _configuration.GetSection("Jwt");
        var key = jwtSection["Key"];
        if (string.IsNullOrWhiteSpace(key))
            key = Environment.GetEnvironmentVariable("JWT_KEY");

        if (string.IsNullOrWhiteSpace(key))
        {
            key = "Agent1-Dev-Key-Change-In-Production-2026";
            _logger.LogWarning("⚠️ JWT Key 未配置，使用开发默认密钥");
        }
        else if (key.Length < 32)
        {
            _logger.LogWarning("⚠️ JWT Key 长度不足 {Actual} 字符（建议 >=32），安全性降低", key.Length);
        }

        var issuer = jwtSection["Issuer"] ?? "Agent1";
        var audience = jwtSection["Audience"] ?? "Agent1.Api";

        // Access Token 过期时间：默认 1 小时
        var expireMinutesStr = jwtSection["AccessTokenExpireMinutes"] ?? "60";
        int expireMinutes = int.TryParse(expireMinutesStr, out var m) ? Math.Clamp(m, 5, 1440) : 60;
        var expiresAt = DateTime.UtcNow.AddMinutes(expireMinutes);

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)) { KeyId = "agent1" };
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
            expires: expiresAt,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    /// <summary>
    /// 生成 Refresh Token（长期，默认 7 天），SHA256 哈希后存入 PostgreSQL
    /// </summary>
    private async Task<(string Token, DateTime Expiry)> GenerateRefreshTokenAsync(string username)
    {
        var refreshExpireDaysStr = _configuration["Jwt:RefreshTokenExpireDays"] ?? "7";
        int expireDays = int.TryParse(refreshExpireDaysStr, out var d) ? Math.Clamp(d, 1, 30) : 7;

        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var expiresAt = DateTime.UtcNow.AddDays(expireDays);

        // DB 中仅存储 SHA256 哈希，防止泄露后直接使用
        var tokenHash = HashRefreshToken(rawToken);
        await _db.StoreRefreshTokenAsync(tokenHash, username, expiresAt);

        // 返回原始 token 给客户端（仅这一次可见）
        return (rawToken, expiresAt);
    }

    /// <summary>
    /// 登出 — 将当前 Access Token 加入黑名单，使其在过期前无法使用。
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    public IActionResult Logout()
    {
        var jti = User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        if (jti != null)
        {
            // 从 exp claim 读取过期时间
            var expClaim = User.FindFirst(JwtRegisteredClaimNames.Exp)?.Value;
            var expiresAt = DateTime.UtcNow.AddHours(1); // 默认 1 小时
            if (expClaim != null && long.TryParse(expClaim, out var expUnix))
                expiresAt = DateTimeOffset.FromUnixTimeSeconds(expUnix).UtcDateTime;

            _tokenBlacklist.Revoke(jti, expiresAt);
            _logger.LogInformation("用户登出: Token jti={Jti} 已加入黑名单, 过期时间={ExpiresAt:O}",
                jti[..Math.Min(jti.Length, 8)], expiresAt);
        }

        return Ok(new { message = "已登出" });
    }

    /// <summary>从当前 HTTP 请求计算设备指纹</summary>
    private string ComputeDeviceFingerprint()
    {
        var ip = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()
                 ?? HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = HttpContext.Request.Headers.UserAgent.ToString();
        return DeviceFingerprintService.ComputeFingerprint(ip, ua);
    }

    /// <summary>生成密码学强度的随机密码（开发环境默认账号使用）</summary>
    private static string GenerateSecureRandomPassword()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(12))[..12];
    }

    /// <summary>对 Refresh Token 做 SHA256 哈希</summary>
    private static string HashRefreshToken(string rawToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToBase64String(hash);
    }
}

/// <summary>账号配置条目</summary>
public class AccountEntry
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Role { get; set; } = "viewer";
}

public record LoginRequest(string Username, string Password);

public record RefreshRequest(string RefreshToken);

public record LoginResponse
{
    public string Token { get; init; } = "";
    public string RefreshToken { get; init; } = "";
    public string Username { get; init; } = "";
    public string Role { get; init; } = "";
    public DateTime ExpiresAt { get; init; }
}


