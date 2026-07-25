
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Agent1.Config;
using Microsoft.SemanticKernel;

namespace Agent1.Services
{
    public class HybridKnowledgeBaseService : IKnowledgeBaseService, IDisposable
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

        public void Dispose()
        {
            _queryCache?.Dispose();
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
                int? documentId = null; // 双层表：关联的文档记录 ID

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
                    if (metadata.ContainsKey("DocumentId") && metadata["DocumentId"] is int did)
                        documentId = did;
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

                // 双层表路由：有 DocumentId → 写入 knowledge_chunks（新表）；否则 → 写入 chemical_documents（旧表，兼容）
                if (documentId.HasValue && documentId.Value > 0)
                {
                    await _databaseService.InsertChunkAsync(record, documentId.Value);
                }
                else
                {
                    await _databaseService.AddChemicalDocumentAsync(record);
                }
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
            // Sprint 5: 查询缓存（评测模式下禁用，确保每条用例独立检索）
            if (!EvalMode.IsActive && _queryCache.TryGet(query, out var cachedResults))
            {
                if (!EvalMode.IsActive)
                    Console.WriteLine($"   💾 缓存命中: \"{query}\"");
                return cachedResults;
            }

            // 缓存未命中
            MetricsCollector.RecordRagCacheMiss();

            // Sprint 4: 查询扩展
            var expandedQuery = ExpandQuery(query);

            if (!EvalMode.IsActive)
                Console.WriteLine($"\n🔍 开始混合检索: {query}");

            var sw = Stopwatch.StartNew();
            List<RetrievedChunk> results;
            var mode = _kbConfig.SearchMode;

            switch (mode)
            {
                case SearchModeType.Bm25:
                    results = await Bm25RetrieveAsync(expandedQuery, topK);
                    break;
                case SearchModeType.Vector:
                    results = await VectorRetrieveAsync(expandedQuery, topK);
                    break;
                case SearchModeType.Hybrid:
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

        public async Task ClearAsync()
        {
            await _bm25Service.ClearAsync();
            await _databaseService.ClearChemicalDocumentsAsync();
        }

        /// <summary>[P3 增量更新] 按源文件删除 BM25 内存分块 + DB 分块</summary>
        public async Task RemoveChunksBySourceFileAsync(string sourceFile)
        {
            var bm25Removed = _bm25Service.RemoveBySourceFile(sourceFile);
            var dbRemoved = await _databaseService.DeleteChemicalDocumentsBySourceAsync(sourceFile);
            if (bm25Removed > 0 || dbRemoved > 0)
                Console.WriteLine($"   ✅ 已清理源文件分块: BM25-{bm25Removed}, DB-{dbRemoved} ({Path.GetFileName(sourceFile)})");
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
        /// <summary>
        /// 化工合规检索
        /// </summary>
        public async Task<List<RetrievedChunk>> RetrieveChemicalRegulationAsync(string query, string? chemicalType = null, string? regulationType = null, int topK = 5, string? regulationNumber = null)
        {
            // Step 1: BM25 检索（若指定 regulationNumber，在查询中追加以提高召回）
            var searchQuery = query;
            if (!string.IsNullOrEmpty(regulationNumber))
                searchQuery = $"{query} {regulationNumber}";
            var allResults = await RetrieveAsync(searchQuery, topK * 2);
            // Step 2: 过滤结果（元数据精确匹配 + regulationNumber 模糊匹配）
            var filteredResults = allResults.Where(r =>
            {
                var metadata = r.Metadata;

                if (!string.IsNullOrEmpty(chemicalType) && metadata.ContainsKey("ChemicalType"))
                {
                    var docChemicalType = metadata["ChemicalType"]?.ToString() ?? "";
                    if (docChemicalType != "通用" && !docChemicalType.Equals(chemicalType, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                if (!string.IsNullOrEmpty(regulationType) && metadata.ContainsKey("RegulationType"))
                {
                    var docRegulationType = metadata["RegulationType"]?.ToString() ?? "";
                    if (!docRegulationType.Equals(regulationType, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                // [Phase 4] regulationNumber 精确+模糊过滤：优先精确匹配，其次内容包含
                if (!string.IsNullOrEmpty(regulationNumber) && metadata.ContainsKey("RegulationNumber"))
                {
                    var docRegNum = metadata["RegulationNumber"]?.ToString() ?? "";
                    var normalizedQuery = KnowledgeBaseService.NormalizeGbNumbers(regulationNumber);
                    var normalizedDoc = KnowledgeBaseService.NormalizeGbNumbers(docRegNum);
                    // 精确匹配或互相包含
                    if (!normalizedDoc.Equals(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                        && !normalizedDoc.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                        && !normalizedQuery.Contains(normalizedDoc, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                return true;
            }).ToList();
            // Step 3: 重排序 + 来源去重（防止单一文档垄断 topK）
            var scored = filteredResults
                .Select(r => new { Chunk = r, Score = CalculateChemicalRelevanceScore(r) })
                .OrderByDescending(x => x.Score)
                .ToList();

            // [E4 FIX] 来源去重：每个源文档最多 maxPerSource 条，至少保证 minSources 个不同来源
            const int maxPerSource = 2;
            const int minSources = 3;
            var diverseResults = new List<RetrievedChunk>();
            var sourceCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var usedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 第一轮：按 maxPerSource 限制取，收集不同来源
            foreach (var item in scored)
            {
                var sourceFile = item.Chunk.Metadata.TryGetValue("SourceFile", out var sf)
                    ? sf?.ToString() ?? "" : "";
                if (!sourceCounts.ContainsKey(sourceFile))
                    sourceCounts[sourceFile] = 0;
                if (sourceCounts[sourceFile] >= maxPerSource)
                    continue;

                sourceCounts[sourceFile]++;
                usedSources.Add(sourceFile);
                diverseResults.Add(item.Chunk);

                if (diverseResults.Count >= topK && usedSources.Count >= minSources)
                    break;
            }

            // 第二轮：如果来源不够 minSources，放宽限制补足
            if (usedSources.Count < minSources)
            {
                foreach (var item in scored)
                {
                    if (diverseResults.Count >= topK) break;
                    if (diverseResults.Contains(item.Chunk)) continue;
                    diverseResults.Add(item.Chunk);
                }
            }

            var rerankedResults = diverseResults;
            // Step 4: 输出结果
            Console.WriteLine($"   🔬 化工合规检索: 查询='{query}', 危化品={chemicalType ?? "全部"}, 法规类型={regulationType ?? "全部"}, 法规编号={regulationNumber ?? "全部"}, 召回={rerankedResults.Count}条");
            return rerankedResults;
        }

        public async Task LoadChemicalKnowledgeBaseAsync(string knowledgeBasePath)
        {
            Console.WriteLine($"   📚 正在加载化工知识库: {knowledgeBasePath}");
            await _bm25Service.LoadChemicalKnowledgeBaseAsync(knowledgeBasePath);
            Console.WriteLine("   ℹ️ 向量存储与BM25同步完成");
        }

        /// <summary>
        /// [Phase 4.5] 使用优化后的 GB 结构感知分块策略重建知识库索引。
        /// 操作流程: 清空现有索引 → 按 GB 章节结构分块 → 批量嵌入写入 BM25 + 向量库。
        /// 目标: 将 Precision@5 从 9.5% 提升到 ≥50%。
        /// </summary>
        public async Task RebuildIndexWithOptimizedChunksAsync(string knowledgeBasePath)
        {
            Console.WriteLine("   🔄 [Phase 4.5] 使用 GB 结构感知分块重建知识库索引...");
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Step 1: 清空现有索引
            await ClearAsync();
            Console.WriteLine("   🧹 已清空现有 BM25 + 向量索引");

            // Step 2: 扫描知识库目录，按 GB 章节结构分块
            var baseDir = new DirectoryInfo(knowledgeBasePath);
            if (!baseDir.Exists)
            {
                Console.WriteLine($"   ⚠️ 知识库目录不存在: {knowledgeBasePath}");
                return;
            }

            var allChunks = new List<(string content, Dictionary<string, object> metadata)>();
            var processedRegulations = new HashSet<string>();

            // 处理国标文档
            var gbDir = Path.Combine(knowledgeBasePath, "国标");
            if (Directory.Exists(gbDir))
            {
                foreach (var file in Directory.GetFiles(gbDir, "*.txt"))
                {
                    var content = await File.ReadAllTextAsync(file, System.Text.Encoding.UTF8);
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    var regNumber = ExtractRegulationNumber(fileName);

                    // 使用 GB 结构感知分块
                    var chunks = ChunkByGbStructure(content, regNumber, file);

                    foreach (var (chunkContent, metadata) in chunks)
                    {
                        // 补充元数据
                        metadata["RegulationType"] = "国标";
                        metadata["Priority"] = "高";
                        metadata["SourceFile"] = file;
                        metadata["ChemicalType"] = "通用";
                        if (!string.IsNullOrEmpty(regNumber))
                            metadata["RegulationNumber"] = regNumber;
                    }

                    allChunks.AddRange(chunks);
                    if (!string.IsNullOrEmpty(regNumber))
                        processedRegulations.Add(regNumber);

                    Console.WriteLine($"   📄 {fileName}: {chunks.Count} 个语义分块 (法规编号: {regNumber ?? "未识别"})");
                }
            }

            // 处理园区规则
            var parkDir = Path.Combine(knowledgeBasePath, "园区规则");
            if (Directory.Exists(parkDir))
            {
                foreach (var file in Directory.GetFiles(parkDir, "*.txt"))
                {
                    var content = await File.ReadAllTextAsync(file, System.Text.Encoding.UTF8);
                    var chunks = ChunkByGbStructure(content, null, file);
                    foreach (var (chunkContent, metadata) in chunks)
                    {
                        metadata["RegulationType"] = "园区规则";
                        metadata["Priority"] = "中";
                        metadata["SourceFile"] = file;
                        metadata["ChemicalType"] = "通用";
                    }
                    allChunks.AddRange(chunks);
                }
            }

            // 处理历史案例
            var caseDir = Path.Combine(knowledgeBasePath, "历史案例");
            if (Directory.Exists(caseDir))
            {
                foreach (var file in Directory.GetFiles(caseDir, "*.txt"))
                {
                    var content = await File.ReadAllTextAsync(file, System.Text.Encoding.UTF8);
                    var chunks = ChunkByGbStructure(content, null, file);
                    foreach (var (chunkContent, metadata) in chunks)
                    {
                        metadata["RegulationType"] = "历史案例";
                        metadata["Priority"] = "低";
                        metadata["SourceFile"] = file;
                        metadata["ChemicalType"] = "通用";
                    }
                    allChunks.AddRange(chunks);
                }
            }

            Console.WriteLine($"   📊 共生成 {allChunks.Count} 个语义分块 (含 {processedRegulations.Count} 个法规编号)");

            // Step 3: 批量写入 BM25 索引
            var contents = allChunks.Select(c => c.content).ToList();
            await _bm25Service.AddDocumentsAsync(contents);
            Console.WriteLine($"   ✅ BM25 索引完成: {contents.Count} 条");

            // Step 4: 批量嵌入写入向量库
            try
            {
                var swEmbed = System.Diagnostics.Stopwatch.StartNew();
                var llmSvc = _llmService as LlmService;
                float[][]? embeddings;

                if (llmSvc != null)
                    embeddings = await llmSvc.GetEmbeddingsBatchAsync(contents);
                else
                    embeddings = await _llmService.GetEmbeddingsAsync(contents);

                if (embeddings != null && embeddings.Length > 0)
                {
                    var records = new List<ChemicalDocumentRecord>();
                    for (int i = 0; i < contents.Count && i < embeddings.Length; i++)
                    {
                        var metadata = allChunks[i].metadata;
                        records.Add(new ChemicalDocumentRecord
                        {
                            Content = contents[i],
                            RegulationType = metadata.TryGetValue("RegulationType", out var rt) ? rt?.ToString() ?? "通用" : "通用",
                            Priority = metadata.TryGetValue("Priority", out var p) ? p?.ToString() ?? "中" : "中",
                            ChemicalType = metadata.TryGetValue("ChemicalType", out var ct) ? ct?.ToString() : null,
                            SourceFile = metadata.TryGetValue("SourceFile", out var sf) ? sf?.ToString() : null,
                            RegulationNumber = metadata.TryGetValue("RegulationNumber", out var rn) ? rn?.ToString() : null,
                            ChapterTitle = metadata.TryGetValue("chapter_title", out var ch) ? ch?.ToString() : null,
                            Embedding = embeddings[i]
                        });
                    }

                    await _databaseService.AddChemicalDocumentsBatchAsync(records);
                    swEmbed.Stop();
                    Console.WriteLine($"   ✅ 向量索引完成: {records.Count} 条, 耗时 {swEmbed.Elapsed.TotalSeconds:F1}s");
                }
                else
                {
                    Console.WriteLine($"   ⚠️ 嵌入生成失败，仅保留 BM25 索引");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ 向量索引构建失败: {ex.Message}，BM25 索引已保留");
            }

            sw.Stop();
            Console.WriteLine($"   ✅ [Phase 4.5] 索引重建完成: {allChunks.Count} 个分块, 总耗时 {sw.Elapsed.TotalSeconds:F1}s");
        }

        /// <summary>从文件名中提取法规编号</summary>
        private static string? ExtractRegulationNumber(string fileName)
        {
            var match = System.Text.RegularExpressions.Regex.Match(fileName,
                @"(GB\s*/?T?\s*\d{4,5}(?:[.\-]\d{2,4})?)");
            return match.Success ? match.Groups[1].Value.Trim() : null;
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

            await _bm25Service.ClearAsync();
            foreach (var record in docs)
            {
                var metadata = new Dictionary<string, object>
                {
                    ["RegulationType"] = record.RegulationType,
                    ["Priority"] = record.Priority,
                    ["SourceFile"] = record.SourceFile ?? "",
                    ["RegulationNumber"] = record.RegulationNumber ?? "",
                    ["ChapterTitle"] = record.ChapterTitle ?? "",
                    ["ClauseNumber"] = record.ClauseNumber ?? "",
                    ["ChunkIndex"] = record.ChunkIndex ?? 0,
                    ["PageNumber"] = record.PageNumber ?? 0,
                    ["ExtractionQuality"] = record.ExtractionQuality ?? ""
                };

                await _bm25Service.AddDocumentAsync(record.Content, metadata);
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
            // [P0-4 FIX] 使用 Id+内容前缀作去重键, 替代 Guid.NewGuid() 避免随机键破坏 RRF 融合
            for (int rank = 0; rank < bm25Results.Count; rank++)
            {
                // [P0-4 FIX] 使用 Id+内容前缀作去重键, 替代 Guid.NewGuid() 避免随机键破坏 RRF 融合
                var key = GetDedupKey(bm25Results[rank], rank);
                var rrfScore = 1.0 / (rrfK + rank + 1);  // rank 0-based, +1 转 1-based
                rrfScores[key] = (bm25Results[rank], rrfScore);
            }

            // Step 2: 累加向量检索排名贡献
            for (int rank = 0; rank < vectorResults.Count; rank++)
            {
                var key = GetDedupKey(vectorResults[rank], rank);
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

        /// <summary>
        /// [P0-4 FIX] RRF 去重键：优先用 Id，无 Id 时用内容前200字符的确定性哈希。
        /// 避免 Content 为 null 或退化到 Guid.NewGuid() 导致同名文档无法融合。
        /// </summary>
        private static string GetDedupKey(RetrievedChunk chunk, int rank)
        {
            // 有 Id 直接用
            if (!string.IsNullOrWhiteSpace(chunk.Id))
                return $"id:{chunk.Id}";
            // 有内容用内容前缀
            if (!string.IsNullOrWhiteSpace(chunk.Content))
                return $"c:{chunk.Content.Trim().Substring(0, Math.Min(200, chunk.Content.Length))}";
            // 最后兜底：用 rank，至少保证同排名可以碰撞
            return $"rank:{rank}";
        }

        // ════════════════════════════════════════
        // Phase 4.2: GB标准结构感知分块优化
        // ════════════════════════════════════════

        /// <summary>
        /// [Phase 4.2] 基于GB标准结构的语义分块：按"第X章"、"X.X.X"条目号边界分割。
        /// 目标: chunk 大小 200-500 字符，每个 chunk 带完整 regulation_number/chapter/clause 元数据。
        /// </summary>
        /// <param name="fullText">完整文档原文</param>
        /// <param name="regulationNumber">法规编号（如 GB 30000.14）</param>
        /// <param name="sourceFile">源文件名</param>
        /// <returns>分块列表，每块含结构化元数据</returns>
        public static List<(string content, Dictionary<string, object> metadata)> ChunkByGbStructure(
            string fullText, string? regulationNumber = null, string? sourceFile = null)
        {
            var chunks = new List<(string content, Dictionary<string, object> metadata)>();
            if (string.IsNullOrWhiteSpace(fullText))
                return chunks;

            // 按 GB 标准章节标题分割：匹配 "第X章" 或 "X.X.X" 条目编号
            var chapterPattern = @"(?:^|\n)\s*(第[一二三四五六七八九十百零0-9]+[章节条]|\d+(?:\.\d+)*)\s+(.+?)(?=\n\s*(?:第[一二三四五六七八九十百零0-9]+[章节条]|\d+(?:\.\d+)*)\s+|$)";
            var matches = System.Text.RegularExpressions.Regex.Matches(fullText, chapterPattern,
                System.Text.RegularExpressions.RegexOptions.Singleline);

            if (matches.Count == 0)
            {
                // 无法按章节分割，按固定大小分块
                chunks.AddRange(ChunkByFixedSize(fullText, regulationNumber, sourceFile));
                return chunks;
            }

            // 收集每个章节段
            var sections = new List<(string chapterId, string chapterTitle, string content)>();

            for (int i = 0; i < matches.Count; i++)
            {
                var match = matches[i];
                var chapterId = match.Groups[1].Value.Trim();
                var chapterTitle = match.Groups[2].Value.Trim();
                var sectionStart = match.Index;
                // 确定段落结束位置
                var sectionEnd = (i + 1 < matches.Count) ? matches[i + 1].Index : fullText.Length;
                var sectionContent = fullText.Substring(sectionStart, sectionEnd - sectionStart).Trim();

                if (!string.IsNullOrWhiteSpace(sectionContent))
                    sections.Add((chapterId, chapterTitle, sectionContent));
            }

            // 对每个章节按 200-500 字符分块
            const int targetSize = 400;
            const int minSize = 200;
            const int maxSize = 600;
            int chunkIndex = 0;

            foreach (var (chapterId, chapterTitle, sectionContent) in sections)
            {
                if (sectionContent.Length <= maxSize)
                {
                    // 短章节直接作为一个 chunk
                    var metadata = new Dictionary<string, object>
                    {
                        ["regulation_number"] = regulationNumber ?? "",
                        ["chapter"] = chapterId,
                        ["chapter_title"] = chapterTitle,
                        ["chunk_index"] = chunkIndex++
                    };
                    if (sourceFile != null)
                        metadata["source"] = sourceFile;
                    chunks.Add((sectionContent, metadata));
                }
                else
                {
                    // 长章节按句子边界再分块
                    var subChunks = SplitBySentenceBoundary(sectionContent, targetSize, minSize, maxSize);
                    for (int j = 0; j < subChunks.Count; j++)
                    {
                        var metadata = new Dictionary<string, object>
                        {
                            ["regulation_number"] = regulationNumber ?? "",
                            ["chapter"] = chapterId,
                            ["chapter_title"] = chapterTitle,
                            ["chunk_index"] = chunkIndex++,
                            ["sub_chunk"] = j
                        };
                        if (sourceFile != null)
                            metadata["source"] = sourceFile;
                        chunks.Add((subChunks[j], metadata));
                    }
                }
            }

            return chunks;
        }

        /// <summary>按句子边界分块，避免在句子中间截断</summary>
        private static List<string> SplitBySentenceBoundary(string text, int targetSize, int minSize, int maxSize)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
                return result;

            var sentences = System.Text.RegularExpressions.Regex.Split(text, @"(?<=[。；！？\n])");
            var current = "";

            foreach (var sentence in sentences)
            {
                if (string.IsNullOrWhiteSpace(sentence))
                    continue;

                if ((current + sentence).Length > maxSize && current.Length >= minSize)
                {
                    result.Add(current.Trim());
                    current = sentence;
                }
                else
                {
                    current += sentence;
                }
            }

            if (current.Trim().Length > 0)
                result.Add(current.Trim());

            return result;
        }

        /// <summary>降级方案：固定大小分块（无章节结构时使用）</summary>
        private static List<(string content, Dictionary<string, object> metadata)> ChunkByFixedSize(
            string text, string? regulationNumber, string? sourceFile)
        {
            var chunks = new List<(string content, Dictionary<string, object> metadata)>();
            const int chunkSize = 400;
            int start = 0;
            int index = 0;

            while (start < text.Length)
            {
                var end = Math.Min(start + chunkSize, text.Length);
                // 尝试在句子末尾截断
                if (end < text.Length)
                {
                    var sentenceEnd = text.LastIndexOfAny(new[] { '。', '；', '！', '？', '\n' }, end, end - start);
                    if (sentenceEnd > start)
                        end = sentenceEnd + 1;
                }

                var chunk = text.Substring(start, end - start).Trim();
                if (!string.IsNullOrEmpty(chunk))
                {
                    var metadata = new Dictionary<string, object>
                    {
                        ["regulation_number"] = regulationNumber ?? "",
                        ["chunk_index"] = index++
                    };
                    if (sourceFile != null)
                        metadata["source"] = sourceFile;
                    chunks.Add((chunk, metadata));
                }
                start = end;
            }

            return chunks;
        }

        // ════════════════════════════════════════
        // Phase 4.4: HyDE (Hypothetical Document Embedding) 检索增强
        // ════════════════════════════════════════

        /// <summary>
        /// [Phase 4.4] HyDE 检索增强：用 LLM 生成理想答案片段，再用该片段做向量检索。
        /// 适用场景：储存兼容性判定、综合审核等需要法规解释的场景，数据库未命中时的兜底检索。
        /// 预期效果：Recall 提升 20-30%。
        /// 注意：仅在数据库未命中场景启用，避免对确定性查询引入额外延迟。
        /// </summary>
        /// <param name="query">原始查询</param>
        /// <param name="context">查询上下文（如涉及化学品名称、场景描述）</param>
        /// <param name="topK">返回数量</param>
        /// <returns>增强后的检索结果</returns>
        public async Task<List<RetrievedChunk>> HydeRetrieveAsync(string query, string? context = null, int topK = 5)
        {
            if (!_kbConfig.EnableQueryExpansion)
                return await RetrieveAsync(query, topK);

            Console.WriteLine($"   🧠 HyDE 检索增强: 生成假设文档...");

            try
            {
                // Step 1: 用 LLM 生成"理想答案片段"
                var hydePrompt = BuildHydePrompt(query, context);
                string? hydeDocument;

                try
                {
                    var llmSvc = _llmService as LlmService;
                    if (llmSvc != null)
                    {
                        // [策略切换] 流式优先（GPU 3090环境），非流式仅作CPU低算力降级
                        // [Bug C 修复] HyDE 是纯文本生成，禁用 FC 防止递归调用 CheckStorageCompatibility
                        hydeDocument = await llmSvc.InvokeStreamWithRetryAsync(hydePrompt, ConsoleColor.Gray, "HyDE生成",
                            fcBehavior: FunctionChoiceBehavior.None());
                        // 流式空回退时降级到非流式
                        if (string.IsNullOrWhiteSpace(hydeDocument))
                            hydeDocument = await llmSvc.InvokeNonStreamingWithRetryAsync(hydePrompt, "HyDE生成(降级)",
                                fcBehavior: FunctionChoiceBehavior.None());
                    }
                    else
                    {
                        // 无 LlmService 引用，降级为普通检索
                        Console.WriteLine($"   ⚠️ HyDE 不可用 (无LlmService)，降级为普通检索");
                        return await RetrieveAsync(query, topK);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ⚠️ HyDE 生成失败: {ex.Message}，降级为普通检索");
                    return await RetrieveAsync(query, topK);
                }

                if (string.IsNullOrWhiteSpace(hydeDocument))
                {
                    Console.WriteLine($"   ⚠️ HyDE 生成空文档，降级为普通检索");
                    return await RetrieveAsync(query, topK);
                }

                // 截断过长生成结果
                if (hydeDocument.Length > 800)
                    hydeDocument = hydeDocument.Substring(0, 800);

                Console.WriteLine($"   📝 HyDE 生成文档: {hydeDocument.Substring(0, Math.Min(100, hydeDocument.Length))}...");

                // Step 2: 用假设文档做向量检索（获取语义匹配的 chunk）
                var embedding = await _llmService.GetEmbeddingAsync(hydeDocument);
                if (embedding == null)
                {
                    Console.WriteLine($"   ⚠️ HyDE 嵌入失败，降级为普通检索");
                    return await RetrieveAsync(query, topK);
                }

                // Step 3: 向量检索（纯语义匹配）
                var hydeResults = await _databaseService.VectorSearchAsync(hydeDocument, embedding, topK * 2);

                // Step 4: 结合原始查询的 BM25 结果做 RRF 融合
                var bm25Results = await _bm25Service.RetrieveAsync(query, topK * 2);

                const double rrfK = 60.0;
                var rrfScores = new Dictionary<string, (RetrievedChunk chunk, double rrfScore)>();

                for (int rank = 0; rank < hydeResults.Count; rank++)
                {
                    var key = GetDedupKey(hydeResults[rank], rank);
                    rrfScores[key] = (hydeResults[rank], 1.0 / (rrfK + rank + 1));
                }

                for (int rank = 0; rank < bm25Results.Count; rank++)
                {
                    var key = GetDedupKey(bm25Results[rank], rank);
                    var score = 1.0 / (rrfK + rank + 1);
                    if (rrfScores.ContainsKey(key))
                        rrfScores[key] = (rrfScores[key].chunk, rrfScores[key].rrfScore + score);
                    else
                        rrfScores[key] = (bm25Results[rank], score);
                }

                var finalResults = rrfScores.Values
                    .OrderByDescending(x => x.rrfScore)
                    .Take(topK)
                    .Select((x, idx) => new RetrievedChunk
                    {
                        Content = x.chunk.Content,
                        Score = x.rrfScore,
                        Rank = idx,
                        Metadata = x.chunk.Metadata,
                        RetrievalMethod = "HyDE-RRF"
                    })
                    .ToList();

                Console.WriteLine($"   ✅ HyDE-RRF 检索完成: {finalResults.Count} 条");
                return finalResults;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ HyDE 检索异常: {ex.Message}，降级为普通检索");
                return await RetrieveAsync(query, topK);
            }
        }

        /// <summary>构建 HyDE 提示词</summary>
        private static string BuildHydePrompt(string query, string? context)
        {
            var contextPart = !string.IsNullOrWhiteSpace(context)
                ? $"\n【上下文信息】{context}"
                : "";

            return $@"你是一名化工安全专家。请根据以下问题，生成一段理想的法规条文摘要（100-300字），这段摘要应包含回答问题所需的关键信息。

【问题】{query}{contextPart}

请生成一段包含关键法规编号、安全距离、分类信息的理想答案片段。只输出内容，不要任何解释或前缀。";
        }
    }
}
