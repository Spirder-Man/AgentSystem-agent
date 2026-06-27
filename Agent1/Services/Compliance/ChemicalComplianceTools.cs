using Microsoft.SemanticKernel;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Agent1.Config;
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
        // [P7 FIX] Faithfulness崩塌修复：RAG结果chunk内容截断上限
        private static int MAX_CHUNK_CHARS => AppConfig.Instance.KnowledgeBase.ChunkOutputMaxChars;
        // [P0 Lazy<T>] 使用 Lazy 打破 DI 循环依赖（ILlmService ↔ IKnowledgeBaseService）
        // .Value 仅在首次实际使用时触发 DI 解析，避免构造函数期死锁
        private readonly Lazy<IKnowledgeBaseService>? _lazyKb;
        private RerankerService? _rerankerService;

        /// <summary>完整构造：启用 RAG 检索模式。lazyKb 为 null 时降级到硬编码字典</summary>
        public ChemicalComplianceTools(Lazy<IKnowledgeBaseService>? lazyKb = null)
        {
            _lazyKb = lazyKb;
        }

        /// <summary>Sprint 3: 注入 Reranker 服务</summary>
        public void SetRerankerService(RerankerService rerankerService)
        {
            _rerankerService = rerankerService;
        }

        // ════════════════════════════════════════
        // [QF-2026-001] L1 编译期质量标记辅助方法
        // ════════════════════════════════════════

        /// <summary>
        /// [L1] 标记工具返回值质量等级，同时设置 AsyncLocal 上下文供 FunctionCallDiagnosticsFilter 和 StoreToolFacts 读取。
        /// 返回原始 string 保持 [KernelFunction] 签名不变。
        /// </summary>
        private static string MarkQuality(string content, QualityLevel quality, params string[] regs)
        {
            ToolQualityContext.Current = new ToolResult
            {
                Content = content,
                Quality = quality,
                RegulationRefs = regs.ToList()
            };
            return content;
        }

        /// <summary>是否启用了 RAG 检索（Lazy 对象非空即表示已配置）</summary>
        private bool UseRag => _lazyKb != null;

        // [P0-2 FIX+P1-6 FIX] RAG 检索缓存：线程安全 ConcurrentDictionary + TTL 淘汰，防止内存泄漏
        private static readonly ConcurrentDictionary<string, (List<RetrievedChunk> chunks, DateTime expiresAt)> RagCache = new();
        private static readonly TimeSpan RagCacheTtl = TimeSpan.FromMinutes(5);
        private const int RagCacheMaxEntries = 200;

        /// <summary>[P2-2] 带缓存的 RAG 检索 + Sprint 3 Reranker 精排</summary>
        private async Task<List<RetrievedChunk>> GetCachedOrRetrieveAsync(string query, string regulationType = "国标", int topK = 3)
        {
            var cacheKey = $"{query}|{regulationType}|{topK}";
            if (RagCache.TryGetValue(cacheKey, out var cached))
            {
                if (cached.expiresAt > DateTime.UtcNow)
                {
                    Console.WriteLine($"   [缓存命中] RAG 结果复用: \"{query}\" ({cached.chunks.Count} 条)");
                    return cached.chunks;
                }
                // TTL 过期，移除
                RagCache.TryRemove(cacheKey, out _);
            }

            // Sprint 3: 粗排召回更多候选 → Reranker 精排
            var candidateTopK = _rerankerService != null && _rerankerService.IsEnabled
                ? Math.Max(topK, AppConfig.Instance.VectorSearch.RerankerCandidateTopK)
                : topK;

            var chunks = await _lazyKb!.Value.RetrieveChemicalRegulationAsync(query, regulationType: regulationType, topK: candidateTopK);

            // Sprint 3: Reranker 精排
            if (_rerankerService != null && _rerankerService.IsEnabled && chunks.Count > topK)
            {
                chunks = await _rerankerService.RerankAsync(query, chunks, topK);
            }
            else if (chunks.Count > topK)
            {
                chunks = chunks.Take(topK).ToList();
            }

            // LRU 淘汰：超出上限时随机删除一半过期条目
            if (RagCache.Count >= RagCacheMaxEntries)
            {
                var now = DateTime.UtcNow;
                var staleKeys = RagCache.Where(kvp => kvp.Value.expiresAt <= now).Select(kvp => kvp.Key).ToList();
                foreach (var key in staleKeys)
                    RagCache.TryRemove(key, out _);
                // 仍然满则暴力淘汰一半
                if (RagCache.Count >= RagCacheMaxEntries)
                {
                    var allKeys = RagCache.Keys.Take(RagCache.Count / 2).ToList();
                    foreach (var key in allKeys)
                        RagCache.TryRemove(key, out _);
                }
            }

            RagCache[cacheKey] = (chunks, DateTime.UtcNow.Add(RagCacheTtl));
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

        [KernelFunction, Description("查询指定危化品的危险类别、危险特性、GHS分类及适用国标（GB 30000 系列）。适用于「XX属于什么危险类别」「XX的危险特性」「XX的GHS分类」等问题。输入参数 substanceName: 危化品名称，如\"苯\"、\"硫酸\"。")]
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

            return MarkQuality(
                FormatRagResult($"危化品「{substanceName}」危险类别检索结果", chunks),
                QualityLevel.RAG_HIT);
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

            return MarkQuality(
                FormatRagResult($"「{substanceA}」与「{substanceB}」储存兼容性检索结果", chunks),
                QualityLevel.RAG_HIT);
        }

        [KernelFunction, Description("查询指定设施类型的安全距离/防火间距要求（依据 GB50160/GB50016）。适用于「XX与XX的安全距离」「XX与XX的防火间距」「XX与XX的最小间距」等问题。参数 facilityType: 设施类型描述，如\"储罐-建筑\"、\"甲类仓库-明火点\"、\"气柜-办公楼\"。")]
        public async Task<string> GetSafetyDistance(string facilityType)
        {
            Console.WriteLine($"   [工具诊断] GetSafetyDistance 被调用, facilityType=\"{facilityType}\"");

            if (!UseRag)
            {
                Console.WriteLine("   [工具诊断] RAG 不可用，降级到硬编码字典");
                return GetSafetyDistanceFallback(facilityType);
            }

            var chunks = await GetCachedOrRetrieveAsync(
                $"{facilityType} GB50160 防火间距 安全距离",
                regulationType: "国标", topK: 5);

            Console.WriteLine($"   [工具诊断] RAG 检索完成, 命中 {chunks.Count} 条结果");

            if (chunks.Count == 0)
            {
                Console.WriteLine("   [工具诊断] 检索为0条，降级到硬编码字典");
                return GetSafetyDistanceFallback(facilityType);
            }

            // [P0-1] 从 RAG 原文中提取数值距离，增强可判读性
            var allText = string.Join("\n", chunks.Select(c => c.Content));
            var (distance, unit, source) = ExtractDistanceFromText(allText, facilityType);

            // [P1-1] 从 chunk 中提取法规编号
            var regSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in chunks)
            {
                if (c.Metadata != null && c.Metadata.TryGetValue("source", out var src))
                    ExtractRegulationRefs(src.ToString() ?? "", regSet);
                ExtractRegulationRefs(c.Content, regSet);
            }

            // [P3 FIX] RAG 提取不到距离时，回退到硬编码字典/数据库，确保工具结果始终含 [DISTANCE: Xm]
            if (!distance.HasValue)
            {
                Console.WriteLine($"   [工具诊断] RAG 未提取到距离，回退硬编码字典");
                var fallback = GetSafetyDistanceFallback(facilityType);
                if (regSet.Count > 0 && !fallback.Contains("[REGULATIONS:"))
                    return MarkQuality(
                        $"[REGULATIONS: {string.Join(", ", regSet.OrderBy(r => r))}]\n{fallback}",
                        QualityLevel.RAG_HIT, regSet.ToArray());
                return fallback; // fallback 内部已有 MarkQuality
            }

            var sb = new StringBuilder();
            if (regSet.Count > 0)
                sb.AppendLine($"[REGULATIONS: {string.Join(", ", regSet.OrderBy(r => r))}]");
            sb.AppendLine($"[DISTANCE: {distance.Value}{unit}]");
            sb.AppendLine($"📋 「{facilityType}」安全间距检索结果");
            sb.AppendLine($"🔢 **提取数值**: {distance.Value} {unit} (来源: {source})");
            sb.AppendLine($"⚠️  实际距离需对照 {facilityType} 场景具体判定");
            sb.AppendLine($"[判定:is_compliant=待核实, 参考距离={distance.Value}{unit}]");
            sb.AppendLine();
            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                var chunkSource = "未知来源";
                if (chunk.Metadata != null && chunk.Metadata.TryGetValue("source", out var src))
                    chunkSource = src.ToString() ?? "未知来源";
                sb.AppendLine($"**【检索结果 {i + 1}】** (来源: {chunkSource}, 相关度: {chunk.Score:P0})");
                sb.AppendLine(TruncateChunk(chunk.Content));
                sb.AppendLine();
            }
            return MarkQuality(sb.ToString().TrimEnd(), QualityLevel.RAG_HIT, regSet.ToArray());
        }

        /// <summary>
        /// [P0-1] 从 RAG 原文中提取安全距离数值。
        /// 匹配模式：不小于X米 / >=Xm / 最小安全距离为Xm / 不得小于X米 等
        /// [P6] contextHint 用于在多距离文档中定位正确的距离值
        /// </summary>
        private static (double? distance, string unit, string source) ExtractDistanceFromText(string text, string? contextHint = null)
        {
            if (!string.IsNullOrWhiteSpace(contextHint))
            {
                var hintKeywords = contextHint.Split('-', ' ', '的', '与', '和')
                    .Where(k => k.Length >= 2)
                    .ToArray();

                foreach (var kw in hintKeywords)
                {
                    var idx = text.IndexOf(kw, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        var start = Math.Max(0, idx - 50);
                        var len = Math.Min(400, text.Length - start);
                        var context = text.Substring(start, len);

                        var (d, u, s) = ExtractDistancePatterns(context);
                        if (d.HasValue)
                            return (d, u, s);
                    }
                }
            }

            return ExtractDistancePatterns(text);
        }

        /// <summary>在给定文本中通过正则提取距离数值</summary>
        private static (double? distance, string unit, string source) ExtractDistancePatterns(string text)
        {
            var patterns = new[]
            {
                @"(不小于|不应小于|不得小于|≥|>=\s*)\s*(\d+(?:\.\d+)?)\s*(米|m)",  // [FIX P0-1] 正则 s*→\s*, 原漏掉反斜杠导致匹配失效
                @"(最小.*?(?:安全|防火)?间距.*?为)\s*(\d+(?:\.\d+)?)\s*(米|m)",
                @"(安全距离.*?)\s*(\d+(?:\.\d+)?)\s*(米|m)",
                @"(间距).*?(\d+(?:\.\d+)?)\s*(米|m)",
                @"(\d+(?:\.\d+)?)\s*(米|m)",
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

        [KernelFunction, Description("查询指定危化品的完整基础属性，包括CAS号、UN编号、分子式、闪点、沸点、爆炸极限和适用国标。注意：如需查询危险类别/GHS分类，请使用 CheckHazardCategory。输入参数 substanceName: 危化品名称，如\"苯\"、\"甲醇\"。")]
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

        /// <summary>
        /// 降级方法：查询指定危化品的危险类别（RAG 检索失败 / 无 kbService 时兜底）。
        /// </summary>
        private string CheckHazardCategoryFallback(string substanceName)
        {
            // [P0-4 FIX] 使用 Id+内容前缀作去重键, 替代 Guid.NewGuid() 避免随机键破坏 RRF 融合
            var sub = ChemicalSubstanceDatabase.Lookup(substanceName);
            if (sub != null && sub.HazardCategories.Count > 0)
            {
                var gbNums = sub.HazardCategories.Select(h => h.GbStandard).Distinct().ToList();
                var catNames = sub.HazardCategories.Select(h =>
                    string.IsNullOrEmpty(h.SubCategory) ? h.Category : $"{h.Category}({h.SubCategory})").ToList();
                return MarkQuality(
                    $"[REGULATIONS: {string.Join(", ", gbNums)}]\n「{sub.Name}」危险类别: {string.Join("; ", catNames)} [判定:is_compliant=unknown]",
                    QualityLevel.DATABASE_HIT, gbNums.ToArray());
            }
            
            foreach (var kvp in HazardCategories)// 遍历所有危化品类别
            {
                if (substanceName.Contains(kvp.Key) || kvp.Key.Contains(substanceName))
                    return MarkQuality(
                        $"[REGULATIONS: {kvp.Value}]\n「{substanceName}」属于「{kvp.Key}」类别，适用标准：{kvp.Value} [判定:is_compliant=unknown]",
                        QualityLevel.DICTIONARY_HIT, kvp.Value);
            }
            return MarkQuality(
                $"「{substanceName}」未在常见危化品类别中直接匹配，建议查阅 GB 30000 系列标准全文（knowledgebase/国标/ 目录下已收录完整标准文件） [判定:is_compliant=unknown]",
                QualityLevel.FALLBACK);
        }

        private string CheckStorageCompatibilityFallback(string substanceA, string substanceB)
        {
            var dbResult = ChemicalSubstanceDatabase.CheckCompatibility(substanceA, substanceB);
            if (dbResult != null)
            {
                var regRef = !string.IsNullOrEmpty(dbResult.RegulationRef) ? $" [依据: {dbResult.RegulationRef}]" : "";
                var gb = string.IsNullOrEmpty(dbResult.RegulationRef) ? "GB 15603" : dbResult.RegulationRef;
                if (dbResult.IsCompatible)
                    return MarkQuality(
                        $"[REGULATIONS: {gb}]\n✅ {dbResult.Reason}{regRef} [判定:is_compliant=true]",
                        QualityLevel.DATABASE_HIT, gb);
                return MarkQuality(
                    $"[REGULATIONS: {gb}]\n⚠️ 禁用：{dbResult.Reason}{regRef} [判定:is_compliant=false]",
                    QualityLevel.DATABASE_HIT, gb);
            }

            foreach (var kvp in StorageIncompatibilities)
            {
                bool aIsIncompatible = kvp.Value.Any(s => substanceB.Contains(s));
                bool bIsIncompatible = kvp.Value.Any(s => substanceA.Contains(s));
                if (aIsIncompatible || bIsIncompatible)
                    return MarkQuality(
                        $"[REGULATIONS: GB15603-1995]\n⚠️ 禁用：「{substanceA}」与「{substanceB}」存在配伍禁忌——{kvp.Key}类不可与之同库贮存。依据：GB15603-1995 第4.2.2条 禁忌物料不得同库贮存 [判定:is_compliant=false]",
                        QualityLevel.DICTIONARY_HIT, "GB15603-1995");
            }
            return MarkQuality(
                $"[REGULATIONS: GB15603]\n✅ 「{substanceA}」与「{substanceB}」在常见禁忌表中未发现直接冲突，但仍建议按照 GB15603 分类贮存原则进行核实（knowledgebase/国标/GB15603 已收录全文） [判定:is_compliant=true]",
                QualityLevel.DICTIONARY_HIT, "GB15603");
        }

        private string GetSafetyDistanceFallback(string facilityType)
        {
            var key = facilityType.Trim();

            var dbRule = ChemicalSubstanceDatabase.GetSafetyDistance(key);
            if (dbRule != null)
            {
                return MarkQuality(
                    $"[REGULATIONS: {dbRule.RegulationRef}]\n[DISTANCE: {dbRule.MinDistanceMeters}m]\n「{dbRule.FacilityPair}」的最小安全间距为 {dbRule.MinDistanceMeters} 米 (依据: {dbRule.RegulationRef}) [判定:is_compliant=待核实]",
                    QualityLevel.DATABASE_HIT, dbRule.RegulationRef);
            }

            if (SafetyDistances.TryGetValue(key, out int distance))
                return MarkQuality(
                    $"[REGULATIONS: GB50160]\n[DISTANCE: {distance}m]\n「{key}」的最小安全间距为 {distance} 米 [判定:is_compliant=待核实]",
                    QualityLevel.DICTIONARY_HIT, "GB50160");
            var matched = SafetyDistances.Keys.Where(k => k.Contains(key) || key.Contains(k)).ToList();
            if (matched.Count > 0)
                return MarkQuality(
                    $"[REGULATIONS: GB50160]\n[DISTANCE: {SafetyDistances[matched[0]]}m]\n已匹配「{matched[0]}」：最小安全间距为 {SafetyDistances[matched[0]]} 米 [判定:is_compliant=待核实]",
                    QualityLevel.DICTIONARY_HIT, "GB50160");
            return MarkQuality(
                $"未找到「{key}」的精确安全距离数值，建议在 knowledgebase/国标/ 目录下查阅 GB50160《石油化工企业设计防火规范》和 GB50016《建筑设计防火规范》全文 [判定:is_compliant=待核实]",
                QualityLevel.FALLBACK);
        }

        // ════════════════════════════════════════
        // 辅助：格式化 RAG 检索结果为 Markdown 原文
        // ════════════════════════════════════════

        /// <summary>[P7 FIX] chunk 内容截断，防止 LLM 输出全文导致 Faithfulness 误判</summary>
        private static string TruncateChunk(string content)
        {
            if (string.IsNullOrEmpty(content))
                return content;
            return content.Length > MAX_CHUNK_CHARS
                ? content.Substring(0, MAX_CHUNK_CHARS) + "..."
                : content;
        }

        private static string FormatRagResult(string title, List<RetrievedChunk> chunks)
        {
            var sb = new StringBuilder();

            var regulationSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var chunk in chunks)
            {
                if (chunk.Metadata != null && chunk.Metadata.TryGetValue("source", out var src))
                {
                    var sourceStr = src.ToString() ?? "";
                    ExtractRegulationRefs(sourceStr, regulationSet);
                }
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
                sb.AppendLine(TruncateChunk(chunk.Content));
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

        // ═══════════════════════════════════════════
        // [P2] 多模态：[KernelFunction] GHS 标签识别
        // ═══════════════════════════════════════════
        [KernelFunction, Description("分析化学品包装上的 GHS 危险标签图片，识别危险象形图、信号词、H/P 声明代码。参数 imagePath: 本地图片文件路径。适用于「识别这张标签」「这张图片上的危险标识是什么」等需要图像分析的问题。")]
        public async Task<string> LookupHazardLabel(string imagePath)
        {
            try
            {
                var multimodal = new MultimodalService();
                return await multimodal.AnalyzeHazardLabelAsync(imagePath);
            }
            catch (Exception ex)
            {
                return $"GHS 标签识别失败: {ex.Message}。请确认图片路径正确且 Ollama 已启动 qwen-vl 模型";
            }
        }
    }
}
