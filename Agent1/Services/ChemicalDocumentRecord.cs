namespace Agent1.Services
{
    /// <summary>
    /// 化工文档入库记录 — 承载全链路元数据，解决管道断裂问题
    /// P0修复：将 PdfExtractor / TextCleaner / SemanticChunker 产出的
    /// 所有元数据统一携带到 DatabaseService
    /// </summary>
    public class ChemicalDocumentRecord
    {
        /// <summary>块文本内容（必填）</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>法规类型：国标 / 园区规则 / 历史案例</summary>
        public string RegulationType { get; set; } = "通用";

        /// <summary>优先级：高 / 中 / 低</summary>
        public string Priority { get; set; } = "中";

        // ── PdfExtractor 产出 ──

        /// <summary>法规编号（如 "GB 30000.7-2013"）</summary>
        public string? RegulationNumber { get; set; }

        /// <summary>提取质量评估：good / partial / failed</summary>
        public string? ExtractionQuality { get; set; }

        /// <summary>原始页码（从 1 开始）</summary>
        public int? PageNumber { get; set; }

        // ── SemanticChunker 产出 ──

        /// <summary>章节标题（如 "第3章 术语和定义"）</summary>
        public string? ChapterTitle { get; set; }

        /// <summary>条款编号（如 "3.1"）</summary>
        public string? ClauseNumber { get; set; }

        /// <summary>同文档内块序号（从 0 开始）</summary>
        public int? ChunkIndex { get; set; }

        // ── 文件来源 ──

        /// <summary>源文件名</summary>
        public string? SourceFile { get; set; }

        /// <summary>危化品类型（如 "甲苯"、"通用"）</summary>
        public string? ChemicalType { get; set; }

        // ── 向量（由 LlmService 产出后填入）──

        /// <summary>Ollama 生成的向量嵌入</summary>
        public float[]? Embedding { get; set; }

        // ── 质量判定 ──

        /// <summary>
        /// 是否为脏数据（应拦截不入库）：
        ///   - 提取质量标记为 failed
        ///   - 内容为空或过短（&lt; 20 字符）
        /// </summary>
        public bool IsDirty =>
            ExtractionQuality == "failed"
            || string.IsNullOrWhiteSpace(Content)
            || Content.Length < 20;
    }
}
