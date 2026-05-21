
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Agent1.Config;

namespace Agent1.Services
{
    public class HybridKnowledgeBaseService : IKnowledgeBaseService
    {
        private readonly IDatabaseService _databaseService;
        private readonly ILlmService _llmService;
        private readonly ChemicalKnowledgeBaseConfig _kbConfig;
        private readonly VectorSearchConfig _vectorConfig;
        private readonly KnowledgeBaseService _bm25Service;

        public HybridKnowledgeBaseService(IDatabaseService databaseService, ILlmService llmService, Config.AppConfig config)
        {
            _databaseService = databaseService;
            _llmService = llmService;
            _kbConfig = config.KnowledgeBase;
            _vectorConfig = config.VectorSearch;
            _bm25Service = new KnowledgeBaseService();
        }

        public async Task AddDocumentAsync(string content, Dictionary<string, object>? metadata = null)
        {
            await _bm25Service.AddDocumentAsync(content, metadata);
            
            try
            {
                var regulationType = "通用";
                var priority = "中";
                string? sourceFile = null;
                string? chemicalType = null;

                if (metadata != null)
                {
                    if (metadata.ContainsKey("RegulationType"))
                        regulationType = metadata["RegulationType"]?.ToString() ?? "通用";
                    if (metadata.ContainsKey("Priority"))
                        priority = metadata["Priority"]?.ToString() ?? "中";
                    if (metadata.ContainsKey("SourceFile"))
                        sourceFile = metadata["SourceFile"]?.ToString();
                    if (metadata.ContainsKey("ChemicalType"))
                        chemicalType = metadata["ChemicalType"]?.ToString();
                }

                var embedding = await _llmService.GetEmbeddingAsync(content);
                await _databaseService.AddChemicalDocumentAsync(content, regulationType, priority, sourceFile, chemicalType, embedding);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ 向量化添加失败: {ex.Message}");
            }
        }

        public Task AddDocumentsAsync(IEnumerable<string> contents)
        {
            return _bm25Service.AddDocumentsAsync(contents);
        }

        public async Task<List<RetrievedChunk>> RetrieveAsync(string query, int topK = 5)
        {
            Console.WriteLine($"\n🔍 开始混合检索: {query}");

            var mode = _kbConfig.SearchMode?.ToLowerInvariant() ?? "hybrid";

            switch (mode)
            {
                case "bm25":
                    return await Bm25RetrieveAsync(query, topK);
                case "vector":
                    return await VectorRetrieveAsync(query, topK);
                case "hybrid":
                default:
                    return await HybridRetrieveAsync(query, topK);
            }
        }

        public string PreprocessQuery(string query)
        {
            return _bm25Service.PreprocessQuery(query);
        }

        public int GetDocumentCount()
        {
            return _bm25Service.GetDocumentCount();
        }

        public void Clear()
        {
            _bm25Service.Clear();
        }

        public async Task AddChemicalRegulationAsync(string content, string regulationType, string priority, string? chemicalType = null)
        {
            await _bm25Service.AddChemicalRegulationAsync(content, regulationType, priority, chemicalType);
            try
            {
                var embedding = await _llmService.GetEmbeddingAsync(content);
                await _databaseService.AddChemicalDocumentAsync(content, regulationType, priority, null, chemicalType, embedding);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ 向量添加失败: {ex.Message}");
            }
        }

        public async Task<List<RetrievedChunk>> RetrieveChemicalRegulationAsync(string query, string? chemicalType = null, string? regulationType = null, int topK = 5)
        {
            var allResults = await RetrieveAsync(query, topK * 2);

            var filteredResults = allResults.Where(r =>
            {
                var metadata = r.Metadata;

                if (!string.IsNullOrEmpty(chemicalType) && metadata.ContainsKey("ChemicalType"))
                {
                    var docChemicalType = metadata["ChemicalType"]?.ToString();
                    if (docChemicalType != "通用" && !docChemicalType.Equals(chemicalType, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                if (!string.IsNullOrEmpty(regulationType) && metadata.ContainsKey("RegulationType"))
                {
                    var docRegulationType = metadata["RegulationType"]?.ToString();
                    if (!docRegulationType.Equals(regulationType, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                return true;
            }).ToList();

            var rerankedResults = filteredResults
                .Select(r => new { Chunk = r, Score = CalculateChemicalRelevanceScore(r) })
                .OrderByDescending(x => x.Score)
                .Take(topK)
                .Select(x => x.Chunk)
                .ToList();

            Console.WriteLine($"   🔬 化工合规检索: 查询='{query}', 危化品={chemicalType ?? "全部"}, 法规类型={regulationType ?? "全部"}, 召回={rerankedResults.Count}条");
            return rerankedResults;
        }

        public async Task LoadChemicalKnowledgeBaseAsync(string knowledgeBasePath)
        {
            Console.WriteLine($"   📚 正在加载化工知识库: {knowledgeBasePath}");
            await _bm25Service.LoadChemicalKnowledgeBaseAsync(knowledgeBasePath);
            Console.WriteLine("   ℹ️ 向量存储与BM25同步完成");
        }

        private async Task<List<RetrievedChunk>> Bm25RetrieveAsync(string query, int topK)
        {
            Console.WriteLine("   📝 使用BM25关键词检索...");
            var results = await _bm25Service.RetrieveAsync(query, topK);
            foreach (var result in results)
            {
                result.RetrievalMethod = "BM25";
            }
            return results;
        }

        private async Task<List<RetrievedChunk>> VectorRetrieveAsync(string query, int topK)
        {
            Console.WriteLine("   🎯 使用向量语义检索...");
            try
            {
                var embedding = await _llmService.GetEmbeddingAsync(query);
                var results = await _databaseService.VectorSearchAsync(query, embedding, topK);
                return results;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ 向量检索失败，降级到BM25: {ex.Message}");
                return await Bm25RetrieveAsync(query, topK);
            }
        }

        private async Task<List<RetrievedChunk>> HybridRetrieveAsync(string query, int topK)
        {
            Console.WriteLine("   🧩 使用BM25+向量混合检索...");

            var bm25Task = _bm25Service.RetrieveAsync(query, topK * 2);
            var vectorTask = VectorRetrieveAsync(query, topK * 2);

            await Task.WhenAll(bm25Task, vectorTask);

            var bm25Results = bm25Task.Result;
            var vectorResults = vectorTask.Result;

            foreach (var result in bm25Results)
            {
                result.RetrievalMethod = "BM25";
            }

            var merged = new Dictionary<string, (RetrievedChunk chunk, double bm25Score, double vectorScore)>();

            foreach (var result in bm25Results)
            {
                var key = GetChunkKey(result);
                merged[key] = (result, result.Score, 0);
            }

            foreach (var result in vectorResults)
            {
                var key = GetChunkKey(result);
                if (merged.ContainsKey(key))
                {
                    var existing = merged[key];
                    merged[key] = (existing.chunk, existing.bm25Score, result.Score);
                }
                else
                {
                    merged[key] = (result, 0, result.Score);
                }
            }

            var finalResults = merged.Values
                .Select(x =>
                {
                    var hybridScore = x.bm25Score * _vectorConfig.Bm25Weight + x.vectorScore * _vectorConfig.VectorWeight;
                    var chunk = new RetrievedChunk
                    {
                        Content = x.chunk.Content,
                        Score = hybridScore,
                        Rank = 0,
                        Metadata = x.chunk.Metadata,
                        RetrievalMethod = "Hybrid"
                    };
                    return (Chunk: chunk, Score: hybridScore);
                })
                .OrderByDescending(x => x.Score)
                .Take(topK)
                .Select((x, idx) => new RetrievedChunk
                {
                    Content = x.Chunk.Content,
                    Score = x.Chunk.Score,
                    Rank = idx,
                    Metadata = x.Chunk.Metadata,
                    RetrievalMethod = x.Chunk.RetrievalMethod
                })
                .ToList();

            Console.WriteLine($"   ✅ 混合检索完成 (召回: {finalResults.Count})");
            return finalResults;
        }

        private string GetChunkKey(RetrievedChunk chunk)
        {
            return chunk.Content.Substring(0, Math.Min(100, chunk.Content.Length)).GetHashCode().ToString();
        }

        private static readonly Dictionary<string, int> _priorityLevels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "高", 3 },
            { "中", 2 },
            { "低", 1 }
        };

        private static readonly HashSet<string> _chemicalTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "甲苯", "甲醇", "乙醇", "丙酮", "过氧化氢", "硫酸", "盐酸", "硝酸",
            "危化品", "危险化学品", "储罐", "防火堤", "消防通道", "安全距离",
            "甲类", "乙类", "丙类", "贮存", "存储", "国标", "GB15603", "GB30000",
            "禁忌物料", "氧化剂", "易燃液体", "易燃固体", "泄漏", "应急"
        };

        private double CalculateChemicalRelevanceScore(RetrievedChunk chunk)
        {
            double baseScore = chunk.Score;

            int priorityBonus = 0;
            if (chunk.Metadata.ContainsKey("Priority"))
            {
                var priority = chunk.Metadata["Priority"]?.ToString();
                if (!string.IsNullOrEmpty(priority) && _priorityLevels.ContainsKey(priority))
                {
                    priorityBonus = _priorityLevels[priority] * 1000;
                }
            }

            int termBonus = 0;
            var content = chunk.Content;
            foreach (var term in _chemicalTerms)
            {
                if (content.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    termBonus += 50;
                }
            }

            return baseScore + priorityBonus + termBonus;
        }

        public List<RetrievedChunk> Retrieve(string query, int topK = 5)
        {
            return RetrieveAsync(query, topK).GetAwaiter().GetResult();
        }
    }
}
