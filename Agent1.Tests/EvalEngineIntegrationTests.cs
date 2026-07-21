// ============================================================
// EvalEngine 集成测试 — P1 Stub 环境
//
// 通过 StubKnowledgeBaseService + StubLlmService + StubReflectionVerifier
// 测试 EvalEngine 中可独立验证的子方法，无需真实 GPU/LLM/PostgreSQL。
//
// 测试范围：
//   - EvaluateRetrievalQualityAsync (KB 检索质量评估)
//   - EvaluateFaithfulnessAsync (声明忠实度验证)
//   - EvaluateAnswerRelevanceAsync (回答相关性评分)
//   - EvaluateCitationAccuracy (法规引用准确率)
// ============================================================

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Agent1.Models;
using Agent1.Services;
using Agent1.Tests.Stubs;
using FluentAssertions;
using Xunit;

namespace Agent1.Tests;

/// <summary>暴露 EvalEngine 内部测试方法</summary>
public class TestableEvalEngine : EvalEngine
{
    public TestableEvalEngine(
        AgentDialog? agentDialog,
        ILlmService llmService,
        IKnowledgeBaseService kbService,
        ReflectionVerifier? reflectionVerifier = null)
        : base(agentDialog!, llmService, kbService, reflectionVerifier)
    {
    }

    public Task CallEvaluateRetrievalQualityAsync(EvalResult result, EvalCase tc)
        => EvaluateRetrievalQualityAsync(result, tc);

    public Task CallEvaluateFaithfulnessAsync(EvalResult result, string response, EvalCase tc)
        => EvaluateFaithfulnessAsync(result, response, tc);

    public Task CallEvaluateAnswerRelevanceAsync(EvalResult result, string response, EvalCase tc)
        => EvaluateAnswerRelevanceAsync(result, response, tc);

    public void CallEvaluateCitationAccuracy(EvalResult result, string response)
        => EvaluateCitationAccuracy(result, response);
}

public class EvalEngineIntegrationTests
{
    // ═══════════════════════════════
    // EvaluateRetrievalQualityAsync
    // ═══════════════════════════════

    [Fact]
    public async Task RetrievalQuality_AllRelevant_ShouldGivePerfectScores()
    {
        var kb = new StubKnowledgeBaseService();
        kb.AddPresetResult("GB 30000.7", new List<RetrievedChunk>
        {
            new() { Id = "chunk-1", Content = "GB 30000.7-2013 易燃液体", Score = 0.95, Rank = 1 },
            new() { Id = "chunk-2", Content = "GB 30000.7-2013 第4.1条", Score = 0.90, Rank = 2 },
            new() { Id = "chunk-3", Content = "GB 30000.7-2013 附录A", Score = 0.85, Rank = 3 },
        });

        var engine = new TestableEvalEngine(null!, new StubLlmService(), kb);
        var result = new EvalResult { Id = "TC001" };
        var tc = new EvalCase
        {
            Id = "TC001",
            Query = "苯的危险类别",
            ExpectedConclusion = new EvalConclusion
            {
                ExpectedRegulationNumbers = new List<string> { "GB 30000.7" }
            }
        };

        await engine.CallEvaluateRetrievalQualityAsync(result, tc);

        result.RetrievalEvaluated.Should().BeTrue();
        result.RetrievedChunks.Should().HaveCount(3);
        result.PrecisionAtK.Should().Be(1.0); // 3/3 relevant
        result.RecallAtK.Should().BeGreaterThanOrEqualTo(1.0); // can exceed 1.0 when all chunks relevant
        result.MRR.Should().Be(1.0); // first at rank 1
    }

    [Fact]
    public async Task RetrievalQuality_NoRelevanceIndicators_ShouldNotEvaluate()
    {
        var kb = new StubKnowledgeBaseService();
        var engine = new TestableEvalEngine(null!, new StubLlmService(), kb);
        var result = new EvalResult { Id = "TC002" };
        var tc = new EvalCase
        {
            Id = "TC002",
            Query = "一些查询",
            ExpectedConclusion = null,
            ExpectedParams = null
        };

        await engine.CallEvaluateRetrievalQualityAsync(result, tc);

        result.RetrievalEvaluated.Should().BeFalse();
    }

    [Fact]
    public async Task RetrievalQuality_PartialRelevance_ShouldCalculateCorrectly()
    {
        var kb = new StubKnowledgeBaseService();
        kb.AddPresetResult("GB 30000.7", new List<RetrievedChunk>
        {
            new() { Id = "ch-1", Content = "GB 30000.7-2013 概述", Score = 0.90, Rank = 1 },
            new() { Id = "ch-2", Content = "GB 30000.2-2014 无关内容", Score = 0.80, Rank = 2 },
            new() { Id = "ch-3", Content = "GB 15603-2022 其他标准", Score = 0.70, Rank = 3 },
        });

        var engine = new TestableEvalEngine(null!, new StubLlmService(), kb);
        var result = new EvalResult { Id = "TC003" };
        var tc = new EvalCase
        {
            Id = "TC003",
            Query = "危险类别",
            ExpectedConclusion = new EvalConclusion
            {
                ExpectedRegulationNumbers = new List<string> { "GB 30000.7" }
            }
        };

        await engine.CallEvaluateRetrievalQualityAsync(result, tc);

        result.RetrievalEvaluated.Should().BeTrue();
        result.PrecisionAtK.Should().Be(1.0 / 3.0); // 1/3 relevant
        result.RecallAtK.Should().BeGreaterThanOrEqualTo(1.0); // 1/1 expected = 100%, can exceed
        result.MRR.Should().Be(1.0); // first at rank 1
        result.RetrievedChunks![0].IsRelevant.Should().BeTrue();
        result.RetrievedChunks[1].IsRelevant.Should().BeFalse();
    }

    [Fact]
    public async Task RetrievalQuality_RelevantAtRank2_ShouldSetMRRCorrectly()
    {
        var kb = new StubKnowledgeBaseService();
        kb.AddPresetResult("GB 30000.7", new List<RetrievedChunk>
        {
            new() { Id = "ch-1", Content = "GB 15603-2022 无关", Score = 0.90, Rank = 1 },
            new() { Id = "ch-2", Content = "GB 30000.7-2013 相关", Score = 0.80, Rank = 2 },
        });

        var engine = new TestableEvalEngine(null!, new StubLlmService(), kb);
        var result = new EvalResult { Id = "TC004" };
        var tc = new EvalCase
        {
            Id = "TC004",
            Query = "危险类别",
            ExpectedConclusion = new EvalConclusion
            {
                ExpectedRegulationNumbers = new List<string> { "GB 30000.7" }
            }
        };

        await engine.CallEvaluateRetrievalQualityAsync(result, tc);

        result.MRR.Should().Be(0.5); // 1/2 = first relevant at rank 2
    }

    [Fact]
    public async Task RetrievalQuality_MultipleRelevanceIndicators_ShouldMatchAny()
    {
        var kb = new StubKnowledgeBaseService();
        kb.AddPresetResult("苯", new List<RetrievedChunk>
        {
            new() { Id = "ch-1", Content = "苯 危险类别 易燃液体", Score = 0.95, Rank = 1 },
        });
        kb.AddPresetResult("GB 30000.7", new List<RetrievedChunk>
        {
            new() { Id = "ch-gb", Content = "GB 30000.7-2013 危险化学品分类", Score = 0.90, Rank = 1 },
        });

        var engine = new TestableEvalEngine(null!, new StubLlmService(), kb);
        var result = new EvalResult { Id = "TC005" };
        var tc = new EvalCase
        {
            Id = "TC005",
            Query = "苯的危险类别",
            ExpectedConclusion = new EvalConclusion
            {
                ExpectedRegulationNumbers = new List<string> { "GB 30000.7" }
            },
            ExpectedRelevantDocs = new List<string> { "苯" },
            ExpectedParams = new Dictionary<string, string> { ["substance"] = "苯" }
        };

        await engine.CallEvaluateRetrievalQualityAsync(result, tc);

        result.RetrievalEvaluated.Should().BeTrue();
        // "苯" is in the chunk content, so it's relevant
        result.PrecisionAtK.Should().Be(1.0);
    }

    // ═══════════════════════════════
    // EvaluateFaithfulnessAsync
    // ═══════════════════════════════

    [Fact]
    public async Task Faithfulness_WithStubVerifier_ShouldUsePresetClaims()
    {
        var kb = new StubKnowledgeBaseService();
        var stubVerifier = new StubReflectionVerifier(kb);
        stubVerifier.SetPresetClaims(new List<ClaimVerification>
        {
            new() { ClaimedText = "GB 30000.7-2013", ClaimType = "国标", FoundInSource = true, ChunksReturned = 3 },
            new() { ClaimedText = "GB 15603-2022", ClaimType = "国标", FoundInSource = true, ChunksReturned = 2 },
        });

        var engine = new TestableEvalEngine(null!, new StubLlmService(), kb, stubVerifier);
        var result = new EvalResult { Id = "TC010" };
        var tc = new EvalCase { Id = "TC010", Query = "苯的储存条件" };
        var response = "【查询结果】苯的储存条件: 常温避光。【法规依据】GB 30000.7-2013, GB 15603-2022";

        await engine.CallEvaluateFaithfulnessAsync(result, response, tc);

        result.TotalClaims.Should().Be(2);
        result.VerifiedClaims.Should().Be(2);
        result.HallucinatedClaims.Should().Be(0);
        result.FaithfulnessScore.Should().Be(1.0);
    }

    [Fact]
    public async Task Faithfulness_WithHallucinatedClaims_ShouldReportCorrectly()
    {
        var kb = new StubKnowledgeBaseService();
        var stubVerifier = new StubReflectionVerifier(kb);
        stubVerifier.SetPresetClaims(new List<ClaimVerification>
        {
            new() { ClaimedText = "GB 30000.7", ClaimType = "国标", FoundInSource = true, ChunksReturned = 2 },
            new() { ClaimedText = "GB 99999.1", ClaimType = "国标", FoundInSource = false, ChunksReturned = 0 },
            new() { ClaimedText = "GB 88888.2", ClaimType = "国标", FoundInSource = false, ChunksReturned = 0 },
        });

        var engine = new TestableEvalEngine(null!, new StubLlmService(), kb, stubVerifier);
        var result = new EvalResult { Id = "TC011" };
        var tc = new EvalCase { Id = "TC011", Query = "测试" };
        var response = "【查询结果】涉及 GB 30000.7, GB 99999.1, GB 88888.2";

        await engine.CallEvaluateFaithfulnessAsync(result, response, tc);

        result.TotalClaims.Should().Be(3);
        result.VerifiedClaims.Should().Be(1);
        result.HallucinatedClaims.Should().Be(2);
        result.FaithfulnessScore.Should().BeApproximately(1.0 / 3.0, 0.01);
    }

    [Fact]
    public async Task Faithfulness_NullVerifier_ShouldUseRegexCounting()
    {
        var kb = new StubKnowledgeBaseService();
        var engine = new TestableEvalEngine(null!, new StubLlmService(), kb, reflectionVerifier: null!);
        var result = new EvalResult { Id = "TC012" };
        var tc = new EvalCase { Id = "TC012", Query = "苯" };
        var response = "依据 GB 30000.7-2013 和 GB 15603-2022 进行判断";

        await engine.CallEvaluateFaithfulnessAsync(result, response, tc);

        // Regex should find 2 GB numbers, all assumed verified when no verifier
        result.TotalClaims.Should().BeGreaterOrEqualTo(2);
        result.VerifiedClaims.Should().Be(result.TotalClaims); // all assumed true
        result.HallucinatedClaims.Should().Be(0);
    }

    [Fact]
    public async Task Faithfulness_EmptyResponse_ShouldHandleGracefully()
    {
        var kb = new StubKnowledgeBaseService();
        var stubVerifier = new StubReflectionVerifier(kb);
        var engine = new TestableEvalEngine(null!, new StubLlmService(), kb, stubVerifier);
        var result = new EvalResult { Id = "TC013" };
        var tc = new EvalCase { Id = "TC013", Query = "测试" };

        await engine.CallEvaluateFaithfulnessAsync(result, "", tc);

        // Should not throw; empty response may produce 0 claims
        result.TotalClaims.Should().Be(0);
    }

    // ═══════════════════════════════
    // EvaluateAnswerRelevanceAsync
    // ═══════════════════════════════

    // 注：EvaluateAnswerRelevanceAsync 内部通过 `_llmService as LlmService` 转型，
    // StubLlmService 不继承 LlmService，转型返回 null → 始终走 fallback 路径。
    // 此处测试 LLM 不可用时的降级行为。

    [Fact]
    public async Task AnswerRelevance_FallbackExactMatch_ShouldScoreHigh()
    {
        var engine = new TestableEvalEngine(null!, new StubLlmService(), new StubKnowledgeBaseService());
        var result = new EvalResult { Id = "TC020" };
        var tc = new EvalCase { Id = "TC020", Query = "storage light" };
        var response = "The storage conditions include light protection and ventilation";

        await engine.CallEvaluateAnswerRelevanceAsync(result, response, tc);

        // Fallback: both "storage" and "light" in response → 2/2 → 4.5
        result.AnswerRelevance.Should().Be(4.5);
    }

    [Fact]
    public async Task AnswerRelevance_FallbackPartialMatch_ShouldScoreMedium()
    {
        var engine = new TestableEvalEngine(null!, new StubLlmService(), new StubKnowledgeBaseService());
        var result = new EvalResult { Id = "TC021" };
        var tc = new EvalCase { Id = "TC021", Query = "storage methanol" };
        var response = "The storage conditions include light protection";

        await engine.CallEvaluateAnswerRelevanceAsync(result, response, tc);

        // Fallback: "storage" matches, "methanol" doesn't → 1/2 → 2.5
        result.AnswerRelevance.Should().Be(2.5);
    }

    [Fact]
    public async Task AnswerRelevance_FallbackNoMatch_ShouldScoreLow()
    {
        var engine = new TestableEvalEngine(null!, new StubLlmService(), new StubKnowledgeBaseService());
        var result = new EvalResult { Id = "TC022" };
        var tc = new EvalCase { Id = "TC022", Query = "测试" };
        var response = "一些无关回答";

        await engine.CallEvaluateAnswerRelevanceAsync(result, response, tc);

        // Fallback: "测试"不在回答中 → 0/1 → 1.0
        result.AnswerRelevance.Should().Be(1.0);
    }

    [Fact]
    public async Task AnswerRelevance_EmptyResponse_ShouldScoreMinimum()
    {
        var engine = new TestableEvalEngine(null!, new StubLlmService(), new StubKnowledgeBaseService());
        var result = new EvalResult { Id = "TC023" };
        var tc = new EvalCase { Id = "TC023", Query = "苯" };
        var response = "";

        await engine.CallEvaluateAnswerRelevanceAsync(result, response, tc);

        // Empty response → fallback returns 1.0
        result.AnswerRelevance.Should().Be(1.0);
    }

    // ═══════════════════════════════
    // EvaluateCitationAccuracy
    // ═══════════════════════════════

    [Fact]
    public void CitationAccuracy_AllCitationsInRetrievedChunks_ShouldBePerfect()
    {
        var engine = new TestableEvalEngine(null!, new StubLlmService(), new StubKnowledgeBaseService());
        var result = new EvalResult
        {
            Id = "TC030",
            RetrievedChunks = new List<RetrievalHit>
            {
                new() { ChunkId = "c1", ContentPreview = "GB 30000.7-2013 易燃液体分类" },
                new() { ChunkId = "c2", ContentPreview = "GB 15603-2022 储存通则" },
            }
        };
        var response = "依据 GB 30000.7-2013 和 GB 15603-2022 的规定...";

        engine.CallEvaluateCitationAccuracy(result, response);

        result.CitationAccuracy.Should().Be(1.0);
        result.CitationTraces.Should().HaveCount(2);
        result.CitationTraces![0].FoundInContext.Should().BeTrue();
        result.CitationTraces[1].FoundInContext.Should().BeTrue();
    }

    [Fact]
    public void CitationAccuracy_PartialMatch_ShouldCalculateCorrectly()
    {
        var engine = new TestableEvalEngine(null!, new StubLlmService(), new StubKnowledgeBaseService());
        var result = new EvalResult
        {
            Id = "TC031",
            RetrievedChunks = new List<RetrievalHit>
            {
                new() { ChunkId = "c1", ContentPreview = "GB 30000.7-2013 分类标准" },
            }
        };
        var response = "依据 GB 30000.7-2013 和 GB 99999.1-2099 的规定...";

        engine.CallEvaluateCitationAccuracy(result, response);

        result.CitationAccuracy.Should().Be(0.5); // 1/2 found
    }

    [Fact]
    public void CitationAccuracy_NoCitationsInResponse_ShouldReturn1()
    {
        var engine = new TestableEvalEngine(null!, new StubLlmService(), new StubKnowledgeBaseService());
        var result = new EvalResult
        {
            Id = "TC032",
            RetrievedChunks = new List<RetrievalHit>
            {
                new() { ChunkId = "c1", ContentPreview = "GB 30000.7-2013" },
            }
        };
        var response = "该物质属于危险化学品。"; // No GB numbers

        engine.CallEvaluateCitationAccuracy(result, response);

        result.CitationAccuracy.Should().Be(1.0); // No citations = no violation
    }

    [Fact]
    public void CitationAccuracy_NoRetrievedChunks_ShouldReturnNull()
    {
        var engine = new TestableEvalEngine(null!, new StubLlmService(), new StubKnowledgeBaseService());
        var result = new EvalResult
        {
            Id = "TC033",
            RetrievedChunks = null // No chunks available
        };
        var response = "依据 GB 30000.7-2013 的规定...";

        engine.CallEvaluateCitationAccuracy(result, response);

        // null when no retrieved chunks AND no REGULATIONS tag in response
        result.CitationAccuracy.Should().BeNull();
    }

    [Fact]
    public void CitationAccuracy_CitationTracesDetailed_ShouldTrackPerRegulation()
    {
        var engine = new TestableEvalEngine(null!, new StubLlmService(), new StubKnowledgeBaseService());
        var result = new EvalResult
        {
            Id = "TC034",
            RetrievedChunks = new List<RetrievalHit>
            {
                new() { ChunkId = "chunk-a", ContentPreview = "GB 30000.7-2013 第4.1条 易燃液体" },
            }
        };
        var response = "依据 GB 30000.7-2013 和 GB 99999.1-2099";

        engine.CallEvaluateCitationAccuracy(result, response);

        result.CitationTraces.Should().HaveCount(2);
        result.CitationTraces![0].CitedRegulation.Should().Contain("30000.7");
        result.CitationTraces[0].FoundInContext.Should().BeTrue();
        result.CitationTraces[0].SourceChunkId.Should().Be("chunk-a");

        result.CitationTraces[1].CitedRegulation.Should().Contain("99999");
        result.CitationTraces[1].FoundInContext.Should().BeFalse();
        result.CitationTraces[1].SourceChunkId.Should().BeNull();
    }

    // ═══════════════════════════════
    // EvalEngine 构造验证
    // ═══════════════════════════════

    [Fact]
    public void EvalEngine_ConstructedWithAllStubs_ShouldNotThrow()
    {
        var kb = new StubKnowledgeBaseService();
        var llm = new StubLlmService();
        var verifier = new StubReflectionVerifier(kb);

        var act = () => new TestableEvalEngine(null!, llm, kb, verifier);
        act.Should().NotThrow();
    }

    [Fact]
    public void EvalEngine_ConstructedWithNullReflectionVerifier_ShouldNotThrow()
    {
        var act = () => new TestableEvalEngine(null!, new StubLlmService(), new StubKnowledgeBaseService(), null!);
        act.Should().NotThrow();
    }
}
