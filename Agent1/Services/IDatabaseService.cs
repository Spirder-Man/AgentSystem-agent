using System.Data;

namespace Agent1.Services
{
    public interface IDatabaseService
    {
        Task<IDbConnection> GetConnectionAsync();
        Task<bool> TestConnectionAsync();
        Task InitializeDatabaseAsync();

        // 文档管理（P0修复：接收完整 ChemicalDocumentRecord，承载全链路元数据）
        Task AddChemicalDocumentAsync(ChemicalDocumentRecord record);

        // 兼容旧版签名（标记为过时，逐步迁移）
        [Obsolete("请使用 AddChemicalDocumentAsync(ChemicalDocumentRecord) 代替")]
        Task AddChemicalDocumentAsync(string content, string regulationType, string priority, string? sourceFile = null, string? chemicalType = null, float[]? embedding = null);

        // 向量检索
        Task<List<RetrievedChunk>> VectorSearchAsync(string query, float[] queryEmbedding, int topK = 5);
    }
}