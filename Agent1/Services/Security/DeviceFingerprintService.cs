using System.Security.Cryptography;
using System.Text;

namespace Agent1.Services.Security;

/// <summary>
/// 设备指纹服务 — 基于 HTTP 请求特征生成设备唯一标识。
/// 用于登录时记录可信设备，换设备登录时可触发额外验证。
/// 
/// 算法：SHA256(IP + UserAgent) 取前 16 位十六进制
/// 局限性：IP 可能变化（NAT/代理），UserAgent 可伪造 → 仅作为辅助验证因子
/// </summary>
public static class DeviceFingerprintService
{
    /// <summary>
    /// 从 HTTP 请求特征计算设备指纹。
    /// </summary>
    /// <param name="ipAddress">客户端 IP（优先 X-Forwarded-For，其次 RemoteIpAddress）</param>
    /// <param name="userAgent">User-Agent 请求头</param>
    /// <returns>16 位十六进制指纹</returns>
    public static string ComputeFingerprint(string? ipAddress, string? userAgent)
    {
        var raw = $"{ipAddress ?? "0.0.0.0"}|{userAgent ?? "unknown"}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
