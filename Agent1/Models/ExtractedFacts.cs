namespace Agent1.Models
{
    /// <summary>
    /// 双通道解耦架构 — 从工具执行结果中提取的结构化事实。
    /// 事实通道（FactAssembler）直接渲染此数据，不走 LLM，
    /// 确保法规引用 100% 确定性。
    /// </summary>
    public class ExtractedFacts
    {
        /// <summary>工具返回的法规编号（唯一白名单来源）</summary>
        public List<string> RegulationRefs { get; set; } = new();

        /// <summary>物质名 → 危险类别描述，如 "苯" → "易燃液体,类别2"</summary>
        public Dictionary<string, string> HazardCategories { get; set; } = new();

        /// <summary>物质对键(A|B) → 合规判定，如 "硝酸|乙酸" → "不得同库储存"</summary>
        public Dictionary<string, string> ComplianceVerdicts { get; set; } = new();

        /// <summary>设施对 → 距离描述，如 "甲类仓库-明火点" → "30米"</summary>
        public Dictionary<string, string> SafetyDistances { get; set; } = new();

        /// <summary>物质名 → 临界量描述，如 "苯" → "50吨"</summary>
        public Dictionary<string, string> Thresholds { get; set; } = new();

        /// <summary>标准编号 → 版本描述，如 "GB 15603" → "GB 15603-2013"</summary>
        public Dictionary<string, string> RegulationVersions { get; set; } = new();

        /// <summary>物质名 → 属性描述</summary>
        public Dictionary<string, string> ChemicalProperties { get; set; } = new();

        /// <summary>所有工具返回的原始文本（供降级调试）</summary>
        public List<string> RawToolOutputs { get; set; } = new();

        /// <summary>是否为信息查询意图</summary>
        public bool IsInfoQuery { get; set; }

        /// <summary>是否有任何工具被触发</summary>
        public bool HasAnyToolResult =>
            HazardCategories.Count > 0 ||
            ComplianceVerdicts.Count > 0 ||
            SafetyDistances.Count > 0 ||
            Thresholds.Count > 0 ||
            RegulationVersions.Count > 0 ||
            ChemicalProperties.Count > 0;

        /// <summary>合规判定汇总（用于 LLM 解读）</summary>
        public string? OverallComplianceVerdict { get; set; }

        /// <summary>汇总后的法规编号列表（去重、排序）</summary>
        public List<string> GetUniqueRegulations()
        {
            return RegulationRefs
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(r => r)
                .ToList();
        }

        /// <summary>生成供 LLM 解读的事实摘要（不含法规编号）</summary>
        public string ToSanitizedSummary()
        {
            var parts = new List<string>();

            foreach (var (substance, category) in HazardCategories)
                parts.Add($"- {substance}: {category}");

            foreach (var (pair, verdict) in ComplianceVerdicts)
            {
                var subs = pair.Split('|');
                if (subs.Length == 2)
                    parts.Add($"- {subs[0]} 与 {subs[1]}: {verdict}");
                else
                    parts.Add($"- {pair}: {verdict}");
            }

            foreach (var (facility, distance) in SafetyDistances)
                parts.Add($"- {facility}: 安全距离 {distance}");

            foreach (var (substance, threshold) in Thresholds)
                parts.Add($"- {substance}: 重大危险源临界量 {threshold}");

            foreach (var (standard, version) in RegulationVersions)
                parts.Add($"- {standard}: {version}");

            foreach (var (substance, props) in ChemicalProperties)
                parts.Add($"- {substance}: {props}");

            return parts.Count > 0
                ? string.Join("\n", parts)
                : "（工具未返回结构化数据）";
        }
    }
}
