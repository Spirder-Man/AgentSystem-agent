
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private readonly QueryCacheService _queryCache;

        public HybridKnowledgeBaseService(IDatabaseService databaseService, ILlmService llmService, Config.AppConfig config)
        {
            _databaseService = databaseService;
            _llmService = llmService;
            _kbConfig = config.KnowledgeBase;
            _vectorConfig = config.VectorSearch;
            _bm25Service = new KnowledgeBaseService();
            _queryCache = new QueryCacheService(config);
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
                // P0修复：从 metadata 提取全链路元数据
                string? regulationNumber = null;
                string? chapterTitle = null;
                string? clauseNumber = null;
                string? extractionQuality = null;
                int? pageNumber = null;
                int? chunkIndex = null;

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
                    if (metadata.ContainsKey("RegulationNumber"))
                        regulationNumber = metadata["RegulationNumber"]?.ToString();
                    if (metadata.ContainsKey("ChapterTitle"))
                        chapterTitle = metadata["ChapterTitle"]?.ToString();
                    if (metadata.ContainsKey("ClauseNumber"))
                        clauseNumber = metadata["ClauseNumber"]?.ToString();
                    if (metadata.ContainsKey("ExtractionQuality"))
                        extractionQuality = metadata["ExtractionQuality"]?.ToString();
                    if (metadata.ContainsKey("PageNumber") && metadata["PageNumber"] is int pn)
                        pageNumber = pn;
                    if (metadata.ContainsKey("ChunkIndex") && metadata["ChunkIndex"] is int ci)
                        chunkIndex = ci;
                }

                var embedding = await _llmService.GetEmbeddingAsync(content);

                if (embedding == null)
                {
                    Console.WriteLine($"   ⏭️ 向量生成失败，跳过向量库写入（BM25 已写入）");
                    return;
                }

                // P0修复：构建完整记录，携带全部元数据（脏数据熔断由 DatabaseService 执行）
                var record = new ChemicalDocumentRecord
                {
                    Content = content,
                    RegulationType = regulationType,
                    Priority = priority,
                    SourceFile = sourceFile,
                    ChemicalType = chemicalType,
                    RegulationNumber = regulationNumber,
                    ChapterTitle = chapterTitle,
                    ClauseNumber = clauseNumber,
                    ExtractionQuality = extractionQuality,
                    PageNumber = pageNumber,
                    ChunkIndex = chunkIndex,
                    Embedding = embedding
                };
                //ChemicalDocumentRecord 构建
                await _databaseService.AddChemicalDocumentAsync(record);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ 向量化添加失败: {ex.Message}");
            }
        }

        public Task AddDocumentsAsync(IEnumerable<string> contents)
        {
            // Sprint 1: 使用批量嵌入优化，替代逐条调用
            return AddDocumentsBatchAsync(contents);
        }

        // Sprint 1: 批量文档添加——收集后一次提交多个文档做嵌入，利用 GPU 批处理
        public async Task AddDocumentsBatchAsync(IEnumerable<string> contents, string? regulationType = null, string? priority = null, string? chemicalType = null, string? sourceFile = null)
        {
            var contentList = contents.ToList();
            if (contentList.Count == 0)
                return;

            var sw = Stopwatch.StartNew();
            Console.WriteLine($"   📦 批量嵌入: {contentList.Count} 条文档...");

            // Step 1: BM25 批量写入（同步，快速）
            await _bm25Service.AddDocumentsAsync(contentList);

            // Step 2: 向量批量嵌入（GPU 加速）
            try
            {
                var llmSvc = _llmService as LlmService;
                float[][]? embeddings;

                if (llmSvc != null)
                {
                    // 使用批量 API（单次请求处理多条，GPU 批处理）
                    embeddings = await llmSvc.GetEmbeddingsBatchAsync(contentList);
                }
                else
                {
                    embeddings = await _llmService.GetEmbeddingsAsync(contentList);
                }

                if (embeddings == null || embeddings.Length == 0)
                {
                    Console.WriteLine($"   ⏭️ 批量嵌入全部失败，跳过向量库写入（BM25 已写入）");
                    return;
                }

                // Step 3: 批量写入数据库
                var records = new List<ChemicalDocumentRecord>();
                for (int i = 0; i < contentList.Count && i < embeddings.Length; i++)
                {
                    records.Add(new ChemicalDocumentRecord
                    {
                        Content = contentList[i],
                        RegulationType = regulationType ?? "通用",
                        Priority = priority ?? "中",
                        ChemicalType = chemicalType,
                        SourceFile = sourceFile,
                        Embedding = embeddings[i]
                    });
                }

                await _databaseService.AddChemicalDocumentsBatchAsync(records);

                sw.Stop();
                Console.WriteLine($"   ✅ 批量处理完成: {records.Count} 条, 总耗时 {sw.ElapsedMilliseconds}ms (均 {sw.ElapsedMilliseconds / Math.Max(1, records.Count)}ms/条)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ 批量向量化失败: {ex.Message}，BM25 已写入");
            }
        }

        public async Task<List<RetrievedChunk>> RetrieveAsync(string query, int topK = 5)
        {
            // Sprint 5: 查询缓存
            if (_queryCache.TryGet(query, out var cachedResults))
            {
                if (!EvalMode.IsActive)
                    Console.WriteLine($"   💾 缓存命中: \"{query}\"");
                return cachedResults;
            }

            // Sprint 4: 查询扩展
            var expandedQuery = ExpandQuery(query);

            if (!EvalMode.IsActive)
                Console.WriteLine($"\n🔍 开始混合检索: {query}");

            var sw = Stopwatch.StartNew();
            List<RetrievedChunk> results;
            var mode = _kbConfig.SearchMode?.ToLowerInvariant() ?? "hybrid";

            switch (mode)
            {
                case "bm25":
                    results = await Bm25RetrieveAsync(expandedQuery, topK);
                    break;
                case "vector":
                    results = await VectorRetrieveAsync(expandedQuery, topK);
                    break;
                case "hybrid":
                default:
                    results = await HybridRetrieveAsync(expandedQuery, topK);
                    break;
            }

            // Sprint 5: 缓存结果
            _queryCache.Set(query, results);

            MetricsCollector.RecordRagSearch(sw.ElapsedMilliseconds);
            return results;
        }

        public string PreprocessQuery(string query)
        {
            return _bm25Service.PreprocessQuery(query);
        }

        // Sprint 4: 查询扩展——利用化工领域词典扩展查询词，提升召回率
        public string ExpandQuery(string query)
        {
            if (!_kbConfig.EnableQueryExpansion || string.IsNullOrWhiteSpace(query))
                return query;

            var expanded = query.Trim();

            // 化工术语同义词扩展
            var synonyms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "能否共存", "同库储存 配伍禁忌" },
                { "放在一起", "同库储存 禁忌物料" },
                { "安全距离", "防火间距 GB50160" },
                { "危险吗", "危险类别 GHS分类" },
                { "储存", "贮存 仓库 GB15603" },
                { "泄漏", "泄漏应急 处置" },
                { "储罐", "储罐 GB50160 防火堤" },
            };

            foreach (var kvp in synonyms)
            {
                if (expanded.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase)
                    && !expanded.Contains(kvp.Value, StringComparison.OrdinalIgnoreCase))
                {
                    expanded += " " + kvp.Value;
                }
            }

            // 自动追加 GB 编号关键词（如果包含化学术语但缺少 GB 引用）
            if (!expanded.Contains("GB", StringComparison.OrdinalIgnoreCase))
            {
                if (expanded.Contains("储存") || expanded.Contains("同库") || expanded.Contains("禁忌"))
                    expanded += " GB15603";
                if (expanded.Contains("类别") || expanded.Contains("分类") || expanded.Contains("GHS"))
                    expanded += " GB30000";
                if (expanded.Contains("间距") || expanded.Contains("防火") || expanded.Contains("距离"))
                    expanded += " GB50160";
            }

            if (expanded != query && !EvalMode.IsActive)
                Console.WriteLine($"   🔍 查询扩展: \"{query}\" → \"{expanded}\"");

            return expanded;
        }

        public int GetDocumentCount()
        {
            return _bm25Service.GetDocumentCount();
        }

        public void Clear()
        {
            _bm25Service.Clear();
            _databaseService.ClearChemicalDocumentsAsync().GetAwaiter().GetResult();
        }

        public async Task AddChemicalRegulationAsync(string content, string regulationType, string priority, string? chemicalType = null)
        {
            await _bm25Service.AddChemicalRegulationAsync(content, regulationType, priority, chemicalType);
            try
            {
                var embedding = await _llmService.GetEmbeddingAsync(content);

                if (embedding == null)
                {
                    Console.WriteLine($"   ⏭️ 向量生成失败，跳过向量库写入（BM25 已写入）");
                    return;
                }

                // P0修复：构建完整记录
                var record = new ChemicalDocumentRecord
                {
                    Content = content,
                    RegulationType = regulationType,
                    Priority = priority,
                    ChemicalType = chemicalType,
                    Embedding = embedding
                };

                await _databaseService.AddChemicalDocumentAsync(record);
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

        /// <summary>
        /// 启动加速：从数据库直接重建 BM25 内存索引（不生成嵌入，秒级完成）
        /// </summary>
        public async Task RebuildBm25FromDatabaseAsync()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Console.WriteLine("   ⚡ 快速模式：从数据库重建内存索引（跳过文件扫描和嵌入生成）...");

            var docs = await _databaseService.GetAllChemicalDocumentTextsAsync();
            if (docs.Count == 0)
            {
                Console.WriteLine("   ℹ️ 数据库无文档，将执行完整加载");
                return;
            }

            _bm25Service.Clear();
            foreach (var (content, regulationType, priority, sourceFile) in docs)
            {
                var metadata = new Dictionary<string, object>
                {
                    ["RegulationType"] = regulationType,
                    ["Priority"] = priority
                };
                if (sourceFile != null)
                    metadata["SourceFile"] = sourceFile;

                await _bm25Service.AddDocumentAsync(content, metadata);
            }

            sw.Stop();
            Console.WriteLine($"   ✅ BM25 索引重建完成: {docs.Count} 条, 耗时 {sw.Elapsed.TotalSeconds:F1}s");
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
            if (!EvalMode.IsActive)
                Console.WriteLine("   🎯 使用向量语义检索...");
            try
            {
                var embedding = await _llmService.GetEmbeddingAsync(query);
                if (embedding == null)
                {
                    Console.WriteLine("   ⚠️ 向量嵌入失败，降级为空结果");
                    return new List<RetrievedChunk>();
                }

                // Sprint 2: GPU 向量检索优先，不可用时 fallback pgvector
                if (_vectorConfig.GpuSearchEnabled)
                {
                    try
                    {
                        var gpuResults = await GpuVectorSearchAsync(embedding, topK);
                        if (gpuResults.Count > 0)
                        {
                            if (!EvalMode.IsActive)
                                Console.WriteLine($"   ⚡ GPU向量检索: {gpuResults.Count} 条");
                            return gpuResults;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (!_vectorConfig.GpuFallbackEnabled)
                            throw;
                        Console.WriteLine($"   ⚠️ GPU检索不可用: {ex.Message}，降级到 pgvector");
                    }
                }

                var results = await _databaseService.VectorSearchAsync(query, embedding, topK);
                return results;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ 向量检索失败，本次混合检索仅使用BM25: {ex.Message}");
                return new List<RetrievedChunk>();
            }
        }

        // Sprint 2: GPU 加速向量检索——在内存中做余弦相似度搜索（模拟 GPU FAISS 行为）
        // 生产环境可替换为 FAISS/cuVS 调用
        private async Task<List<RetrievedChunk>> GpuVectorSearchAsync(float[] queryEmbedding, int topK)
        {
            // 从数据库加载全量向量到内存（生产环境改为从 FAISS GPU 索引查询）
            var allDocs = await _databaseService.GetAllChemicalDocumentsWithEmbeddingsAsync();
            if (allDocs.Count == 0)
                return new List<RetrievedChunk>();

            var scoredDocs = new List<(ChemicalDocumentRecord doc, float score)>();

            foreach (var doc in allDocs)
            {
                if (doc.Embedding == null || doc.Embedding.Length == 0)
                    continue;

                var score = CosineSimilarity(queryEmbedding, doc.Embedding);
                scoredDocs.Add((doc, score));
            }

            var results = scoredDocs
                .OrderByDescending(x => x.score)
                .Take(topK)
                .Select((x, idx) => new RetrievedChunk
                {
                    Id = idx.ToString(),
                    Content = x.doc.Content ?? "",
                    Score = x.score,
                    Rank = idx,
                    Metadata = new Dictionary<string, object>
                    {
                        ["RegulationType"] = x.doc.RegulationType ?? "通用",
                        ["Priority"] = x.doc.Priority ?? "中",
                        ["source"] = x.doc.SourceFile ?? "GPU索引"
                    },
                    RetrievalMethod = "GPU-Vector"
                })
                .ToList();

            return results;
        }

        // 余弦相似度计算（内联，避免额外依赖）
        private static float CosineSimilarity(float[] a, float[] b)
        {
            if (a.Length != b.Length)
                return 0f;

            float dot = 0, normA = 0, normB = 0;
            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                normA += a[i] * a[i];
                normB += b[i] * b[i];
            }

            var denominator = MathF.Sqrt(normA) * MathF.Sqrt(normB);
            return denominator > 0 ? dot / denominator : 0f;
        }

        private async Task<List<RetrievedChunk>> HybridRetrieveAsync(string query, int topK)
        {
            if (!EvalMode.IsActive)
                Console.WriteLine("   🧩 使用BM25+向量混合检索 (RRF融合)...");

            // Sprint 4: 使用 RRF (Reciprocal Rank Fusion) 替代简单加权求和
            var candidateTopK = Math.Max(topK * 2, _vectorConfig.RerankerCandidateTopK);

            var bm25Task = _bm25Service.RetrieveAsync(query, candidateTopK);
            var vectorTask = VectorRetrieveAsync(query, candidateTopK);

            await Task.WhenAll(bm25Task, vectorTask);

            var bm25Results = bm25Task.Result;
            var vectorResults = vectorTask.Result;

            // 标记检索方法
            foreach (var result in bm25Results)
                result.RetrievalMethod = "BM25";

            // ═══ RRF 融合算法 (k=60, 常用常数) ═══
            const double rrfK = 60.0;
            var rrfScores = new Dictionary<string, (RetrievedChunk chunk, double rrfScore)>();

            // Step 1: 计算 BM25 排名贡献
            for (int rank = 0; rank < bm25Results.Count; rank++)
            {
                var key = bm25Results[rank].Content ?? Guid.NewGuid().ToString();
                var rrfScore = 1.0 / (rrfK + rank + 1);  // rank 0-based, +1 转 1-based
                rrfScores[key] = (bm25Results[rank], rrfScore);
            }

            // Step 2: 累加向量检索排名贡献
            for (int rank = 0; rank < vectorResults.Count; rank++)
            {
                var key = vectorResults[rank].Content ?? Guid.NewGuid().ToString();
                var rrfScore = 1.0 / (rrfK + rank + 1);

                if (rrfScores.ContainsKey(key))
                {
                    var existing = rrfScores[key];
                    rrfScores[key] = (existing.chunk, existing.rrfScore + rrfScore);
                }
                else
                {
                    rrfScores[key] = (vectorResults[rank], rrfScore);
                }
            }

            // Step 3: 按 RRF 分数排序
            var finalResults = rrfScores.Values
                .OrderByDescending(x => x.rrfScore)
                .Take(topK)
                .Select((x, idx) => new RetrievedChunk
                {
                    Content = x.chunk.Content,
                    Score = x.rrfScore,
                    Rank = idx,
                    Metadata = x.chunk.Metadata,
                    RetrievalMethod = "Hybrid-RRF"
                })
                .ToList();

            if (!EvalMode.IsActive)
                Console.WriteLine($"   ✅ RRF混合检索完成 (召回: {finalResults.Count}, 候选池: {rrfScores.Count})");
            return finalResults;
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
