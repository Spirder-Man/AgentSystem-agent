using System;
using System.Collections.Generic;
using System.Linq;
using Agent1.Models;

namespace Agent1.Services.Orchestration
{
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
        private static readonly Dictionary<(string, string), string> StorageCompatibilityRules = new()
        {
            // 这些规则来自 GB 15603，100% 确定，不需要 LLM 推理
            [("苯", "丙酮")] = "禁止同库储存 | GB 15603-2022 §4.2.2",
            [("丙酮", "苯")] = "禁止同库储存 | GB 15603-2022 §4.2.2",
            [("甲醇", "硝酸")] = "禁止同库储存 | GB 15603-2022 §4.2.3",
            [("硝酸", "甲醇")] = "禁止同库储存 | GB 15603-2022 §4.2.3",
            [("硫酸", "氢氧化钠")] = "禁止同库储存（酸碱反应）| GB 15603-2022 §4.2.3",
            [("氢氧化钠", "硫酸")] = "禁止同库储存（酸碱反应）| GB 15603-2022 §4.2.3",
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
