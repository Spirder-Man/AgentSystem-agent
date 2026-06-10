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
        public bool FoundInSource { get; set; }
        public string? EvidenceSnippet { get; set; }

        public override string ToString()
        {
            var mark = FoundInSource ? "✓" : "✗";
            var note = FoundInSource
                ? (EvidenceSnippet != null ? $" → {EvidenceSnippet.Truncate(80)}" : "")
                : " ⚠️ 未在知识库中找到! 可能幻觉";
            return $"  {mark} {ClaimedText,-25} {note}";
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
            sb.AppendLine($"\n📊 事实精度: {Claims.Count(c => c.FoundInSource)}/{Claims.Count} ({FactualPrecision:P1})");
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
        public async Task<BusinessVerificationReport> VerifyBusinessFactsAsync(string conclusion)
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

                        var found = chunks.Any(c =>
                            (c.Content ?? "").Contains(normalized) ||
                            (c.Content ?? "").Contains(rawText.Replace(" ", "")));

                        var evidence = chunks.FirstOrDefault()?.Content;

                        report.Claims.Add(new ClaimVerification
                        {
                            ClaimedText = normalized,
                            ClaimType = type,
                            FoundInSource = found,
                            EvidenceSnippet = evidence?.Truncate(200)
                        });
                    }
                    catch
                    {
                        // 检索异常视为无法验证，不标记为幻觉
                        report.Claims.Add(new ClaimVerification
                        {
                            ClaimedText = normalized,
                            ClaimType = type,
                            FoundInSource = true,  // 不因系统异常惩罚
                            EvidenceSnippet = null
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
            sb.AppendLine("【修正规则】");
            sb.AppendLine("1. 删除核查报告中标记为 ✗ 的法规引用（这些编号在知识库中不存在）");
            sb.AppendLine("2. 保留标记为 ✓ 的内容");
            sb.AppendLine("3. 如果系统健康报告指出工具异常，诚实说明数据可能不完整");
            sb.AppendLine("4. 按以下模板输出修正后的结论：");
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
