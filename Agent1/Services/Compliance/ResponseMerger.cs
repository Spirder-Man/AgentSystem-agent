using System;
using System.Text.RegularExpressions;

namespace Agent1.Services
{
    /// <summary>
    /// 双通道解耦架构 — 响应合并器。
    /// 将确定性事实通道输出与 LLM 解释通道输出合并为最终响应。
    /// </summary>
    public static class ResponseMerger
    {
        /// <summary>
        /// 合并事实通道和解释通道的输出。
        /// </summary>
        /// <param name="factOutput">FactAssembler 确定性渲染的事实文本</param>
        /// <param name="llmExplanation">LLM 生成的解释/建议文本（已过 OutputSanitizer）</param>
        /// <returns>合并后的最终输出</returns>
        public static string Merge(string factOutput, string llmExplanation)
        {
            if (string.IsNullOrWhiteSpace(factOutput))
                return llmExplanation ?? "";

            if (string.IsNullOrWhiteSpace(llmExplanation))
                return factOutput;

            // 如果 LLM 输出已经包含类似的事实内容（评测路径），不重复合并
            var llmCleaned = CleanLlmOutput(llmExplanation);

            return $"{factOutput}\n\n{llmCleaned}";
        }

        /// <summary>
        /// 清理 LLM 输出中可能与事实通道重复的内容。
        /// </summary>
        private static string CleanLlmOutput(string llmOutput)
        {
            if (string.IsNullOrWhiteSpace(llmOutput))
                return llmOutput ?? "";

            // 移除 LLM 输出开头的重复分隔线
            var cleaned = Regex.Replace(llmOutput,
                @"^[━═]+.*?[━═]+\s*", "", RegexOptions.Multiline);

            // 移除可能残留的空行
            cleaned = Regex.Replace(cleaned, @"^\s*\n", "");
            cleaned = cleaned.Trim();

            return cleaned;
        }
    }
}
