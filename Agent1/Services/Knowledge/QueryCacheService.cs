using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Agent1.Config;

namespace Agent1.Services
{
    /// <summary>
    /// Sprint 5: 检索查询缓存层。
    /// 
    /// 对高频查询结果进行 LRU 缓存，避免重复执行昂贵的嵌入+检索操作。
    /// 
    /// 配置：
    /// - TTL: 默认 5 分钟（可通过 KnowledgeBase.QueryCacheTtlMinutes 配置）
    /// - 最大条目: 默认 500
    /// - 线程安全：ConcurrentDictionary + 后台清理
    /// </summary>
    public class QueryCacheService : IDisposable
    {
        private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
        private readonly int _ttlMinutes;
        private readonly int _maxEntries;
        private readonly Timer? _cleanupTimer;
        private long _hits;
        private long _misses;

        public long Hits => _hits;
        public long Misses => _misses;
        public double HitRate => (_hits + _misses) > 0 ? (double)_hits / (_hits + _misses) : 0;
        public int EntryCount => _cache.Count;

        public QueryCacheService(AppConfig config)
        {
            var kbConfig = config.KnowledgeBase;
            _ttlMinutes = kbConfig.QueryCacheTtlMinutes;
            _maxEntries = kbConfig.QueryCacheMaxEntries;

            if (_ttlMinutes > 0 && _maxEntries > 0)
            {
                // 每 2 分钟清理一次过期条目
                _cleanupTimer = new Timer(_ => CleanupExpired(), null,
                    TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2));
                Console.WriteLine($"   🗄️ 查询缓存已启用: TTL={_ttlMinutes}min, 最大={_maxEntries} 条");
            }
            else
            {
                Console.WriteLine("   ℹ️ 查询缓存已禁用 (TTL=0 或 MaxEntries=0)");
            }
        }

        /// <summary>
        /// 尝试从缓存获取检索结果。
        /// </summary>
        /// <param name="query">查询文本</param>
        /// <param name="results">缓存的检索结果（命中时）</param>
        /// <returns>是否命中缓存</returns>
        public bool TryGet(string query, out List<RetrievedChunk> results)
        {
            results = new List<RetrievedChunk>();

            if (_ttlMinutes <= 0)
            {
                Interlocked.Increment(ref _misses);
                return false;
            }

            var cacheKey = NormalizeKey(query);
            if (_cache.TryGetValue(cacheKey, out var entry))
            {
                if (entry.ExpiresAt > DateTime.UtcNow)
                {
                    Interlocked.Increment(ref _hits);
                    results = entry.Results;
                    return true;
                }

                // 过期，移除
                _cache.TryRemove(cacheKey, out _);
            }

            Interlocked.Increment(ref _misses);
            return false;
        }

        /// <summary>
        /// 将检索结果加入缓存。
        /// </summary>
        public void Set(string query, List<RetrievedChunk> results)
        {
            if (_ttlMinutes <= 0 || results.Count == 0)
                return;

            var cacheKey = NormalizeKey(query);

            // LRU 淘汰：超过最大条目时，清空所有过期条目
            if (_cache.Count >= _maxEntries)
            {
                CleanupExpired();

                // 如果还是满的，随机淘汰一半（简单策略）
                if (_cache.Count >= _maxEntries)
                {
                    var keys = _cache.Keys;
                    int toRemove = _cache.Count / 2;
                    int removed = 0;
                    foreach (var key in keys)
                    {
                        if (removed >= toRemove) break;
                        if (_cache.TryRemove(key, out _))
                            removed++;
                    }
                }
            }

            _cache[cacheKey] = new CacheEntry
            {
                Results = results,
                ExpiresAt = DateTime.UtcNow.AddMinutes(_ttlMinutes)
            };
        }

        /// <summary>
        /// 清空所有缓存。
        /// </summary>
        public void Clear()
        {
            _cache.Clear();
            _hits = 0;
            _misses = 0;
        }

        /// <summary>
        /// 获取缓存命中率统计。
        /// </summary>
        public QueryCacheStats GetStats()
        {
            return new QueryCacheStats
            {
                Hits = _hits,
                Misses = _misses,
                HitRate = HitRate,
                EntryCount = _cache.Count,
                MaxEntries = _maxEntries,
                TtlMinutes = _ttlMinutes
            };
        }

        // ── 内部方法 ──

        private static string NormalizeKey(string query)
        {
            // 去除多余空白，统一小写
            var normalized = (query ?? "").Trim();
            // 截断过长 key
            if (normalized.Length > 200)
                normalized = normalized.Substring(0, 200);
            return normalized.ToLowerInvariant();
        }

        private void CleanupExpired()
        {
            var now = DateTime.UtcNow;
            foreach (var kvp in _cache)
            {
                if (kvp.Value.ExpiresAt <= now)
                    _cache.TryRemove(kvp.Key, out _);
            }
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
        }

        private class CacheEntry
        {
            public List<RetrievedChunk> Results { get; set; } = new();
            public DateTime ExpiresAt { get; set; }
        }
    }

    public class QueryCacheStats
    {
        public long Hits { get; set; }
        public long Misses { get; set; }
        public double HitRate { get; set; }
        public int EntryCount { get; set; }
        public int MaxEntries { get; set; }
        public int TtlMinutes { get; set; }
    }
}
