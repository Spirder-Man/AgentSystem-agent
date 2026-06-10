
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Agent1.Services
{
    public interface IKnowledgeBaseService
    {
        // 现有方法（保留）
        Task AddDocumentAsync(string content, Dictionary<string, object>? metadata = null);
        Task AddDocumentsAsync(IEnumerable<string> contents);
        Task<List<RetrievedChunk>> RetrieveAsync(string query, int topK = 5);
        string PreprocessQuery(string query);
        int GetDocumentCount();
        void Clear();
        
        // 新增：化工场景专用方法
        Task AddChemicalRegulationAsync(string content, string regulationType, string priority, string? chemicalType = null);
        Task<List<RetrievedChunk>> RetrieveChemicalRegulationAsync(string query, string? chemicalType = null, string? regulationType = null, int topK = 5);
        Task LoadChemicalKnowledgeBaseAsync(string knowledgeBasePath);
    }
}

