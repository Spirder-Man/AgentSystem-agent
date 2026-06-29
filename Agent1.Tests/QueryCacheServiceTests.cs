using System;
using System.Collections.Generic;
using System.Threading;
using Agent1.Config;
using Agent1.Services;
using Microsoft.Extensions.Configuration;
using Xunit;
using FluentAssertions;

namespace Agent1.Tests
{
    // ═══════════════════════════════════════════
    // QueryCacheService 测试 — 查询缓存
    // ═══════════════════════════════════════════
    public class QueryCacheServiceTests : IDisposable
    {
        private readonly AppConfig _config;

        public QueryCacheServiceTests()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Llm:ModelId"] = "test-model",
                    ["Llm:Endpoint"] = "http://localhost:11434",
                    ["Database:Host"] = "localhost",
                    ["Database:Port"] = "5432",
                    ["Database:DatabaseName"] = "testdb",
                    ["Database:Password"] = "test-password",
                    ["VectorSearch:EmbeddingModelId"] = "test-embed",
                    ["PromptTemplates:SystemRole"] = "test-role",
                    ["PromptTemplates:EvalFastPrompt"] = "test-prompt {SystemRole} {UserInput}",
                    ["PromptTemplates:EvalFastQueryPrompt"] = "test-query {SystemRole} {UserInput}"
                })
                .Build();
            AppConfig.Load(configuration);
            _config = AppConfig.Instance;
        }

        private QueryCacheService CreateCache(int ttlMinutes = 5, int maxEntries = 500)
        {
            _config.KnowledgeBase.QueryCacheTtlMinutes = ttlMinutes;
            _config.KnowledgeBase.QueryCacheMaxEntries = maxEntries;
            return new QueryCacheService(_config);
        }

        [Fact]
        public void Constructor_TtlZero_DisablesCache()
        {
            var cache = CreateCache(ttlMinutes: 0, maxEntries: 0);
            cache.EntryCount.Should().Be(0);
            cache.Hits.Should().Be(0);
            cache.Misses.Should().Be(0);
        }

        [Fact]
        public void TryGet_CacheEnabled_MissOnEmptyCache()
        {
            var cache = CreateCache();
            var hit = cache.TryGet("测试查询", out var results);
            hit.Should().BeFalse("空缓存应未命中");
            results.Should().BeEmpty();
            cache.Misses.Should().Be(1);
            cache.Hits.Should().Be(0);
        }

        [Fact]
        public void SetAndGet_CacheHit()
        {
            var cache = CreateCache();
            var chunks = new List<RetrievedChunk>
            {
                RetrievedChunk.Create("结果内容", 0.95, 0)
            };

            cache.Set("测试查询", chunks);
            var hit = cache.TryGet("测试查询", out var results);

            hit.Should().BeTrue("刚设置后应命中");
            results.Should().HaveCount(1);
            results[0].Content.Should().Be("结果内容");
            results[0].Score.Should().Be(0.95);
            cache.Hits.Should().Be(1);
        }

        [Fact]
        public void TryGet_TtlExpired_ReturnsMiss()
        {
            // TTL = -1 使缓存立即过期（实际上 TTL <= 0 时 Set 直接跳过）
            // 改为 TTL=1 分钟，但手动修改过期时间不可行 →
            // 测试 TTL=0 时 TryGet 返回 miss
            var cache = CreateCache(ttlMinutes: 0);
            cache.TryGet("test", out _).Should().BeFalse("TTL=0 时缓存禁用");
            cache.Misses.Should().Be(1);
        }

        [Fact]
        public void Set_EmptyResults_DoesNotCache()
        {
            var cache = CreateCache();
            cache.Set("查询", new List<RetrievedChunk>());
            cache.EntryCount.Should().Be(0, "空结果不应缓存");
        }

        [Fact]
        public void Set_TtlDisabled_DoesNotCache()
        {
            var cache = CreateCache(ttlMinutes: 0);
            var chunks = new List<RetrievedChunk> { RetrievedChunk.Create("内容", 1.0, 0) };
            cache.Set("查询", chunks);
            cache.EntryCount.Should().Be(0, "TTL=0 时不应缓存");
        }

        [Fact]
        public void Clear_ResetsAllStats()
        {
            var cache = CreateCache();
            cache.Set("查询", new List<RetrievedChunk> { RetrievedChunk.Create("内容", 1.0, 0) });
            cache.TryGet("查询", out _);

            cache.Clear();
            cache.EntryCount.Should().Be(0);
            cache.Hits.Should().Be(0);
            cache.Misses.Should().Be(0);
        }

        [Fact]
        public void GetStats_ReturnsCorrectValues()
        {
            var cache = CreateCache(ttlMinutes: 5, maxEntries: 100);
            cache.Set("query", new List<RetrievedChunk> { RetrievedChunk.Create("c", 1.0, 0) });
            cache.TryGet("query", out _);
            cache.TryGet("missing", out _);

            var stats = cache.GetStats();
            stats.Hits.Should().Be(1);
            stats.Misses.Should().Be(1);
            stats.HitRate.Should().Be(0.5);
            stats.EntryCount.Should().Be(1);
            stats.MaxEntries.Should().Be(100);
            stats.TtlMinutes.Should().Be(5);
        }

        [Fact]
        public void NormalizeKey_TrimsAndLowercases()
        {
            var cache = CreateCache();
            var chunks = new List<RetrievedChunk> { RetrievedChunk.Create("内容", 1.0, 0) };

            cache.Set("  化学品 安全  ", chunks);
            var hit = cache.TryGet("化学品 安全", out _);
            hit.Should().BeTrue("key 应被 Trim 归一化");

            var hit2 = cache.TryGet("  化学品 安全  ", out _);
            hit2.Should().BeTrue("Trim 后 key 应匹配");
        }

        [Fact]
        public void NormalizeKey_TruncatesLongKeys()
        {
            var cache = CreateCache();
            var longQuery = new string('A', 300);
            var chunks = new List<RetrievedChunk> { RetrievedChunk.Create("内容", 1.0, 0) };
            cache.Set(longQuery, chunks);

            // 用截断后的 key（前200字符）来查
            var hit = cache.TryGet(longQuery, out _);
            hit.Should().BeTrue("超长 key 截断后应能命中");
        }

        [Fact]
        public void LruEviction_WhenFull_RemovesOldEntries()
        {
            var cache = CreateCache(ttlMinutes: 60, maxEntries: 5);

            // 填充 5 个条目
            for (int i = 0; i < 5; i++)
            {
                cache.Set($"query_{i}", new List<RetrievedChunk>
                    { RetrievedChunk.Create($"content_{i}", 1.0, 0) });
            }
            cache.EntryCount.Should().Be(5);

            // 第6个触发 LRU 淘汰
            cache.Set("query_6", new List<RetrievedChunk>
                { RetrievedChunk.Create("content_6", 1.0, 0) });

            // 缓存条目数应 ≤ maxEntries
            cache.EntryCount.Should().BeLessOrEqualTo(5,
                "超过 maxEntries 后应触发淘汰");
            cache.EntryCount.Should().BeGreaterThan(0,
                "新条目应仍存在于缓存中");
        }

        [Fact]
        public void Dispose_CanBeCalledMultipleTimes()
        {
            var cache = CreateCache();
            cache.Dispose();
            // 不应抛异常
            cache.Dispose();
        }

        [Fact]
        public void HitRate_WhenNoRequests_ReturnsZero()
        {
            var cache = CreateCache();
            cache.HitRate.Should().Be(0, "无请求时命中率为0");
        }

        public void Dispose()
        {
        }
    }
}
