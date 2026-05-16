using System.Data;

namespace Agent1.Services
{
    public interface IDatabaseService
    {
        Task<IDbConnection> GetConnectionAsync();
        Task<bool> TestConnectionAsync();
        Task InitializeDatabaseAsync();

        // 文档管理
        Task AddChemicalDocumentAsync(string content, string regulationType, string priority, string? sourceFile = null, string? chemicalType = null, float[]? embedding = null);

        // 向量检索
        Task<List<RetrievedChunk>> VectorSearchAsync(string query, float[] queryEmbedding, int topK = 5);
    }
}