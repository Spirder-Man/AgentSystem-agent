namespace Agent1.Services
{
    /// <summary>
    /// P0-3: 外部系统集成服务 —— 显式降级，禁止静默返回空数据。
    /// 未接入的外部系统调用将抛出 NotSupportedException，
    /// 调用方应在异常处理中降级为"外部系统未接入"提示。
    /// </summary>
    public class IntegrationService : IIntegrationService
    {
        public IntegrationService()
        {
        }

        public Task<List<WarehouseRecord>> GetWarehouseRecordsAsync(string? chemicalName = null)
        {
            throw new NotSupportedException(
                "仓储台账查询接口尚未接入外部ERP系统。请联系管理员配置ERP集成。");
        }

        public Task<List<EHSTicket>> GetEHSTicketsAsync(bool? isCompleted = null)
        {
            throw new NotSupportedException(
                "EHS工单查询接口尚未接入外部EHS系统。请联系管理员配置EHS集成。");
        }

        public Task SyncERPDataAsync()
            => throw new NotSupportedException("ERP数据同步接口未接入。请联系管理员配置ERP集成。");

        public Task SyncWMSDataAsync()
            => throw new NotSupportedException("WMS数据同步接口未接入。请联系管理员配置WMS集成。");

        public Task SyncEHSDataAsync()
            => throw new NotSupportedException("EHS数据同步接口未接入。请联系管理员配置EHS集成。");
    }
}
