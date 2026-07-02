using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Agent1.Services.AI
{
    /// <summary>
    /// LLM 上下文 Token 预算管理器。
    /// 负责在发送请求前估算 Prompt 的 Token 数，检查是否超出 KV Cache 容量，
    /// 并在超预算时按优先级裁剪非必要内容（工具定义 > 输出格式指令 > 反幻觉指令）。
    /// </summary>
    public class TokenBudgetManager
    {
        /// <summary>llama-server KV Cache 总容量（token 数）</summary>
        public int MaxBudget { get; init; } = 32768;

        /// <summary>安全系数：最多使用 MaxBudget 的 80%，预留空间给模型响应和 Function Calling 第二轮</summary>
        public double SafetyMargin { get; init; } = 0.8;

        /// <summary>实际可用预算</summary>
        public int EffectiveBudget => (int)(MaxBudget * SafetyMargin);

        // ═══════════════════════════════════
        // Token 估算常量
        // Qwen3 tokenizer 粗略估算：
        //   中文 ≈ 1.5 字符/token
        //   英文/数字 ≈ 3.5 字符/token
        //   JSON schema ≈ 2.0 字符/token（混合中英文）
        // ═══════════════════════════════════

        /// <summary>估算字符串的 Token 数（混合中英文）</summary>
        public int EstimateTokens(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            int chineseChars = 0;
            int otherChars = 0;

            foreach (char c in text)
            {
                if (c >= 0x4E00 && c <= 0x9FFF ||  // CJK 统一汉字
                    c >= 0x3400 && c <= 0x4DBF ||  // CJK 扩展 A
                    c >= 0xF900 && c <= 0xFAFF)    // CJK 兼容汉字
                {
                    chineseChars++;
                }
                else if (!char.IsWhiteSpace(c))
                {
                    otherChars++;
                }
            }

            // 中文 ~1.5 字符/token, 其他 ~3.5 字符/token
            return (int)(chineseChars / 1.5 + otherChars / 3.5);
        }

        /// <summary>估算工具定义的 Token 数（每个工具 ~200-400 tokens，含 name + description + parameters schema）</summary>
        public int EstimateToolTokens(string toolName, string toolDescription, int paramCount = 1)
        {
            // 工具定义的 token 构成：
            //   - name: ~5 tokens
            //   - description (中文): ~30-80 tokens
            //   - parameters JSON schema: ~100-300 tokens per param
            int baseTokens = 50; // type + name overhead
            int descTokens = EstimateTokens(toolDescription);
            int paramTokens = paramCount * 150; // average JSON schema per parameter

            return baseTokens + descTokens + paramTokens;
        }

        /// <summary>
        /// 估算完整 Prompt 的 Token 数。
        /// 包括：system role + tool definitions + user prompt 指令 + user query + 预期工具返回结果
        /// </summary>
        public int EstimateFullPromptTokens(
            string systemRole,
            string promptTemplate,
            string userQuery,
            IReadOnlyList<(string Name, string Description, int ParamCount)> tools,
            int expectedToolResultTokens = 500)
        {
            int total = 0;

            // System role + prompt template（不含工具定义和 user query）
            total += EstimateTokens(systemRole);
            total += EstimateTokens(promptTemplate);

            // 工具定义
            foreach (var tool in tools)
            {
                total += EstimateToolTokens(tool.Name, tool.Description, tool.ParamCount);
            }

            // User query
            total += EstimateTokens(userQuery);

            // 预期工具返回结果（RAG 检索文本）
            total += expectedToolResultTokens;

            return total;
        }

        /// <summary>检查是否会超出预算</summary>
        public bool WouldExceedBudget(int estimatedTokens)
        {
            return estimatedTokens > EffectiveBudget;
        }

        /// <summary>
        /// 按优先级裁剪 Prompt 文本。
        /// 优先级（从低到高，越低越先被裁剪）：
        ///   1. 输出格式示例（"【合规判断】是/否..." 是最低优先级）
        ///   2. 反幻觉指令
        ///   3. FC 格式指令
        ///   4. System Role（永不裁剪）
        /// </summary>
        public string TrimPrompt(string prompt, int targetTokens)
        {
            if (EstimateTokens(prompt) <= targetTokens)
                return prompt;

            // 裁剪策略：按段落标记拆分，从最低优先级段落开始移除
            var sections = new List<(string Marker, string Content, int Priority)>();

            // 提取各段落并按优先级排序
            ExtractSection(prompt, "【输出格式】", "【", 1, sections);
            ExtractSection(prompt, "【反幻觉指令】", "【", 2, sections);
            ExtractSection(prompt, "【强制工具调用指令】", "【", 3, sections);

            // 按优先级升序排列（低优先级先被移除）
            var removable = sections.OrderBy(s => s.Priority).ToList();

            string result = prompt;
            foreach (var section in removable)
            {
                if (EstimateTokens(result) <= targetTokens)
                    break;

                // 移除该段落
                int idx = result.IndexOf(section.Content, StringComparison.Ordinal);
                if (idx >= 0)
                {
                    // 找到段落结束位置（下一个段落标记或字符串末尾）
                    int endIdx = result.IndexOf('\n', idx + section.Content.Length);
                    if (endIdx < 0) endIdx = result.Length;

                    // 移除段落及其后续换行
                    while (endIdx < result.Length && result[endIdx] == '\n')
                        endIdx++;

                    result = result.Remove(idx, endIdx - idx).Trim();
                }
            }

            return result;
        }

        /// <summary>从 Prompt 中提取指定标记的段落</summary>
        private static void ExtractSection(
            string prompt, string startMarker, string nextMarkerPrefix,
            int priority, List<(string Marker, string Content, int Priority)> sections)
        {
            int startIdx = prompt.IndexOf(startMarker, StringComparison.Ordinal);
            if (startIdx < 0) return;

            // 从 startMarker 之后找下一个段落标记
            int searchFrom = startIdx + startMarker.Length;
            int nextIdx = prompt.IndexOf(nextMarkerPrefix, searchFrom, StringComparison.Ordinal);
            int endIdx = nextIdx >= 0 ? nextIdx : prompt.Length;

            string content = prompt.Substring(startIdx, endIdx - startIdx).Trim();
            sections.Add((startMarker, content, priority));
        }

        /// <summary>
        /// 生成 Token 预算摘要，用于日志诊断。
        /// </summary>
        public string GenerateBudgetReport(
            string systemRole, string promptTemplate, string userQuery,
            IReadOnlyList<(string Name, string Description, int ParamCount)> tools)
        {
            int systemTokens = EstimateTokens(systemRole);
            int templateTokens = EstimateTokens(promptTemplate);
            int queryTokens = EstimateTokens(userQuery);
            int toolsTokens = tools.Sum(t => EstimateToolTokens(t.Name, t.Description, t.ParamCount));
            int total = systemTokens + templateTokens + queryTokens + toolsTokens;

            return string.Join("\n",
                $"   [Token预算] 总计预估 {total}/{EffectiveBudget} tokens (上限 {MaxBudget}×{SafetyMargin:P0})",
                $"     System Role: {systemTokens}",
                $"     Prompt 指令: {templateTokens}",
                $"     工具定义 ({tools.Count}个): {toolsTokens}",
                $"     User Query: {queryTokens}",
                $"     状态: {(total > EffectiveBudget ? "⚠️ 超预算" : "✅ 安全")}"
            );
        }

        // ═══════════════════════════════════
        // 预定义的工具描述（用于按意图裁剪工具集）
        // ═══════════════════════════════════

        /// <summary>信息查询类 case 需要的工具（仅查询，不做合规判断）</summary>
        public static readonly (string Name, string Description, int ParamCount)[] InfoQueryTools =
        {
            ("CheckHazardCategory", "查询指定危化品的危险类别/GHS分类/适用国标", 1),
            ("GetSafetyDistance", "查询设施类型的安全距离/防火间距要求", 1),
            ("GetCurrentTime", "获取当前时间和日期", 0),
        };

        /// <summary>合规判断类 case 需要的工具（需要完整的储存兼容性 + 法规核查）</summary>
        public static readonly (string Name, string Description, int ParamCount)[] ComplianceJudgeTools =
        {
            ("CheckStorageCompatibility", "查询两种危化品是否可以同库储存", 2),
            ("CheckHazardCategory", "查询指定危化品的危险类别/GHS分类/适用国标", 1),
            ("GetSafetyDistance", "查询设施类型的安全距离/防火间距要求", 1),
            ("CheckRegulation", "查询指定法规标准的版本状态及全文收录情况", 1),
            ("GetCurrentTime", "获取当前时间和日期", 0),
        };

        /// <summary>全量工具（用于 FC 就绪性检查）</summary>
        public static readonly (string Name, string Description, int ParamCount)[] AllTools =
        {
            ("CheckHazardCategory", "查询指定危化品的危险类别/GHS分类/适用国标", 1),
            ("CheckStorageCompatibility", "查询两种危化品是否可以同库储存", 2),
            ("GetSafetyDistance", "查询设施类型的安全距离/防火间距要求", 1),
            ("CheckRegulation", "查询指定法规标准的版本状态及全文收录情况", 1),
            ("GetSubstanceProperties", "查询指定危化品的完整基础属性", 1),
            ("GetCriticalQuantity", "查询危化品的重大危险源临界量", 1),
            ("GetCurrentTime", "获取当前时间和日期", 0),
        };
    }
}
