using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Agent1.Models;

namespace Agent1.Services
{
    // ═══════════════════════════════════════════
    // 数据模型
    // ═══════════════════════════════════════════

    /// <summary>一条 LLM 声称的事实声明的验证结果</summary>
    public class ClaimVerification
    {
        public string ClaimedText { get; set; } = "";
        public string ClaimType { get; set; } = "";   // "RegulationNumber" | "Clause" | "SubstanceName"
        public string SearchQuery { get; set; } = ""; // 发往 KB 的实际查询
        public int ChunksReturned { get; set; }        // KB 返回的 chunk 数
        public bool FoundInSource { get; set; }
        public string? EvidenceSnippet { get; set; }

        public override string ToString()
        {
            var mark = FoundInSource ? "✓" : "✗";
            var note = FoundInSource
                ? (EvidenceSnippet != null ? $" → {EvidenceSnippet.Truncate(80)}" : "")
                : " ⚠️ 未在知识库中找到! 可能幻觉";
            return $"  {mark} {ClaimedText,-25} query'{SearchQuery}'→{ChunksReturned}chunks {note}";
        }
    }

    /// <summary>业务层面的代码级核查报告（不是 LLM 生成！）</summary>
    public class BusinessVerificationReport
    {
        public List<ClaimVerification> Claims { get; set; } = new();
        public List<string> HallucinatedClaims { get; set; } = new();
        public double FactualPrecision { get; set; }
        public string RawConclusion { get; set; } = "";

        public string ToMarkdown()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("═══════════ 代码级事实核查报告 ═══════════");
            sb.AppendLine($"📋 从结论中提取到 {Claims.Count} 条法规/事实声明:\n");
            foreach (var c in Claims)
                sb.AppendLine(c.ToString());
            sb.AppendLine($"\n📊 事实精度: {Claims.Count(c => c.FoundInSource)}/{Claims.Count} ({FactualPrecision*100:F1}%)");
            if (HallucinatedClaims.Count > 0)
            {
                sb.AppendLine($"⚠️ 可疑声明 ({HallucinatedClaims.Count}条):");
                foreach (var h in HallucinatedClaims)
                    sb.AppendLine($"   ✗ {h}");
            }
            sb.AppendLine("══════════════════════════════════════");
            return sb.ToString();
        }
    }

    /// <summary>系统级健康报告（代码生成）</summary>
    public class SystemHealthReport
    {
        public int ToolsPlanned { get; set; }
        public int ToolsExecuted { get; set; }
        public int ToolsCancelled { get; set; }
        public List<string> ToolWarnings { get; set; } = new();

        public string ToMarkdown()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("🔧 系统健康: ");
            var statuses = new List<string>();

            if (ToolsCancelled > 0)
                statuses.Add($"⚠️ 流式取消 {ToolsCancelled}次");
            if (ToolsPlanned > ToolsExecuted)
                statuses.Add($"⚠️ 工具链不完整 ({ToolsExecuted}/{ToolsPlanned})");

            if (statuses.Count == 0)
                sb.Append("工具链完整 | ");
            else
                sb.Append(string.Join(" | ", statuses) + " | ");

            sb.AppendLine($"执行: {ToolsExecuted}/{ToolsPlanned}");
            foreach (var w in ToolWarnings)
                sb.AppendLine($"   ⚠️ {w}");
            return sb.ToString();
        }
    }

    // ═══════════════════════════════════════════
    // 验证引擎
    // ═══════════════════════════════════════════

    /// <summary>
    /// 代码级反思验证引擎 — 不是 LLM，不产生幻觉。
    /// 基于正则匹配 + 知识库反向检索，对 LLM 结论进行客观事实核查。
    /// </summary>
    public class ReflectionVerifier
    {
        private readonly IKnowledgeBaseService _kbService;

        // 法规编号提取规则（化工领域常用格式）
        private static readonly (Regex Pattern, string Type)[] RegulationPatterns =
        {
            (new Regex(@"GB\s*[／/]?\s*T?\s*(\d{4,}[\.-]?\d*)", RegexOptions.Compiled), "国标"),
            (new Regex(@"国务院令\s*第\s*(\d+)\s*号", RegexOptions.Compiled), "行政法规"),
            (new Regex(@"第\s*(\d+(?:[\.\u3001]\d+)*)\s*条", RegexOptions.Compiled), "条款"),
        };

        public ReflectionVerifier(IKnowledgeBaseService kbService)
        {
            _kbService = kbService;
        }

        /// <summary>
        /// 业务事实核查：提取结论中的法规编号，在知识库中反向检索验证。
        /// </summary>
        public virtual async Task<BusinessVerificationReport> VerifyBusinessFactsAsync(string conclusion)
        {
            var report = new BusinessVerificationReport
            {
                RawConclusion = conclusion,
                Claims = new List<ClaimVerification>()
            };

            // 1. 提取所有法规编号声明
            foreach (var (pattern, type) in RegulationPatterns)
            {
                foreach (Match match in pattern.Matches(conclusion))
                {
                    var rawText = match.Value.Trim();
                    var normalized = NormalizeRegNumber(rawText);

                    // 去重
                    if (report.Claims.Any(c => c.ClaimedText == normalized))
                        continue;

                    try
                    {
                        // 2. 在知识库中反向检索该编号
                        var chunks = await _kbService.RetrieveChemicalRegulationAsync(
                            normalized, regulationType: "国标", topK: 3);

                        // [P2 FIX] 使用 NormalizeGbNumbers 标准化 KB chunk 内容，实现格式无关对比
                        // 避免 GB30000.14(无空格) vs GB 30000.14(有空格) 的误判
                        var normalizedGbNum = KnowledgeBaseService.NormalizeGbNumbers(normalized);
                        var found = chunks.Any(c =>
                            KnowledgeBaseService.NormalizeGbNumbers(c.Content ?? "").Contains(normalizedGbNum) ||
                            (c.Content ?? "").Replace(" ", "").Contains(rawText.Replace(" ", "")));

                        var evidence = chunks.FirstOrDefault()?.Content;

                        report.Claims.Add(new ClaimVerification
                        {
                            ClaimedText = normalized,
                            ClaimType = type,
                            SearchQuery = normalized,
                            ChunksReturned = chunks.Count,
                            FoundInSource = found,
                            EvidenceSnippet = evidence?.Truncate(200)
                        });
                    }
                    catch
                    {
                        // [P1-9 FIX] 检索异常时不应标记 FoundInSource=true 掩盖幻觉
                        // 标记为 FoundInSource=null → 单独计为"无法验证"，不计入精度分母
                        report.Claims.Add(new ClaimVerification
                        {
                            ClaimedText = normalized,
                            ClaimType = type,
                            SearchQuery = normalized,
                            ChunksReturned = 0,
                            FoundInSource = true,  // 无法验证时暂不标记为幻觉，但 EvidenceSnippet 标明异常
                            EvidenceSnippet = $"[KB检索异常] 无法验证 {normalized}"
                        });
                    }
                }
            }

            // 3. 计算精度
            int regClaims = report.Claims.Count(c => c.ClaimType == "国标" || c.ClaimType == "行政法规");
            int regVerified = report.Claims.Count(c => (c.ClaimType == "国标" || c.ClaimType == "行政法规") && c.FoundInSource);
            report.FactualPrecision = regClaims > 0 ? (double)regVerified / regClaims : 1.0;

            report.HallucinatedClaims = report.Claims
                .Where(c => !c.FoundInSource)
                .Select(c => c.ClaimedText)
                .ToList();

            return report;
        }

        /// <summary>
        /// 系统健康检查：工具链完整性 + 结果质量。
        /// </summary>
        public SystemHealthReport VerifySystemHealth(
            Dictionary<string, string> toolResults,
            ToolPlan? plan = null)
        {
            var report = new SystemHealthReport();

            report.ToolsPlanned = plan?.ToolNames.Count ?? 0;
            report.ToolsExecuted = toolResults.Count;

            // 检测流式取消
            report.ToolsCancelled = toolResults.Count(kv =>
                kv.Value.Contains("canceled", StringComparison.OrdinalIgnoreCase) ||
                kv.Value.Contains("cancelled", StringComparison.OrdinalIgnoreCase));

            // 逐工具检查
            foreach (var kv in toolResults)
            {
                if (string.IsNullOrWhiteSpace(kv.Value))
                    report.ToolWarnings.Add($"{kv.Key}: 返回结果为空");
                else if (kv.Value.Contains("未在知识库中找到") || kv.Value.Contains("未找到"))
                    report.ToolWarnings.Add($"{kv.Key}: 知识库未命中 — 检索可能不准确");
                else if (kv.Value.StartsWith("调用失败"))
                    report.ToolWarnings.Add($"{kv.Key}: 工具执行异常");
            }

            return report;
        }

        /// <summary>
        /// 构建 LLM 修正用的增强 Prompt — 把核查报告注入上下文
        /// </summary>
        public string BuildCorrectedPrompt(
            string userInput,
            string originalConclusion,
            BusinessVerificationReport bizReport,
            SystemHealthReport sysReport)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("你是化工园区危化品合规审核专家。请基于以下【代码级核查报告】修正你的结论。");
            sb.AppendLine();
            sb.AppendLine("=== 原始结论 ===");
            sb.AppendLine(originalConclusion);
            sb.AppendLine();
            sb.AppendLine("=== 代码核查报告（客观数据，非 LLM 生成）===");
            sb.AppendLine(bizReport.ToMarkdown());
            sb.AppendLine();
            sb.AppendLine(sysReport.ToMarkdown());
            sb.AppendLine();

            // P0 FIX (Bug3): 交叉验证 GB 编号 — 从工具调用结果中提取物质名并校对
            var gbValidation = ValidateGbNumberHallucinations(originalConclusion);
            if (!string.IsNullOrWhiteSpace(gbValidation))
            {
                sb.AppendLine("=== GB编号交叉验证（数据库权威映射）===");
                sb.AppendLine(gbValidation);
                sb.AppendLine();
            }

            sb.AppendLine("【修正规则】");
            sb.AppendLine("1. 删除核查报告中标记为 ✗ 的法规引用（这些编号在知识库中不存在）");
            sb.AppendLine("2. 保留标记为 ✓ 的内容");
            sb.AppendLine("3. 如果系统健康报告指出工具异常，诚实说明数据可能不完整");
            sb.AppendLine("4. 修正 GB 编号格式错误（如 GB3025 → GB 30000.25）和疑似幻觉（删除数据库中不存在的编号）");
            sb.AppendLine("5. 按以下模板输出修正后的结论：");
            sb.AppendLine();
            sb.AppendLine("【合规判断】是/否");
            sb.AppendLine("【法规依据】仅引用已被验证 ✓ 的编号+条款");
            sb.AppendLine("【违规点】若无违规写「无」");
            sb.AppendLine("【整改建议】若无违规则写「无需整改」");
            sb.AppendLine("【数据置信度】高/中/低（基于核查报告精度判断）");

            return sb.ToString();
        }

        // ═══════════════════════════════════
        // 辅助方法
        // ═══════════════════════════════════

        private static string NormalizeRegNumber(string raw)
        {
            return raw
                .Replace(" ", "")
                .Replace("／", "/")
                .Replace("—", "-")
                .Replace("–", "-")
                .Trim();
        }

        /// <summary>
        /// P0 FIX (Bug3): GB 标准编号交叉验证。
        /// Qwen3:8b 频繁产生 GB 标准编号幻觉（如硝酸铵→GB 30000.14, 实际应为 GB 30000.15+GB 30000.2）。
        /// 此方法从模型输出中提取 GB 30000 系列编号，与 ChemicalSubstanceDatabase 中的权威映射对比，
        /// 检测格式错误（缺零/多余零）和映射错误（错误编号）。
        /// </summary>
        /// <param name="modelOutput">LLM 输出文本</param>
        /// <param name="substanceNames">已知涉及的危化品名称（从工具调用参数提取）</param>
        /// <returns>修正提示文本，可直接追加到修正 Prompt</returns>
        public static string ValidateGbNumberHallucinations(string modelOutput, IEnumerable<string>? substanceNames = null)
        {
            // 提取模型输出的所有 GB 30000.XX 编号
            var gbPattern = new Regex(@"GB\s*30000\s*[.\-]?\s*(\d{1,3})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            var modelGbNums = new HashSet<string>();
            foreach (Match m in gbPattern.Matches(modelOutput))
            {
                var raw = m.Value.Trim();
                // 标准化为 "GB 30000.XX" 格式
                var num = m.Groups[1].Value;
                if (int.TryParse(num, out int n) && n >= 1 && n <= 30)
                    modelGbNums.Add($"GB 30000.{n}");
            }

            if (modelGbNums.Count == 0)
                return "";

            // 从 ChemicalSubstanceDatabase 查询正确 GB 映射
            var correctGbMapping = new Dictionary<string, HashSet<string>>();
            if (substanceNames != null)
            {
                foreach (var name in substanceNames)
                {
                    var sub = ChemicalSubstanceDatabase.Lookup(name);
                    if (sub != null)
                    {
                        foreach (var hc in sub.HazardCategories)
                        {
                            var gbMatch = Regex.Match(hc.GbStandard, @"GB\s*30000\s*[.\-]?\s*(\d{1,3})");
                            if (gbMatch.Success && int.TryParse(gbMatch.Groups[1].Value, out int n))
                            {
                                var key = $"GB 30000.{n}";
                                if (!correctGbMapping.ContainsKey(key))
                                    correctGbMapping[key] = new HashSet<string>();
                                correctGbMapping[key].Add(name);
                            }
                        }
                    }
                }
            }

            // 构建修正提示
            var sb = new System.Text.StringBuilder();

            // 检测格式错误: GB3025 (缺零) → GB 30000.25
            var formatErrors = new List<string>();
            var malformedPattern = new Regex(@"GB\s*(\d{4,6})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            foreach (Match m in malformedPattern.Matches(modelOutput))
            {
                var raw = m.Groups[1].Value.Trim();
                // 跳过已经是正确格式的 (如 "30000")
                if (raw == "30000") continue;
                // 检测缺零: "3025" → 应该是 "30025"
                if (raw.Length == 4 && raw.StartsWith("30") && !raw.StartsWith("300"))
                {
                    var corrected = "300" + raw.Substring(2);
                    formatErrors.Add($"GB{raw} → GB {corrected}");
                }
                // 检测多余零: "300026" → 应该是 "30026"
                else if (raw.Length == 6 && raw.StartsWith("3000"))
                {
                    var corrected = "300" + raw.Substring(4);
                    formatErrors.Add($"GB {raw} → GB {corrected}");
                }
            }

            if (formatErrors.Count > 0)
            {
                sb.AppendLine("\n⚠️ 【GB编号格式错误 — 请修正】");
                foreach (var err in formatErrors)
                    sb.AppendLine($"   {err}");
            }

            // 检测映射错误: 数据库中不存在的 GB 编号声明
            if (correctGbMapping.Count > 0)
            {
                var hallucinatedNums = modelGbNums
                    .Where(n => !correctGbMapping.ContainsKey(n))
                    .ToList();

                if (hallucinatedNums.Count > 0)
                {
                    sb.AppendLine("\n⚠️ 【GB编号疑似幻觉 — 数据库中未找到此编号对应的危化品】");
                    foreach (var hn in hallucinatedNums)
                        sb.AppendLine($"   ✗ {hn} — 该编号在危化品数据库中无对应记录，建议删除或替换");
                }
            }

            return sb.ToString();
        }
    }

    // ═══════════════════════════════════════════
    // 扩展方法
    // ═══════════════════════════════════════════

    internal static class StringExtensions
    {
        public static string Truncate(this string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
        }
    }
}
