using System.Security.Cryptography;
using System.Text;

namespace Agent1.Services
{
    /// <summary>
    /// [P1] 审计哈希链算法（AuditService 与 DatabaseService 共用的唯一实现）。
    ///
    /// 背景：DatabaseService.AddAuditLogAsync 是底层直插 API，历史上调用方
    /// 不传 chainHash 时会写入 NULL，在链上产生空洞（篡改旁路信号）。
    /// 修复后该直插点内部也强制补算哈希，因此算法必须只有一份实现，
    /// 防止 AuditService 与 DatabaseService 各持一份导致漂移。
    /// </summary>
    public static class AuditChainHash
    {
        /// <summary>
        /// 统一归一为 UTC 微秒精度，确保写入时与读回重算时的时间戳一致。
        /// - PostgreSQL timestamptz 为微秒精度，C# tick 为 100ns，故截断到 10 tick(=1微秒)的倍数
        /// - Unspecified 视为 UTC，避免 DB 读回时区不确定导致漂移
        /// </summary>
        public static DateTime NormalizeToMicroseconds(DateTime dt)
        {
            var utc = dt.Kind switch
            {
                DateTimeKind.Utc => dt,
                DateTimeKind.Local => dt.ToUniversalTime(),
                _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc)
            };
            return new DateTime(utc.Ticks - (utc.Ticks % 10), DateTimeKind.Utc);
        }

        /// <summary>
        /// 计算哈希链值：SHA256(前一条ChainHash + "|" + 本条内容)。
        /// 固定 UTC/微秒格式，避免 :O(100ns) 与 DB 微秒精度不匹配。
        /// </summary>
        public static string ComputeChainHash(string? prevHash, string userId, string operation, string details, DateTime createTime)
        {
            var ts = NormalizeToMicroseconds(createTime).ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ", System.Globalization.CultureInfo.InvariantCulture);
            var input = $"{prevHash ?? "GENESIS"}|{userId}|{operation}|{details}|{ts}";
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
    }
}
