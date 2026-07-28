using System.Text.RegularExpressions;

namespace Agent1.Services;

/// <summary>
/// 化工命名推断引擎 — 知识图谱 Phase 4
/// 
/// 对未收录在 ChemicalKnowledgeGraph 中的化学品名称，
/// 基于 IUPAC 系统命名法和中文命名惯例进行模式匹配，
/// 推断其可能的危险类别和化学家族。
/// 
/// 这是"正向闭环"的关键组件：即使物质不在数据库中，
/// 也能通过命名规则推断其基本属性，使 Gate/Handler 不会因陌生物质而失效。
/// </summary>
public class ChemicalNamingInference
{
    /// <summary>推断结果</summary>
    public class NamingInferenceResult
    {
        /// <summary>推断的化学家族/类别（如"烷烃"、"苯系"、"无机酸"）</summary>
        public string InferredFamily { get; set; } = "";

        /// <summary>推断的危险类别（如"易燃液体"、"腐蚀品"）</summary>
        public string InferredHazardCategory { get; set; } = "";

        /// <summary>置信度 0.0-1.0</summary>
        public double Confidence { get; set; }

        /// <summary>是否成功匹配到任何命名模式</summary>
        public bool IsRecognized => Confidence > 0;
    }

    // ════════════════════════════════════════
    // IUPAC 系统命名模式
    // ════════════════════════════════════════

    /// <summary>烷烃后缀模式：甲烷、乙烷、丙烷、丁烷、戊烷、己烷、庚烷、辛烷...</summary>
    private static readonly Regex AlkanePattern = new(
        @"(甲|乙|丙|丁|戊|己|庚|辛|壬|癸|[一二三四五六七八九十]|正|异|新|环)(烷)$",
        RegexOptions.Compiled);

    /// <summary>烯烃/炔烃：乙烯、丙烯、丁二烯、乙炔、丙炔...</summary>
    private static readonly Regex AlkenePattern = new(
        @"(甲|乙|丙|丁|戊|己|庚|辛|壬|癸|[一二三四五六七八九十]|二|三|四)(烯|炔|二烯|三烯)",
        RegexOptions.Compiled);

    /// <summary>苯及衍生物：苯、甲苯、二甲苯、苯酚、苯胺、硝基苯、氯苯、溴苯...</summary>
    private static readonly Regex BenzenePattern = new(
        @"(苯)(酚|胺|甲酸|乙酸|甲醛|乙醛|甲酮|乙酮|乙烯|乙炔|磺酸|腈|酰氯|肼|醌)?|" +
        @"(甲苯|二甲苯|三甲苯|乙苯|丙苯|异丙苯|氯苯|溴苯|碘苯|硝基苯|二硝基苯|三硝基苯|" +
        @"苯酚|苯胺|苯甲酸|苯乙酸|苯甲醛|苯乙酮|苯乙烯|苯磺酸|苯腈|联苯|二苯)",
        RegexOptions.Compiled);

    /// <summary>醇类：甲醇、乙醇、丙醇、丁醇、乙二醇、丙三醇...</summary>
    private static readonly Regex AlcoholPattern = new(
        @"(甲|乙|丙|丁|戊|己|庚|辛|壬|癸|[一二三四五六七八九十]|.*基)(醇|二醇|三醇)$",
        RegexOptions.Compiled);

    /// <summary>醛类：甲醛、乙醛、丙醛、苯甲醛...</summary>
    private static readonly Regex AldehydePattern = new(
        @"(甲|乙|丙|丁|戊|己|庚|辛|壬|癸|.*基|苯)(醛)$",
        RegexOptions.Compiled);

    /// <summary>酮类：丙酮、丁酮、戊酮、苯乙酮...</summary>
    private static readonly Regex KetonePattern = new(
        @"(甲|乙|丙|丁|戊|己|庚|辛|壬|癸|.*基|苯)(酮)$",
        RegexOptions.Compiled);

    /// <summary>羧酸类：甲酸、乙酸、丙酸、丁酸、苯甲酸、草酸、柠檬酸...</summary>
    private static readonly Regex AcidPattern = new(
        @"(甲|乙|丙|丁|戊|己|庚|辛|壬|癸|.*基|苯|草|柠檬|酒石|苹果|丁二|戊二|己二|" +
        @"油|硬脂|软脂|月桂|棕榈|水杨|乙酰水杨|苦味|氨基|磺基)(酸)$",
        RegexOptions.Compiled);

    /// <summary>酯类：甲酸甲酯、乙酸乙酯...</summary>
    private static readonly Regex EsterPattern = new(
        @"(.*酸.*酯|.*酯)$",
        RegexOptions.Compiled);

    /// <summary>醚类：乙醚、甲醚、石油醚...</summary>
    private static readonly Regex EtherPattern = new(
        @"(甲|乙|丙|丁|.*基|石油)(醚)$",
        RegexOptions.Compiled);

    /// <summary>胺类：甲胺、乙胺、苯胺、乙二胺...</summary>
    private static readonly Regex AminePattern = new(
        @"(甲|乙|丙|丁|.*基|苯|乙二|己二)(胺)$",
        RegexOptions.Compiled);

    /// <summary>卤代烃：氯甲烷、溴乙烷、四氯化碳、三氯甲烷、二氯甲烷...</summary>
    private static readonly Regex HalocarbonPattern = new(
        @"(氯|溴|碘|氟)(代|化)?(甲|乙|丙|丁|.*)(烷|烯|炔|苯|碳|仿)?|" +
        @"(四氯化碳|三氯甲烷|二氯甲烷|四氯乙烯|三氯乙烯|氯乙烯)",
        RegexOptions.Compiled);

    /// <summary>酰胺类：甲酰胺、乙酰胺、丙烯酰胺...</summary>
    private static readonly Regex AmidePattern = new(
        @"(甲|乙|丙|丁|.*基|苯|丙烯)(酰胺)$",
        RegexOptions.Compiled);

    /// <summary>腈类：乙腈、丙烯腈、苯甲腈...</summary>
    private static readonly Regex NitrilePattern = new(
        @"(甲|乙|丙|丁|.*基|苯|丙烯)(腈)$",
        RegexOptions.Compiled);

    // ════════════════════════════════════════
    // 无机物命名模式
    // ════════════════════════════════════════

    /// <summary>强酸：硫酸、硝酸、盐酸、磷酸、氢氟酸、高氯酸...</summary>
    private static readonly Regex InorganicAcidPattern = new(
        @"(硫|硝|盐|磷|氢氟|氢溴|氢碘|高氯|氯|次氯|亚硫|亚硝|硼|硅|铬|氢氰)(酸)$",
        RegexOptions.Compiled);

    /// <summary>碱/氢氧化物：氢氧化钠、氢氧化钾、氢氧化钙...</summary>
    private static readonly Regex HydroxidePattern = new(
        @"氢氧化(.*|钠|钾|钙|镁|铝|铁|铜|锌|钡|锂|铵)$",
        RegexOptions.Compiled);

    /// <summary>氧化物：氧化钙、氧化铁、二氧化硫、三氧化硫...</summary>
    private static readonly Regex OxidePattern = new(
        @"(一|二|三|四|五|六|七|八|九|十|过)?氧化(二|三)?(.*|钙|铁|铜|锌|铝|镁|钠|钾|碳|硫|氮|磷|硅|氯)$",
        RegexOptions.Compiled);

    /// <summary>盐类模式：氯化钠、硫酸铜、碳酸钙、硝酸银、高锰酸钾...</summary>
    private static readonly Regex SaltPattern = new(
        @"(氯化|硫酸|硝酸|碳酸|磷酸|醋酸|草酸|硅酸|硼酸|氢氧|铬酸|重铬酸|高锰酸|" +
        @"次氯酸|亚硫酸|亚硝酸|硫代硫酸|氟化|溴化|碘化)(.*|钠|钾|钙|镁|铝|铁|铜|锌|钡|银|铵|铅|锰|钴|镍)",
        RegexOptions.Compiled);

    /// <summary>过氧化物：过氧化钠、过氧化苯甲酰...</summary>
    private static readonly Regex PeroxidePattern = new(
        @"过氧化(.*|钠|钾|氢|钙|钡|苯甲酰|二苯甲酰|甲乙酮)$",
        RegexOptions.Compiled);

    // ════════════════════════════════════════
    // 中文偏旁识别
    // ════════════════════════════════════════

    /// <summary>金属元素（钅字旁 + 特殊）</summary>
    private static readonly Regex MetalElementPattern = new(
        @"[钠钾钙镁铝铁铜锌钡银铂金锰钴镍铬铅锡钨钼钛钒锆铌钽铪锂铷铯铍锶镭锗镓铟铊钪钇]",
        RegexOptions.Compiled);

    /// <summary>气态非金属（气字头）</summary>
    private static readonly Regex GasNonmetalPattern = new(
        @"[氢氧氮氯氟氦氖氩氪氙氡]",
        RegexOptions.Compiled);

    /// <summary>固态非金属（石字旁）</summary>
    private static readonly Regex SolidNonmetalPattern = new(
        @"[碳硫磷硅碘硒碲硼砷]",
        RegexOptions.Compiled);

    // ════════════════════════════════════════
    // 数字模式识别
    // ════════════════════════════════════════

    private static readonly Regex CasPattern = new(
        @"\b\d{2,7}-\d{2}-\d\b",
        RegexOptions.Compiled);

    private static readonly Regex UnPattern = new(
        @"\bUN\d{4}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // ════════════════════════════════════════
    // 推断引擎
    // ════════════════════════════════════════

    /// <summary>
    /// 对输入文本中的化学物质名称进行命名推断。
    /// 从文本中提取可能的化学品名，并逐个推断其家族和危险类别。
    /// </summary>
    public NamingInferenceResult InferSubstanceType(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new NamingInferenceResult();

        // 按常⻅分隔符提取可能的名词片段
        var segments = SplitChemicalTerms(text);

        foreach (var segment in segments)
        {
            var result = InferSingle(segment);
            if (result.IsRecognized) return result;
        }

        return new NamingInferenceResult();
    }

    /// <summary>对单个术语进行推断</summary>
    private NamingInferenceResult InferSingle(string term)
    {
        if (term.Length < 2) return new NamingInferenceResult();

        // ── IUPAC 有机物 ──
        if (AlkanePattern.IsMatch(term))
            return new NamingInferenceResult { InferredFamily = "烷烃", InferredHazardCategory = "易燃液体", Confidence = 0.8 };
        if (AlkenePattern.IsMatch(term))
            return new NamingInferenceResult { InferredFamily = "烯烃/炔烃", InferredHazardCategory = "易燃气体/易燃液体", Confidence = 0.8 };
        if (BenzenePattern.IsMatch(term))
            return new NamingInferenceResult { InferredFamily = "苯系", InferredHazardCategory = "易燃液体/有毒", Confidence = 0.7 };
        if (AlcoholPattern.IsMatch(term))
            return new NamingInferenceResult { InferredFamily = "醇类", InferredHazardCategory = "易燃液体", Confidence = 0.8 };
        if (AldehydePattern.IsMatch(term))
            return new NamingInferenceResult { InferredFamily = "醛类", InferredHazardCategory = "易燃液体/有毒", Confidence = 0.7 };
        if (KetonePattern.IsMatch(term))
            return new NamingInferenceResult { InferredFamily = "酮类", InferredHazardCategory = "易燃液体", Confidence = 0.8 };
        if (AcidPattern.IsMatch(term))
            return new NamingInferenceResult { InferredFamily = "羧酸", InferredHazardCategory = "腐蚀品/易燃液体", Confidence = 0.7 };
        if (EsterPattern.IsMatch(term))
            return new NamingInferenceResult { InferredFamily = "酯类", InferredHazardCategory = "易燃液体", Confidence = 0.8 };
        if (EtherPattern.IsMatch(term))
            return new NamingInferenceResult { InferredFamily = "醚类", InferredHazardCategory = "易燃液体", Confidence = 0.8 };
        if (HalocarbonPattern.IsMatch(term))
            return new NamingInferenceResult { InferredFamily = "卤代烃", InferredHazardCategory = "有毒/麻醉", Confidence = 0.7 };
        if (AminePattern.IsMatch(term))
            return new NamingInferenceResult { InferredFamily = "胺类", InferredHazardCategory = "易燃液体/腐蚀品", Confidence = 0.7 };
        if (AmidePattern.IsMatch(term))
            return new NamingInferenceResult { InferredFamily = "酰胺", InferredHazardCategory = "有毒", Confidence = 0.6 };
        if (NitrilePattern.IsMatch(term))
            return new NamingInferenceResult { InferredFamily = "腈类", InferredHazardCategory = "易燃液体/剧毒", Confidence = 0.7 };

        // ── 无机物 ──
        if (InorganicAcidPattern.IsMatch(term))
            return new NamingInferenceResult { InferredFamily = "无机酸", InferredHazardCategory = "腐蚀品", Confidence = 0.9 };
        if (HydroxidePattern.IsMatch(term))
            return new NamingInferenceResult { InferredFamily = "碱/氢氧化物", InferredHazardCategory = "腐蚀品", Confidence = 0.9 };
        if (OxidePattern.IsMatch(term))
            return new NamingInferenceResult { InferredFamily = "氧化物", InferredHazardCategory = "氧化剂/腐蚀品", Confidence = 0.6 };
        if (PeroxidePattern.IsMatch(term))
            return new NamingInferenceResult { InferredFamily = "过氧化物", InferredHazardCategory = "氧化剂/爆炸物", Confidence = 0.8 };
        if (SaltPattern.IsMatch(term))
            return new NamingInferenceResult { InferredFamily = "无机盐", InferredHazardCategory = "不确定（需具体判断）", Confidence = 0.5 };

        // ── 偏旁推断（弱信号，低置信度） ──
        if (term.Length <= 3)
        {
            if (MetalElementPattern.IsMatch(term) && SolidNonmetalPattern.IsMatch(term))
                return new NamingInferenceResult { InferredFamily = "金属-非金属化合物", InferredHazardCategory = "不确定", Confidence = 0.4 };
            if (GasNonmetalPattern.IsMatch(term) && MetalElementPattern.IsMatch(term))
                return new NamingInferenceResult { InferredFamily = "金属-非金属化合物", InferredHazardCategory = "不确定", Confidence = 0.3 };
        }

        return new NamingInferenceResult();
    }

    /// <summary>
    /// 从用户查询文本中分离可能的化学品术语。
    /// 按中文常见分隔符（"和"、"与"、"、"、"可以"、"能"、"不能"等）分割。
    /// </summary>
    public static List<string> SplitChemicalTerms(string text)
    {
        // 按常见分隔符/功能词切分
        var parts = Regex.Split(text,
            @"(?:和|与|、|,|，|可以|不能|能否|能和|不可|放在|搁在|堆在|存在|存放于|" +
            @"储存于|一起|一块|同库|同区|混合|能不能|可不可以|可以不可以|能|吗|\?|？|\s+)");

        var terms = new List<string>();
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.Length >= 2 && !IsFunctionWord(trimmed))
                terms.Add(trimmed);
        }
        return terms;
    }

    private static readonly HashSet<string> FunctionWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "的", "是", "在", "有", "会", "吗", "呢", "啊", "吧", "请", "问",
        "如何", "怎么", "什么", "是否", "需要", "应该", "必须", "注意"
    };

    private static bool IsFunctionWord(string word)
        => FunctionWords.Contains(word) || word.Length <= 1;
}
