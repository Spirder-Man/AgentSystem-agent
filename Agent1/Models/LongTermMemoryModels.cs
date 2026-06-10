namespace Agent1.Models
{
    /// <summary>
    /// Phase 2.1: 长期记忆记录 — 对应 pgvector 表 long_term_memories
    /// </summary>
    public class LongTermMemoryRecord
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string UserId { get; set; } = "default";
        
        /// <summary>记忆类型: user_preference | chemical_fact | compliance_experience | regulation_ref</summary>
        public string MemoryType { get; set; } = "chemical_fact";
        
        public string Content { get; set; } = "";
        
        /// <summary>768维向量 (nomic-embed-text)</summary>
        public float[]? Embedding { get; set; }
        
        public Guid? SourceSessionId { get; set; }
        public int SourceTurnIndex { get; set; }
        
        /// <summary>重要性权重 0-1，初始由 LLM 评估，后续随命中次数动态调整</summary>
        public float Importance { get; set; } = 0.5f;
        
        public int HitCount { get; set; }
        public DateTime? LastHitAt { get; set; }
        
        /// <summary>软删除标记</summary>
        public bool IsActive { get; set; } = true;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// FactExtractor 输出：从对话中提取的长期记忆候选事实
    /// </summary>
    public class ExtractedFact
    {
        /// <summary>user_preference | chemical_fact | compliance_experience | regulation_ref</summary>
        public string Type { get; set; } = "chemical_fact";
        
        public string Content { get; set; } = "";
        
        /// <summary>LLM 评估的重要性 0-1</summary>
        public float Importance { get; set; } = 0.5f;
    }

    /// <summary>长期记忆统计</summary>
    public class LongTermMemoryStats
    {
        public int TotalCount { get; set; }
        public int ActiveCount { get; set; }
        public int RegulationRefCount { get; set; }
        public int ChemicalFactCount { get; set; }
        public int ComplianceExperienceCount { get; set; }
        public int UserPreferenceCount { get; set; }
        public int TotalHitCount { get; set; }
    }
}
