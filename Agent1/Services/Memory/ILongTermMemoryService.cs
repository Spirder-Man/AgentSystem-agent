using Agent1.Models;

namespace Agent1.Services
{
    /// <summary>
    /// Phase 2-3: 长期记忆服务——跨会话持久化记忆的 Record/Retrieve 管道。
    /// Record: 对话 → LLM事实提取 → 向量化 → pgvector
    /// Retrieve: Query → 向量化 → pgvector语义检索 → 重排序 → 注入上下文
    /// </summary>
    public interface ILongTermMemoryService
    {
        // ═══ Record 管道 ═══

        /// <summary>从对话中提取并持久化长期记忆事实</summary>
        Task<List<LongTermMemoryRecord>> RecordAsync(
            string userId, string userInput, string assistantResponse,
            Guid? sourceSessionId = null, int sourceTurnIndex = 0,
            IReadOnlyDictionary<string, string>? toolResults = null);

        /// <summary>批量添加记忆记录（迁移/导入用）</summary>
        Task AddMemoryAsync(LongTermMemoryRecord record);

        // ═══ Retrieve 管道 ═══

        /// <summary>语义检索相关长期记忆（按用户）</summary>
        Task<List<LongTermMemoryRecord>> RetrieveAsync(
            string userId, string query, int topK = 5,
            string? memoryTypeFilter = null);

        /// <summary>按关键词搜索记忆（数据库直接 LIKE 查询）</summary>
        Task<List<LongTermMemoryRecord>> SearchByKeywordAsync(
            string userId, string keyword, int topK = 10);

        // ═══ 生命周期管理 ═══

        /// <summary>命中反馈：增加计数 + 更新时间</summary>
        Task RecordHitAsync(Guid memoryId);

        /// <summary>软删除（标记 is_active=false）</summary>
        Task DeactivateAsync(Guid memoryId);

        /// <summary>解决冲突：同一类型+相似内容的新记录到达时停用旧记录</summary>
        Task ResolveConflictsAsync(string userId, string memoryType, string newContent);

        /// <summary>清理过期/已停用记录（超过保留期限的物理删除）</summary>
        Task<int> CleanupAsync(int retentionDays = 180);

        // ═══ 统计 ═══

        /// <summary>获取指定用户的长期记忆统计</summary>
        Task<LongTermMemoryStats> GetStatsAsync(string userId);
    }
}
