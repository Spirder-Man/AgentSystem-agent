using System.Security.Cryptography;
using System.Text;

namespace Agent1.Services
{
    public class AuditService : IAuditService
    {
        private readonly List<AuditLog> _auditLogs = new();
        private readonly object _lock = new();
        private readonly IDatabaseService? _db;
        // [P1] 哈希链：记录上一条日志的 SHA256 哈希，用于构建不可篡改的日志链
        private string? _lastChainHash;
        // [P3 哈希链] 重启后从 DB 尾条恢复链头，避免新记录错误链接到 GENESIS 导致断链
        private bool _chainInitialized;
        private readonly SemaphoreSlim _initLock = new(1, 1);

        public AuditService(IDatabaseService? db = null)
        {
            _db = db;
        }

        // [P3 哈希链] 统一归一为 UTC 微秒精度，确保写入时与读回重算时的时间戳一致
        // - PostgreSQL timestamptz 为微秒精度，C# tick 为 100ns，故截断到 10 tick(=1微秒)的倍数
        // - Unspecified 视为 UTC，避免 DB 读回时区不确定导致漂移
        private static DateTime NormalizeToMicroseconds(DateTime dt)
        {
            var utc = dt.Kind switch
            {
                DateTimeKind.Utc => dt,
                DateTimeKind.Local => dt.ToUniversalTime(),
                _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc)
            };
            return new DateTime(utc.Ticks - (utc.Ticks % 10), DateTimeKind.Utc);
        }

        // [P1] 计算哈希链值：SHA256(前一条ChainHash + "|" + 本条内容)
        private static string ComputeChainHash(string? prevHash, string userId, string operation, string details, DateTime createTime)
        {
            // [P3 哈希链] 固定 UTC/微秒格式，避免 :O(100ns) 与 DB 微秒精度不匹配
            var ts = NormalizeToMicroseconds(createTime).ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ", System.Globalization.CultureInfo.InvariantCulture);
            var input = $"{prevHash ?? "GENESIS"}|{userId}|{operation}|{details}|{ts}";
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        // [P3 哈希链] 首次写入前从 DB 尾条恢复链头，确保跨重启链连续
        private async Task EnsureChainInitializedAsync()
        {
            if (_chainInitialized || _db == null) return;
            await _initLock.WaitAsync();
            try
            {
                if (_chainInitialized) return;
                var last = await _db.GetLastAuditChainHashAsync();
                if (!string.IsNullOrEmpty(last))
                    _lastChainHash = last;
                _chainInitialized = true;
            }
            finally { _initLock.Release(); }
        }

        public async Task LogOperationAsync(string userId, string operation, string details, bool isSensitive = false)
        {
            // [P3 哈希链] 确保链头已从 DB 恢复
            await EnsureChainInitializedAsync();

            // Task 3: 敏感信息脱敏 — 审计日志写入前对 details 进行脱敏
            var maskedDetails = isSensitive ? SensitiveDataMasker.Mask(details) : details;
            // [P3 哈希链] 使用 UTC/微秒归一时间，同一值既算哈希又写入 DB
            var createTime = NormalizeToMicroseconds(DateTime.UtcNow);

            // [P1] 计算哈希链
            var chainHash = ComputeChainHash(_lastChainHash, userId, operation, maskedDetails, createTime);

            // 确定 IP（从 HttpContext 获取，此处由调用方通过 details 传递）
            string? ipAddress = null;

            // 主路径：写入 PostgreSQL（持久化）
            if (_db != null)
            {
                try
                {
                    await _db.AddAuditLogAsync(userId, operation, maskedDetails, ipAddress, chainHash, createTime);
                    _lastChainHash = chainHash; // 仅 DB 成功才更新链
                    return; // DB 写入成功，跳过内存
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ⚠️ 审计日志 DB 写入失败，降级到内存: {ex.Message}");
                }
            }

            // 降级路径：内存存储（DB 不可用时）
            lock (_lock)
            {
                _auditLogs.Add(new AuditLog
                {
                    Id = _auditLogs.Count + 1,
                    UserId = userId,
                    Operation = operation,
                    Details = maskedDetails,
                    IsSensitive = isSensitive,
                    CreateTime = createTime,
                    ChainHash = chainHash  // [P1] 记录哈希链
                });
                _lastChainHash = chainHash;
            }
        }

        public async Task<List<AuditLog>> GetAuditLogsAsync(DateTime? startTime, DateTime? endTime, string? userId = null)
        {
            // 主路径：从 PostgreSQL 查询
            if (_db != null)
            {
                try
                {
                    return await _db.GetAuditLogsAsync(startTime, endTime, userId);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ⚠️ 审计日志 DB 查询失败，降级到内存: {ex.Message}");
                }
            }

            // 降级路径：内存查询
            List<AuditLog> snapshot;
            lock (_lock)
            {
                snapshot = _auditLogs.ToList();
            }

            var query = snapshot.AsEnumerable();

            if (startTime.HasValue)
                query = query.Where(l => l.CreateTime >= startTime.Value);
            if (endTime.HasValue)
                query = query.Where(l => l.CreateTime <= endTime.Value);
            if (!string.IsNullOrEmpty(userId))
                query = query.Where(l => l.UserId == userId);

            return query.OrderByDescending(l => l.CreateTime).ToList();
        }

        public Task<string> ExportAuditReportAsync(DateTime startTime, DateTime endTime)
        {
            List<AuditLog> snapshot;
            lock (_lock)
            {
                snapshot = _auditLogs.ToList();
            }

            var logs = snapshot
                .Where(l => l.CreateTime >= startTime && l.CreateTime <= endTime)
                .OrderByDescending(l => l.CreateTime)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine("化工园区危化品合规审核 - 审计日志报告");
            sb.AppendLine($"报告时间范围: {startTime:yyyy-MM-dd HH:mm:ss} 至 {endTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"记录总数: {logs.Count}");
            sb.AppendLine();

            foreach (var log in logs)
            {
                // Task 3: 导出报告时对敏感记录再次脱敏
                var exportDetails = log.IsSensitive
                    ? SensitiveDataMasker.Mask(log.Details)
                    : log.Details;

                sb.AppendLine($"[{log.CreateTime:yyyy-MM-dd HH:mm:ss}] 用户:{log.UserId} 操作:{log.Operation}");
                sb.AppendLine($"  详情: {exportDetails}");
                sb.AppendLine();
            }

            return Task.FromResult(sb.ToString());
        }

        // [P1] 验证哈希链完整性：从头逐条重算，检测任何篡改
        public async Task<(bool intact, long? brokenAtId, string detail)> VerifyIntegrityAsync()
        {
            var logs = await GetAuditLogsAsync(null, null);  // [P3] 此时返回的 AuditLog 含 ChainHash（从 DB 读取）
            logs = logs.OrderBy(l => l.Id).ToList();

            if (logs.Count == 0)
                return (true, null, "无审计日志");

            string? expectedHash = null;
            foreach (var log in logs)
            {
                var computed = ComputeChainHash(expectedHash, log.UserId, log.Operation, log.Details, log.CreateTime);
                if (log.ChainHash != null && !string.Equals(computed, log.ChainHash, StringComparison.OrdinalIgnoreCase))
                {
                    return (false, log.Id,
                        $"哈希链断裂于 ID={log.Id}: 期望 {computed[..16]}..., 实际 {(log.ChainHash.Length >= 16 ? log.ChainHash[..16] : log.ChainHash)}...");
                }
                expectedHash = log.ChainHash ?? computed;
            }

            return (true, null, $"哈希链完整，共 {logs.Count} 条记录");
        }

        // [P3 哈希链修复] 逐条重算所有 chain_hash 并回写 DB，修复因时间精度不一致导致的历史断链
        public async Task<(int repaired, string detail)> RepairChainAsync()
        {
            if (_db == null)
                return (0, "数据库不可用，无法执行修复");

            var logs = await _db.GetAuditLogsAsync(null, null);
            logs = logs.OrderBy(l => l.Id).ToList();

            if (logs.Count == 0)
                return (0, "无审计日志，无需修复");

            int repaired = 0;
            string? prevHash = null;

            foreach (var log in logs)
            {
                var computed = ComputeChainHash(prevHash, log.UserId, log.Operation, log.Details, log.CreateTime);

                // 仅当哈希不匹配时才回写（避免无意义 UPDATE）
                if (log.ChainHash == null || !string.Equals(computed, log.ChainHash, StringComparison.OrdinalIgnoreCase))
                {
                    await _db.UpdateAuditChainHashAsync(log.Id, computed);
                    repaired++;
                }

                prevHash = computed;
            }

            // 同步内存链头到最新
            _lastChainHash = prevHash;

            return (repaired, $"哈希链修复完成：共 {logs.Count} 条记录，修复 {repaired} 条不匹配的哈希值");
        }
    }
}
