using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Agent1.Services
{
    /// <summary>
    /// [P1-2] 结论验证中间层。
    /// 在 LLM 最终输出到评测器之间，对合规结论进行格式和语义校验。
    /// 
    /// 校验维度：
    /// 1. 法规编号格式（GB XXXX-XXXX）
    /// 2. 知识库反向检索验证（替代静态白名单）
    /// 3. 合规判断与工具数据的语义一致性
    /// 4. 安全距离数值比对
    /// </summary>
    public static class ConclusionVerifier
    {
        // ── 法规编号格式正则：GB[ /T]? 数字-数字 ──
        private static readonly Regex RegulationPattern = new(
            @"GB\s*/?T?\s*(\d{4,5})[.\-](\d+(?:\.\d+)?)",
            RegexOptions.Compiled);

        /// <summary>
        /// [P1-2 升级] 验证结论的完整校验结果（含 KB 反向检索）。
        /// kbService 为 null 时跳过 KB 反向验证。
        /// [P7 FIX] 空数据检测：若响应明确声明数据不足，不标记为验证失败。
        /// </summary>
        public static async Task<VerificationResult> VerifyAsync(
            string llmResponse,
            List<FunctionCallRecord> toolCalls,
            IKnowledgeBaseService? kbService = null,
            string? category = null)
        {
            var result = new VerificationResult();

            if (string.IsNullOrWhiteSpace(llmResponse))
            {
                result.IsPassed = false;
                result.FailureReasons.Add("LLM 响应为空");
                return result;
            }

            // [P7 FIX] 空数据检测：若 LLM 明确声明数据不足/未检索到/无记录，视为正确识别知识边界
            var hasEmptyData = llmResponse.Contains("无数据") || llmResponse.Contains("未检索到")
                || llmResponse.Contains("无记录") || llmResponse.Contains("数据不足");
            if (hasEmptyData)
            {
                result.IsPassed = true;
                result.Warnings.Add("系统正确识别知识边界（无数据声明），不视为验证失败");
                return result;
            }

            // 1. 法规编号格式校验
            var regMatches = RegulationPattern.Matches(llmResponse);
            if (regMatches.Count == 0)
            {
                result.IsPassed = false;
                result.FailureReasons.Add("未找到有效法规编号（格式: GB XXXX-XXXX）");
            }
            else
            {
                foreach (Match m in regMatches)
                {
                    var full = m.Value;
                    result.RegulationsFound.Add(full);
                }
            }

            // 2. [P1-2] KB 反向检索验证: 替代静态白名单
            if (kbService != null && result.RegulationsFound.Count > 0)
            {
                await VerifyRegulationsAgainstKBAsync(result.RegulationsFound, kbService, result);
            }

            // 3. [P2-12 FIX] 合规判断标签校验：同时匹配 【合规判断】 和 [判定:is_compliant=...] 两种格式
            var conclusionMatch = Regex.Match(llmResponse, @"【合规判断】\s*(是|否)");
            var tagMatch = Regex.Match(llmResponse, @"\[判定\s*:\s*is_compliant\s*=\s*(true|false|unknown|待核实|依据原文)\s*\]", RegexOptions.IgnoreCase);
            if (conclusionMatch.Success)
            {
                result.ConclusionValue = conclusionMatch.Groups[1].Value;
            }
            else if (tagMatch.Success)
            {
                var tagValue = tagMatch.Groups[1].Value.Trim();
                if (tagValue.Equals("true", StringComparison.OrdinalIgnoreCase))
                    result.ConclusionValue = "是";
                else if (tagValue.Equals("false", StringComparison.OrdinalIgnoreCase))
                    result.ConclusionValue = "否";
                else
                    result.ConclusionValue = tagValue;
            }
            else
            {
                result.Warnings.Add("未找到合规判断标签（【合规判断】或[判定:is_compliant=...]）");
            }

            // 4. 工具数据一致性检查
            if (toolCalls != null && toolCalls.Count > 0)
            {
                foreach (var call in toolCalls)
                {
                    result.ToolsCalled.Add(call.FunctionName);
                }
            }
            else
            {
                result.Warnings.Add("未调用任何工具，结论可能不可靠");
            }

            // 5. 安全距离专项
            if (category == "安全距离")
            {
                var hasDistance = Regex.IsMatch(llmResponse, @"(\d+(?:\.\d+)?)\s*(米|m)");
                var hasDataInsufficient = llmResponse.Contains("数据不足") || llmResponse.Contains("未检索到");
                result.HasDistanceValue = hasDistance;
                if (!hasDistance && !hasDataInsufficient)
                {
                    result.FailureReasons.Add("安全距离类未输出数值距离或数据不足声明");
                }
            }

            result.IsPassed = result.FailureReasons.Count == 0;
            return result;
        }

        /// <summary>[P1-2] KB 反向检索: 对 LLM 输出的每个法规编号，到知识库中验证是否存在</summary>
        private static async Task VerifyRegulationsAgainstKBAsync(
            List<string> regulations,
            IKnowledgeBaseService kbService,
            VerificationResult result)
        {
            foreach (var reg in regulations)
            {
                try
                {
                    // 用法规编号作为查询词反向检索
                    var chunks = await kbService.RetrieveChemicalRegulationAsync(
                        reg, regulationType: null, topK: 1);

                    if (chunks.Count > 0)
                    {
                        result.VerifiedRegulations.Add(reg);
                    }
                    else
                    {
                        result.HallucinatedRegulations.Add(reg);
                        result.Warnings.Add($"法规编号 {reg} 在知识库中未找到，可能是幻觉");
                    }
                }
                catch
                {
                    // KB 检索异常不阻塞验证流程
                    result.Warnings.Add($"法规编号 {reg} 反向验证失败（KB 检索异常）");
                }
            }
        }

        /// <summary>
        /// 快速验证：仅检查法规编号格式是否有效。
        /// </summary>
        public static bool HasValidRegulation(string llmResponse)
        {
            return RegulationPattern.IsMatch(llmResponse);
        }

        /// <summary>
        /// 从 LLM 输出中提取所有法规引用。
        /// </summary>
        public static List<string> ExtractRegulations(string llmResponse)
        {
            var result = new List<string>();
            var matches = RegulationPattern.Matches(llmResponse);
            foreach (Match m in matches)
            {
                result.Add(m.Value.Trim());
            }
            return result;
        }
    }

    /// <summary>
    /// 结论验证结果。
    /// </summary>
    public class VerificationResult
    {
        public bool IsPassed { get; set; } = true;
        public string? ConclusionValue { get; set; }
        public bool HasDistanceValue { get; set; }
        public List<string> RegulationsFound { get; set; } = new();
        public List<string> VerifiedRegulations { get; set; } = new();
        public List<string> HallucinatedRegulations { get; set; } = new();
        public List<string> ToolsCalled { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public List<string> FailureReasons { get; set; } = new();
    }
}