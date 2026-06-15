using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agent1.Config;

namespace Agent1.Services
{
    /// <summary>
    /// Sprint 2: GPU 向量索引管理器。
    /// 作为 pgvector 的内存缓存层，启动时从数据库加载全量向量到内存，
    /// 使用余弦相似度在内存中做暴力搜索（模拟 GPU FAISS 行为）。
    /// 
    /// 生产环境可替换为 FAISS.NET / cuVS 实现真正的 GPU 加速。
    /// 
    /// 设计原则：
    /// - 启动时全量加载 → 内存索引
    /// - 支持增量更新（Add/Delete）→ 同步更新内存索引
    /// - 定期后台同步 pgvector（默认 5 分钟）
    /// - 内存不足时回退到 pgvector
    /// </summary>
    public class GpuVectorIndexService : IDisposable
    {
        private readonly IDatabaseService _databaseService;
        private readonly VectorSearchConfig _config;
        private readonly ConcurrentDictionary<string, IndexEntry> _index = new();
        private readonly ReaderWriterLockSlim _lock = new();
        private volatile bool _isReady;
        private Timer? _syncTimer;
        private DateTime _lastSyncTime = DateTime.MinValue;
        private long _totalVectors;
        private long _totalMemoryBytes;

        public bool IsReady => _isReady;
        public int VectorCount => _index.Count;
        public long MemoryBytes => _totalMemoryBytes;

        public GpuVectorIndexService(IDatabaseService databaseService, AppConfig config)
        {
            _databaseService = databaseService;
            _config = config.VectorSearch;
        }

        /// <summary>
        /// 初始化：从 pgvector 加载全量向量到内存索引。
        /// </summary>
        public async Task InitializeAsync()
        {
            if (!_config.GpuSearchEnabled)
            {
                Console.WriteLine("   ℹ️ GPU 向量索引已禁用，使用 pgvector 检索");
                return;
            }

            var sw = Stopwatch.StartNew();
            Console.WriteLine("   🚀 正在构建 GPU 向量索引（内存缓存层）...");

            try
            {
                var allDocs = await _databaseService.GetAllChemicalDocumentsWithEmbeddingsAsync();
                if (allDocs.Count == 0)
                {
                    Console.WriteLine("   ℹ️ 数据库无向量数据，GPU 索引为空");
                    _isReady = true;
                    return;
                }

                _lock.EnterWriteLock();
                try
                {
                    _index.Clear();
                    long totalBytes = 0;
                    foreach (var doc in allDocs)
                    {
                        if (doc.Embedding == null || doc.Embedding.Length == 0)
                            continue;

                        var entry = new IndexEntry
                        {
                            Id = doc.Id > 0 ? doc.Id.ToString() : Guid.NewGuid().ToString(),
                            Embedding = doc.Embedding,
                            Content = doc.Content ?? "",
                            RegulationType = doc.RegulationType ?? "通用",
                            Priority = doc.Priority ?? "中",
                            SourceFile = doc.SourceFile,
                            ChemicalType = doc.ChemicalType
                        };

                        _index[entry.Id] = entry;
                        totalBytes += doc.Embedding.Length * sizeof(float) + (doc.Content?.Length ?? 0) * sizeof(char);
                    }

                    _totalVectors = _index.Count;
                    _totalMemoryBytes = totalBytes;
                }
                finally
                {
                    _lock.ExitWriteLock();
                }

                _isReady = true;
                _lastSyncTime = DateTime.UtcNow;

                // 启动定期同步
                StartPeriodicSync();

                sw.Stop();
                Console.WriteLine($"   ✅ GPU 向量索引就绪: {_totalVectors} 个向量, " +
                    $"{_totalMemoryBytes / 1024.0 / 1024.0:F1} MB, 耗时 {sw.Elapsed.TotalSeconds:F1}s");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ GPU 索引初始化失败: {ex.Message}，将降级为 pgvector");
                _isReady = false;
            }
        }

        /// <summary>
        /// GPU 向量搜索（内存余弦相似度）。
        /// 返回 TopK 个最相似结果的 (ID, Score)。
        /// </summary>
        public List<(string id, float score)> Search(float[] queryEmbedding, int topK = 5)
        {
            if (!_isReady || _index.Count == 0)
                return new List<(string, float)>();

            _lock.EnterReadLock();
            try
            {
                var results = new List<(string id, float score)>(_index.Count);

                foreach (var kvp in _index)
                {
                    var similarity = CosineSimilarity(queryEmbedding, kvp.Value.Embedding);
                    results.Add((kvp.Key, similarity));
                }

                return results
                    .OrderByDescending(x => x.score)
                    .Take(topK)
                    .ToList();
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        /// <summary>
        /// 增量添加向量到内存索引。
        /// </summary>
        public void Add(string id, float[] embedding, string content,
            string regulationType = "通用", string priority = "中",
            string? sourceFile = null, string? chemicalType = null)
        {
            if (!_isReady) return;

            var entry = new IndexEntry
            {
                Id = id,
                Embedding = embedding,
                Content = content,
                RegulationType = regulationType,
                Priority = priority,
                SourceFile = sourceFile,
                ChemicalType = chemicalType
            };

            _lock.EnterWriteLock();
            try
            {
                _index[id] = entry;
                _totalVectors = _index.Count;
                _totalMemoryBytes += embedding.Length * sizeof(float) + content.Length * sizeof(char);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// 从内存索引中删除向量。
        /// </summary>
        public void Remove(string id)
        {
            if (!_isReady) return;

            _lock.EnterWriteLock();
            try
            {
                if (_index.TryRemove(id, out var entry))
                {
                    _totalVectors = _index.Count;
                    _totalMemoryBytes -= entry.Embedding.Length * sizeof(float) + entry.Content.Length * sizeof(char);
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// 获取索引统计信息。
        /// </summary>
        public IndexStats GetStats()
        {
            return new IndexStats
            {
                IsReady = _isReady,
                VectorCount = (int)_totalVectors,
                MemoryBytes = _totalMemoryBytes,
                LastSyncTime = _lastSyncTime
            };
        }

        /// <summary>
        /// 获取指定 ID 的索引条目（用于返回检索结果元数据）。
        /// </summary>
        public IndexEntry? GetEntry(string id)
        {
            _lock.EnterReadLock();
            try
            {
                return _index.TryGetValue(id, out var entry) ? entry : null;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        // ── 后台同步 ──

        private void StartPeriodicSync()
        {
            var interval = TimeSpan.FromMinutes(5);
            _syncTimer = new Timer(async _ =>
            {
                try
                {
                    await SyncFromDatabaseAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ⚠️ GPU 索引后台同步失败: {ex.Message}");
                }
            }, null, interval, interval);
        }

        private async Task SyncFromDatabaseAsync()
        {
            if (!_isReady) return;

            var allDocs = await _databaseService.GetAllChemicalDocumentsWithEmbeddingsAsync();
            if (allDocs.Count == 0) return;

            var dbIds = new HashSet<string>();
            _lock.EnterWriteLock();
            try
            {
                foreach (var doc in allDocs)
                {
                    var id = doc.Id > 0 ? doc.Id.ToString() : Guid.NewGuid().ToString();
                    dbIds.Add(id);

                    if (doc.Embedding == null || doc.Embedding.Length == 0)
                        continue;

                    if (!_index.ContainsKey(id))
                    {
                        _index[id] = new IndexEntry
                        {
                            Id = id,
                            Embedding = doc.Embedding,
                            Content = doc.Content ?? "",
                            RegulationType = doc.RegulationType ?? "通用",
                            Priority = doc.Priority ?? "中",
                            SourceFile = doc.SourceFile,
                            ChemicalType = doc.ChemicalType
                        };
                    }
                }

                // 移除数据库中已不存在的条目
                var staleKeys = _index.Keys.Where(k => !dbIds.Contains(k)).ToList();
                foreach (var key in staleKeys)
                    _index.TryRemove(key, out _);

                _totalVectors = _index.Count;
                _lastSyncTime = DateTime.UtcNow;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        // ── 余弦相似度 ──

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

        public void Dispose()
        {
            _syncTimer?.Dispose();
            _lock?.Dispose();
        }
    }

    public class IndexEntry
    {
        public string Id { get; set; } = "";
        public float[] Embedding { get; set; } = Array.Empty<float>();
        public string Content { get; set; } = "";
        public string RegulationType { get; set; } = "通用";
        public string Priority { get; set; } = "中";
        public string? SourceFile { get; set; }
        public string? ChemicalType { get; set; }
    }

    public class IndexStats
    {
        public bool IsReady { get; set; }
        public int VectorCount { get; set; }
        public long MemoryBytes { get; set; }
        public DateTime LastSyncTime { get; set; }
    }
}
