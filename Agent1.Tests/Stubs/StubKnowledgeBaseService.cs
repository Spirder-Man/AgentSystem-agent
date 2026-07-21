// Stub IKnowledgeBaseService for integration testing
// Supports preset retrieval results for predictable test behavior

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Agent1.Services;

public class StubKnowledgeBaseService : IKnowledgeBaseService
{
    private readonly Dictionary<string, List<RetrievedChunk>> _presetResults = new(StringComparer.OrdinalIgnoreCase);
    private int _documentCount;

    /// <summary>Add a preset retrieval result for a specific query</summary>
    public void AddPresetResult(string query, List<RetrievedChunk> chunks)
    {
        _presetResults[query] = chunks;
    }

    /// <summary>Set the document count returned by GetDocumentCount</summary>
    public void SetDocumentCount(int count) => _documentCount = count;

    public Task AddDocumentAsync(string content, Dictionary<string, object>? metadata = null)
    {
        _documentCount++;
        return Task.CompletedTask;
    }

    public Task AddDocumentsAsync(IEnumerable<string> contents)
    {
        _documentCount += contents.Count();
        return Task.CompletedTask;
    }

    public Task<List<RetrievedChunk>> RetrieveAsync(string query, int topK = 5)
    {
        var chunks = FindMatchingChunks(query, topK);
        return Task.FromResult(chunks);
    }

    public string PreprocessQuery(string query) => query.Trim();

    public int GetDocumentCount() => _documentCount;

    public Task ClearAsync()
    {
        _presetResults.Clear();
        _documentCount = 0;
        return Task.CompletedTask;
    }

    public Task AddChemicalRegulationAsync(string content, string regulationType, string priority, string? chemicalType = null)
    {
        _documentCount++;
        return Task.CompletedTask;
    }

    public Task<List<RetrievedChunk>> RetrieveChemicalRegulationAsync(
        string query, string? chemicalType = null, string? regulationType = null,
        int topK = 5, string? regulationNumber = null)
    {
        var chunks = FindMatchingChunks(query, topK);
        return Task.FromResult(chunks);
    }

    public Task LoadChemicalKnowledgeBaseAsync(string knowledgeBasePath)
        => Task.CompletedTask;

    public Task RemoveChunksBySourceFileAsync(string sourceFile)
        => Task.CompletedTask;

    private List<RetrievedChunk> FindMatchingChunks(string query, int topK)
    {
        // Exact match first
        if (_presetResults.TryGetValue(query, out var exact))
            return exact.Take(topK).ToList();

        // Substring match
        foreach (var kv in _presetResults)
        {
            if (query.Contains(kv.Key, StringComparison.OrdinalIgnoreCase)
                || kv.Key.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                return kv.Value.Take(topK).ToList();
            }
        }

        return new List<RetrievedChunk>();
    }
}
