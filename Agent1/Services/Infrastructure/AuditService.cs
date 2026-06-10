using System.Text;

namespace Agent1.Services
{
    public class AuditService : IAuditService
    {
        private readonly List<AuditLog> _auditLogs = new();
        private readonly object _lock = new();
        private readonly IDatabaseService? _db;

        public AuditService(IDatabaseService? db = null)
        {
            _db = db;
        }

        public async Task LogOperationAsync(string userId, string operation, string details, bool isSensitive = false)
        {
            // Task 3: 敏感信息脱敏 — 审计日志写入前对 details 进行脱敏
            var maskedDetails = isSensitive ? SensitiveDataMasker.Mask(details) : details;

            // 确定 IP（从 HttpContext 获取，此处由调用方通过 details 传递）
            string? ipAddress = null;

            // 主路径：写入 PostgreSQL（持久化）
            if (_db != null)
            {
                try
                {
                    await _db.AddAuditLogAsync(userId, operation, maskedDetails, ipAddress);
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
                    CreateTime = DateTime.Now
                });
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
    }
}
