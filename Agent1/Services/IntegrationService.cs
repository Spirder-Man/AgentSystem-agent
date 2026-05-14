namespace Agent1.Services
{
    public class IntegrationService : IIntegrationService
    {
        public IntegrationService()
        {
        }

        public Task<List<WarehouseRecord>> GetWarehouseRecordsAsync(string? chemicalName = null)
        {
            // 空实现：后续对接真实ERP时完善
            return Task.FromResult(new List<WarehouseRecord>());
        }

        public Task<List<EHSTicket>> GetEHSTicketsAsync(bool? isCompleted = null)
        {
            // 空实现：后续对接真实EHS时完善
            return Task.FromResult(new List<EHSTicket>());
        }

        public Task SyncERPDataAsync() => Task.CompletedTask;
        public Task SyncWMSDataAsync() => Task.CompletedTask;
        public Task SyncEHSDataAsync() => Task.CompletedTask;
    }
}
