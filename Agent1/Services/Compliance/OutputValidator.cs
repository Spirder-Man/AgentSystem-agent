using System.Text.RegularExpressions;
using Agent1.Models;

namespace Agent1.Services;

/// <summary>
/// 化工合规输出校验器 —— 零失误架构安全组件
/// 
/// 职责:
///   1. 法规编号强制校验 —— LLM输出中引用的GB编号必须在工具返回的允许列表中
///   2. 数值一致性校验 —— 安全距离/临界量等数值必须与工具返回一致
///   3. 引用锁定 —— 仅保留已验证的法规引用，移除或标注未验证引用
///   4. 不确定度标识 —— 根据数据来源标注置信度等级
///   5. 标准化拒绝模板 —— 数据库/RAG均未命中时生成标准拒绝回复
/// 
/// 原则: "宁可拒绝也不能错" —— 无法验证的信息必须标注为未验证或拒绝
/// </summary>
public static class OutputValidator
{
    /// <summary>置信度等级</summary>
    public enum ConfidenceLevel
    {
        /// <summary>数据库命中 + 法规编号精确匹配 + 数值一致</summary>
        HIGH_CONFIDENCE,
        /// <summary>RAG检索命中 + 法规编号存在但非数据库确认</summary>
        MEDIUM_CONFIDENCE,
        /// <summary>RAG检索无精确命中 / 仅模糊匹配 / 硬编码字典</summary>
        LOW_CONFIDENCE,
        /// <summary>任何数据源均未命中 → 应触发标准化拒绝</summary>
        UNKNOWN
    }

    /// <summary>校验结果</summary>
    public class ValidationResult
    {
        /// <summary>是否存在幻觉（法规编号不在允许列表中）</summary>
        public bool HasHallucination { get; set; }
        /// <summary>是否存在数值矛盾</summary>
        public bool HasContradiction { get; set; }
        /// <summary>置信度等级</summary>
        public ConfidenceLevel Confidence { get; set; }
        /// <summary>违规的法规编号列表</summary>
        public List<string> HallucinatedRegulations { get; set; } = new();
        /// <summary>矛盾描述列表</summary>
        public List<string> Contradictions { get; set; } = new();
        /// <summary>经校验后的输出（引用已锁定/标注）</summary>
        public string SanitizedOutput { get; set; } = "";
        /// <summary>原始输出</summary>
        public string OriginalOutput { get; set; } = "";
    }

    // ════════════════════════════════════════
    // 公共接口
    // ════════════════════════════════════════

    /// <summary>
    /// 校验LLM输出：提取法规编号与工具返回的允许列表比对，检测幻觉和矛盾。
    /// </summary>
    /// <param name="llmOutput">LLM生成的完整回复</param>
    /// <param name="toolOutput">工具返回的内容（含 [REGULATIONS: ...] 标签）</param>
    /// <param name="qualityLevel">工具返回的质量等级</param>
    /// <returns>校验结果</returns>
    public static ValidationResult Validate(string llmOutput, string? toolOutput, QualityLevel? qualityLevel = null)
    {
        var result = new ValidationResult
        {
            OriginalOutput = llmOutput,
            SanitizedOutput = llmOutput
        };

        if (string.IsNullOrWhiteSpace(llmOutput))
        {
            result.Confidence = ConfidenceLevel.UNKNOWN;
            result.SanitizedOutput = GetRefusalTemplate("（空输出）", new List<string>());
            return result;
        }

        // Step 1: 从工具输出中提取允许的法规编号
        var allowedRegs = ExtractAllowedRegulations(toolOutput);

        // Step 2: 从 LLM 输出中提取引用的法规编号
        var llmRegs = ExtractGbNumbers(llmOutput);

        // Step 3: 检测幻觉 —— LLM引用了工具未返回的法规编号
        foreach (var reg in llmRegs)
        {
            if (!IsRegulationAllowed(reg, allowedRegs))
            {
                result.HasHallucination = true;
                result.HallucinatedRegulations.Add(reg);
            }
        }

        // Step 4: 数值一致性校验
        CheckNumericConsistency(llmOutput, toolOutput, result);

        // Step 5: 确定置信度
        result.Confidence = DetermineConfidence(qualityLevel, result.HasHallucination, result.HasContradiction,
            allowedRegs.Count > 0, llmRegs.Count > 0);

        // Step 6: 引用锁定 —— 标注未验证的法规引用
        if (result.HasHallucination)
        {
            result.SanitizedOutput = LockCitations(llmOutput, allowedRegs, result.HallucinatedRegulations);
        }

        return result;
    }

    /// <summary>
    /// 标准化拒绝模板 —— 当数据库和RAG均无法给出确定结论时使用。
    /// 原则: "宁可拒绝也不能错"
    /// </summary>
    public static string GetRefusalTemplate(string substanceName, List<string> searchedRegulations)
    {
        var regList = searchedRegulations.Count > 0
            ? string.Join("\n", searchedRegulations.Select(r => $"  - {r}"))
            : "  - 未检索到相关法规";

        return $@"⚠️ 基于现有资料无法给出确定结论，建议联系安环部门人工确认。

【已检索法规】:
{regList}

【不确定原因】:
- 「{substanceName}」未收录于结构化危险化学品数据库
- 知识库检索未返回直接匹配的分类/距离信息

【建议】:
1. 查阅国家标准全文公开系统 (openstd.samr.gov.cn)
2. 联系企业安全环保部门人工判定
3. 将「{substanceName}」的SDS安全数据表上传至知识库以增强检索能力

[判定:is_compliant=无法判定]
[置信度: UNKNOWN]";
    }

    /// <summary>
    /// 根据质量等级和数据来源确定置信度标签字符串
    /// </summary>
    public static string GetConfidenceTag(QualityLevel? quality)
    {
        return quality switch
        {
            QualityLevel.DATABASE_HIT => "[置信度: HIGH]",
            QualityLevel.RAG_HIT => "[置信度: MEDIUM]",
            QualityLevel.DICTIONARY_HIT => "[置信度: LOW]",
            _ => "[置信度: UNKNOWN]"
        };
    }

    // ════════════════════════════════════════
    // 内部方法
    // ════════════════════════════════════════

    /// <summary>从工具输出中提取允许的法规编号</summary>
    private static HashSet<string> ExtractAllowedRegulations(string? toolOutput)
    {
        var regs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(toolOutput))
            return regs;

        // 匹配 [REGULATIONS: GB30000.7, GB50160, ...]
        var regBlock = Regex.Match(toolOutput, @"\[REGULATIONS:\s*([^\]]+)\]");
        if (regBlock.Success)
        {
            foreach (var reg in regBlock.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = reg.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                    regs.Add(trimmed);
            }
        }
        else
        {
            // [E6 FIX] 兜底：仅在没有 [REGULATIONS:] 标签时，才从全文提取 GB 编号
            // 避免工具输出中的解释性文本（如"参考标准：GB30000.2"）被误加入白名单
            foreach (var gb in ExtractGbNumbers(toolOutput))
                regs.Add(gb);
        }

        return regs;
    }

    /// <summary>从文本中提取所有 GB 编号</summary>
    private static HashSet<string> ExtractGbNumbers(string text)
    {
        var regs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text))
            return regs;

        // P1-2: 统一使用 GbCodeHelper.GbCodePattern
        var matches = GbCodeHelper.GbCodePattern.Matches(text);
        foreach (Match m in matches)
        {
            var normalized = Regex.Replace(m.Value, @"\s+", " ").Trim();
            regs.Add(normalized);
        }
        return regs;
    }

    /// <summary>检查法规编号是否在允许列表中（仅精确匹配，防止跨标准误放行）</summary>
    private static bool IsRegulationAllowed(string reg, HashSet<string> allowedRegs)
    {
        if (allowedRegs.Count == 0)
            return true; // 无允许列表时不拦截（可能是无工具调用的场景）

        var nReg = NormalizeRegNumber(reg);
        foreach (var allowed in allowedRegs)
        {
            var nAllowed = NormalizeRegNumber(allowed);
            // 仅精确匹配：GB 30000.2 ≠ GB 30000.27（两个独立标准）
            if (nReg.Equals(nAllowed, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 标准化法规编号：去空格/连字符/斜杠/T标记，剥离年份后缀，
    /// 确保同一标准的不同写法（GB 30000.27、GB/T 30000.27-2013）归一为同一键。
    /// </summary>
    private static string NormalizeRegNumber(string reg)
    {
        // Step 1: 剥离年份后缀（-2020, —2013 等）
        var normalized = Regex.Replace(reg.Trim(), @"[\s\-—]\d{4}$", "");
        // Step 2: 去除空格、连字符、斜杠、T（GB/T → GB）
        normalized = Regex.Replace(normalized, @"[\s\-/T]", "");
        return normalized.ToUpperInvariant();
    }

    /// <summary>数值一致性校验</summary>
    private static void CheckNumericConsistency(string llmOutput, string? toolOutput, ValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(toolOutput))
            return;

        // 从工具输出中提取距离值 [DISTANCE: Xm]
        var distMatch = Regex.Match(toolOutput, @"\[DISTANCE:\s*(\d+(?:\.\d+)?)\s*(米|m)\]");
        if (distMatch.Success && double.TryParse(distMatch.Groups[1].Value, out var toolDistance))
        {
            var llmDistances = ExtractNumericDistances(llmOutput);
            foreach (var ld in llmDistances)
            {
                // 允许 5% 误差（四舍五入差异）
                if (Math.Abs(ld - toolDistance) / toolDistance > 0.05)
                {
                    result.HasContradiction = true;
                    result.Contradictions.Add(
                        $"安全距离矛盾: 工具返回 {toolDistance}m，LLM输出引用 {ld}m");
                }
            }
        }

        // 从工具输出中提取临界量
        var thresholdMatch = Regex.Match(toolOutput, @"临界量[：:]\s*\*?\*?(\d+(?:\.\d+)?)\s*吨");
        if (thresholdMatch.Success && double.TryParse(thresholdMatch.Groups[1].Value, out var toolThreshold))
        {
            var llmThresholds = ExtractThresholds(llmOutput);
            foreach (var lt in llmThresholds)
            {
                if (Math.Abs(lt - toolThreshold) > 0.1)
                {
                    result.HasContradiction = true;
                    result.Contradictions.Add(
                        $"临界量矛盾: 工具返回 {toolThreshold}吨，LLM输出引用 {lt}吨");
                }
            }
        }
    }

    private static List<double> ExtractNumericDistances(string text)
    {
        var distances = new List<double>();
        var matches = Regex.Matches(text, @"(\d+(?:\.\d+)?)\s*(?:米|m)");
        foreach (Match m in matches)
        {
            if (double.TryParse(m.Groups[1].Value, out var d))
                distances.Add(d);
        }
        return distances;
    }

    private static List<double> ExtractThresholds(string text)
    {
        var thresholds = new List<double>();
        var matches = Regex.Matches(text, @"(\d+(?:\.\d+)?)\s*吨");
        foreach (Match m in matches)
        {
            if (double.TryParse(m.Groups[1].Value, out var t))
                thresholds.Add(t);
        }
        return thresholds;
    }

    /// <summary>确定置信度等级</summary>
    private static ConfidenceLevel DetermineConfidence(QualityLevel? quality, bool hasHallucination,
        bool hasContradiction, bool hasToolRegs, bool hasLlmRegs)
    {
        // 幻觉或矛盾 → 置信度降级
        if (hasHallucination || hasContradiction)
            return ConfidenceLevel.LOW_CONFIDENCE;

        return quality switch
        {
            QualityLevel.DATABASE_HIT => ConfidenceLevel.HIGH_CONFIDENCE,
            QualityLevel.RAG_HIT => ConfidenceLevel.MEDIUM_CONFIDENCE,
            QualityLevel.DICTIONARY_HIT => ConfidenceLevel.LOW_CONFIDENCE,
            QualityLevel.FALLBACK => ConfidenceLevel.UNKNOWN,
            _ => hasToolRegs ? ConfidenceLevel.MEDIUM_CONFIDENCE : ConfidenceLevel.UNKNOWN
        };
    }

    /// <summary>
    /// 引用锁定：将 LLM 输出中不在允许列表中的法规引用标注为 [未验证引用]。
    /// 若移除未验证引用后结论失去支撑 → 在末尾追加降级声明。
    /// </summary>
    private static string LockCitations(string llmOutput, HashSet<string> allowedRegs, List<string> hallucinated)
    {
        var output = llmOutput;

        foreach (var hall in hallucinated)
        {
            // 用宽松的正则匹配该法规编号的所有变体
            var escaped = Regex.Escape(hall);
            var pattern = $@"{escaped}";
            output = Regex.Replace(output, pattern, $"{hall} [未验证引用]", RegexOptions.IgnoreCase);
        }

        // 追加降级声明
        if (hallucinated.Count > 0)
        {
            output += $"\n\n⚠️ [引用校验] 以下法规编号未在工具返回中确认，可能为模型推断或已废止版本: {string.Join(", ", hallucinated)}。请以工具返回的法规编号为准。";
        }

        return output;
    }
}
