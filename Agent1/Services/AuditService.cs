using System.Text;

namespace Agent1.Services
{
    public class AuditService : IAuditService
    {
        private readonly List<AuditLog> _auditLogs = new();
        private readonly object _lock = new();

        public Task LogOperationAsync(string userId, string operation, string details, bool isSensitive = false)
        {
            lock (_lock)
            {
                _auditLogs.Add(new AuditLog
                {
                    Id = _auditLogs.Count + 1,
                    UserId = userId,
                    Operation = operation,
                    Details = details,
                    IsSensitive = isSensitive,
                    CreateTime = DateTime.Now
                });
            }
            return Task.CompletedTask;
        }

        public Task<List<AuditLog>> GetAuditLogsAsync(DateTime? startTime, DateTime? endTime, string? userId = null)
        {
            var query = _auditLogs.AsEnumerable();

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
            var logs = _auditLogs
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
                sb.AppendLine($"[{log.CreateTime:yyyy-MM-dd HH:mm:ss}] 用户:{log.UserId} 操作:{log.Operation}");
                sb.AppendLine($"  详情: {log.Details}");
                sb.AppendLine();
            }

            return Task.FromResult(sb.ToString());
        }
    }
}
