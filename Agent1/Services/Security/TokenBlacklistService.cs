using System.Collections.Concurrent;

namespace Agent1.Services.Security;

/// <summary>
/// JWT Token 黑名单服务 — 登出时将 Access Token 的 jti 加入黑名单。
/// 中间件在每次请求时检查 jti 是否已被撤销。
/// 
/// 设计决策：
///   - 内存存储：单机部署足够，多机部署需换用 Redis
///   - 定时清理：每 60s 清除已过期的条目（jti 有效期 = Token 过期时间）
///   - 线程安全：ConcurrentDictionary + 后台 Timer
/// </summary>
public class TokenBlacklistService : IDisposable
{
    private readonly ConcurrentDictionary<string, DateTime> _revokedTokens = new();
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    public TokenBlacklistService()
    {
        // 每分钟清理已过期的黑名单条目
        _cleanupTimer = new Timer(CleanupExpiredTokens, null,
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// 撤销指定 Token（将其 jti 加入黑名单直到过期）。
    /// </summary>
    /// <param name="jti">JWT 的 jti claim</param>
    /// <param name="expiresAt">Token 的过期时间（UTC）</param>
    public void Revoke(string jti, DateTime expiresAt)
    {
        _revokedTokens[jti] = expiresAt;
    }

    /// <summary>
    /// 检查指定 jti 是否已被撤销。
    /// </summary>
    public bool IsRevoked(string jti)
    {
        return _revokedTokens.ContainsKey(jti);
    }

    /// <summary>
    /// 获取当前黑名单中的条目数量（用于监控 /stats 端点）
    /// </summary>
    public int Count => _revokedTokens.Count;

    private void CleanupExpiredTokens(object? state)
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in _revokedTokens)
        {
            if (kvp.Value <= now)
                _revokedTokens.TryRemove(kvp.Key, out _);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cleanupTimer.Dispose();
        _revokedTokens.Clear();
    }
}
