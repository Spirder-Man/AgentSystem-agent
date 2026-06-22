using Serilog.Core;
using Serilog.Events;

namespace Agent1.Services.Logging.Filters;

/// <summary>
/// 敏感关键词日志过滤器：拦截包含密码/密钥/token 等敏感字的日志事件。
/// 在 Serilog Pipeline 中执行，被拦截的日志不会到达任何 Sink。
/// 
/// 注意：这是日志层的第二道防线。第一道防线是业务层的 SensitiveDataMasker（脱敏），
/// 本过滤器确保即使脱敏层被绕过，敏感信息也不会写入日志文件。
/// </summary>
public class KeywordLogFilter : ILogEventFilter
{
    private static readonly HashSet<string> SensitiveKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "密码", "passwd",
        "secret", "密钥",
        "token", "令牌",
        "api_key", "apikey",
        "authorization",
        "connectionstring",
        "privatekey",
    };

    /// <summary>
    /// 检查日志事件是否应被过滤。
    /// 返回 true 表示保留（通过），false 表示丢弃。
    /// </summary>
    public bool IsEnabled(LogEvent logEvent)
    {
        // 检查 MessageTemplate 文本
        var messageText = logEvent.MessageTemplate.Text;
        if (ContainsSensitiveKeyword(messageText))
            return false;

        // 检查所有属性值（包括 Enricher 注入的字段）
        foreach (var property in logEvent.Properties)
        {
            if (property.Value is ScalarValue sv && sv.Value is string strValue)
            {
                if (ContainsSensitiveKeyword(strValue))
                    return false;
            }
        }

        return true;
    }

    private static bool ContainsSensitiveKeyword(string text)
    {
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var keyword in SensitiveKeywords)
        {
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
