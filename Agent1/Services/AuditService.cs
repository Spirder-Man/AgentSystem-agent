using System.Text;

namespace Agent1.Services
{
    public class AuditService : IAuditService
    {
        private readonly List<AuditLog> _auditLogs = new();
        private readonly object _lock = new();

        public Task LogOperationAsync(string userId, string operation, string details, bool isSensitive = false)
        {
            // Task 3: 敏感信息脱敏 — 审计日志写入前对 details 进行脱敏
            var maskedDetails = isSensitive ? SensitiveDataMasker.Mask(details) : details;

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
            return Task.CompletedTask;
        }

        public Task<List<AuditLog>> GetAuditLogsAsync(DateTime? startTime, DateTime? endTime, string? userId = null)
        {
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

            return Task.FromResult(query.OrderByDescending(l => l.CreateTime).ToList());
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
