using Agent1.Config;
using Agent1.Models;

namespace Agent1.Services
{
    /// <summary>
    /// [P2] 化工风险评估服务 — 基于物质属性 × 环境因素的简化 3×3 风险矩阵。
    /// </summary>
    public class RiskAssessmentService
    {
        private readonly IKnowledgeBaseService _kbService;

        public RiskAssessmentService(IKnowledgeBaseService kbService)
        {
            _kbService = kbService;
        }

        /// <summary>
        /// 评估化学品存储风险。输入物质名称和周边环境描述，返回风险等级和建议。
        /// </summary>
        public async Task<RiskAssessmentResult> AssessStorageRiskAsync(
            string chemicalName,
            string? environmentDescription = null)
        {
            var result = new RiskAssessmentResult { ChemicalName = chemicalName };

            // 1. 查询化学物质属性
            var substance = ChemicalSubstanceDatabase.Lookup(chemicalName);
            if (substance == null)
            {
                result.RiskLevel = RiskLevel.Unknown;
                result.Recommendation = $"数据库中未找到 \"{chemicalName}\" 的物化性质数据，建议人工查阅 MSDS";
                return result;
            }

            // 2. 计算物质风险等级（基于闪点 + 毒性 + 重大危险源阈值）
            var hazardScore = CalculateHazardScore(substance);
            result.HazardScore = hazardScore;
            result.FlashPointC = substance.FlashPointC;
            result.HazardCategories = substance.HazardCategories.Select(h => h.Category).ToList();
            result.MajorHazardThresholdTons = substance.MajorHazardThresholdTons;

            // 3. 评估环境暴露风险（基于周边环境描述的关键词）
            var exposureScore = EvaluateExposureRisk(environmentDescription);
            result.ExposureScore = exposureScore;

            // 4. 3×3 风险矩阵判定
            result.RiskLevel = DetermineRiskLevel(hazardScore, exposureScore);
            result.Recommendation = GenerateRecommendation(result);

            return result;
        }

        /// <summary>
        /// 物质危险性评分 (1-3): 基于闪点、毒性、重大危险源阈值
        /// </summary>
        private static int CalculateHazardScore(ChemicalSubstance substance)
        {
            int score = 1;

            // 闪点越低越危险 (-11℃ vs 300℃)
            if (substance.FlashPointC.HasValue)
            {
                if (substance.FlashPointC.Value <= 23) score = Math.Max(score, 3);
                else if (substance.FlashPointC.Value <= 60) score = Math.Max(score, 2);
            }

            // 毒性类别
            var categories = substance.HazardCategories.Select(h => h.Category).ToList();
            if (categories.Any(c => c.Contains("剧毒") || c.Contains("致癌")))
                score = Math.Max(score, 3);
            else if (categories.Any(c => c.Contains("毒性") || c.Contains("有毒")))
                score = Math.Max(score, 2);

            // 重大危险源阈值越小越危险（10吨 vs 5000吨）
            if (substance.MajorHazardThresholdTons <= 50) score = Math.Max(score, 3);
            else if (substance.MajorHazardThresholdTons <= 200) score = Math.Max(score, 2);

            return score;
        }

        /// <summary>
        /// 环境暴露风险评分 (1-3): 基于描述中的关键词
        /// </summary>
        private static int EvaluateExposureRisk(string? environmentDescription)
        {
            if (string.IsNullOrWhiteSpace(environmentDescription))
                return 2; // 默认中等

            var desc = environmentDescription.ToLower();
            int score = 1;

            // 高暴露因素
            if (desc.Contains("居民区") || desc.Contains("学校") || desc.Contains("医院") ||
                desc.Contains("水源") || desc.Contains("河流") || desc.Contains("500m内"))
                score = Math.Max(score, 3);
            // 中暴露因素
            if (desc.Contains("道路") || desc.Contains("厂区") || desc.Contains("工业区") ||
                desc.Contains("1km内") || desc.Contains("2000m内"))
                score = Math.Max(score, 2);

            return score;
        }

        /// <summary>
        /// 3×3 风险矩阵:
        ///   暴露 →
        /// 危 ↓  1(低)  2(中)  3(高)
        ///  1    低     低     中
        ///  2    低     中     高
        ///  3    中     高     极高
        /// </summary>
        private static RiskLevel DetermineRiskLevel(int hazardScore, int exposureScore)
        {
            return (hazardScore, exposureScore) switch
            {
                (1, 1) or (1, 2) or (2, 1) => RiskLevel.Low,
                (1, 3) or (2, 2) or (3, 1) => RiskLevel.Medium,
                (2, 3) or (3, 2) => RiskLevel.High,
                (3, 3) => RiskLevel.Critical,
                _ => RiskLevel.Medium
            };
        }

        private static string GenerateRecommendation(RiskAssessmentResult result)
        {
            return result.RiskLevel switch
            {
                RiskLevel.Low => "风险可控，建议按日常巡检频率检查，确保消防设施可用",
                RiskLevel.Medium => "需要关注，建议增加巡检频率，检查储存容器完整性，确认消防间距符合 GB 50016",
                RiskLevel.High => "高风险，建议立即检查：1)安全间距 2)泄漏防护 3)消防系统 4)应急预案。必要时上报安全主管",
                RiskLevel.Critical => "极高风险！建议立即启动应急响应：1)疏散周边人员 2)通知安全总监 3)检查 DCS 报警 4)准备消防力量",
                RiskLevel.Unknown => "无法评估，请提供完整信息或查阅 MSDS",
                _ => "请咨询安全专家"
            };
        }
    }

    public enum RiskLevel
    {
        Unknown = 0,
        Low = 1,
        Medium = 2,
        High = 3,
        Critical = 4
    }

    public class RiskAssessmentResult
    {
        public string ChemicalName { get; set; } = string.Empty;
        public int HazardScore { get; set; }
        public int ExposureScore { get; set; }
        public RiskLevel RiskLevel { get; set; }
        public double? FlashPointC { get; set; }
        public List<string> HazardCategories { get; set; } = new();
        public double? MajorHazardThresholdTons { get; set; }
        public string? Recommendation { get; set; }
    }
}
