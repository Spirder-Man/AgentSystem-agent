using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Agent1.Services;

/// <summary>
/// P1-2: 统一的 GB 法规编号工具类。
/// 全链路复用单一正则，消除 OutputSanitizer / ComplianceFactExtractor /
/// PromptSanitizer / OutputValidator / ConclusionVerifier / InspectionModels
/// 中各写各的 GB 正则问题。
/// 
/// 匹配模式示例：
///   GB 30000.7-2013, GB/T 18218-2020, GB30000.7, GB 15603, GB 12345.6-2020
/// </summary>
public static class GbCodeHelper
{
    /// <summary>
    /// 统一的 GB 编号提取正则（含子编号可选，含年份版本可选）。
    /// </summary>
    public static readonly Regex GbCodePattern = new(
        @"GB\s*/?T?\s*\d{4,5}(?:[.\-]\d+(?:\.\d+)?)?(?:\s*-\s*\d{4})?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// 从文本中提取所有 GB 法规编号（去重）。
    /// </summary>
    public static HashSet<string> ExtractGbCodes(string text)
    {
        var codes = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text))
            return codes;

        foreach (Match m in GbCodePattern.Matches(text))
        {
            var normalized = NormalizeGbCode(m.Value);
            if (!string.IsNullOrWhiteSpace(normalized))
                codes.Add(normalized);
        }
        return codes;
    }

    /// <summary>
    /// 标准化 GB 编号格式：移除多余空格，统一为 "GB XXXXX.XX-XXXX" 形式。
    /// </summary>
    public static string NormalizeGbCode(string gbCode)
    {
        if (string.IsNullOrWhiteSpace(gbCode))
            return gbCode ?? "";

        // 移除多余空格
        var normalized = Regex.Replace(gbCode.Trim(), @"\s+", " ");
        // 确保 "GB" 后有一个空格（不破坏 "GB/T" 格式）
        normalized = Regex.Replace(normalized, @"^GB(?!/|\s)", "GB ");
        return normalized;
    }

    /// <summary>
    /// 判断一个 GB 编号是否在允许的白名单中。
    /// 支持前缀匹配（如 "GB 30000.7-2013" 应匹配白名单中的 "GB 30000.7"）。
    /// </summary>
    public static bool IsInWhitelist(string gbNumber, HashSet<string> whitelist)
    {
        if (whitelist.Count == 0)
            return true;

        var normalized = NormalizeGbCode(gbNumber);
        var noSpace = normalized.Replace(" ", "");

        foreach (var w in whitelist)
        {
            var wNormalized = NormalizeGbCode(w);
            var wNoSpace = wNormalized.Replace(" ", "");

            if (normalized.Equals(wNormalized, System.StringComparison.OrdinalIgnoreCase)
                || noSpace.Equals(wNoSpace, System.StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith(wNormalized, System.StringComparison.OrdinalIgnoreCase)
                || wNormalized.StartsWith(normalized, System.StringComparison.OrdinalIgnoreCase)
                || noSpace.StartsWith(wNoSpace, System.StringComparison.OrdinalIgnoreCase)
                || wNoSpace.StartsWith(noSpace, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
