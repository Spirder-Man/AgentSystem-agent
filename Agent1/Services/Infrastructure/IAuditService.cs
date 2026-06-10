namespace Agent1.Services
{
    /// <summary>
    /// 等保三级操作审计接口
    /// </summary>
    public interface IAuditService
    {
        Task LogOperationAsync(string userId, string operation, string details, bool isSensitive = false);
        Task<List<AuditLog>> GetAuditLogsAsync(DateTime? startTime, DateTime? endTime, string? userId = null);
        Task<string> ExportAuditReportAsync(DateTime startTime, DateTime endTime);
    }

    // 审计日志模型
    public class AuditLog
    {
        public long Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string Operation { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public bool IsSensitive { get; set; }
        public DateTime CreateTime { get; set; }
    }
}
