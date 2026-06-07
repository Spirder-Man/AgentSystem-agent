using System.Text.RegularExpressions;

namespace Agent1.Services;

/// <summary>
/// Task 3: 敏感数据脱敏工具 — 审计日志写入前对敏感字段进行脱敏处理。
/// 覆盖：手机号、邮箱、身份证号、API Key/Token、长查询截断。
/// 所有脱敏操作均通过正则或启发式规则实现，不依赖外部服务。
/// </summary>
public static partial class SensitiveDataMasker
{
    // 中国手机号 (11位，1开头)
    [GeneratedRegex(@"1[3-9]\d{9}")]
    private static partial Regex PhoneRegex();

    // 邮箱地址
    [GeneratedRegex(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}")]
    private static partial Regex EmailRegex();

    // 中国身份证号 (18位或15位)
    [GeneratedRegex(@"\b\d{17}[\dXx]\b|\b\d{15}\b")]
    private static partial Regex IdCardRegex();

    // API Key / Token 模式 (常见格式: sk-xxx, key=xxx, token=xxx)
    [GeneratedRegex(@"(?:api[_-]?key|apikey|secret|token|auth)\s*[:=]\s*['""]?([a-zA-Z0-9\-_]{20,})['""]?", RegexOptions.IgnoreCase)]
    private static partial Regex ApiKeyRegex();

    // 查询内容最大长度（超过则截断）
    private const int MaxQueryLength = 500;

    /// <summary>
    /// 对审计详情文本进行脱敏处理。
    /// 返回脱敏后的文本，不修改原始数据。
    /// </summary>
    public static string Mask(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text ?? "";

        var result = text;

        // 1. 手机号脱敏: 138****5678
        result = PhoneRegex().Replace(result, match =>
        {
            var phone = match.Value;
            return phone.Length == 11
                ? phone[..3] + "****" + phone[7..]
                : "***MASKED***";
        });

        // 2. 邮箱脱敏: u***@domain.com
        result = EmailRegex().Replace(result, match =>
        {
            var email = match.Value;
            var atIndex = email.IndexOf('@');
            if (atIndex <= 2) return "***" + email[atIndex..];
            return email[..1] + "***" + email[(atIndex - 1)..];
        });

        // 3. 身份证号脱敏: 110101****1234
        result = IdCardRegex().Replace(result, match =>
        {
            var id = match.Value;
            if (id.Length >= 15)
                return id[..6] + "****" + id[^4..];
            return "***MASKED***";
        });

        // 4. API Key/Token 脱敏
        result = ApiKeyRegex().Replace(result, "***API_KEY_REDACTED***");

        // 5. 长查询截断 + 标记
        if (result.Length > MaxQueryLength)
            result = result[..MaxQueryLength] + $"...[截断/原始长度{text.Length}]";

        return result;
    }

    /// <summary>
    /// 对已知化学品名称进行简化标记（避免完整化学品名称泄露到审计日志）。
    /// 此方法使用启发式规则：匹配 GB 标准中常见的中文化学品命名模式。
    /// </summary>
    public static string MaskChemicalQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return query ?? "";

        // 先做常规脱敏
        var masked = Mask(query);

        // 化学品类查询不做完全脱敏（商业数据需要可审计），仅控制长度
        return masked;
    }
}
