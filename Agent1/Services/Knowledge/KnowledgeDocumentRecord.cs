namespace Agent1.Services
{
    /// <summary>
    /// 知识库文档级记录 — 对应 knowledge_documents 表
    /// 每个物理文件一行，承载文件来源和法规属性元数据
    /// </summary>
    public class KnowledgeDocumentRecord
    {
        /// <summary>数据库主键 ID（新增时为 0）</summary>
        public int Id { get; set; } = 0;

        // ── 文件来源 ──

        /// <summary>相对路径（如 "化工专业条例/化工专业条例/GB 30000.7-2013.pdf"）</summary>
        public string SourcePath { get; set; } = string.Empty;

        /// <summary>展示名（如 "GB 30000.7-2013 化学品分类和标签规范 第7部分"）</summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>文件格式：pdf / doc / docx / txt</summary>
        public string FileFormat { get; set; } = string.Empty;

        /// <summary>文件大小（字节），可选</summary>
        public long? FileSizeBytes { get; set; }

        // ── 法规属性 ──

        /// <summary>法规类型：国标 / 园区规则 / 历史案例 / 企业制度 / 化工专业条例</summary>
        public string RegulationType { get; set; } = "通用";

        /// <summary>法规编号（如 "GB 30000.7-2013"）</summary>
        public string? RegulationNumber { get; set; }

        /// <summary>法规标题（如 "化学品分类和标签规范 第7部分：易燃液体"）</summary>
        public string? RegulationTitle { get; set; }

        /// <summary>优先级：高 / 中 / 低</summary>
        public string Priority { get; set; } = "中";

        // ── H166 层级（仅企业制度类文件使用）──

        /// <summary>父级分类路径（如 "1.法律法规/1.1识别和获取"）</summary>
        public string? ParentCategory { get; set; }

        // ── 质量标记 ──

        /// <summary>提取质量：good / partial / failed</summary>
        public string? ExtractionQuality { get; set; } = "good";

        /// <summary>PDF 原始页数</summary>
        public int? PageCount { get; set; }

        /// <summary>是否全文索引（false=仅文件名摘要入库）</summary>
        public bool IsFullText { get; set; } = true;

        /// <summary>该文件产生的分块总数</summary>
        public int TotalChunks { get; set; } = 0;

        // ── 变更追踪 ──

        /// <summary>文件内容 SHA-256 哈希，用于增量更新检测</summary>
        public string? ContentHash { get; set; }

        /// <summary>文件最后修改时间</summary>
        public DateTime? LastModified { get; set; }

        /// <summary>入库时间</summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
