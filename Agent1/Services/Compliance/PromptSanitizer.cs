using System.Text.RegularExpressions;

namespace Agent1.Services
{
    /// <summary>
    /// 双通道解耦架构 — Prompt 消毒器。
    /// 在 LLM 看到工具结果之前，剥离所有法规编号（GB 模式、[REGULATIONS:] 标签等）。
    /// 从源头杜绝 LLM 接触法规编号，使其无法编造。
    /// </summary>
    public static class PromptSanitizer
    {
        /// <summary>匹配 [REGULATIONS:...] 整标签</summary>
        private static readonly Regex RegulationsTagRegex = new(
            @"\[REGULATIONS:\s*[^\]]*\]\s*",
            RegexOptions.Compiled);

        /// <summary>匹配 [判定:...] 标签</summary>
        private static readonly Regex VerdictTagRegex = new(
            @"\[判定[：:][^\]]*\]\s*",
            RegexOptions.Compiled);

        // P1-2: 统一使用 GbCodeHelper.GbCodePattern，替代私有 GbStandaloneRegex
        private static Regex GbStandaloneRegex => GbCodeHelper.GbCodePattern;

        /// <summary>匹配条款号模式（如"第3.2条"、"第5.3.2条"）</summary>
        private static readonly Regex ClauseRegex = new(
            @"第\d+(?:\.\d+)*条",
            RegexOptions.Compiled);

        /// <summary>
        /// 消毒工具结果文本 —— 剥离法规编号，保留语义信息。
        /// </summary>
        /// <param name="toolResult">原始工具结果文本</param>
        /// <returns>消毒后的描述性文本</returns>
        public static string SanitizeToolResult(string toolResult)
        {
            if (string.IsNullOrWhiteSpace(toolResult))
                return toolResult ?? "";

            var sanitized = toolResult;

            // 1. 移除 [REGULATIONS:...] 标签（含前缀换行）
            sanitized = RegulationsTagRegex.Replace(sanitized, "");

            // 2. 移除 [判定:...] 标签
            sanitized = VerdictTagRegex.Replace(sanitized, "");

            // 3. 移除独立的 GB 编号
            sanitized = GbStandaloneRegex.Replace(sanitized, "");

            // 4. 移除条款号
            sanitized = ClauseRegex.Replace(sanitized, "");

            // 5. 清理多余空格和空行
            sanitized = Regex.Replace(sanitized, @"\n{3,}", "\n\n");
            sanitized = Regex.Replace(sanitized, @"^[ \t]+|[ \t]+$", "", RegexOptions.Multiline);
            sanitized = sanitized.Trim();

            return sanitized;
        }

        /// <summary>
        /// 消毒完整的 System Role prompt —— 移除要求 LLM 输出法规编号的指令，
        /// 并注入版本约束（禁止 LLM 自行推断法规年代版本号）。
        /// </summary>
        /// <param name="systemPrompt">原始 System Role prompt</param>
        /// <returns>消毒后的 prompt（不含要求法规引用的指令）</returns>
        public static string SanitizeSystemPrompt(string systemPrompt)
        {
            if (string.IsNullOrWhiteSpace(systemPrompt))
                return systemPrompt ?? "";

            var sanitized = systemPrompt;

            // 移除要求引用法规编号的指令行
            sanitized = Regex.Replace(sanitized,
                @"【法规依据】[^\n]*",
                "", RegexOptions.Compiled);

            // 移除"引用具体标准编号"类指令
            sanitized = Regex.Replace(sanitized,
                @"引用具体标准编号\+条款",
                "引用已知事实中的法规依据",
                RegexOptions.Compiled);

            // 替换 output format 中的法规依据为专业解读
            sanitized = Regex.Replace(sanitized,
                @"【法规依据】",
                "【专业解读】",
                RegexOptions.Compiled);

            // [P1 FIX] Bug C: 注入版本约束 —— 禁止 LLM 推断法规的年代版本号
            // 法规版本号（如 GB 15603-2022 中的 2022）必须由系统从 ChemicalSubstanceDatabase
            // 中查询。LLM 只负责输出法规编号（如 GB 15603），不得追加年份后缀。
            if (!sanitized.Contains("禁止输出法规年代版本号"))
            {
                sanitized += "\n\n【合规约束】法规版本号（如 -2022、-2018）由系统从数据库自动查询获取。" +
                    "你只输出法规编号（如 GB 15603、GB 30000.7），禁止在编号后追加年代版本号（如 -2023）。" +
                    "若需引用法规版本信息，请使用工具查询，不要自行推断。";
            }

            return sanitized;
        }
    }
}
