using System.Text;
using Agent1.Config;
using Agent1.Models;

namespace Agent1.Services
{
    /// <summary>
    /// [P3] 化工应急响应服务 — 对标 AQ/T 3043 和 ERG 2024 指南。
    /// 输入事故场景（化学品+泄漏量+环境），输出完整应急方案：
    /// 初始隔离距离 → PPE等级 → 灭火介质 → 泄漏处置 → 医疗急救 → 通报模板。
    /// </summary>
    public class EmergencyResponseService
    {
        private readonly ILlmService _llmService;
        private readonly IKnowledgeBaseService _kbService;
        private readonly IAuditService _auditService;

        public EmergencyResponseService(ILlmService llmService, IKnowledgeBaseService kbService, IAuditService auditService)
        {
            _llmService = llmService;
            _kbService = kbService;
            _auditService = auditService;
        }

        private static bool HasHazard(ChemicalSubstance sub, params string[] keywords)
        {
            return sub.HazardCategories.Any(h => keywords.Any(k => h.Category.Contains(k)));
        }

        /// <summary>核心入口：根据事故场景生成完整应急方案</summary>
        public async Task<EmergencyPlan> GeneratePlanAsync(EmergencyScenario scenario)
        {
            var plan = new EmergencyPlan { Scenario = scenario };

            // 1. 物质识别
            var substance = ChemicalSubstanceDatabase.Lookup(scenario.ChemicalName);
            if (substance == null)
            {
                plan.Error = $"未找到化学品 \"{scenario.ChemicalName}\" 的物化数据，请使用标准名称（如氯、氨、苯）";
                return plan;
            }
            plan.SubstanceName = substance.Name;
            plan.CasNumber = substance.CasNumber;
            plan.UnNumber = substance.UnNumber;
            plan.FlashPointC = substance.FlashPointC;
            plan.HazardCategories = substance.HazardCategories.Select(h => h.Category).ToList();

            // 2. 疏散距离计算（基于 ERG 指南简化表）
            var (isolation, protective) = CalculateEvacuationZones(scenario, substance);
            plan.IsolationZoneM = isolation;
            plan.ProtectiveZoneM = protective;

            // 3. PPE 等级推荐
            plan.PpeLevel = RecommendPpe(scenario, substance);

            // 4. 灭火介质选择
            plan.FireMedia = SelectFireMedia(substance);

            // 5. 泄漏处置
            plan.ContainmentMethod = DetermineContainment(scenario, substance);

            // 6. 急救指南
            AssignFirstAid(plan, substance);

            // 7. 通报模板
            plan.NotificationTemplate = BuildNotificationTemplate(plan);

            // 8. RAG 检索 + LLM 增强
            await EnrichWithRagAsync(plan);

            // 9. 审计
            await _auditService.LogOperationAsync("system", "EmergencyResponse",
                $"化学品:{scenario.ChemicalName}, 事故类型:{scenario.IncidentType}, 隔离:{isolation}m");

            return plan;
        }

        // ═══════════════════════════════════════
        // 疏散距离计算 (ERG 简化查表)
        // ═══════════════════════════════════════
        private static (int isolation, int protective) CalculateEvacuationZones(EmergencyScenario scenario, ChemicalSubstance substance)
        {
            // 判断泄漏规模
            bool isLarge = scenario.QuantityKg > 200;
            bool isGas = substance.FlashPointC == null || substance.FlashPointC <= -10;
            bool isToxic = HasHazard(substance, "毒性", "剧毒", "有毒");

            // ERG 简化表 (初始隔离半径, 米)
            // 实际使用时应查 ERG Table 1 - Initial Isolation and Protective Action Distances
            if (isToxic && isGas)
            {
                // 有毒气体 (如氯气、氨气)
                return isLarge ? (500, 3000) : (100, 500);
            }
            if (isToxic && !isGas)
            {
                // 有毒液体 (如苯)
                return isLarge ? (100, 500) : (50, 200);
            }
            if (isGas)
            {
                // 易燃气体 (如液化石油气)
                return isLarge ? (300, 1500) : (50, 300);
            }
            // 易燃液体/固体
            return isLarge ? (100, 300) : (30, 100);
        }

        // ═══════════════════════════════════════
        // PPE 等级推荐
        // ═══════════════════════════════════════
        private static string RecommendPpe(EmergencyScenario scenario, ChemicalSubstance substance)
        {
            bool isToxic = HasHazard(substance, "毒性", "剧毒", "致癌");
            bool isCorrosive = HasHazard(substance, "腐蚀", "酸");
            bool isGas = substance.FlashPointC == null || substance.FlashPointC <= -10;

            if (isToxic && isGas)
                return "B级 — 自给式呼吸器(SCBA) + 全封闭防化服 + 防化手套/靴";

            if (isToxic || isCorrosive)
                return "C级 — 全面罩防毒面具(有机蒸气滤毒盒) + 防化服 + 耐酸碱手套";

            if (isGas || scenario.IncidentType == "火灾")
                return "B级 — SCBA + 防火服 + 防爆手电";

            return "D级 — 安全护目镜 + 防化手套 + 防静电工作服 (最低要求)";
        }

        // ═══════════════════════════════════════
        // 灭火介质选择
        // ═══════════════════════════════════════
        private static string SelectFireMedia(ChemicalSubstance substance)
        {
            var cats = substance.HazardCategories.Select(c => c.Category).ToList();

            // 遇水反应物质 — 绝对禁止用水
            if (cats.Any(c => c.Contains("遇水") || c.Contains("禁水") || c.Contains("自燃")))
                return "干粉灭火剂 / 二氧化碳 — ⛔ 严禁用水";

            // 易燃液体 — 泡沫/干粉
            if (substance.FlashPointC.HasValue && substance.FlashPointC <= 60)
                return "抗溶性泡沫 / 干粉灭火剂 / 二氧化碳";

            // 氧化剂
            if (cats.Any(c => c.Contains("氧化")))
                return "大量水（冷却容器）+ 干粉 — 氧化剂本身不燃但助燃";

            // 腐蚀性
            if (cats.Any(c => c.Contains("腐蚀") || c.Contains("酸")))
                return "干粉 / 二氧化碳 — ⛔ 避免直射水流（防飞溅）";

            return "水雾 / 干粉 / 泡沫 / 二氧化碳";
        }

        // ═══════════════════════════════════════
        // 泄漏处置方法
        // ═══════════════════════════════════════
        private static string DetermineContainment(EmergencyScenario scenario, ChemicalSubstance substance)
        {
            bool isGas = substance.FlashPointC == null || substance.FlashPointC <= -10;
            var cats = substance.HazardCategories.Select(c => c.Category).ToList();

            if (isGas)
            {
                var baseMethod = "1. 立即撤离非必要人员至侧风向\n2. 关闭泄漏源阀门（如可安全接近）\n3. 使用水雾稀释蒸气云（仅限非遇水反应气体）";
                if (cats.Any(c => c.Contains("氯")))
                    return baseMethod + "\n4. 碱液（NaOH/Ca(OH)₂）中和 — 禁止直接喷水（生成盐酸）\n5. 使用氨水检漏（白烟）";
                return baseMethod + "\n4. 喷雾状水驱散蒸气\n5. 禁止接触液态泄漏物";
            }

            return "1. 构筑围堰/挖沟槽防止扩散\n" +
                   "2. 使用不燃吸收材料（沙土/蛭石/硅藻土）覆盖\n" +
                   "3. 使用防爆工具收集至专用容器\n" +
                   "4. 污染区域用大量水冲洗（注意收集冲洗废水）\n" +
                   "5. 禁止排入下水道/水体";
        }

        // ═══════════════════════════════════════
        // 医疗急救 (四路径)
        // ═══════════════════════════════════════
        private static void AssignFirstAid(EmergencyPlan plan, ChemicalSubstance substance)
        {
            bool isToxic = HasHazard(substance, "毒性", "剧毒", "致癌");
            bool isCorrosive = HasHazard(substance, "腐蚀", "酸");

            plan.FirstAidInhale = "立即将患者移至新鲜空气处，保持呼吸道通畅。" +
                (isToxic ? "如呼吸困难，给予吸氧（仅限培训人员）。如呼吸停止，进行人工呼吸。" : "") +
                "保持半卧位安静休息，立即拨打120。";

            plan.FirstAidSkin = isCorrosive
                ? "立即脱去污染衣物，用大量流动清水冲洗至少15分钟。禁用化学中和剂。如有灼伤，覆盖无菌敷料。"
                : "脱去污染衣物，用肥皂水+清水彻底冲洗皮肤。如有刺激持续，就医。";

            plan.FirstAidEye = isCorrosive
                ? "立即用大量清水或生理盐水冲洗至少20分钟（翻开眼睑）。立即就医，途中继续冲洗。"
                : "用清水冲洗15分钟。如刺激持续，就医。";

            plan.FirstAidIngest = isToxic || isCorrosive
                ? "禁止催吐！（腐蚀性物质反流造成二次灼伤）。用清水漱口。立即就医。不要给意识不清者喂任何东西。"
                : "用清水漱口。立即就医。如患者清醒，可饮少量水稀释。";
        }

        // ═══════════════════════════════════════
        // 通报模板 (向安监/环保/消防)
        // ═══════════════════════════════════════
        private static string BuildNotificationTemplate(EmergencyPlan plan)
        {
            var sb = new StringBuilder();
            sb.AppendLine("══════════════ 事故通报模板 ══════════════");
            sb.AppendLine($"致: 应急管理局 / 生态环境局 / 消防救援支队");
            sb.AppendLine($"化学品: {plan.SubstanceName} (CAS {plan.CasNumber} / UN {plan.UnNumber})");
            sb.AppendLine($"事故类型: {plan.Scenario.IncidentType}");
            sb.AppendLine($"泄漏量: 约 {plan.Scenario.QuantityKg}kg");
            sb.AppendLine($"危险类别: {string.Join(", ", plan.HazardCategories)}");
            sb.AppendLine($"初始隔离: {plan.IsolationZoneM}m | 防护行动区: {plan.ProtectiveZoneM}m");
            if (plan.Scenario.DistanceToPop > 0)
                sb.AppendLine($"距最近居民区: {plan.Scenario.DistanceToPop}m");
            sb.AppendLine($"PPE要求: {plan.PpeLevel}");
            sb.AppendLine($"灭火介质: {plan.FireMedia}");
            sb.AppendLine("══════════════════════════════════════════");
            return sb.ToString();
        }

        // ═══════════════════════════════════════
        // RAG 检索增强
        // ═══════════════════════════════════════
        private async Task EnrichWithRagAsync(EmergencyPlan plan)
        {
            try
            {
                var query = $"{plan.SubstanceName} 泄漏 应急处置 ERG 指南";
                var chunks = await _kbService.RetrieveChemicalRegulationAsync(query, topK: 3);
                if (chunks.Count > 0)
                {
                    var references = new StringBuilder();
                    references.AppendLine("\n【知识库补充建议】");
                    foreach (var chunk in chunks.Take(2))
                    {
                        var content = chunk.Content ?? "";
                        if (content.Length > 300) content = content[..300] + "...";
                        references.AppendLine($"  · {content}");
                    }
                    plan.RagSupplement = references.ToString();
                }
            }
            catch
            {
                plan.RagSupplement = "（知识库检索失败，以上方案仅基于内置数据）";
            }
        }
    }

    // ═══════════════════════════════════════
    // 数据模型
    // ═══════════════════════════════════════

    public class EmergencyScenario
    {
        public string ChemicalName { get; set; } = string.Empty;
        public string IncidentType { get; set; } = "泄漏";
        public double QuantityKg { get; set; }
        public double WindSpeed { get; set; }
        public string WindDirection { get; set; } = "未知";
        public double DistanceToPop { get; set; }
    }

    public class EmergencyPlan
    {
        public EmergencyScenario Scenario { get; set; } = new();
        public string? Error { get; set; }

        // 物质信息
        public string SubstanceName { get; set; } = string.Empty;
        public string CasNumber { get; set; } = string.Empty;
        public string UnNumber { get; set; } = string.Empty;
        public double? FlashPointC { get; set; }
        public List<string> HazardCategories { get; set; } = new();

        // 疏散
        public int IsolationZoneM { get; set; }
        public int ProtectiveZoneM { get; set; }

        // 防护
        public string PpeLevel { get; set; } = string.Empty;
        public string FireMedia { get; set; } = string.Empty;
        public string ContainmentMethod { get; set; } = string.Empty;

        // 急救
        public string FirstAidInhale { get; set; } = string.Empty;
        public string FirstAidSkin { get; set; } = string.Empty;
        public string FirstAidEye { get; set; } = string.Empty;
        public string FirstAidIngest { get; set; } = string.Empty;

        // 通报
        public string NotificationTemplate { get; set; } = string.Empty;

        // RAG
        public string? RagSupplement { get; set; }
    }
}
