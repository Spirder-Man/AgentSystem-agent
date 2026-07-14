using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Agent1.Config;
using Agent1.Services;
using FluentAssertions;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// P5a-1: RerankerService 精排服务测试
///
/// 聚焦 LocalHeuristicRerank（纯逻辑，无需 External API）：
///   - 关键词密度加权 (query terms frequency in content)
///   - 位置加权 (<100 字符=1.5x, <300=1.2x, else=1.0x)
///   - GB 编号匹配加分 (+2.0)
///   - 综合分公式: Score*0.3 + bonus*0.1
///   - 空候选/候选不足 topK/disabled 直接返回
/// </summary>
public class RerankerServiceTests
{
    // ── Helper: 创建最小 AppConfig (reranker disabled, 不触发远程调用) ──

    private static AppConfig DisabledConfig => new()
    {
        VectorSearch = new VectorSearchConfig
        {
            RerankerEnabled = false,
            RerankerEndpoint = "",
            RerankerModelId = ""
        }
    };

    private static AppConfig EnabledConfig => new()
    {
        VectorSearch = new VectorSearchConfig
        {
            RerankerEnabled = true,
            RerankerEndpoint = "http://localhost:9999/rerank",
            RerankerModelId = "bge-reranker-v2-m3"
        }
    };

    // ── 关键词密度加权 ──

    [Fact]
    public void LocalHeuristicRerank_KeywordFrequency_HigherWins()
    {
        var svc = new RerankerService(EnabledConfig);
        var candidates = new List<RetrievedChunk>
        {
            new() { Id = "1", Content = "GB 15603 危险化学品储存通则", Score = 0.8 },
            new() { Id = "2", Content = "GB 15603 §4.2.2 明确规定了禁忌物料不得同库储存 GB 15603 是该领域核心标准", Score = 0.8 }
        };

        // query 包含 "GB 15603"，在两个文档中都有出现
        // doc2 出现更多次 → 应有更高分
        var result = CallLocalHeuristicRerank(svc, "GB 15603 禁忌物料", candidates, 2);

        result.Should().HaveCount(2);
        // doc2 包含 "GB 15603" 和 "禁忌"，得分应≥doc1
        result[0].Id.Should().Be("2");
    }

    [Fact]
    public void LocalHeuristicRerank_PositionWeight_EarlyPositionWins()
    {
        var svc = new RerankerService(EnabledConfig);
        var candidates = new List<RetrievedChunk>
        {
            new() { Id = "early", Content = "GB 30000 化学品分类标准详细说明...", Score = 0.7 },
            new() { Id = "late", Content = "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx GB 30000 (此处仅末尾提及)", Score = 0.7 }
        };

        // "GB 30000" 在早期出现 → 1.5x 权重
        var result = CallLocalHeuristicRerank(svc, "GB 30000 分类", candidates, 2);

        result[0].Id.Should().Be("early");
    }

    [Fact]
    public void LocalHeuristicRerank_GbNumberMatch_GetsBonus()
    {
        var svc = new RerankerService(EnabledConfig);
        var candidates = new List<RetrievedChunk>
        {
            new() { Id = "no-gb", Content = "化学品储存需要遵循相关标准", Score = 0.9 },
            new() { Id = "has-gb", Content = "... GB 15603-2022 §4.2.2 规定...", Score = 0.7 }
        };

        var result = CallLocalHeuristicRerank(svc, "储存标准", candidates, 2);

        // 带 GB 编号的文档因 regex 匹配获 +2.0 bonus → 总分更高
        result[0].Id.Should().Be("has-gb");
    }

    [Fact]
    public void LocalHeuristicRerank_GbNumberVariants_AllMatch()
    {
        var svc = new RerankerService(EnabledConfig);
        var variants = new[]
        {
            "GB 15603",
            "GB/T 15603",
            "GB15603",
            "GB50016"
        };

        foreach (var variant in variants)
        {
            var candidates = new List<RetrievedChunk>
            {
                new() { Id = "gb", Content = variant, Score = 0.5 }
            };
            var result = CallLocalHeuristicRerank(svc, "test", candidates, 1);
            result.Should().HaveCount(1);
            result[0].RetrievalMethod.Should().Contain("+LocalRerank");
        }
    }

    [Fact]
    public void LocalHeuristicRerank_EmptyQuery_KeepsOriginalOrder()
    {
        var svc = new RerankerService(EnabledConfig);
        var candidates = new List<RetrievedChunk>
        {
            new() { Id = "a", Content = "文档A", Score = 0.9 },
            new() { Id = "b", Content = "文档B", Score = 0.5 }
        };

        var result = CallLocalHeuristicRerank(svc, "", candidates, 2);

        result.Should().HaveCount(2);
        result[0].Id.Should().Be("a"); // 原始序保留
    }

    // ── 边界条件 ──

    [Fact]
    public async Task RerankAsync_Disabled_ReturnsAsIs()
    {
        var svc = new RerankerService(DisabledConfig);
        var candidates = new List<RetrievedChunk>
        {
            new() { Id = "1", Content = "doc1", Score = 0.9 },
            new() { Id = "2", Content = "doc2", Score = 0.5 }
        };

        var result = await svc.RerankAsync("query", candidates, 2);

        result.Should().HaveCount(2);
        result[0].Id.Should().Be("1"); // 原始序不变
    }

    [Fact]
    public async Task RerankAsync_EmptyCandidates_ReturnsEmpty()
    {
        var svc = new RerankerService(EnabledConfig);
        var result = await svc.RerankAsync("query", new List<RetrievedChunk>(), 5);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task RerankAsync_LessThanTopK_ReturnsAll()
    {
        var svc = new RerankerService(EnabledConfig);
        var candidates = new List<RetrievedChunk>
        {
            new() { Id = "1", Content = "doc", Score = 0.8 }
        };

        var result = await svc.RerankAsync("query", candidates, 5);

        result.Should().HaveCount(1); // 只有 1 条, 不足 topK=5
    }

    [Fact]
    public async Task RerankAsync_TopK_TruncatesResult()
    {
        var svc = new RerankerService(EnabledConfig);
        var candidates = Enumerable.Range(1, 20).Select(i =>
            new RetrievedChunk { Id = i.ToString(), Content = $"doc {i}", Score = 1.0 / i }
        ).ToList();

        // remote unavailable → falls back to LocalHeuristicRerank
        var result = await svc.RerankAsync("keyword", candidates, 3);

        result.Should().HaveCount(3); // topK=3
    }

    // ── 综合分公式验证 ──

    [Fact]
    public void LocalHeuristicRerank_ScoreFormula_WeightedCorrectly()
    {
        var svc = new RerankerService(EnabledConfig);
        var candidates = new List<RetrievedChunk>
        {
            // 无匹配关键词 → bonus=0 → heuristicScore = 0.8*0.3 + 0*0.1 = 0.24
            new() { Id = "a", Content = "无关内容", Score = 0.8 },
            // 匹配 "标准" 1次 (pos<100 → 1.5x) → bonus=1.5 → heuristicScore = 0.5*0.3 + 1.5*0.1 = 0.15+0.15=0.30
            new() { Id = "b", Content = "标准文件说明", Score = 0.5 }
        };

        var result = CallLocalHeuristicRerank(svc, "标准", candidates, 2);

        // b Should rank higher despite lower original score
        result[0].Id.Should().Be("b");
    }

    [Fact]
    public void LocalHeuristicRerank_MultipleKeywords_AccumulateBonus()
    {
        var svc = new RerankerService(EnabledConfig);
        var candidates = new List<RetrievedChunk>
        {
            // 匹配 "化工" 1次(位置<100=1.5) + "安全" 1次(位置<100=1.5) = 3.0
            // heuristicScore = 0.5*0.3 + 3.0*0.1 = 0.15+0.30=0.45
            new() { Id = "multi", Content = "化工安全规范", Score = 0.5 },
            // 匹配 "化工" 1次 = 1.5
            // heuristicScore = 0.8*0.3 + 1.5*0.1 = 0.24+0.15=0.39
            new() { Id = "single", Content = "化工标准", Score = 0.8 }
        };

        var result = CallLocalHeuristicRerank(svc, "化工 安全", candidates, 2);

        result[0].Id.Should().Be("multi");
        result[0].RetrievalMethod.Should().Contain("+LocalRerank");
    }

    [Fact]
    public void LocalHeuristicRerank_RankUpdated_AfterSort()
    {
        var svc = new RerankerService(EnabledConfig);
        var candidates = Enumerable.Range(1, 5).Select(i =>
            new RetrievedChunk { Id = i.ToString(), Content = $"GB 15603 doc{i}", Score = 0.5 }
        ).ToList();

        var result = CallLocalHeuristicRerank(svc, "GB 15603", candidates, 3);

        result.Should().HaveCount(3);
        for (int i = 0; i < result.Count; i++)
            result[i].Rank.Should().Be(i);
    }

    // ── IsEnabled 属性 ──

    [Fact]
    public void IsEnabled_WhenConfigDisabled_ReturnsFalse()
    {
        var svc = new RerankerService(DisabledConfig);
        svc.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void IsEnabled_WhenConfigEnabled_ReturnsTrue()
    {
        var svc = new RerankerService(EnabledConfig);
        svc.IsEnabled.Should().BeTrue();
    }

    // ── Helper: 通过反射直接调用 private LocalHeuristicRerank ──
    // RerankAsync 在 candidates.Count <= topK 时短路返回，不触发重排序。
    // 反射直调确保纯逻辑测试不受远程调用和短路影响。

    private static List<RetrievedChunk> CallLocalHeuristicRerank(
        RerankerService svc, string query, List<RetrievedChunk> candidates, int topK)
    {
        var method = typeof(RerankerService).GetMethod(
            "LocalHeuristicRerank",
            BindingFlags.NonPublic | BindingFlags.Instance);

        if (method == null)
            throw new InvalidOperationException("未找到 LocalHeuristicRerank 方法");

        return (List<RetrievedChunk>)method.Invoke(svc, new object[] { query, candidates, topK })!;
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var svc = new RerankerService(EnabledConfig);
        var act = () => svc.Dispose();
        act.Should().NotThrow();
    }
}
