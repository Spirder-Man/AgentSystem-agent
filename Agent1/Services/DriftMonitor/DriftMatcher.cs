using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Agent1.Services.DriftMonitor;

/// <summary>
/// 认知漂移监测·断言匹配器 —— 测量仪的核心比对逻辑（纯函数，可单测）。
///
/// 测量哲学（可证伪性）：只对"能证伪"的断言下结论。
///   · 从锚点基准值中抽取"强标记"（端口数字 / 文件路径 / 大写标识符）
///   · 文本命中全部强标记 → match=1；命中部分 → 0.5；零命中 → 0
///   · 无强标记的锚点（纯描述性）→ 不产生断言（无法证伪，避免测量噪音）
/// </summary>
public static class DriftMatcher
{
    // 端口/关键数字（3-5 位，避开年份/个位数量）
    private static readonly Regex PortToken = new(@"\b\d{3,5}\b", RegexOptions.Compiled);

    // 文件路径/文件名（含扩展名）
    private static readonly Regex PathToken = new(
        @"[A-Za-z0-9_\-/.\\]+\.(?:cs|json|sql|ts|vue|md|db|txt|ps1|sh|yml|yaml|html|env)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // 大写标识符（配置键/协议/环境变量/缩写，≥3 字符）
    private static readonly Regex UpperToken = new(@"\b[A-Z][A-Z0-9_]{2,}\b", RegexOptions.Compiled);

    // 下划线标识符（snake_case 表名/字段名，如 audit_logs、chain_hash——含下划线才算，避免误抓普通词）
    private static readonly Regex SnakeToken = new(@"\b[a-z][a-z0-9]*_[a-z0-9_]{2,}\b", RegexOptions.Compiled);

    /// <summary>
    /// 文本规范化：全角→半角、大写→小写、去空白。
    /// 比对前双方都必须经过本规范化（大小写/全半角/空格差异不算漂移）。
    /// </summary>
    public static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c)) continue;
            // 全角字母数字转半角（0xFF01-0xFF5E 映射到 0x21-0x7E）并小写化
            var code = (int)c;
            if (code >= 0xFF01 && code <= 0xFF5E)
                sb.Append(char.ToLowerInvariant((char)(code - 0xFEE0)));
            else if (c == '\u3000') // 全角空格
                continue;
            else
                sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>
    /// 从锚点基准值中抽取强标记 token。
    /// 返回规范化后的 token 列表（全部小写/半角，与文本侧同构，Ordinal 可比）。
    /// 强标记来源：端口数字 / 文件路径 / 大写标识符 / 下划线表名。
    /// </summary>
    public static List<string> ExtractStrongTokens(string canonicalValue)
    {
        if (string.IsNullOrWhiteSpace(canonicalValue))
            return new List<string>();

        var tokens = new List<string>();
        foreach (Match m in PortToken.Matches(canonicalValue))
            tokens.Add(m.Value);
        foreach (Match m in PathToken.Matches(canonicalValue))
            tokens.Add(Normalize(m.Value));
        foreach (Match m in UpperToken.Matches(canonicalValue))
            tokens.Add(Normalize(m.Value)); // 统一小写——文本侧已规范化，Ordinal 比对必须同侧
        foreach (Match m in SnakeToken.Matches(canonicalValue))
            tokens.Add(m.Value);
        return tokens.Distinct().ToList();
    }

    /// <summary>
    /// 锚点是否可证伪（含至少一个强标记）。
    /// 不可证伪的锚点不产生断言——测量仪只测能测的。
    /// </summary>
    public static bool IsFalsifiable(DriftAnchor anchor)
        => ExtractStrongTokens(anchor.CanonicalValue).Count > 0;

    /// <summary>
    /// 匹配打分：文本 vs 锚点基准值。
    /// 返回 0（全漂移）/ 0.5（部分漂移）/ 1（一致）。
    /// 锚点不可证伪时返回 1（提及即视为一致，不制造噪音）。
    /// </summary>
    public static double MatchScore(string text, string canonicalValue)
    {
        var tokens = ExtractStrongTokens(canonicalValue);
        if (tokens.Count == 0)
            return 1.0;

        var normalizedText = Normalize(text);
        var hits = tokens.Count(t => normalizedText.Contains(t, StringComparison.Ordinal));

        if (hits == tokens.Count)
            return 1.0;
        if (hits > 0)
            return 0.5;
        return 0.0;
    }

    /// <summary>从文本中提取命中的强标记（用于明细展示 actual 列）</summary>
    public static List<string> ExtractMentionedTokens(string text, string canonicalValue)
    {
        var tokens = ExtractStrongTokens(canonicalValue);
        if (tokens.Count == 0)
            return new List<string>();
        var normalizedText = Normalize(text);
        return tokens.Where(t => normalizedText.Contains(t, StringComparison.Ordinal)).ToList();
    }
}
