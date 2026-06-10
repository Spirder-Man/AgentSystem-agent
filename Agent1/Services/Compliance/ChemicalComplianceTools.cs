using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Agent1.Models;
using Agent1.Services;

namespace Agent1.Services
{
    /// <summary>
    /// 化工园区危化品合规审核专用工具集
    /// Phase 2a 重构: [KernelFunction] 绑定到 RAG 检索方法，工具返回知识库原文而非 LLM 生成
    /// 降级策略: RAG 检索 → 硬编码字典 → 诚实声明
    /// </summary>
    public class ChemicalComplianceTools
    {
        private IKnowledgeBaseService? _kbService;

        /// <summary>完整构造：启用 RAG 检索模式。kbService 为 null 时降级到硬编码字典</summary>
        public ChemicalComplianceTools(IKnowledgeBaseService? kbService = null)
        {
            _kbService = kbService;
        }

        /// <summary>Phase 2a 修复: DI 循环依赖解决方案 — 延迟注入 kbService</summary>
        public void SetKnowledgeBaseService(IKnowledgeBaseService kbService)
        {
            _kbService = kbService;
        }

        /// <summary>是否启用了 RAG 检索</summary>
        private bool UseRag => _kbService != null;

        // [P2-2] RAG 检索缓存：评测时同化学品不重复查向量库
        private static readonly Dictionary<string, List<RetrievedChunk>> RagCache = new();

        /// <summary>[P2-2] 带缓存的 RAG 检索</summary>
        private async Task<List<RetrievedChunk>> GetCachedOrRetrieveAsync(string query, string regulationType = "国标", int topK = 3)
        {
            var cacheKey = $"{query}|{regulationType}|{topK}";
            if (RagCache.TryGetValue(cacheKey, out var cached))
            {
                Console.WriteLine($"   [缓存命中] RAG 结果复用: \"{query}\" ({cached.Count} 条)");
                return cached;
            }
            var chunks = await _kbService!.RetrieveChemicalRegulationAsync(query, regulationType: regulationType, topK: topK);
            RagCache[cacheKey] = chunks;
            return chunks;
        }

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

        // [P2-1] 化学品别名映射表：评测中常见的非标准名称归一化
        private static readonly Dictionary<string, string> SubstanceAliasMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["液氯"] = "氯",
            ["氨水"] = "氨溶液",
            ["烧碱"] = "氢氧化钠",
            ["盐酸"] = "氯化氢",
            ["双氧水"] = "过氧化氢",
            ["火碱"] = "氢氧化钠",
            ["苛性钠"] = "氢氧化钠",
            ["苛性钾"] = "氢氧化钾",
            ["酒精"] = "乙醇",
            ["甘油"] = "丙三醇",
            ["醋酸"] = "乙酸",
            ["丙酮"] = "丙酮",  // 保留标准名
            ["甲醛溶液"] = "甲醛",
            ["福尔马林"] = "甲醛",
        };

        /// <summary>[P2-1] 将化学品别名归一化为标准名称</summary>
        private static string NormalizeSubstanceName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return name;
            var trimmed = name.Trim();

            // Step 1: 硬编码别名映射（向后兼容）
            if (SubstanceAliasMap.TryGetValue(trimmed, out var normalized))
                return normalized;

            // Step 2: [Task 10] 查询结构化化学品数据库（别名 → 标准名）
            var substance = ChemicalSubstanceDatabase.Lookup(trimmed);
            if (substance != null)
                return substance.Name;

            return trimmed;
        }

        // ════════════════════════════════════════
        // 主力：[KernelFunction] RAG 检索版（SK Auto Function Calling 入口）
        // 工具返回知识库原文，不依赖 LLM 流式生成
        // ════════════════════════════════════════

        [KernelFunction, Description("查询指定危化品的危险类别及适用国标（GB 30000 系列）。输入参数 substanceName: 危化品名称，如\"苯\"、\"硫酸\"。")]
        public async Task<string> CheckHazardCategory(string substanceName)
        {
            // [P2-1] 别名归一化
            substanceName = NormalizeSubstanceName(substanceName);
            Console.WriteLine($"   [工具诊断] CheckHazardCategory 被调用, substanceName=\"{substanceName}\"");

            if (!UseRag)
            {
                Console.WriteLine("   [工具诊断] RAG 不可用，降级到硬编码字典");
                return CheckHazardCategoryFallback(substanceName);
            }

            var chunks = await GetCachedOrRetrieveAsync(
                $"{substanceName} 危险类别 分类 规范",
                regulationType: "国标", topK: 3);

            Console.WriteLine($"   [工具诊断] RAG 检索完成, 命中 {chunks.Count} 条结果");

            if (chunks.Count == 0)
            {
                Console.WriteLine("   [工具诊断] 检索为0条，降级到硬编码字典");
                return CheckHazardCategoryFallback(substanceName);
            }

            return FormatRagResult($"危化品「{substanceName}」危险类别检索结果", chunks);
        }

        [KernelFunction, Description("查询两种危化品是否可以同库储存（依据 GB15603）。参数 substanceA: 第一种危化品名称, substanceB: 第二种危化品名称。")]
        public async Task<string> CheckStorageCompatibility(string substanceA, string substanceB)
        {
            // [P2-1] 别名归一化
            substanceA = NormalizeSubstanceName(substanceA);
            substanceB = NormalizeSubstanceName(substanceB);
            Console.WriteLine($"   [工具诊断] CheckStorageCompatibility 被调用, A=\"{substanceA}\", B=\"{substanceB}\"");

            if (!UseRag)
            {
                Console.WriteLine("   [工具诊断] RAG 不可用，降级到硬编码字典");
                return CheckStorageCompatibilityFallback(substanceA, substanceB);
            }

            var chunks = await GetCachedOrRetrieveAsync(
                $"{substanceA} {substanceB} 同库储存 配伍禁忌",
                regulationType: "国标", topK: 3);

            Console.WriteLine($"   [工具诊断] RAG 检索完成, 命中 {chunks.Count} 条结果");

            if (chunks.Count == 0)
            {
                Console.WriteLine("   [工具诊断] 检索为0条，降级到硬编码字典");
                return CheckStorageCompatibilityFallback(substanceA, substanceB);
            }

            return FormatRagResult($"「{substanceA}」与「{substanceB}」储存兼容性检索结果", chunks);
        }

        [KernelFunction, Description("查询指定设施类型的安全距离要求（依据 GB50160/GB50016）。参数 facilityType: 设施类型，如\"储罐-建筑\"、\"甲类仓库-明火点\"。")]
        public async Task<string> GetSafetyDistance(string facilityType)
        {
            Console.WriteLine($"   [工具诊断] GetSafetyDistance 被调用, facilityType=\"{facilityType}\"");

            if (!UseRag)
            {
                Console.WriteLine("   [工具诊断] RAG 不可用，降级到硬编码字典");
                return GetSafetyDistanceFallback(facilityType);
            }

            var chunks = await GetCachedOrRetrieveAsync(
                $"{facilityType} 安全间距 距离 米",
                regulationType: "国标", topK: 3);

            Console.WriteLine($"   [工具诊断] RAG 检索完成, 命中 {chunks.Count} 条结果");

            if (chunks.Count == 0)
            {
                Console.WriteLine("   [工具诊断] 检索为0条，降级到硬编码字典");
                return GetSafetyDistanceFallback(facilityType);
            }

            // [P0-1] 从 RAG 原文中提取数值距离，增强可判读性
            var allText = string.Join("\n", chunks.Select(c => c.Content));
            var (distance, unit, source) = ExtractDistanceFromText(allText);

            // [P1-1] 从 chunk 中提取法规编号
            var regSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in chunks)
            {
                if (c.Metadata != null && c.Metadata.TryGetValue("source", out var src))
                    ExtractRegulationRefs(src.ToString() ?? "", regSet);
                ExtractRegulationRefs(c.Content, regSet);
            }

            var sb = new StringBuilder();
            if (regSet.Count > 0)
                sb.AppendLine($"[REGULATIONS: {string.Join(", ", regSet.OrderBy(r => r))}]");
            if (distance.HasValue)
                sb.AppendLine($"[DISTANCE: {distance.Value}{unit}]");
            sb.AppendLine($"📋 「{facilityType}」安全间距检索结果");
            if (distance.HasValue)
            {
                sb.AppendLine($"🔢 **提取数值**: {distance.Value} {unit} (来源: {source})");
                sb.AppendLine($"⚠️  实际距离需对照 {facilityType} 场景具体判定");
                sb.AppendLine($"[判定:is_compliant=待核实, 参考距离={distance.Value}{unit}]");
            }
            else
            {
                sb.AppendLine("⚠️  未从检索结果中提取到具体数值距离");
                sb.AppendLine("[判定:is_compliant=数据不足]");
            }
            sb.AppendLine();
            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                var chunkSource = "未知来源";
                if (chunk.Metadata != null && chunk.Metadata.TryGetValue("source", out var src))
                    chunkSource = src.ToString() ?? "未知来源";
                sb.AppendLine($"**【检索结果 {i + 1}】** (来源: {chunkSource}, 相关度: {chunk.Score:P0})");
                sb.AppendLine(chunk.Content);
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// [P0-1] 从 RAG 原文中提取安全距离数值。
        /// 匹配模式：不小于X米 / >=Xm / 最小安全距离为Xm / 不得小于X米 等
        /// </summary>
        private static (double? distance, string unit, string source) ExtractDistanceFromText(string text)
        {
            // 模式1: "不小于 X 米" / "不得小于 X m" / "≥ X m"
            var patterns = new[]
            {
                @"(不小于|不得小于|≥|>=s*)s*(\d+(?:\.\d+)?)\s*(米|m)",
                @"(最小.*?(?:安全|防火)?间距.*?为)\s*(\d+(?:\.\d+)?)\s*(米|m)",
                @"(安全距离.*?)\s*(\d+(?:\.\d+)?)\s*(米|m)",
                @"(间距).*?(\d+(?:\.\d+)?)\s*(米|m)",
                @"(\d+(?:\.\d+)?)\s*(米|m)",  // 最宽泛：直接找 "X米"或"X m"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(text, pattern);
                if (match.Success)
                {
                    var distStr = match.Groups.Values
                        .FirstOrDefault(g => double.TryParse(g.Value, out _))?.Value;
                    if (distStr != null && double.TryParse(distStr, out var d))
                    {
                        var contextStart = Math.Max(0, match.Index - 20);
                        var contextLen = Math.Min(80, text.Length - contextStart);
                        var context = text.Substring(contextStart, contextLen).Replace("\n", " ");
                        return (d, "米", context);
                    }
                }
            }

            return (null, "", "");
        }

        // ════════════════════════════════════════
        // [Task 10] 新增工具：结构化化学品属性 + 重大危险源 + 法规版本
        // ════════════════════════════════════════

        [KernelFunction, Description("查询指定危化品的完整基础属性，包括CAS号、UN编号、分子式、闪点、沸点、爆炸极限、危险类别和适用国标。输入参数 substanceName: 危化品名称，如\"苯\"、\"甲醇\"。")]
        public string LookupChemicalProperties(string substanceName)
        {
            substanceName = NormalizeSubstanceName(substanceName);
            Console.WriteLine($"   [工具诊断] LookupChemicalProperties 被调用, substanceName=\"{substanceName}\"");

            var sub = ChemicalSubstanceDatabase.Lookup(substanceName);
            if (sub == null)
            {
                var searchResults = ChemicalSubstanceDatabase.Search(substanceName, 3);
                if (searchResults.Count == 0)
                    return $"未找到「{substanceName}」的化学品属性数据。建议在 knowledgebase/国标/ 目录下查阅 GB 30000 系列标准全文。";

                var altSb = new StringBuilder();
                altSb.AppendLine($"未精确匹配「{substanceName}」，找到以下相近化学品：");
                foreach (var r in searchResults)
                    altSb.AppendLine($"  - {r.Name} (CAS: {r.CasNumber}, UN: {r.UnNumber})");
                altSb.AppendLine($"[判定:is_compliant=无精确数据]");
                return altSb.ToString().TrimEnd();
            }

            var sb = new StringBuilder();
            sb.AppendLine($"📋 **{sub.Name}** ({sub.NameEn})");
            sb.AppendLine($"   分子式: {sub.Formula}  |  CAS: {sub.CasNumber}  |  UN: {sub.UnNumber}");
            sb.AppendLine($"   理化状态: {sub.PhysicalState}");
            if (sub.FlashPointC.HasValue)
                sb.AppendLine($"   闪点: {sub.FlashPointC}°C");
            if (sub.BoilingPointC.HasValue)
                sb.AppendLine($"   沸点: {sub.BoilingPointC}°C");
            if (sub.ExplosiveLowerLimit.HasValue && sub.ExplosiveUpperLimit.HasValue)
                sb.AppendLine($"   爆炸极限: {sub.ExplosiveLowerLimit}% ~ {sub.ExplosiveUpperLimit}% (V/V)");
            if (sub.AutoIgnitionTempC.HasValue)
                sb.AppendLine($"   自燃温度: {sub.AutoIgnitionTempC}°C");
            if (sub.RelativeDensity.HasValue)
                sb.AppendLine($"   相对密度(水=1): {sub.RelativeDensity}");
            if (sub.VaporDensity.HasValue)
                sb.AppendLine($"   蒸气密度(空气=1): {sub.VaporDensity}");

            sb.AppendLine($"   危险类别:");
            foreach (var hc in sub.HazardCategories)
            {
                var note = !string.IsNullOrEmpty(hc.SubCategory) ? $" ({hc.SubCategory})" : "";
                sb.AppendLine($"     - {hc.Category}{note} [依据: {hc.GbStandard}]");
            }

            if (sub.MajorHazardThresholdTons > 0)
                sb.AppendLine($"   ⚠️ GB 18218 重大危险源临界量: {sub.MajorHazardThresholdTons} 吨");

            if (sub.IncompatibleWith.Count > 0)
                sb.AppendLine($"   🚫 储存禁忌: {string.Join("、", sub.IncompatibleWith)}");

            sb.AppendLine($"[判定:is_compliant=数据查询,来源:ChemicalSubstanceDatabase]");
            return sb.ToString().TrimEnd();
        }

        [KernelFunction, Description("查询指定危化品在GB 18218《危险化学品重大危险源辨识》中的临界量（吨）。输入参数 substanceName: 危化品名称。")]
        public string GetMajorHazardThreshold(string substanceName)
        {
            substanceName = NormalizeSubstanceName(substanceName);
            Console.WriteLine($"   [工具诊断] GetMajorHazardThreshold 被调用, substanceName=\"{substanceName}\"");

            var sub = ChemicalSubstanceDatabase.Lookup(substanceName);
            if (sub == null)
                return $"未找到「{substanceName}」的重大危险源临界量数据。请查阅 GB 18218-2018 表1/表2 获取完整名录。";

            if (sub.MajorHazardThresholdTons <= 0)
                return $"「{sub.Name}」不属于 GB 18218 明确列名的重大危险源物质 (CAS: {sub.CasNumber})。\n[REGULATIONS: GB 18218-2018]\n[判定:is_compliant=非列名物质]";

            return $"[REGULATIONS: GB 18218-2018]\n📋 「{sub.Name}」(CAS: {sub.CasNumber})\n   重大危险源临界量: **{sub.MajorHazardThresholdTons} 吨**\n   若实际存在量 ≥ {sub.MajorHazardThresholdTons}t，则构成重大危险源，需按照《危险化学品安全管理条例》第19条和总局令40号进行登记备案和分级管理。\n[判定:is_compliant=依据GB18218待定量核查]";
        }

        [KernelFunction, Description("查询指定法规标准的版本状态（现行/废止）及全文收录情况。输入参数 regulationNumber: 法规编号，如\"GB 15603\"、\"GB 18218\"。")]
        public string CheckRegulationVersion(string regulationNumber)
        {
            Console.WriteLine($"   [工具诊断] CheckRegulationVersion 被调用, regulationNumber=\"{regulationNumber}\"");

            var version = ChemicalSubstanceDatabase.GetRegulationVersion(regulationNumber);
            if (version == null)
                return $"未找到「{regulationNumber}」的版本追踪数据。建议参考国家标准全文公开系统 (openstd.samr.gov.cn) 查询最新版本。";

            var sb = new StringBuilder();
            sb.AppendLine($"📋 [{version.RegulationNumber}] {version.Title}");
            sb.AppendLine($"   现行版本: {version.CurrentVersion}");
            if (version.DeprecatedVersions.Count > 0)
                sb.AppendLine($"   已废止版本: {string.Join(", ", version.DeprecatedVersions)}");
            sb.AppendLine($"   知识库收录状态: {(version.HasFullText ? "✅ 已收录全文" : "⚠️ 仅收录摘要/引用（建议上传全文PDF）")}");
            if (!string.IsNullOrEmpty(version.ChangeNotes))
                sb.AppendLine($"   关键变更: {version.ChangeNotes}");
            sb.AppendLine($"[判定:is_compliant=法规版本查询]");
            return sb.ToString().TrimEnd();
        }

        // ════════════════════════════════════════
        // 降级方法：硬编码字典（RAG 检索失败 / 无 kbService 时兜底）
        // ════════════════════════════════════════

        private string CheckHazardCategoryFallback(string substanceName)
        {
            // [Task 10] 优先查询结构化化学品数据库
            var sub = ChemicalSubstanceDatabase.Lookup(substanceName);
            if (sub != null && sub.HazardCategories.Count > 0)
            {
                var gbNums = sub.HazardCategories.Select(h => h.GbStandard).Distinct().ToList();
                var catNames = sub.HazardCategories.Select(h =>
                    string.IsNullOrEmpty(h.SubCategory) ? h.Category : $"{h.Category}({h.SubCategory})").ToList();
                return $"[REGULATIONS: {string.Join(", ", gbNums)}]\n「{sub.Name}」危险类别: {string.Join("; ", catNames)} [判定:is_compliant=unknown]";
            }

            // 原降级逻辑：通用类别关键词匹配
            foreach (var kvp in HazardCategories)
            {
                if (substanceName.Contains(kvp.Key) || kvp.Key.Contains(substanceName))
                    return $"[REGULATIONS: {kvp.Value}]\n「{substanceName}」属于「{kvp.Key}」类别，适用标准：{kvp.Value} [判定:is_compliant=unknown]";
            }
            return $"「{substanceName}」未在常见危化品类别中直接匹配，建议查阅 GB 30000 系列标准全文（knowledgebase/国标/ 目录下已收录完整标准文件） [判定:is_compliant=unknown]";
        }

        private string CheckStorageCompatibilityFallback(string substanceA, string substanceB)
        {
            // [Task 10] 优先查询精确化学品配对规则
            var dbResult = ChemicalSubstanceDatabase.CheckCompatibility(substanceA, substanceB);
            if (dbResult != null)
            {
                var regRef = !string.IsNullOrEmpty(dbResult.RegulationRef) ? $" [依据: {dbResult.RegulationRef}]" : "";
                if (dbResult.IsCompatible)
                    return $"[REGULATIONS: {(string.IsNullOrEmpty(dbResult.RegulationRef) ? "GB 15603" : dbResult.RegulationRef)}]\n✅ {dbResult.Reason}{regRef} [判定:is_compliant=true]";
                return $"[REGULATIONS: {(string.IsNullOrEmpty(dbResult.RegulationRef) ? "GB 15603" : dbResult.RegulationRef)}]\n⚠️ 禁用：{dbResult.Reason}{regRef} [判定:is_compliant=false]";
            }

            // 原降级逻辑：通用类别禁忌匹配
            foreach (var kvp in StorageIncompatibilities)
            {
                bool aIsIncompatible = kvp.Value.Any(s => substanceB.Contains(s));
                bool bIsIncompatible = kvp.Value.Any(s => substanceA.Contains(s));
                if (aIsIncompatible || bIsIncompatible)
                    return $"[REGULATIONS: GB15603-1995]\n⚠️ 禁用：「{substanceA}」与「{substanceB}」存在配伍禁忌——{kvp.Key}类不可与之同库贮存。依据：GB15603-1995 第4.2.2条 禁忌物料不得同库贮存 [判定:is_compliant=false]";
            }
            return $"[REGULATIONS: GB15603]\n✅ 「{substanceA}」与「{substanceB}」在常见禁忌表中未发现直接冲突，但仍建议按照 GB15603 分类贮存原则进行核实（knowledgebase/国标/GB15603 已收录全文） [判定:is_compliant=true]";
        }

        private string GetSafetyDistanceFallback(string facilityType)
        {
            var key = facilityType.Trim();

            // [Task 10] 优先查询扩展安全距离规则表
            var dbRule = ChemicalSubstanceDatabase.GetSafetyDistance(key);
            if (dbRule != null)
            {
                return $"[REGULATIONS: {dbRule.RegulationRef}]\n[DISTANCE: {dbRule.MinDistanceMeters}m]\n「{dbRule.FacilityPair}」的最小安全间距为 {dbRule.MinDistanceMeters} 米 (依据: {dbRule.RegulationRef}) [判定:is_compliant=待核实]";
            }

            // 原硬编码字典
            if (SafetyDistances.TryGetValue(key, out int distance))
                return $"[REGULATIONS: GB50160]\n[DISTANCE: {distance}m]\n「{key}」的最小安全间距为 {distance} 米 [判定:is_compliant=待核实]";
            var matched = SafetyDistances.Keys.Where(k => k.Contains(key) || key.Contains(k)).ToList();
            if (matched.Count > 0)
                return $"[REGULATIONS: GB50160]\n[DISTANCE: {SafetyDistances[matched[0]]}m]\n已匹配「{matched[0]}」：最小安全间距为 {SafetyDistances[matched[0]]} 米 [判定:is_compliant=待核实]";
            return $"未找到「{key}」的精确安全距离数值，建议在 knowledgebase/国标/ 目录下查阅 GB50160《石油化工企业设计防火规范》和 GB50016《建筑设计防火规范》全文 [判定:is_compliant=待核实]";
        }

        // ════════════════════════════════════════
        // 辅助：格式化 RAG 检索结果为 Markdown 原文
        // ════════════════════════════════════════

        private static string FormatRagResult(string title, List<RetrievedChunk> chunks)
        {
            var sb = new StringBuilder();

            // [P1-1] 结构化法规编号提取: 从 chunk 元数据和内容中提取法规引用
            var regulationSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var chunk in chunks)
            {
                // 从元数据 source 字段提取
                if (chunk.Metadata != null && chunk.Metadata.TryGetValue("source", out var src))
                {
                    var sourceStr = src.ToString() ?? "";
                    ExtractRegulationRefs(sourceStr, regulationSet);
                }
                // 从 chunk 内容中提取法规编号
                ExtractRegulationRefs(chunk.Content, regulationSet);
            }
            if (regulationSet.Count > 0)
                sb.AppendLine($"[REGULATIONS: {string.Join(", ", regulationSet.OrderBy(r => r))}]");

            sb.AppendLine($"📋 {title}");
            sb.AppendLine();
            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                var source = "未知来源";
                if (chunk.Metadata != null && chunk.Metadata.TryGetValue("source", out var src))
                    source = src.ToString() ?? "未知来源";
                sb.AppendLine($"**【检索结果 {i + 1}】** (来源: {source}, 相关度: {chunk.Score:P0})");
                sb.AppendLine(chunk.Content);
                sb.AppendLine();
            }
            sb.AppendLine("[判定:is_compliant=依据原文]");
            return sb.ToString().TrimEnd();
        }

        /// <summary>[P1-1] 从文本中提取法规编号 (GB XXXX-XXXX 格式)</summary>
        private static void ExtractRegulationRefs(string text, HashSet<string> resultSet)
        {
            var matches = Regex.Matches(text, @"GB\s*/?T?\s*\d{4,5}[.\-]\d{2,4}(?:\s*-\s*\d{4})?");
            foreach (Match m in matches)
            {
                var normalized = Regex.Replace(m.Value, @"\s+", " ").Trim();
                resultSet.Add(normalized);
            }
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
