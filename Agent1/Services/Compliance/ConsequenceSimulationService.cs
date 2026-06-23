using System;
using System.Collections.Generic;
using System.Text;
using Agent1.Models;

namespace Agent1.Services
{
    /// <summary>
    /// [P3] 事故后果模拟服务 — 基于化学品物化性质 + 环境条件的简化扩散模型。
    /// 
    /// 输入: 化学品名称 + 泄漏量 + 风速 + 风向 + 距居民区距离
    /// 输出: 影响范围(半径) + 影响人口估算 + 建议疏散范围 + 持续时间估算
    /// 
    /// 模型简化假设（非专业扩散模型如 ALOHA/PHAST，仅用于快速初步评估）:
    ///   - 采用高斯扩散简化公式
    ///   - 风速分级: 静风(<1m/s) / 微风(1-3m/s) / 中风(3-6m/s) / 大风(>6m/s)
    ///   - 不考虑地形、建筑物、温度层结
    /// </summary>
    public class ConsequenceSimulationService
    {
        /// <summary>
        /// 模拟事故后果
        /// </summary>
        public ConsequenceResult Simulate(string chemicalName, double quantityKg, 
            double windSpeed = 3.0, string windDirection = "北风", double distanceToPop = 500)
        {
            var result = new ConsequenceResult
            {
                ChemicalName = chemicalName,
                QuantityKg = quantityKg,
                WindSpeed = windSpeed,
                WindDirection = windDirection,
                DistanceToPop = distanceToPop
            };

            // 1. 物质识别
            var substance = ChemicalSubstanceDatabase.Lookup(chemicalName);
            if (substance == null)
            {
                result.Error = $"未找到化学品 \"{chemicalName}\" 的数据";
                return result;
            }

            result.IsToxic = substance.HazardCategories.Any(h => 
                h.Category.Contains("毒性") || h.Category.Contains("剧毒"));
            result.IsFlammable = substance.FlashPointC.HasValue && substance.FlashPointC <= 60;
            result.IsGas = substance.FlashPointC == null || substance.FlashPointC <= -10;

            // 2. 影响半径估算（简化高斯扩散）
            result.ImpactRadiusM = CalculateImpactRadius(quantityKg, result.IsToxic, result.IsGas, windSpeed);

            // 3. 影响人口估算（假设人口密度 500人/km² = 市区郊区交界）
            double areaKm2 = Math.PI * Math.Pow(result.ImpactRadiusM / 1000.0, 2);
            result.EstimatedPopulationAffected = (int)(areaKm2 * 500);

            // 4. 建议疏散范围（1.5倍影响半径，加安全余量）
            result.RecommendedEvacuationM = (int)(result.ImpactRadiusM * 1.5);

            // 5. 持续时间估算
            result.EstimatedDurationMinutes = EstimateDuration(quantityKg, windSpeed, result.IsGas);

            // 6. 风险等级
            result.RiskLevel = DetermineConsequenceLevel(result);

            // 7. 居民区影响判定
            result.PopulationAtRisk = distanceToPop < result.ImpactRadiusM;
            result.TimeToReachPopulation = distanceToPop > 0 && windSpeed > 0
                ? $"约 {(int)(distanceToPop / (windSpeed * 60))} 分钟"
                : "无法估算";

            return result;
        }

        /// <summary>影响半径估算（简化高斯扩散）</summary>
        private static int CalculateImpactRadius(double quantityKg, bool isToxic, bool isGas, double windSpeed)
        {
            // 基准半径：每100kg泄漏，基础影响半径50m
            double baseRadius = Math.Sqrt(quantityKg / 100.0) * 50;

            // 物质修正
            if (isToxic && isGas)
                baseRadius *= 4.0;     // 有毒气体扩散范围大
            else if (isToxic && !isGas)
                baseRadius *= 1.5;     // 有毒液体
            else if (isGas)
                baseRadius *= 2.0;     // 一般气体

            // 风速修正: 静风→扩散慢但浓度高，大风→扩散快但稀释快
            if (windSpeed <= 1)
                baseRadius *= 0.8;     // 静风，小范围高浓度
            else if (windSpeed <= 3)
                baseRadius *= 1.0;     // 微风，基准
            else if (windSpeed <= 6)
                baseRadius *= 1.3;     // 中风，扩散范围增大
            else
                baseRadius *= 0.7;     // 大风，快速稀释

            return (int)Math.Min(baseRadius, 5000); // 上限5km
        }

        /// <summary>事故持续时间估算</summary>
        private static int EstimateDuration(double quantityKg, double windSpeed, bool isGas)
        {
            // 气体泄漏: 假设泄放速率 1kg/s, 持续时间 = 总量/速率
            if (isGas)
                return (int)(quantityKg / 1.0 / 60); // 分钟

            // 液体泄漏: 蒸发速率约 0.1kg/s
            double evaporationRate = 0.1;
            // 风速越大蒸发越快
            if (windSpeed > 3) evaporationRate *= 1.5;

            return (int)(quantityKg / evaporationRate / 60);
        }

        private static string DetermineConsequenceLevel(ConsequenceResult result)
        {
            if (result.PopulationAtRisk && result.ImpactRadiusM > 500) return "极高风险 (Level 4)";
            if (result.ImpactRadiusM > 300) return "高风险 (Level 3)";
            if (result.ImpactRadiusM > 100) return "中等风险 (Level 2)";
            return "低风险 (Level 1)";
        }
    }

    public class ConsequenceResult
    {
        public string ChemicalName { get; set; } = "";
        public double QuantityKg { get; set; }
        public double WindSpeed { get; set; }
        public string WindDirection { get; set; } = "";
        public double DistanceToPop { get; set; }
        public string? Error { get; set; }

        // 物质特性
        public bool IsToxic { get; set; }
        public bool IsFlammable { get; set; }
        public bool IsGas { get; set; }

        // 模拟结果
        public int ImpactRadiusM { get; set; }
        public int EstimatedPopulationAffected { get; set; }
        public int RecommendedEvacuationM { get; set; }
        public int EstimatedDurationMinutes { get; set; }
        public string RiskLevel { get; set; } = "";

        // 居民区影响
        public bool PopulationAtRisk { get; set; }
        public string TimeToReachPopulation { get; set; } = "";

        /// <summary>生成人读摘要</summary>
        public string ToSummary()
        {
            if (Error != null) return $"❌ {Error}";
            var sb = new StringBuilder();
            sb.AppendLine($"化学品: {ChemicalName} | 泄漏量: {QuantityKg}kg | 风速: {WindSpeed}m/s");
            sb.AppendLine($"影响半径: {ImpactRadiusM}m | 建议疏散: {RecommendedEvacuationM}m");
            sb.AppendLine($"预估影响人口: ~{EstimatedPopulationAffected}人 | 持续时间: ~{EstimatedDurationMinutes}分钟");
            sb.AppendLine($"风险等级: {RiskLevel}");
            sb.AppendLine($"居民区影响: {(PopulationAtRisk ? $"⚠️ 在影响范围内 ({TimeToReachPopulation})" : "✅ 未受影响")}");
            return sb.ToString();
        }
    }
}
