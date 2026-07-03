using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Agent1.Models;

namespace Agent1.Services
{
    /// <summary>
    /// 双通道解耦架构 — 事实提取器。
    /// 从 [KernelFunction] 工具返回的文本中提取结构化事实（法规编号、类别、判定等），
    /// 提供正则解析 + AsyncLocal 降级的双路径。
    /// </summary>
    public static class ComplianceFactExtractor
    {
        // ── 通用正则 ──

        /// <summary>从工具结果中提取 [REGULATIONS:...] 标签中的法规编号</summary>
        private static readonly Regex RegulationsTagRegex = new(
            @"\[REGULATIONS:\s*([^\]]+)\]",
            RegexOptions.Compiled);

        /// <summary>提取单独的 GB 编号，如 GB 30000.7、GB30000.7-2013</summary>
        private static readonly Regex GbNumberRegex = new(
            @"GB\s*/?T?\s*\d{4,5}(?:[.\-]\d+(?:\.\d+)?)?(?:\s*-\s*\d{4})?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>提取「物质名」危险类别: ... 模式</summary>
        private static readonly Regex HazardCategoryRegex = new(
            @"「(.+?)」危险类别[：:]\s*(.+?)(?:\s*\[判定|$)",
            RegexOptions.Compiled);

        /// <summary>提取合规判定标记</summary>
        private static readonly Regex VerdictRegex = new(
            @"\[判定[：:]\s*is_compliant\s*=\s*(\w+)\]",
            RegexOptions.Compiled);

        /// <summary>提取物质名参数</summary>
        private static readonly Regex SubstanceNameRegex = new(
            @"substanceName\s*=\s*""?([^,""]+)""?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>提取物质对参数 (A/B 或 substanceA/substanceB)</summary>
        private static readonly Regex SubstancePairRegex = new(
            @"(?:substance)?[AB]\s*=\s*""?([^,""]+)""?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>提取设施类型参数</summary>
        private static readonly Regex FacilityTypeRegex = new(
            @"facilityType\s*=\s*""?([^,""]+)""?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>提取合规判定结果的文本描述</summary>
        private static readonly Regex ComplianceResultRegex = new(
            @"(不得同库|严禁同库|禁止.*同库|可同库|可以同库|一般可同库|同属.*可同库)",
            RegexOptions.Compiled);

        /// <summary>提取化学品属性查询结果（前缀后内容）</summary>
        private static readonly Regex PropertyResultRegex = new(
            @"危险特性[：:]\s*(.+?)(?:\r?\n|$)",
            RegexOptions.Compiled);

        /// <summary>提取阈值数值</summary>
        private static readonly Regex ThresholdRegex = new(
            @"临界量[为是]?\s*(\d+)\s*吨",
            RegexOptions.Compiled);

        /// <summary>提取法规版本</summary>
        private static readonly Regex VersionRegex = new(
            @"(?:现行|最新|有效)?版本[为是]?\s*(GB\s*\d+(?:[.\-]\d+)?(?:\s*-\s*\d+)?)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // ── 公共入口 ──

        /// <summary>
        /// 从工具调用记录中提取结构化事实。
        /// </summary>
        /// <param name="toolCalls">SK Auto FC 产生的工具调用记录</param>
        /// <param name="isInfoQuery">是否为信息查询意图</param>
        public static ExtractedFacts Extract(List<FunctionCallRecord> toolCalls, bool isInfoQuery)
        {
            var facts = new ExtractedFacts { IsInfoQuery = isInfoQuery };

            if (toolCalls == null || toolCalls.Count == 0)
                return facts;

            foreach (var tc in toolCalls)
            {
                if (tc.Result == null)
                    continue;

                facts.RawToolOutputs.Add(tc.Result);

                // 提取法规编号
                var regs = ExtractRegulations(tc.Result);
                foreach (var reg in regs)
                {
                    if (!string.IsNullOrWhiteSpace(reg))
                        facts.RegulationRefs.Add(reg.Trim());
                }

                // 按工具类型分发解析
                switch (tc.FunctionName)
                {
                    case "CheckHazardCategory":
                        ParseHazardCategory(tc, facts);
                        break;
                    case "CheckStorageCompatibility":
                        ParseStorageCompatibility(tc, facts);
                        break;
                    case "GetSafetyDistance":
                        ParseSafetyDistance(tc, facts);
                        break;
                    case "GetMajorHazardThreshold":
                        ParseThreshold(tc, facts);
                        break;
                    case "LookupChemicalProperties":
                        ParseChemicalProperties(tc, facts);
                        break;
                    case "CheckRegulationVersion":
                        ParseRegulationVersion(tc, facts);
                        break;
                    default:
                        // 未知工具：仅做通用法规编号提取（已在上方完成）
                        break;
                }
            }

            // 汇总合规判定
            if (facts.ComplianceVerdicts.Count > 0)
            {
                facts.OverallComplianceVerdict = facts.ComplianceVerdicts.Values
                    .FirstOrDefault(v => v.Contains("不得") || v.Contains("严禁") || v.Contains("禁止"))
                    ?? facts.ComplianceVerdicts.Values.FirstOrDefault();
            }

            return facts;
        }

        // ── 通用提取方法 ──

        /// <summary>从工具结果文本中提取所有法规编号</summary>
        public static List<string> ExtractRegulations(string resultText)
        {
            var regs = new List<string>();

            // 路径1: [REGULATIONS:...] 标签
            var tagMatch = RegulationsTagRegex.Match(resultText);
            if (tagMatch.Success)
            {
                var innerText = tagMatch.Groups[1].Value;
                foreach (Match m in GbNumberRegex.Matches(innerText))
                    regs.Add(m.Value.Trim());
            }

            // 路径2: 自由文本中的 GB 编号（如工具未使用 MarkQuality 标签）
            if (regs.Count == 0)
            {
                foreach (Match m in GbNumberRegex.Matches(resultText))
                {
                    var val = m.Value.Trim();
                    if (!regs.Contains(val))
                        regs.Add(val);
                }
            }

            return regs;
        }

        /// <summary>从参数文本中提取物质名</summary>
        private static string ExtractSubstanceName(FunctionCallRecord tc)
        {
            // 从 Arguments 字段提取，如 "substanceName=苯"
            var m = SubstanceNameRegex.Match(tc.Arguments);
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }

        /// <summary>从参数文本中提取物质对</summary>
        private static (string a, string b) ExtractSubstancePair(FunctionCallRecord tc)
        {
            var matches = SubstancePairRegex.Matches(tc.Arguments);
            var a = matches.Count > 0 ? matches[0].Groups[1].Value.Trim() : "";
            var b = matches.Count > 1 ? matches[1].Groups[1].Value.Trim() : "";
            return (a, b);
        }

        /// <summary>从参数文本中提取设施类型</summary>
        private static string ExtractFacilityType(FunctionCallRecord tc)
        {
            var m = FacilityTypeRegex.Match(tc.Arguments);
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }

        // ── 工具特定解析方法 ──

        private static void ParseHazardCategory(FunctionCallRecord tc, ExtractedFacts facts)
        {
            var substance = ExtractSubstanceName(tc);
            if (string.IsNullOrWhiteSpace(substance))
                return;

            var m = HazardCategoryRegex.Match(tc.Result ?? "");
            if (m.Success)
            {
                var cat = m.Groups[2].Value.Trim();
                if (!string.IsNullOrWhiteSpace(cat))
                {
                    facts.HazardCategories[substance] = cat;
                    return;
                }
            }

            // 降级：从结果文本中提取类别描述（第一行非标签内容）
            var cleanText = RegulationsTagRegex.Replace(tc.Result ?? "", "").Trim();
            var firstLine = cleanText.Split('\n', '\r').FirstOrDefault()?.Trim();
            if (!string.IsNullOrWhiteSpace(firstLine) && !firstLine.StartsWith("["))
                facts.HazardCategories[substance] = firstLine;
        }

        private static void ParseStorageCompatibility(FunctionCallRecord tc, ExtractedFacts facts)
        {
            var (a, b) = ExtractSubstancePair(tc);
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
                return;

            var key = $"{a}|{b}";
            var result = tc.Result ?? "";

            // 判定标记
            var verdictMatch = VerdictRegex.Match(result);
            if (verdictMatch.Success)
            {
                var isCompliant = verdictMatch.Groups[1].Value;
                var verdictText = isCompliant.Equals("true", StringComparison.OrdinalIgnoreCase)
                    ? "可同库储存"
                    : "不得同库储存";

                // 尝试提取更精确的判定文本
                var complianceMatch = ComplianceResultRegex.Match(result);
                if (complianceMatch.Success)
                    verdictText = complianceMatch.Groups[1].Value;

                facts.ComplianceVerdicts[key] = verdictText;
                return;
            }

            // 降级：从结果文本中提取第一行作为判定
            var cleanText = RegulationsTagRegex.Replace(result, "").Trim();
            var firstLine = cleanText.Split('\n', '\r').FirstOrDefault()?.Trim();
            if (!string.IsNullOrWhiteSpace(firstLine) && firstLine.Length < 30)
                facts.ComplianceVerdicts[key] = firstLine;
        }

        private static void ParseSafetyDistance(FunctionCallRecord tc, ExtractedFacts facts)
        {
            var facility = ExtractFacilityType(tc);
            if (string.IsNullOrWhiteSpace(facility))
                facility = "指定设施";

            var result = tc.Result ?? "";
            // 提取距离数值
            var distMatch = Regex.Match(result, @"(\d+)\s*米");
            if (distMatch.Success)
            {
                facts.SafetyDistances[facility] = $"{distMatch.Groups[1].Value}米";
                return;
            }

            // 降级：提取第一行文本
            var cleanText = RegulationsTagRegex.Replace(result, "").Trim();
            var firstLine = cleanText.Split('\n', '\r').FirstOrDefault()?.Trim();
            if (!string.IsNullOrWhiteSpace(firstLine) && firstLine.Length < 60)
                facts.SafetyDistances[facility] = firstLine;
        }

        private static void ParseThreshold(FunctionCallRecord tc, ExtractedFacts facts)
        {
            var substance = ExtractSubstanceName(tc);
            if (string.IsNullOrWhiteSpace(substance))
            {
                // 尝试从 query 中提取物质名（备选）
                var match = SubstanceNameRegex.Match(tc.Arguments);
                if (!match.Success)
                    return;
                substance = match.Groups[1].Value.Trim();
            }

            var result = tc.Result ?? "";
            var m = ThresholdRegex.Match(result);
            if (m.Success)
            {
                facts.Thresholds[substance] = $"{m.Groups[1].Value}吨";
                return;
            }

            // 降级
            var cleanText = RegulationsTagRegex.Replace(result, "").Trim();
            var firstLine = cleanText.Split('\n', '\r').FirstOrDefault()?.Trim();
            if (!string.IsNullOrWhiteSpace(firstLine))
                facts.Thresholds[substance] = firstLine;
        }

        private static void ParseChemicalProperties(FunctionCallRecord tc, ExtractedFacts facts)
        {
            var substance = ExtractSubstanceName(tc);
            if (string.IsNullOrWhiteSpace(substance))
                return;

            var result = tc.Result ?? "";

            // 尝试提取危险特性
            var propMatch = PropertyResultRegex.Match(result);
            if (propMatch.Success)
            {
                facts.ChemicalProperties[substance] = propMatch.Groups[1].Value.Trim();
                return;
            }

            // 降级
            var cleanText = RegulationsTagRegex.Replace(result, "").Trim();
            var firstLine = cleanText.Split('\n', '\r').FirstOrDefault()?.Trim();
            if (!string.IsNullOrWhiteSpace(firstLine) && firstLine.Length < 100)
                facts.ChemicalProperties[substance] = firstLine;
        }

        private static void ParseRegulationVersion(FunctionCallRecord tc, ExtractedFacts facts)
        {
            var result = tc.Result ?? "";

            // 提取 GB 编号
            var gbMatch = GbNumberRegex.Match(result);
            var standard = gbMatch.Success ? gbMatch.Value.Trim() : "指定标准";

            // 提取版本
            var versionMatch = VersionRegex.Match(result);
            if (versionMatch.Success)
            {
                facts.RegulationVersions[standard] = versionMatch.Groups[1].Value.Trim();
                return;
            }

            // 降级：提取所有 GB 编号作为版本信息
            var allGbs = GbNumberRegex.Matches(result);
            if (allGbs.Count > 1)
            {
                facts.RegulationVersions[standard] = allGbs[1].Value.Trim();
            }
            else if (allGbs.Count == 1)
            {
                facts.RegulationVersions[standard] = allGbs[0].Value.Trim();
            }
        }
    }
}
