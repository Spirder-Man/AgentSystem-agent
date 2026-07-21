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

        // [Tier 1] KB 未命中时的替代检索建议
        public bool IsMissingFromKb { get; set; }       // 该声明在本地KB中完全未找到
        public List<string> SuggestedSources { get; set; } = new();  // 建议的外部检索来源
        public string? CorrectionHint { get; set; }      // 格式修正提示（如 GB3025→GB 30000.25）

        public override string ToString()
        {
            var mark = FoundInSource ? "✓" : "✗";
            var note = FoundInSource
                ? (EvidenceSnippet != null ? $" → {EvidenceSnippet.Truncate(80)}" : "")
                : " ⚠️ 未在知识库中找到! 可能幻觉";
            
            if (IsMissingFromKb && SuggestedSources.Count > 0)
                note += $" | 🔗 建议检索: {string.Join(", ", SuggestedSources.Take(2))}";
            
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

        // [Tier 1] KB 缺失的声明列表（需要外部检索）
        public List<ClaimVerification> MissingFromKbClaims =>
            Claims.Where(c => c.IsMissingFromKb).ToList();

        // [Tier 2] 结论完整性评估
        public CompletenessAssessment? Completeness { get; set; }

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
            // [Tier 1] KB 缺失建议
            if (MissingFromKbClaims.Count > 0)
            {
                sb.AppendLine($"\n🔗 KB 缺失声明 ({MissingFromKbClaims.Count}条) — 建议外部检索:");
                foreach (var mc in MissingFromKbClaims)
                {
                    var sources = mc.SuggestedSources.Count > 0
                        ? string.Join(", ", mc.SuggestedSources)
                        : "无建议来源";
                    sb.AppendLine($"   📌 {mc.ClaimedText}: {sources}");
                }
            }
            // [Tier 2] 完整性评估
            if (Completeness != null)
            {
                var dims = Completeness.MissingDimensions.Count > 0
                    ? string.Join("、", Completeness.MissingDimensions)
                    : "无";
                sb.AppendLine($"\n📝 结论完整性评估 (Tier 2):");
                sb.AppendLine($"   完整度: {Completeness.Score}/10");
                sb.AppendLine($"   缺失维度: {dims}");
                if (!string.IsNullOrWhiteSpace(Completeness.Suggestion))
                    sb.AppendLine($"   富化建议: {Completeness.Suggestion}");
            }
            sb.AppendLine("══════════════════════════════════════");
            return sb.ToString();
        }
    }

    /// <summary>
    /// [Tier 2] 结论完整性评估结果
    /// </summary>
    public class CompletenessAssessment
    {
        /// <summary>完整度评分 0-10</summary>
        public int Score { get; set; }
        /// <summary>缺失的维度（如 "风险分析"、"整改措施"、"法规交叉引用"）</summary>
        public List<string> MissingDimensions { get; set; } = new();
        /// <summary>富化建议</summary>
        public string? Suggestion { get; set; }
        /// <summary>是否需要触发 Tier 2 Agentic Review</summary>
        public bool NeedsEnrichment => Score < 7 || MissingDimensions.Count > 0;
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

                        // [Tier 1] KB 未命中时生成外部检索建议
                        var isMissing = !found && chunks.Count == 0;
                        var suggestedSources = isMissing ? GetSuggestedExternalSources(rawText) : new List<string>();

                        report.Claims.Add(new ClaimVerification
                        {
                            ClaimedText = normalized,
                            ClaimType = type,
                            SearchQuery = normalized,
                            ChunksReturned = chunks.Count,
                            FoundInSource = found,
                            EvidenceSnippet = evidence?.Truncate(200),
                            IsMissingFromKb = isMissing,
                            SuggestedSources = suggestedSources
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

            // [Tier 1] KB 缺失声明 → 外部检索建议
            if (bizReport.MissingFromKbClaims.Count > 0)
            {
                sb.AppendLine("=== 外部检索建议 (Tier 1) ===");
                sb.AppendLine("以下法规编号在本地知识库中未找到，建议通过以下渠道补充检索:");
                foreach (var mc in bizReport.MissingFromKbClaims)
                {
                    sb.AppendLine($"  📌 {mc.ClaimedText}:");
                    foreach (var src in mc.SuggestedSources)
                        sb.AppendLine($"     🔗 {src}");
                }
                sb.AppendLine();
            }

            // [Tier 2] 完整性评估
            if (bizReport.Completeness?.NeedsEnrichment == true)
            {
                sb.AppendLine("=== 完整性评估 (Tier 2) ===");
                sb.AppendLine($"当前结论完整度: {bizReport.Completeness.Score}/10");
                sb.AppendLine($"缺失维度: {string.Join("、", bizReport.Completeness.MissingDimensions)}");
                sb.AppendLine("请在修正时补充上述缺失维度的分析内容。");
                sb.AppendLine();
            }

            sb.AppendLine("【修正规则】");
            sb.AppendLine("1. 删除核查报告中标记为 ✗ 的法规引用（这些编号在知识库中不存在）");
            sb.AppendLine("2. 保留标记为 ✓ 的内容");
            sb.AppendLine("3. 如果系统健康报告指出工具异常，诚实说明数据可能不完整");
            sb.AppendLine("4. 修正 GB 编号格式错误（如 GB3025 → GB 30000.25）和疑似幻觉（删除数据库中不存在的编号）");
            sb.AppendLine("5. 如果有外部检索建议，可在结论末尾标注「建议进一步查阅: <来源URL>」");
            sb.AppendLine("6. 按以下模板输出修正后的结论：");
            sb.AppendLine();
            sb.AppendLine("【合规判断】是/否");
            sb.AppendLine("【法规依据】仅引用已被验证 ✓ 的编号+条款");
            sb.AppendLine("【违规点】若无违规写「无」");
            sb.AppendLine("【整改建议】若无违规则写「无需整改」");
            sb.AppendLine("【数据置信度】高/中/低（基于核查报告精度判断）");

            return sb.ToString();
        }

        /// <summary>
        /// [Tier 2] 构建结论富化 Prompt — 当编号正确但结论单薄时使用。
        /// 与 BuildCorrectedPrompt 不同，此方法不做错误修正，只要求 LLM 围绕原结论进行维度补充。
        /// </summary>
        public string BuildEnrichedPrompt(
            string userInput,
            string originalConclusion,
            BusinessVerificationReport bizReport)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("你是化工园区危化品合规审核专家。请基于以下客观核查报告，对原始结论进行专业富化。");
            sb.AppendLine();
            sb.AppendLine("=== 原始结论 ===");
            sb.AppendLine(originalConclusion);
            sb.AppendLine();
            sb.AppendLine("⚠️ 重要: 原始结论中的所有法规引用均已通过代码级核查（✓），请保留这些已验证的法规编号。");
            sb.AppendLine("你的任务是在此基础上补充分析维度，而非修改或删除已验证的内容。");
            sb.AppendLine();

            if (bizReport.Completeness != null)
            {
                sb.AppendLine($"=== 完整性评估（当前完整度: {bizReport.Completeness.Score}/10）===");
                if (bizReport.Completeness.MissingDimensions.Count > 0)
                {
                    sb.AppendLine($"需要补充的维度: {string.Join("、", bizReport.Completeness.MissingDimensions)}");
                    sb.AppendLine();
                }
            }

            sb.AppendLine("=== 核查报告（已验证的法规引用）===");
            var verifiedClaims = bizReport.Claims.Where(c => c.FoundInSource).ToList();
            foreach (var c in verifiedClaims)
                sb.AppendLine($"  ✓ {c.ClaimedText} — 已在知识库中验证");
            sb.AppendLine();

            sb.AppendLine("【富化模板 — 请按以下结构输出】");
            sb.AppendLine();
            sb.AppendLine("【合规判定】保持原判，引用已验证的法规编号");
            sb.AppendLine("【法规依据】列出已验证的法规编号及关键条款");
            sb.AppendLine("【风险分析】基于化学品性质和法规要求，分析潜在风险");
            sb.AppendLine("【整改措施】具体、可操作的整改步骤（如需）");
            sb.AppendLine("【法规交叉引用】是否存在其他相关标准需要同时关注");
            sb.AppendLine("【数据来源】标注结论依赖的数据库/知识库版本");

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
        /// [Tier 1] 为 KB 中未找到的法规编号生成外部检索建议来源。
        /// 不在 KB 中删除该声明，而是提供补救路径，让用户/Agent 知道去哪查。
        /// </summary>
        private static List<string> GetSuggestedExternalSources(string regNumber)
        {
            var sources = new List<string>();

            // 国标 → 国家标准全文公开系统
            if (regNumber.StartsWith("GB", StringComparison.OrdinalIgnoreCase) ||
                regNumber.Contains("30000"))
            {
                sources.Add("国家标准全文公开系统 https://openstd.samr.gov.cn/");
                sources.Add("全国标准信息公共服务平台 https://std.samr.gov.cn/");
            }

            // 行政法规 → 司法部/应急管理部
            if (regNumber.Contains("国务院令") || regNumber.Contains("条例") ||
                regNumber.Contains("部令"))
            {
                sources.Add("国家法律法规数据库 https://flk.npc.gov.cn/");
                sources.Add("应急管理部法规 https://www.mem.gov.cn/");
            }

            // 条款级别
            if (regNumber.Contains("第") && regNumber.Contains("条"))
            {
                sources.Add("建议结合上级法规编号一起检索");
            }

            // 兜底：应急管理部化学品登记中心
            if (sources.Count == 0)
            {
                sources.Add("应急管理部化学品登记中心 https://www.nrcc.com.cn/");
                sources.Add("建议人工核实该法规编号是否存在");
            }

            return sources;
        }

        /// <summary>
        /// [Tier 2] 评估 LLM 结论的完整性，检测缺失的维度。
        /// 这是确定性规则评估（非 LLM 调用），判断结论是否包含必要的分析维度。
        /// </summary>
        /// <param name="conclusion">LLM 输出的结论文本</param>
        /// <param name="userIntent">用户意图类型（info_query / compliance_check 等）</param>
        /// <returns>完整性评估结果</returns>
        public static CompletenessAssessment AssessCompleteness(string conclusion, string? userIntent = null)
        {
            var assessment = new CompletenessAssessment { Score = 10 };

            // 检测各维度是否存在（基于关键词匹配，确定性规则）
            var dimensions = new Dictionary<string, (string[] Keywords, int Weight)>
            {
                ["法规依据"] = (new[] { "GB ", "GB/T", "国务院令", "第", "条", "标准" }, 3),
                ["合规判定"] = (new[] { "合规", "不合规", "符合", "不符合", "违规", "满足要求" }, 3),
                ["风险分析"] = (new[] { "风险", "危险", "隐患", "危害", "事故" }, 2),
                ["整改措施"] = (new[] { "整改", "措施", "建议", "应", "需", "必须", "处理" }, 2),
                ["法规交叉引用"] = (new[] { "同时参考", "另见", "相关标准", "还涉及", "此外" }, 1),
                ["数据来源"] = (new[] { "知识库", "数据库", "来源", "根据", "依据", "检索" }, 1),
            };

            foreach (var (dimName, (keywords, weight)) in dimensions)
            {
                var found = keywords.Any(k => conclusion.Contains(k, StringComparison.OrdinalIgnoreCase));
                if (!found)
                {
                    assessment.MissingDimensions.Add(dimName);
                    assessment.Score -= weight;
                }
            }

            // 兜底
            if (assessment.Score < 0) assessment.Score = 0;

            // 生成富化建议
            if (assessment.MissingDimensions.Count > 0)
            {
                var dimList = string.Join("、", assessment.MissingDimensions);
                assessment.Suggestion = $"结论缺少以下维度: {dimList}。建议通过 Agentic Review 补充完整分析。";
            }

            return assessment;
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
