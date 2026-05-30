using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Agent1.Services;

namespace Agent1
{
    /// <summary>
    /// 化工园区危化品合规审核专用工具集
    /// Phase 2a: 双模工具 —— 有 IKnowledgeBaseService 走 RAG 检索，无则降级到硬编码字典（方案 A 兜底）
    /// </summary>
    public class ChemicalComplianceTools
    {
        private readonly IKnowledgeBaseService? _kbService;
        private readonly ILlmService? _llmService;

        /// <summary>完整构造：启用 RAG 检索模式（方案 B 入口）</summary>
        public ChemicalComplianceTools(IKnowledgeBaseService kbService, ILlmService llmService)
        {
            _kbService = kbService;
            _llmService = llmService;
        }

        /// <summary>无参构造：降级到硬编码字典模式（方案 A 兜底 / RAG.cs 旧流程兼容）</summary>
        public ChemicalComplianceTools()
        {
            _kbService = null;
            _llmService = null;
        }

        /// <summary>是否启用了 RAG 检索</summary>
        private bool UseRag => _kbService != null && _llmService != null;

        // ════════════════════════════════════════
        // 硬编码字典（方案 A 降级兜底 + 旧代码兼容）
        // ════════════════════════════════════════
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

        private static readonly Dictionary<string, List<string>> StorageIncompatibilities = new()
        {
            ["氧化剂"] = new() { "易燃液体", "易燃固体", "还原剂", "有机过氧化物" },
            ["易燃液体"] = new() { "氧化剂", "强酸", "自燃物品" },
            ["腐蚀品"] = new() { "易燃液体", "易燃固体", "氧化剂" },
            ["压缩气体"] = new() { "易燃液体", "易燃固体", "自燃物品" },
            ["爆炸品"] = new() { "一切其他类别" },
        };

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

        // ════════════════════════════════════════
        // 同步方法：硬编码字典（旧代码兼容 / 方案 A 降级）
        // ════════════════════════════════════════

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
                bool aIsIncompatible = kvp.Value.Any(s => substanceB.Contains(s));
                bool bIsIncompatible = kvp.Value.Any(s => substanceA.Contains(s));
                if (aIsIncompatible || bIsIncompatible)
                    return $"⚠️ 禁用：「{substanceA}」与「{substanceB}」存在配伍禁忌——{kvp.Key}类不可与之同库贮存。依据：GB15603-1995 第4.2.2条 禁忌物料不得同库贮存";
            }
            return $"✅ 「{substanceA}」与「{substanceB}」在常见禁忌表中未发现直接冲突，但仍建议按照 GB15603 分类贮存原则进行核实（knowledgebase/国标/GB15603 已收录全文）";
        }

        [KernelFunction, Description("查询指定设施类型的安全距离要求（储罐间距、消防通道间距等），返回最小安全距离（米）")]
        public string GetSafetyDistance(string facilityType)
        {
            var key = facilityType.Trim();
            if (SafetyDistances.TryGetValue(key, out int distance))
                return $"「{key}」的最小安全间距为 {distance} 米";
            var matched = SafetyDistances.Keys.Where(k => k.Contains(key) || key.Contains(k)).ToList();
            if (matched.Count > 0)
                return $"已匹配「{matched[0]}」：最小安全间距为 {SafetyDistances[matched[0]]} 米";
            return $"未找到「{key}」的精确安全距离数值，建议在 knowledgebase/国标/ 目录下查阅 GB50160《石油化工企业设计防火规范》和 GB50016《建筑设计防火规范》全文";
        }

        // ════════════════════════════════════════
        // 异步方法：RAG 检索 + LLM 生成（方案 B 主力）
        // ════════════════════════════════════════

        public async Task<string> CheckHazardCategoryAsync(string substanceName)
        {
            if (!UseRag) return CheckHazardCategory(substanceName);

            var chunks = await _kbService!.RetrieveChemicalRegulationAsync(
                $"{substanceName} 危险类别 分类 规范",
                regulationType: "国标", topK: 3);

            if (chunks.Count == 0)
                return $"未在知识库中找到「{substanceName}」的危险类别信息，建议查阅 GB 30000 系列标准全文";

            var context = string.Join("\n---\n", chunks.Select(c => c.Content));
            var prompt = $"根据以下知识库内容判断「{substanceName}」属于哪个危险类别及对应国标。直接给出结果，格式：「XX」属于「XX类别」，适用标准：GB XXXXX-XXXX\n\n{context}";
            return await _llmService!.InvokeStreamWithRetryAsync(prompt, ConsoleColor.Gray, "RAG-CheckHazard");
        }

        public async Task<string> CheckStorageCompatibilityAsync(string substanceA, string substanceB)
        {
            if (!UseRag) return CheckStorageCompatibility(substanceA, substanceB);

            var chunks = await _kbService!.RetrieveChemicalRegulationAsync(
                $"{substanceA} {substanceB} 同库储存 配伍禁忌",
                regulationType: "国标", topK: 3);

            if (chunks.Count == 0)
                return $"未在知识库中找到「{substanceA}」与「{substanceB}」的储存兼容性信息，建议查阅 GB15603 全文";

            var context = string.Join("\n---\n", chunks.Select(c => c.Content));
            var prompt = $"根据以下知识库内容判断「{substanceA}」与「{substanceB}」是否可以同库储存。直接给出结果，包含禁止/允许结论及依据标准。\n\n{context}";
            return await _llmService!.InvokeStreamWithRetryAsync(prompt, ConsoleColor.Gray, "RAG-CheckStorage");
        }

        public async Task<string> GetSafetyDistanceAsync(string facilityType)
        {
            if (!UseRag) return GetSafetyDistance(facilityType);

            var chunks = await _kbService!.RetrieveChemicalRegulationAsync(
                $"{facilityType} 安全间距 距离 要求",
                regulationType: "国标", topK: 3);

            if (chunks.Count == 0)
                return $"未在知识库中找到「{facilityType}」的安全距离信息，建议查阅 GB50160 和 GB50016 全文";

            var context = string.Join("\n---\n", chunks.Select(c => c.Content));
            var prompt = $"根据以下知识库内容查询「{facilityType}」的安全间距要求。直接给出结果，必须包含具体数值（米）及依据标准。如果知识库中无明确数值，诚实说明。\n\n{context}";
            return await _llmService!.InvokeStreamWithRetryAsync(prompt, ConsoleColor.Gray, "RAG-GetDistance");
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
