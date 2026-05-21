using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Agent1
{
    /// <summary>
    /// 化工园区危化品合规审核专用工具集
    /// 替代原有的 IndustrialTools（机床温度/主轴阈值等工业制造场景工具）
    /// </summary>
    public class ChemicalComplianceTools
    {
        // 危化品类别映射表（GB 30000 系列）
        private static readonly Dictionary<string, string> HazardCategories = new()
        {
            ["爆炸物"] = "GB 30000.2-2013",
            ["易燃气体"] = "GB 30000.3-2013",
            ["气溶胶"] = "GB 30000.4-2013",
            ["氧化性气体"] = "GB 30000.5-2013",
            ["加压气体"] = "GB 30000.6-2013",
            ["易燃液体"] = "GB 30000.7-2013",
            ["易燃固体"] = "GB 30000.8-2013",
            ["自燃液体"] = "GB 30000.10-2013",
            ["自燃固体"] = "GB 30000.11-2013",
            ["遇水放出易燃气体"] = "GB 30000.13-2013",
            ["氧化性液体"] = "GB 30000.14-2013",
            ["氧化性固体"] = "GB 30000.15-2013",
            ["有机过氧化物"] = "GB 30000.16-2013",
            ["金属腐蚀物"] = "GB 30000.17-2013",
            ["急性毒性"] = "GB 30000.18-2013",
            ["皮肤腐蚀/刺激"] = "GB 30000.19-2013",
            ["严重眼损伤/刺激"] = "GB 30000.20-2013",
            ["呼吸道致敏"] = "GB 30000.21-2013",
            ["致癌性"] = "GB 30000.23-2013",
            ["生殖毒性"] = "GB 30000.24-2013",
        };

        // 危化品存储禁忌表（常见配伍禁忌，依据 GB15603）
        private static readonly Dictionary<string, List<string>> StorageIncompatibilities = new()
        {
            ["氧化剂"] = new() { "易燃液体", "易燃固体", "还原剂", "有机过氧化物" },
            ["易燃液体"] = new() { "氧化剂", "强酸", "自燃物品" },
            ["腐蚀品"] = new() { "易燃液体", "易燃固体", "氧化剂" },
            ["压缩气体"] = new() { "易燃液体", "易燃固体", "自燃物品" },
            ["爆炸品"] = new() { "一切其他类别" },
        };

        // 安全距离参考表（GB50160/GB50016 简化）
        private static readonly Dictionary<string, int> SafetyDistances = new()
        {
            ["储罐-储罐"] = 15,
            ["储罐-建筑"] = 25,
            ["储罐-消防通道"] = 15,
            ["储罐-厂区边界"] = 30,
            ["液化烃储罐-储罐"] = 20,
            ["甲类仓库-建筑"] = 20,
            ["甲类仓库-明火点"] = 30,
        };

        [KernelFunction, Description("查询指定危化品属于哪个危险类别，返回对应的国标编号（GB 30000 系列）")]
        public string CheckHazardCategory(string substanceName)
        {
            foreach (var kvp in HazardCategories)
            {
                if (substanceName.Contains(kvp.Key) || kvp.Key.Contains(substanceName))
                    return $"「{substanceName}」属于「{kvp.Key}」类别，适用标准：{kvp.Value}";
            }
            return $"「{substanceName}」未在常见危化品类别中直接匹配，建议查阅 GB 30000 系列标准全文（knowledgebase/国标/ 目录下已收录完整标准文件）";
        }

        [KernelFunction, Description("查询两种危化品是否可以同库储存，返回禁忌信息（依据 GB15603-1995 4.2.2）")]
        public string CheckStorageCompatibility(string substanceA, string substanceB)
        {
            foreach (var kvp in StorageIncompatibilities)
            {
                bool aMatchesCategory = substanceA.Contains(kvp.Key);
                bool bMatchesCategory = substanceB.Contains(kvp.Key);
                bool aIsIncompatible = kvp.Value.Any(s => substanceB.Contains(s));
                bool bIsIncompatible = kvp.Value.Any(s => substanceA.Contains(s));

                if (aIsIncompatible || bIsIncompatible)
                {
                    string incompatibleClass = aIsIncompatible ? kvp.Key : kvp.Key;
                    return $"⚠️ 禁用：「{substanceA}」与「{substanceB}」存在配伍禁忌——{incompatibleClass}类不可与之同库贮存。依据：GB15603-1995 第4.2.2条 禁忌物料不得同库贮存";
                }
            }
            return $"✅ 「{substanceA}」与「{substanceB}」在常见禁忌表中未发现直接冲突，但仍建议按照 GB15603 分类贮存原则进行核实（knowledgebase/国标/GB15603 已收录全文）";
        }

        [KernelFunction, Description("查询指定设施类型的安全距离要求（储罐间距、消防通道间距等），返回最小安全距离（米）")]
        public string GetSafetyDistance(string facilityType)
        {
            var key = facilityType.Trim();
            if (SafetyDistances.TryGetValue(key, out int distance))
                return $"「{key}」的最小安全间距为 {distance} 米";

            var matched = SafetyDistances.Keys
                .Where(k => k.Contains(key) || key.Contains(k))
                .ToList();
            if (matched.Count > 0)
                return $"已匹配「{matched[0]}」：最小安全间距为 {SafetyDistances[matched[0]]} 米";

            return $"未找到「{key}」的精确安全距离数值，建议在 knowledgebase/国标/ 目录下查阅 GB50160《石油化工企业设计防火规范》和 GB50016《建筑设计防火规范》全文";
        }

        [KernelFunction, Description("获取当前时间和日期")]
        public string GetCurrentTime()
        {
            return $"当前时间：{DateTime.Now:yyyy年MM月dd日 HH:mm:ss}";
        }

        [KernelFunction, Description("计算数学表达式，支持加减乘除和括号")]
        public string Calculate(string expression)
        {
            try
            {
                var result = new System.Data.DataTable().Compute(expression, null);
                return $"计算结果：{expression} = {result}";
            }
            catch (Exception ex)
            {
                return $"计算失败：{ex.Message}";
            }
        }
    }
}
