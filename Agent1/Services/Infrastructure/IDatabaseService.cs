using System.Data;
using Agent1.Models;

namespace Agent1.Services
{
    public interface IDatabaseService
    {
        Task<IDbConnection> GetConnectionAsync();
        //每次调用给你一个全新的已连接数据库的连接。谁要查数据，先来这儿领钥匙。
        Task<bool> TestConnectionAsync();
        //探探数据库还活着吗？活着→true，死了→打印原因并返回 false。
        // 注意它绝不抛异常——这正好是你学过的入口原则三的活例：
        // TestConnection 是预期分支（数据库可能挂），
        // 所以用 return false 而不是 throw 报警器。启动时调它判断"能不能继续"。
        Task<string> GetDatabaseInfoAsync();
        //一次 SQL 问三件事：我在哪个库？什么版本？我是谁？ 拼成人类可读的字符串返回——给诊断/启动横幅用的。
        Task<List<string>> GetTableNamesAsync();
        //问 PostgreSQL 的"元数据字典"（information_schema）：你这个库里有哪些表？返回 List<string>——给"看库结构"的功能用。
        Task InitializeDatabaseAsync();
        //启动时跑一次"没有就建"仪式——把项目需要的 9 张表全部 CREATE TABLE IF NOT EXISTS。
        // 这也是为什么注释说"启动加速：检查数据库是否已有文档"（接口 L30）——表建好才能干活。
        //三板斧骨架：5 个方法全是 CreateConnection → OpenAsync → 执行 SQL → 返回——门面服务的标准样板
        //TestConnection 的 catch = 你笔记里的"return 管人的问题"：数据库挂了是预期场景，友好返回 false 而不是甩堆栈
        // Initialize 幂等：IF NOT EXISTS 风格，启动跑 10 次也不会炸——可重入设计（呼应 Fail-Fast 的"早退比晚退好"：它先确保地基存在才让上层跑）

        // 文档管理（P0修复：接收完整 ChemicalDocumentRecord，承载全链路元数据）
        Task AddChemicalDocumentAsync(ChemicalDocumentRecord record);

        // Sprint 1: 批量文档入库（一次 DB 连接写入多条，减少往返开销）
        Task AddChemicalDocumentsBatchAsync(List<ChemicalDocumentRecord> records);

        // 兼容旧版签名（标记为过时，逐步迁移）
        [Obsolete("请使用 AddChemicalDocumentAsync(ChemicalDocumentRecord) 代替")]
        Task AddChemicalDocumentAsync(string content, string regulationType, string priority, string? sourceFile = null, string? chemicalType = null, float[]? embedding = null);

        // 清空向量存储（与 BM25 Clear 同步）
        Task ClearChemicalDocumentsAsync();

        // 向量检索
        Task<List<RetrievedChunk>> VectorSearchAsync(string query, float[] queryEmbedding, int topK = 5);

        // 启动加速：检查数据库是否已有文档（避免重复嵌入生成）
        Task<int> GetChemicalDocumentCountAsync();
        /// <summary>获取全量分块文本（双层表 JOIN，返回完整元数据）</summary>
        Task<List<ChemicalDocumentRecord>> GetAllChemicalDocumentTextsAsync();

        // Sprint 2: 加载全量文档及向量嵌入（GPU 索引重建 / 内存检索用）
        Task<List<ChemicalDocumentRecord>> GetAllChemicalDocumentsWithEmbeddingsAsync();
        // [P3 增量更新] 按源文件删除文档（文件被删除时清理 DB 分块）
        Task<int> DeleteChemicalDocumentsBySourceAsync(string sourceFile);
        // [增量全格式] 按相对路径删除新表文档记录（knowledge_chunks 经 ON DELETE CASCADE 级联清理）
        Task<int> DeleteKnowledgeDocumentBySourcePathAsync(string sourcePath);

        // ═══════════════════════════════════════════
        // 知识库双层表架构 — 文档级 + 分块级写入
        // ═══════════════════════════════════════════
        /// <summary>插入文档记录，返回自增主键 document_id</summary>
        Task<int> InsertDocumentAsync(KnowledgeDocumentRecord doc);
        /// <summary>插入单个分块（含向量）到 knowledge_chunks</summary>
        Task InsertChunkAsync(ChemicalDocumentRecord chunk, int documentId);
        /// <summary>批量插入分块（事务保护，用于加载管道加速）</summary>
        Task InsertChunksBatchAsync(List<ChemicalDocumentRecord> chunks, int documentId);
        /// <summary>更新文档的 total_chunks 计数</summary>
        Task UpdateDocumentChunkCountAsync(int documentId, int totalChunks);
        /// <summary>查询新表中文档总数（用于启动判断：走全量加载还是快速模式）</summary>
        Task<int> GetKnowledgeDocumentCountAsync();
        /// <summary>查询新表中分块总数</summary>
        Task<int> GetKnowledgeChunkCountAsync();

        // ── 审计日志持久化（生产安全加固）──
        // [P3 哈希链] chainHash: SHA256 链式哈希，用于检测日志篡改
        // [P3 哈希链] createTime: 由调用方提供（UTC），确保参与哈希的时间与入库时间一致
        Task AddAuditLogAsync(string userId, string operation, string details, string? ipAddress = null, string? chainHash = null, DateTime? createTime = null);
        Task<List<AuditLog>> GetAuditLogsAsync(DateTime? startTime, DateTime? endTime, string? userId = null);
        // [P3 哈希链] 取尾条 chain_hash，供服务重启后恢复链头，避免断链
        Task<string?> GetLastAuditChainHashAsync();
        // [P3 哈希链修复] 批量回写重算后的 chain_hash（用于一键修复历史断链）
        Task UpdateAuditChainHashAsync(long id, string chainHash);

        // ═══════════════════════════════════════════
        // Task 2.2: Refresh Token 持久化
        // ═══════════════════════════════════════════
        Task StoreRefreshTokenAsync(string tokenHash, string username, DateTime expiresAt);
        Task<string?> ValidateAndRemoveRefreshTokenAsync(string tokenHash);

        // ═══════════════════════════════════════════
        // Phase 2.1: 长期记忆存储
        // ═══════════════════════════════════════════
        Task AddLongTermMemoryAsync(LongTermMemoryRecord record);
        Task<List<LongTermMemoryRecord>> SearchLongTermMemoriesAsync(string userId, float[] queryEmbedding, int topK = 5, string? memoryTypeFilter = null);
        Task<List<LongTermMemoryRecord>> SearchLongTermMemoriesByKeywordAsync(string userId, string keyword, int topK = 10);
        Task UpdateMemoryHitAsync(Guid memoryId);
        Task DeactivateMemoryAsync(Guid memoryId);
        Task DeactivateConflictingMemoriesAsync(string userId, string memoryType, string content);
        Task<int> CleanupMemoriesAsync(int retentionDays);
        Task<LongTermMemoryStats> GetLongTermMemoryStatsAsync(string userId);
        Task<long> GetLongTermMemoryCountAsync(string userId);
    }
}
