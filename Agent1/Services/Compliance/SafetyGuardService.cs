using System.Text.RegularExpressions;
using Agent1.Config;

namespace Agent1.Services
{
    /// <summary>
    /// [P3 安全加固] 化工合规 Agent 安全卫士 — Prompt 注入检测 + LLM 输出高危断言拦截。
    /// 
    /// 化工场景下错误的安全建议可能造成严重事故，不能仅依赖 LLM 自觉。
    /// 本服务在输入层和输出层各设一道防线，确保危险建议被强制拦截。
    /// </summary>
    public class SafetyGuardService
    {
        // ═══════════════════════════════════
        // 输入检测：Prompt 注入
        // ═══════════════════════════════════

        private static int MaxInputLength => AppConfig.Instance.Safety.MaxInputLength;
        private static int MaxImagePathLength => AppConfig.Instance.Safety.MaxImagePathLength;

        // 注入攻击特征模式
        private static readonly Regex[] InjectionPatterns =
        {
            new(@"忽略.*指令|忽略.*规则|不要.*遵守|别.*管.*规则", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"你.*不是.*专家|你.*不懂|你.*不会", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"密码|密钥|token|api_key|access_key", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"SELECT\s|INSERT\s|DELETE\s|DROP\s|UPDATE\s.*FROM|UNION\s.*SELECT", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"<script|<iframe|javascript:|onerror\s*=", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"system\s*:|assistant\s*:|你的新角色|你现在是", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"忘记你.*说过|删除.*记忆|重置.*对话|清除.*上下文", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        };

        // ═══════════════════════════════════
        // 输出检测：高危断言
        // ═══════════════════════════════════

        // 存储兼容性 — 绝对不能无依据说"可以同库"
        private static readonly Regex[] StorageDangerPatterns =
        {
            new(@"可以同库|可以共存|可以一起[储存存放]|能[够放在一起]", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"允许同库|允许共存|可以混合[储存存放]|混合[储存存放].*安全", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        };

        // 安全距离 — 不能无依据说"0"或"不需要"
        private static readonly Regex[] DistanceDangerPatterns =
        {
            new(@"安全距离[为是]?\s*0|无需安全距离|不需要安全距离|没有安全距离要求", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"防火间距[为是]?\s*0|无需防火间距|不受距离限制", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        };

        // 绝对安全断言 — 化工场景没有绝对安全
        private static readonly Regex[] AbsoluteSafetyPatterns =
        {
            new(@"绝对安全|百分百.*安全|毫无危险|没有任何风险|完全无害", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"肯定不会.*爆炸|绝对不会.*泄漏|永远不会.*着火", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        };

        // GB 编号格式 — 标记疑似不存在的标准
        private static readonly Regex GbNumberPattern = new(
            @"GB\s*/?T?\s*(\d{4,5})[.\-](\d+(?:\.\d+)?)",
            RegexOptions.Compiled);

        /// <summary>
        /// 输入安全验证。返回 (通过, 拦截原因)。
        /// </summary>
        public static (bool safe, string? reason) ValidateInput(string? input, bool isImagePath = false)
        {
            if (string.IsNullOrWhiteSpace(input))
                return (false, "输入为空");

            // 长度限制
            var maxLen = isImagePath ? MaxImagePathLength : MaxInputLength;
            if (input.Length > maxLen)
                return (false, $"输入超出长度限制 ({input.Length}/{maxLen} 字符)");

            // 注入检测
            foreach (var pattern in InjectionPatterns)
            {
                var match = pattern.Match(input);
                if (match.Success)
                    return (false, $"检测到潜在注入攻击: 匹配模式 \"{Trunc(match.Value, 50)}\"");
            }

            return (true, null);
        }

        private static string Trunc(string t, int max)
            => t.Length <= max ? t : t[..max] + "...";

        /// <summary>
        /// 输出安全验证。返回 (通过, 拦截原因列表)。
        /// 不通过的原因会附加到 LLM 输出末尾作为强制警告。
        /// </summary>
        public static (bool safe, List<string> warnings) ValidateOutput(string? llmResponse)
        {
            var warnings = new List<string>();

            if (string.IsNullOrWhiteSpace(llmResponse))
                return (true, warnings);

            // 存储兼容性高危断言检测
            foreach (var pattern in StorageDangerPatterns)
            {
                var match = pattern.Match(llmResponse);
                if (match.Success)
                {
                    warnings.Add($"⚠️ 强制复核: 检测到存储兼容性断言 \"{Trunc(match.Value, 40)}\"。" +
                                 "同库储存判断必须基于具体 GB 标准（GB15603），" +
                                 "请人工核实化学品安全技术说明书 (MSDS) 中的储存禁忌配伍表。");
                }
            }

            // 安全距离高危断言检测
            foreach (var pattern in DistanceDangerPatterns)
            {
                var match = pattern.Match(llmResponse);
                if (match.Success)
                {
                    warnings.Add($"⛔ 强制拦截: 检测到安全距离异常断言 \"{Trunc(match.Value, 40)}\"。" +
                                 "化工设施间必须保持安全距离（依据 GB50160/GB50016），" +
                                 "请查阅具体条款后人工确认。");
                }
            }

            // 绝对安全断言检测
            foreach (var pattern in AbsoluteSafetyPatterns)
            {
                var match = pattern.Match(llmResponse);
                if (match.Success)
                {
                    warnings.Add($"⚠️ 注意: 检测到绝对化安全表述 \"{Trunc(match.Value, 40)}\"。" +
                                 "化工场景不存在绝对安全，请补充风险说明和前提条件。");
                }
            }

            return (warnings.Count == 0, warnings);
        }

        /// <summary>
        /// 提取输出中的 GB 编号列表，供后续 ConclusionVerifier 验证。
        /// </summary>
        public static List<string> ExtractGbNumbers(string llmResponse)
        {
            var numbers = new List<string>();
            foreach (Match m in GbNumberPattern.Matches(llmResponse))
                numbers.Add(m.Value.Trim());
            return numbers.Distinct().ToList();
        }
    }
}
