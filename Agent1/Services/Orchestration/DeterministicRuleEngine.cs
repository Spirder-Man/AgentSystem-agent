using System;
using System.Collections.Generic;
using System.Linq;
using Agent1.Models;

namespace Agent1.Services.Orchestration
{
    // ═══════════════════════════════════════════════════════
    // LLM 降级查询责任链 — 可扩展的确定性兜底
    // ═══════════════════════════════════════════════════════

    /// <summary>合规查询处理器接口：每个实现负责一类化工合规场景的确定性回答。</summary>
    public interface IComplianceQueryHandler
    {
        /// <summary>尝试处理用户查询，返回确定性回答；null 表示当前 handler 不适用。</summary>
        ComplianceFallbackResult? TryHandle(string query);
    }

    /// <summary>
    /// 化工双层门卫 — 正向闭环设计，不再依赖硬编码信号词白名单。
    ///
    /// Tier 1 (物质名门卫): 查询中包含 ChemicalSubstanceDatabase 中任一化学品名/别名 → 直接放行。
    ///   覆盖 50+ 化学品 + 别名，新增物质到数据库即自动获得 Gate 覆盖，无需手动维护。
    ///   "苯和乙醇能搁一块儿吗？" → 含"苯" → 放行 ✅
    /// Tier 2 (信号词门卫): 查询中包含化工领域通用信号词 → 放行。
    ///   覆盖不提名具体物质的化工通用查询，如"危化品储存规范有哪些要求？"。
    /// 两层均不满足 → 拦截"蜘蛛侠""今天天气"等完全无关的输入。
    /// </summary>
    internal static class ChemicalSignalGate
    {
        private static readonly string[] Signals =
        {
            "危险", "安全", "储存", "存放", "化学品", "库房", "仓库", "合规", "法规", "GB", "禁忌",
            "物质", "特性", "类别", "分类", "分级", "间距", "距离", "防火",
            "同库", "共存", "混合", "配伍", "闪点", "沸点", "危害", "储罐"
        };

        /// <summary>双层门卫：物质名 Tier 1 → 信号词 Tier 2 → 拦截</summary>
        public static bool Pass(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return false;

            // Tier 1: 化学物质名匹配（正向闭环：数据库增长自动覆盖）
            if (MentionsChemicalSubstance(query))
                return true;

            // Tier 2: 信号词匹配（覆盖不提名具体物质的化工通用查询）
            if (Signals.Any(s => query.Contains(s)))
                return true;

            return false;
        }

        /// <summary>检查查询中是否包含 ChemicalSubstanceDatabase 中任一化学品名或别名</summary>
        private static bool MentionsChemicalSubstance(string query)
        {
            foreach (var sub in ChemicalSubstanceDatabase.GetAll())
            {
                if (query.Contains(sub.Name, StringComparison.OrdinalIgnoreCase))
                    return true;
                foreach (var alias in sub.Aliases)
                {
                    if (query.Contains(alias, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }
    }

    /// <summary>储存兼容性查询处理器</summary>
    internal class StorageCompatibilityHandler : IComplianceQueryHandler
    {
        private static readonly string[] Keywords =
            { "同库", "共存", "混合", "禁忌", "配伍", "储存", "一起存放", "放在一起",
              "同库存放", "兼容", "能否", "可以", "不可" };

        public ComplianceFallbackResult? TryHandle(string query)
        {
            if (!Keywords.Any(k => query.Contains(k)))
                return null;

            var allSubstances = ChemicalSubstanceDatabase.GetAll();
            var mentioned = allSubstances
                .Where(s => query.Contains(s.Name, StringComparison.OrdinalIgnoreCase))
                .Select(s => s.Name)
                .ToList();

            foreach (var sub in allSubstances)
            {
                if (!mentioned.Contains(sub.Name))
                {
                    foreach (var alias in sub.Aliases)
                    {
                        if (query.Contains(alias, StringComparison.OrdinalIgnoreCase))
                        {
                            mentioned.Add(sub.Name);
                            break;
                        }
                    }
                }
            }

            if (mentioned.Count < 2)
            {
                foreach (var rule in DeterministicRuleEngine.StorageCompatibilityRules)
                {
                    var (a, b) = rule.Key;
                    if (query.Contains(a) && query.Contains(b))
                    {
                        return new ComplianceFallbackResult
                        {
                            Answer = $"【储存兼容性】{a}与{b}禁止同库储存",
                            RegulationRefs = new List<string> { DeterministicRuleEngine.ExtractRegulationRef(rule.Value) },
                            Quality = "DICTIONARY_HIT"
                        };
                    }
                }
                return null;
            }

            var subA = mentioned[0];
            var subB = mentioned[1];

            foreach (var rule in DeterministicRuleEngine.StorageCompatibilityRules)
            {
                var (a, b) = rule.Key;
                if ((string.Equals(a, subA, StringComparison.OrdinalIgnoreCase) && string.Equals(b, subB, StringComparison.OrdinalIgnoreCase)) ||
                    (string.Equals(a, subB, StringComparison.OrdinalIgnoreCase) && string.Equals(b, subA, StringComparison.OrdinalIgnoreCase)))
                {
                    return new ComplianceFallbackResult
                    {
                        Answer = $"【储存兼容性】{subA}与{subB}禁止同库储存",
                        RegulationRefs = new List<string> { DeterministicRuleEngine.ExtractRegulationRef(rule.Value) },
                        Quality = "DICTIONARY_HIT"
                    };
                }
            }

            var compat = ChemicalSubstanceDatabase.CheckCompatibility(subA, subB);
            if (compat != null)
            {
                var verdict = compat.IsCompatible ? "可同库储存" : "不得同库储存";
                return new ComplianceFallbackResult
                {
                    Answer = $"【储存兼容性】{subA}与{subB}: {verdict}\n原因: {compat.Reason}",
                    RegulationRefs = new List<string> { compat.RegulationRef ?? "GB 15603" },
                    Quality = "DATABASE_HIT"
                };
            }

            return null;
        }
    }

    /// <summary>危险类别查询处理器</summary>
    internal class HazardCategoryHandler : IComplianceQueryHandler
    {
        private static readonly string[] Keywords =
            { "类别", "分类", "属于", "危险", "危害", "特性", "性质", "闪点", "沸点" };

        public ComplianceFallbackResult? TryHandle(string query)
        {
            if (!Keywords.Any(k => query.Contains(k)))
                return null;

            var allSubstances = ChemicalSubstanceDatabase.GetAll();
            foreach (var sub in allSubstances)
            {
                if (query.Contains(sub.Name, StringComparison.OrdinalIgnoreCase))
                {
                    var hazardInfo = sub.HazardCategories.FirstOrDefault()?.Category ?? "未知";
                    var gbStandard = sub.HazardCategories.FirstOrDefault()?.GbStandard ?? "";
                    var answer = $"【危险类别】{sub.Name}: {hazardInfo}";
                    if (!string.IsNullOrEmpty(gbStandard))
                        answer += $"（{gbStandard}）";
                    if (sub.FlashPointC.HasValue)
                        answer += $"\n闪点: {sub.FlashPointC}°C";
                    if (sub.BoilingPointC.HasValue)
                        answer += $"\n沸点: {sub.BoilingPointC}°C";

                    var refs = new List<string>();
                    if (!string.IsNullOrEmpty(gbStandard))
                        refs.Add(gbStandard);
                    foreach (var hc in sub.HazardCategories)
                    {
                        if (!string.IsNullOrEmpty(hc.GbStandard) && !refs.Contains(hc.GbStandard))
                            refs.Add(hc.GbStandard);
                    }

                    return new ComplianceFallbackResult
                    {
                        Answer = answer,
                        RegulationRefs = refs,
                        Quality = "DATABASE_HIT"
                    };
                }
            }

            return null;
        }
    }

    /// <summary>安全距离查询处理器</summary>
    internal class SafetyDistanceHandler : IComplianceQueryHandler
    {
        private static readonly string[] Keywords =
            { "安全距离", "间距", "消防通道", "防火间距", "储罐间距" };

        public ComplianceFallbackResult? TryHandle(string query)
        {
            if (!Keywords.Any(k => query.Contains(k)))
                return null;

            var distances = ChemicalSubstanceDatabase.GetAllSafetyDistances();
            foreach (var sd in distances)
            {
                if (query.Contains(sd.FacilityPair, StringComparison.OrdinalIgnoreCase) ||
                    sd.FacilityPair.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries)
                        .All(part => query.Contains(part.Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    return new ComplianceFallbackResult
                    {
                        Answer = $"【安全距离】{sd.FacilityPair}: 最小安全距离 {sd.MinDistanceMeters}m",
                        RegulationRefs = new List<string> { sd.RegulationRef ?? "GB 50016" },
                        Quality = "DATABASE_HIT"
                    };
                }
            }

            return null;
        }
    }

    // ═══════════════════════════════════════════════════════
    // 确定性规则引擎
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// 确定性规则引擎 — 传统化工安全系统的核心范式。
    /// 
    /// 设计哲学（来自传统系统铁律）：
    ///   规则引擎处理确定性问题（100%准确，零延迟）
    ///   LLM 处理模糊推理问题（覆盖长尾）
    /// 
    /// 执行优先级：
    ///   1. NumericCheck → 规则引擎直接比对标准值，不调 LLM
    ///   2. BooleanCheck → 规则引擎匹配是/否关键词
    ///   3. StorageCompatibility → 查内置禁忌配对表
    ///   4. 以上均未命中 → 转 LLM 推理
    /// </summary>
    public class DeterministicRuleEngine
    {
        // 内置储存禁忌配对表（来自 ChemicalSubstanceDatabase）
        internal static readonly Dictionary<(string, string), string> StorageCompatibilityRules = new()
        {
            // 这些规则来自 GB 15603，100% 确定，不需要 LLM 推理
            [("苯", "丙酮")] = "禁止同库储存 | GB 15603-2022 §4.2.2",
            [("丙酮", "苯")] = "禁止同库储存 | GB 15603-2022 §4.2.2",
            [("甲醇", "硝酸")] = "禁止同库储存 | GB 15603-2022 §4.2.3",
            [("硝酸", "甲醇")] = "禁止同库储存 | GB 15603-2022 §4.2.3",
            [("硫酸", "氢氧化钠")] = "禁止同库储存（酸碱反应）| GB 15603-2022 §4.2.3",
            [("氢氧化钠", "硫酸")] = "禁止同库储存（酸碱反应）| GB 15603-2022 §4.2.3",
        };

        /// <summary>LLM 降级查询责任链 — 新增场景在此添加 handler 即可</summary>
        private readonly List<IComplianceQueryHandler> _handlers = new()
        {
            new StorageCompatibilityHandler(),
            new HazardCategoryHandler(),
            new SafetyDistanceHandler(),
        };

        /// <summary>
        /// 按检查类型分派：规则引擎优先，LLM兜底。
        /// 返回 (直接结果, 是否需要继续走LLM)
        /// </summary>
        public (InspectionItemResult? directResult, bool needsLLM) TryDetermine(
            InspectionItem item, string? userInput = null)
        {
            switch (item.CheckType)
            {
                case InspectionCheckType.NumericCheck:
                    return TryNumericCheck(item, userInput);

                case InspectionCheckType.BooleanCheck:
                    return TryBooleanCheck(item, userInput);

                case InspectionCheckType.AIInference:
                    // 先尝试内置规则匹配
                    var ruleResult = TryStorageRuleMatch(item.Query);
                    if (ruleResult != null)
                        return (ruleResult, false);
                    // 未命中 → 转LLM
                    return (null, true);

                default:
                    return (null, true);
            }
        }

        /// <summary>数值比对：温度≤30℃，实测28℃ → 合规</summary>
        private static (InspectionItemResult?, bool) TryNumericCheck(
            InspectionItem item, string? userInput)
        {
            if (string.IsNullOrWhiteSpace(userInput))
                return (null, true); // 需要用户输入实际值

            // 尝试从用户输入中提取数值
            var number = ExtractNumber(userInput);
            if (number == null)
                return (null, true);

            // 解析标准值（如 "≤30℃" → 30）
            var standard = ParseStandardValue(item.StandardValue);
            if (standard == null)
                return (null, true);

            bool isCompliant = item.StandardValue?.Contains("≤") == true
                ? number <= standard
                : number >= standard;

            var result = new InspectionItemResult
            {
                ItemId = item.ItemId,
                IsCompliant = isCompliant,
                Conclusion = isCompliant
                    ? $"✅ 合规: 实测{number}，标准{item.StandardValue}"
                    : $"❌ 不合规: 实测{number}，标准{item.StandardValue}",
                RegulationRef = item.ExpectedRegulation ?? ""
            };

            return (result, false);
        }

        /// <summary>是/否检查：从用户输入中判断</summary>
        private static (InspectionItemResult?, bool) TryBooleanCheck(
            InspectionItem item, string? userInput)
        {
            if (string.IsNullOrWhiteSpace(userInput))
                return (null, true);

            var lower = userInput.ToLower();
            bool? isCompliant = null;
            string conclusion;

            if (lower.Contains("正常") || lower.Contains("是") || lower.Contains("完好") ||
                lower.Contains("合规") || lower.Contains("通过") || lower.Contains("正常运转") ||
                lower.Contains("没有") && !lower.Contains("没有通过"))
            {
                isCompliant = true;
                conclusion = $"✅ 合规: {item.Query} → {userInput}";
            }
            else if (lower.Contains("异常") || lower.Contains("否") || lower.Contains("损坏") ||
                     lower.Contains("不合规") || lower.Contains("未通过") || lower.Contains("故障"))
            {
                isCompliant = false;
                conclusion = $"❌ 不合规: {item.Query} → {userInput}";
            }
            else
            {
                return (null, true); // 无法判断，转LLM
            }

            var result = new InspectionItemResult
            {
                ItemId = item.ItemId,
                IsCompliant = isCompliant,
                Conclusion = conclusion,
                RegulationRef = item.ExpectedRegulation ?? ""
            };

            return (result, false);
        }

        /// <summary>储存禁忌规则匹配 — 从内置配对表中查找</summary>
        private static InspectionItemResult? TryStorageRuleMatch(string query)
        {
            foreach (var rule in StorageCompatibilityRules)
            {
                var (a, b) = rule.Key;
                if (query.Contains(a) && query.Contains(b))
                {
                    return new InspectionItemResult
                    {
                        IsCompliant = false,
                        Conclusion = $"❌ {a}与{b}禁止同库储存",
                        RegulationRef = rule.Value
                    };
                }
            }
            return null;
        }

        // ═══════════════════════════════════════════════════════
        // LLM 不可用时的合规查询降级路径
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// 尝试用确定性规则回答合规查询（不依赖 LLM）。
        /// 使用责任链模式：信号词门卫 → handler 链 → 首个命中返回。
        /// 覆盖场景：储存兼容性、危化品危险类别、安全距离。
        /// 新增场景只需在 _handlers 列表中添加新的 IComplianceQueryHandler 实现。
        /// </summary>
        /// <returns>确定性回答；null 表示超出规则引擎能力范围</returns>
        public ComplianceFallbackResult? TryHandleComplianceQuery(string userQuery)
        {
            if (string.IsNullOrWhiteSpace(userQuery))
                return null;

            // ── 信号词门卫：拦截完全不相关的输入 ──
            if (!ChemicalSignalGate.Pass(userQuery))
                return null;

            // ── 责任链：按注册顺序依次尝试 handler ──
            foreach (var handler in _handlers)
            {
                var result = handler.TryHandle(userQuery);
                if (result != null)
                    return result;
            }

            return null;
        }

        /// <summary>从规则文本中提取法规引用</summary>
        internal static string ExtractRegulationRef(string ruleText)
        {
            var match = System.Text.RegularExpressions.Regex.Match(ruleText, @"GB\s*\d{4,5}[^|]*");
            return match.Success ? match.Value.Trim() : "GB 15603";
        }

        // ── 辅助 ──

        private static double? ExtractNumber(string input)
        {
            var match = System.Text.RegularExpressions.Regex.Match(input, @"(\d+\.?\d*)");
            if (match.Success && double.TryParse(match.Groups[1].Value, out var val))
                return val;
            return null;
        }

        private static double? ParseStandardValue(string? std)
        {
            if (string.IsNullOrWhiteSpace(std)) return null;
            var match = System.Text.RegularExpressions.Regex.Match(std, @"(\d+\.?\d*)");
            if (match.Success && double.TryParse(match.Groups[1].Value, out var val))
                return val;
            return null;
        }
    }
}
