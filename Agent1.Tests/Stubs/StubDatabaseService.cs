using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Agent1.Models;
using Agent1.Services;

namespace Agent1.Tests.Stubs;

/// <summary>
/// IDatabaseService 内存存根 — 用于 ApiIntegrationTests。
/// 替代 PostgreSQL 连接，支持 refresh token 存储（供 AuthController 登录/刷新使用）。
/// </summary>
public class StubDatabaseService : IDatabaseService
{
    private readonly ConcurrentDictionary<string, StoredRefreshToken> _refreshTokens = new();

    // ═══════════════════════════════════════
    // 连接管理（存根返回成功）
    // ═══════════════════════════════════════

    public Task<IDbConnection> GetConnectionAsync()
        => throw new NotSupportedException("StubDatabaseService 不支持获取真实数据库连接");

    public Task<bool> TestConnectionAsync()
        => Task.FromResult(true);

    public Task<string> GetDatabaseInfoAsync()
        => Task.FromResult("Stub Database (in-memory for testing)");

    public Task<List<string>> GetTableNamesAsync()
        => Task.FromResult(new List<string>());

    public Task InitializeDatabaseAsync()
        => Task.CompletedTask;

    // ═══════════════════════════════════════
    // 文档管理（存根返回空）
    // ═══════════════════════════════════════

    public Task AddChemicalDocumentAsync(ChemicalDocumentRecord record)
        => Task.CompletedTask;

    public Task AddChemicalDocumentsBatchAsync(List<ChemicalDocumentRecord> records)
        => Task.CompletedTask;

    [Obsolete]
    public Task AddChemicalDocumentAsync(string content, string regulationType, string priority,
        string? sourceFile = null, string? chemicalType = null, float[]? embedding = null)
        => Task.CompletedTask;

    public Task ClearChemicalDocumentsAsync()
        => Task.CompletedTask;

    public Task<List<RetrievedChunk>> VectorSearchAsync(string query, float[] queryEmbedding, int topK = 5)
        => Task.FromResult(new List<RetrievedChunk>());

    public Task<int> GetChemicalDocumentCountAsync()
        => Task.FromResult(0);

    public Task<List<ChemicalDocumentRecord>> GetAllChemicalDocumentTextsAsync()
        => Task.FromResult(new List<ChemicalDocumentRecord>());

    public Task<List<ChemicalDocumentRecord>> GetAllChemicalDocumentsWithEmbeddingsAsync()
        => Task.FromResult(new List<ChemicalDocumentRecord>());

    public Task<int> DeleteChemicalDocumentsBySourceAsync(string sourceFile)
        => Task.FromResult(0);

    public Task<int> DeleteKnowledgeDocumentBySourcePathAsync(string sourcePath)
        => Task.FromResult(0);

    // ═══════════════════════════════════════
    // 知识库双层表架构 — 存根（Phase 1-6）
    // ═══════════════════════════════════════

    public Task<int> InsertDocumentAsync(KnowledgeDocumentRecord doc)
        => Task.FromResult(1);

    public Task InsertChunkAsync(ChemicalDocumentRecord chunk, int documentId)
        => Task.CompletedTask;

    public Task InsertChunksBatchAsync(List<ChemicalDocumentRecord> chunks, int documentId)
        => Task.CompletedTask;

    public Task UpdateDocumentChunkCountAsync(int documentId, int totalChunks)
        => Task.CompletedTask;

    public Task<int> GetKnowledgeDocumentCountAsync()
        => Task.FromResult(0);

    public Task<int> GetKnowledgeChunkCountAsync()
        => Task.FromResult(0);

    // ═══════════════════════════════════════
    // 审计日志（存根返回空）
    // ═══════════════════════════════════════

    public Task AddAuditLogAsync(string userId, string operation, string details,
        string? ipAddress = null, string? chainHash = null, DateTime? createTime = null)
        => Task.CompletedTask;

    public Task<string?> GetLastAuditChainHashAsync() => Task.FromResult<string?>(null);

    public Task UpdateAuditChainHashAsync(long id, string chainHash)
        => Task.CompletedTask;

    public Task<List<AuditLog>> GetAuditLogsAsync(DateTime? startTime, DateTime? endTime, string? userId = null)
        => Task.FromResult(new List<AuditLog>());

    // ═══════════════════════════════════════
    // Refresh Token 持久化（内存存储，供 AuthController 使用）
    // ═══════════════════════════════════════

    public Task StoreRefreshTokenAsync(string tokenHash, string username, DateTime expiresAt)
    {
        _refreshTokens[tokenHash] = new StoredRefreshToken
        {
            TokenHash = tokenHash,
            Username = username,
            ExpiresAt = expiresAt
        };
        return Task.CompletedTask;
    }

    public Task<string?> ValidateAndRemoveRefreshTokenAsync(string tokenHash)
    {
        if (_refreshTokens.TryRemove(tokenHash, out var stored))
        {
            if (stored.ExpiresAt > DateTime.UtcNow)
                return Task.FromResult<string?>(stored.Username);
        }
        return Task.FromResult<string?>(null);
    }

    // ═══════════════════════════════════════
    // 长期记忆（存根返回空）
    // ═══════════════════════════════════════

    public Task AddLongTermMemoryAsync(LongTermMemoryRecord record)
        => Task.CompletedTask;

    public Task<List<LongTermMemoryRecord>> SearchLongTermMemoriesAsync(
        string userId, float[] queryEmbedding, int topK = 5, string? memoryTypeFilter = null)
        => Task.FromResult(new List<LongTermMemoryRecord>());

    public Task<List<LongTermMemoryRecord>> SearchLongTermMemoriesByKeywordAsync(
        string userId, string keyword, int topK = 10)
        => Task.FromResult(new List<LongTermMemoryRecord>());

    public Task UpdateMemoryHitAsync(Guid memoryId)
        => Task.CompletedTask;

    public Task DeactivateMemoryAsync(Guid memoryId)
        => Task.CompletedTask;

    public Task DeactivateConflictingMemoriesAsync(string userId, string memoryType, string content)
        => Task.CompletedTask;

    public Task<int> CleanupMemoriesAsync(int retentionDays)
        => Task.FromResult(0);

    public Task<LongTermMemoryStats> GetLongTermMemoryStatsAsync(string userId)
        => Task.FromResult(new LongTermMemoryStats());

    public Task<long> GetLongTermMemoryCountAsync(string userId)
        => Task.FromResult(0L);

    private class StoredRefreshToken
    {
        public string TokenHash { get; set; } = "";
        public string Username { get; set; } = "";
        public DateTime ExpiresAt { get; set; }
    }
}
