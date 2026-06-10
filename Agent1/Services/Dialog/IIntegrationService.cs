namespace Agent1.Services
{
    /// <summary>
    /// 化工园区工业系统集成接口
    /// 对接ERP/WMS/EHS系统
    /// </summary>
    public interface IIntegrationService
    {
        // 仓储台账查询（危化品）
        Task<List<WarehouseRecord>> GetWarehouseRecordsAsync(string? chemicalName = null);

        // EHS工单查询
        Task<List<EHSTicket>> GetEHSTicketsAsync(bool? isCompleted = null);

        // 数据同步
        Task SyncERPDataAsync();
        Task SyncWMSDataAsync();
        Task SyncEHSDataAsync();
    }

    // 仓储记录模型
    public class WarehouseRecord
    {
        public string ChemicalName { get; set; } = string.Empty;
        public string ChemicalType { get; set; } = string.Empty;
        public double Quantity { get; set; }
        public string StorageLocation { get; set; } = string.Empty;
        public DateTime UpdateTime { get; set; }
    }

    // EHS工单模型
    public class EHSTicket
    {
        public string TicketId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Priority { get; set; } = string.Empty;
        public bool IsCompleted { get; set; }
        public DateTime CreateTime { get; set; }
    }
}
