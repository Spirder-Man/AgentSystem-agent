namespace Agent1.Services
{
    /// <summary>
    /// [Bug-043 FIX] 检索链 RetrievedChunk.Metadata 的规范键名常量。
    ///
    /// 背景：Metadata 是弱类型 Dictionary&lt;string, object&gt;，读写两端各自硬编码字符串键，
    /// 无契约约束。历史上生产端以 PascalCase（SourceFile/RegulationType/Priority）为主，
    /// 但个别生产端（GpuVectorSearch、分块方法）与消费端（rag-test 接口）误用了
    /// 小写 "source"，且消费端还读了从未被任何生产端写入的 "importance" →
    /// TryGetValue 恒 miss、恒落默认值「未知」「未标注」。
    ///
    /// 本类将键名收敛为唯一真源，读写两端一律引用常量，杜绝字符串错位。
    /// 规范键名统一采用 PascalCase（沿用既有主流约定，改动面最小）。
    /// </summary>
    public static class MetadataKeys
    {
        /// <summary>来源文件名（不含扩展名）。历史遗留别名：小写 "source"。</summary>
        public const string SourceFile = "SourceFile";

        /// <summary>法规/文档类型（国标 / 园区规则 / 历史案例 / 企业制度 等）。</summary>
        public const string RegulationType = "RegulationType";

        /// <summary>优先级（高 / 中 / 低）。rag-test 接口的 importance 字段即映射此键。</summary>
        public const string Priority = "Priority";

        /// <summary>GB 法规编号（如 "GB 15603-2022"）。</summary>
        public const string RegulationNumber = "regulation_number";

        /// <summary>章节标识（如 "第3章"）。</summary>
        public const string Chapter = "chapter";

        /// <summary>章节标题。</summary>
        public const string ChapterTitle = "chapter_title";

        /// <summary>分块序号。</summary>
        public const string ChunkIndex = "chunk_index";

        /// <summary>
        /// [兼容读取] 历史上 "source"（小写）曾被少数生产端写入。消费端优先读规范键
        /// <see cref="SourceFile"/>，miss 时回退此别名，兼容尚未重灌的旧数据。
        /// </summary>
        public const string LegacySourceLower = "source";
    }
}
