using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Agent1.Services
{
    /// <summary>
    /// 双通道解耦架构 — 输出消毒器（硬拦截兜底）。
    /// 在 LLM 输出文本中扫描所有法规引用模式，
    /// 与 ExtractedFacts.RegulationRefs 白名单逐一比对，
    /// 不在白名单中的编号和条款号直接硬删除。
    /// 这是 LLM 输出到用户之前的最后一道防线。
    /// </summary>
    public static class OutputSanitizer
    {
        /// <summary>匹配 GB 编号模式（含变体）</summary>
        private static readonly Regex GbNumberRegex = new(
            @"GB\s*/?T?\s*\d{4,5}(?:[.\-]\d+(?:\.\d+)?)?(?:\s*-\s*\d{4})?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>[P1 FIX] Bug C: 匹配 GB 编号+年代版本号的完整模式（如 GB 15603-2023）</summary>
        private static readonly Regex GbWithYearRegex = new(
            @"(GB\s*/?T?\s*\d{4,5}(?:[.\-]\d+(?:\.\d+)?)?)\s*-\s*(\d{4})",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>匹配条款号模式</summary>
        private static readonly Regex ClauseRegex = new(
            @"第\d+(?:\.\d+)*条",
            RegexOptions.Compiled);

        /// <summary>匹配"GB 编号 + 条款号"的复合模式</summary>
        private static readonly Regex GbWithClauseRegex = new(
            @"(GB\s*/?T?\s*\d{4,5}(?:[.\-]\d+(?:\.\d+)?)?(?:\s*-\s*\d{4})?)\s*第\d+(?:\.\d+)*条",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>匹配法规全称中的 GB 编号模式（如"GB 30000.14 氧化性液体"）</summary>
        private static readonly Regex GbWithDescriptionRegex = new(
            @"(GB\s*/?T?\s*\d{4,5}(?:[.\-]\d+(?:\.\d+)?)?(?:\s*-\s*\d{4})?)\s*《[^》]+》",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// 消毒 LLM 输出 —— 移除所有不在白名单中的法规引用。
        /// </summary>
        /// <param name="llmOutput">LLM 生成的原始文本</param>
        /// <param name="regulationWhitelist">工具返回的法规编号白名单</param>
        /// <returns>消毒后的输出文本</returns>
        public static string Sanitize(string llmOutput, IReadOnlyList<string>? regulationWhitelist)
        {
            if (string.IsNullOrWhiteSpace(llmOutput))
                return llmOutput ?? "";

            var whitelist = NormalizeWhitelist(regulationWhitelist);
            var sanitized = llmOutput;

            // 1. 处理"GB编号 第X条"复合模式
            sanitized = GbWithClauseRegex.Replace(sanitized, match =>
            {
                var gbPart = match.Groups[1].Value;
                var normalized = NormalizeGbNumber(gbPart);
                return IsInWhitelist(normalized, whitelist)
                    ? match.Value  // 在白名单中 → 保留原文
                    : "[已移除未验证的法规引用]";
            });

            // 2. 处理"GB编号《法规全称》"复合模式
            sanitized = GbWithDescriptionRegex.Replace(sanitized, match =>
            {
                var gbPart = match.Groups[1].Value;
                var normalized = NormalizeGbNumber(gbPart);
                return IsInWhitelist(normalized, whitelist)
                    ? match.Value
                    : "[已移除未验证的法规引用]";
            });

            // 3. 处理独立的 GB 编号
            sanitized = GbNumberRegex.Replace(sanitized, match =>
            {
                var normalized = NormalizeGbNumber(match.Value);
                if (IsInWhitelist(normalized, whitelist))
                {
                    // 在白名单中 → 修正为标准格式
                    return NormalizeGbNumber(match.Value);
                }
                // 不在白名单中 → 硬删除
                return "";
            });

            // 4. 处理孤立的条款号（没有了前面的 GB 编号做上下文的）
            sanitized = ClauseRegex.Replace(sanitized, match =>
            {
                // 如果条款号后面紧跟着不可验证内容，一起移除
                return "";
            });

            // 5. 清理多余空格和空行
            sanitized = Regex.Replace(sanitized, @"[ \t]{2,}", " ");
            sanitized = Regex.Replace(sanitized, @"\n{3,}", "\n\n");
            sanitized = sanitized.Trim();

            // [P1 FIX] Bug C: 6. 硬校验 GB 编号的年代版本号
            sanitized = ValidateVersionYear(sanitized);

            return sanitized;
        }

        /// <summary>标准化 GB 编号（统一格式为 "GB XXXXX.XX-XXXX"）</summary>
        public static string NormalizeGbNumber(string gbNumber)
        {
            if (string.IsNullOrWhiteSpace(gbNumber))
                return gbNumber ?? "";

            // 移除多余空格
            var normalized = Regex.Replace(gbNumber.Trim(), @"\s+", " ");
            // 确保 "GB" 后有一个空格（不破坏 "GB/T" 格式）
            normalized = Regex.Replace(normalized, @"^GB(?!/|\s)", "GB ");
            // 统一连字符
            normalized = normalized.Replace("\u2013", "-").Replace("\u2014", "-");

            return normalized;
        }

        /// <summary>标准化白名单</summary>
        private static HashSet<string> NormalizeWhitelist(IReadOnlyList<string>? whitelist)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (whitelist == null)
                return set;

            foreach (var reg in whitelist)
            {
                if (!string.IsNullOrWhiteSpace(reg))
                {
                    // 也加入标准化版本和无空格版本
                    var normalized = NormalizeGbNumber(reg);
                    set.Add(normalized);
                    set.Add(normalized.Replace(" ", ""));
                }
            }
            return set;
        }

        /// <summary>检查某个 GB 编号是否在白名单中</summary>
        private static bool IsInWhitelist(string gbNumber, HashSet<string> whitelist)
        {
            if (whitelist.Count == 0)
                return false; // 空白名单 → 不存在合法引用

            var normalized = NormalizeGbNumber(gbNumber);
            var noSpace = normalized.Replace(" ", "");

            // 精确匹配
            if (whitelist.Contains(normalized) || whitelist.Contains(noSpace))
                return true;

            // 前缀匹配：如 "GB 30000.7-2013" 应匹配白名单中的 "GB 30000.7"
            foreach (var w in whitelist)
            {
                if (normalized.StartsWith(w, StringComparison.OrdinalIgnoreCase) ||
                    w.StartsWith(normalized, StringComparison.OrdinalIgnoreCase) ||
                    noSpace.StartsWith(w.Replace(" ", ""), StringComparison.OrdinalIgnoreCase) ||
                    w.Replace(" ", "").StartsWith(noSpace, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// [P1 FIX] Bug C: 校验输出中所有 GB 编号的年代版本号。
        /// 将 LLM 生成的版本号（如 -2023）替换为 ChemicalSubstanceDatabase 中记录的
        /// 正确版本号（如 -2022）。若数据库中无此法规记录，则剥离年份后缀。
        /// </summary>
        public static string ValidateVersionYear(string sanitized)
        {
            if (string.IsNullOrWhiteSpace(sanitized))
                return sanitized;

            return GbWithYearRegex.Replace(sanitized, match =>
            {
                var gbNumber = match.Groups[1].Value; // 如 "GB 15603"
                var yearText = match.Groups[2].Value; // 如 "2023"

                // 规范化 GB 编号用于数据库查询
                var normalizedGb = NormalizeGbNumber(gbNumber).Replace(" ", "");

                try
                {
                    var regVersion = ChemicalSubstanceDatabase.GetRegulationVersion(normalizedGb);
                    if (regVersion != null)
                    {
                        var dbYear = ExtractYear(regVersion.CurrentVersion);
                        if (dbYear.HasValue && int.TryParse(yearText, out var llmYear))
                        {
                            if (llmYear == dbYear.Value)
                            {
                                // 年份正确 → 保留原文
                                return match.Value;
                            }
                            // 年份错误 → 强制替换为数据库版本
                            Console.WriteLine($"      🔧 版本校正: {gbNumber}-{yearText} → {gbNumber}-{dbYear.Value} (数据库: {regVersion.CurrentVersion})");
                            return $"{gbNumber}-{dbYear.Value}";
                        }
                    }

                    // 数据库中无此法规记录 → 剥离年份后缀，只保留法规编号
                    Console.WriteLine($"      🔧 剥离未知版本: {match.Value} → {gbNumber} (数据库无记录)");
                    return gbNumber;
                }
                catch
                {
                    // 数据库查询异常 → 安全处理：剥离年份
                    return gbNumber;
                }
            });
        }

        /// <summary>从版本字符串（如 "现行2022版"）中提取年份</summary>
        private static int? ExtractYear(string versionText)
        {
            if (string.IsNullOrWhiteSpace(versionText))
                return null;

            var yearMatch = Regex.Match(versionText, @"\b(\d{4})\b");
            if (yearMatch.Success && int.TryParse(yearMatch.Groups[1].Value, out var year)
                && year >= 1990 && year <= 2099)
                return year;

            return null;
        }
    }
}
