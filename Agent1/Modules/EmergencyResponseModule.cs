using Agent1.Services;
using Agent1.Models;

namespace Agent1.Modules
{
    /// <summary>
    /// [P3] 化工应急响应模块 — 对标 ERG 指南 + AQ/T 3043。
    /// 输入事故场景（化学品+泄漏量+环境），输出完整应急方案。
    /// 覆盖：初始隔离距离/PPE等级/灭火介质/泄漏处置/医疗急救/通报模板。
    /// </summary>
    public class EmergencyResponseModule : IInferenceModule
    {
        public string Name => "应急响应方案";
        public string Description => "输入事故场景（化学品泄漏/火灾/爆炸），生成完整应急响应方案";

        public Task<CliExecutionResult> RunWithResultAsync(string userInput)
            => Task.FromResult(new CliExecutionResult
            {
                Success = true,
                DisplayOutput = "应急响应方案模块仅支持交互式运行，请使用 RunAsync()",
                AuditRecord = "交互模式"
            });

        private readonly ILlmService _llmService;
        private readonly IKnowledgeBaseService _kbService;
        private readonly IAuditService _auditService;
        private readonly IIntegrationService _integrationService;

        public EmergencyResponseModule(
            ILlmService llmService,
            IKnowledgeBaseService kbService,
            IAuditService auditService,
            IIntegrationService integrationService)
        {
            _llmService = llmService;
            _kbService = kbService;
            _auditService = auditService;
            _integrationService = integrationService;
        }

        public async Task RunAsync()
        {
            Console.WriteLine("\n========== 化工应急响应 ==========");
            Console.WriteLine("输入事故场景信息:");

            Console.Write("化学品名称: ");
            var chemical = Console.ReadLine() ?? "";

            Console.WriteLine("事故类型: 1.泄漏 2.火灾 3.爆炸 4.中毒");
            Console.Write("选择 (默认1): ");
            var typeChoice = Console.ReadLine() ?? "1";
            var incidentType = typeChoice switch
            {
                "2" => "火灾", "3" => "爆炸", "4" => "中毒", _ => "泄漏"
            };

            Console.Write("泄漏量/储存量(kg): ");
            double.TryParse(Console.ReadLine() ?? "0", out var qty);

            Console.Write("风速(m/s, 可选): ");
            double.TryParse(Console.ReadLine() ?? "0", out var windSpeed);

            Console.Write("距最近居民区(m, 可选): ");
            double.TryParse(Console.ReadLine() ?? "0", out var distToPop);

            if (string.IsNullOrWhiteSpace(chemical))
            {
                Console.WriteLine("化学品名称不能为空！");
                return;
            }

            var scenario = new EmergencyScenario
            {
                ChemicalName = chemical.Trim(),
                IncidentType = incidentType,
                QuantityKg = qty,
                WindSpeed = windSpeed,
                DistanceToPop = distToPop
            };

            // [P3 工业集成] 查询周边库存，评估联动风险（管线就绪）
            var nearbyInventory = await _integrationService.GetWarehouseRecordsAsync(chemical);
            if (nearbyInventory.Count > 0)
            {
                Console.WriteLine($"\n📦 周边库存: {nearbyInventory.Count} 条相关记录");
                foreach (var r in nearbyInventory.Take(3))
                    Console.WriteLine($"   ⚠️ {r.ChemicalName} ×{r.Quantity}kg @{r.StorageLocation}");
            }

            var service = new EmergencyResponseService(_llmService, _kbService, _auditService);
            var plan = await service.GeneratePlanAsync(scenario);

            if (plan.Error != null)
            {
                Console.WriteLine($"\n❌ {plan.Error}");
                return;
            }

            PrintPlan(plan);
        }

        private static void PrintPlan(EmergencyPlan plan)
        {
            Console.WriteLine($"\n══════════ 应急响应方案 ══════════");
            Console.WriteLine($"化学品: {plan.SubstanceName} (CAS {plan.CasNumber} / UN {plan.UnNumber})");
            Console.WriteLine($"危险类别: {string.Join(", ", plan.HazardCategories)}");
            Console.WriteLine($"闪点: {plan.FlashPointC?.ToString() ?? "不适用"}℃");
            Console.WriteLine();

            Console.WriteLine("━━━ 疏散与隔离 ━━━");
            Console.WriteLine($"  初始隔离半径: {plan.IsolationZoneM}m");
            Console.WriteLine($"  防护行动区: {plan.ProtectiveZoneM}m");
            Console.WriteLine();

            Console.WriteLine("━━━ 个人防护装备 (PPE) ━━━");
            Console.WriteLine($"  {plan.PpeLevel}");
            Console.WriteLine();

            Console.WriteLine("━━━ 灭火介质 ━━━");
            Console.WriteLine($"  {plan.FireMedia}");
            Console.WriteLine();

            Console.WriteLine("━━━ 泄漏处置 ━━━");
            Console.WriteLine($"  {plan.ContainmentMethod}");
            Console.WriteLine();

            Console.WriteLine("━━━ 医疗急救 ━━━");
            Console.WriteLine($"  吸入: {plan.FirstAidInhale}");
            Console.WriteLine($"  皮肤: {plan.FirstAidSkin}");
            Console.WriteLine($"  眼睛: {plan.FirstAidEye}");
            Console.WriteLine($"  食入: {plan.FirstAidIngest}");
            Console.WriteLine();

            Console.WriteLine(plan.NotificationTemplate);

            if (plan.RagSupplement != null)
                Console.WriteLine(plan.RagSupplement);

            Console.WriteLine($"\n⚠️ 本方案为 AI 辅助生成，仅供参考。实际应急响应应遵循现场指挥和应急预案。");
            Console.WriteLine("═══════════════════════════════════");
        }
    }
}
