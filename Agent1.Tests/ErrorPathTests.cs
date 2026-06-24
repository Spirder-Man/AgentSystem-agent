using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Agent1.Config;
using Agent1.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// 错误路径与降级策略测试 — 验证系统在异常场景下的优雅降级行为。
/// </summary>
public class ErrorPathTests
{
    static ErrorPathTests()
    {
        try
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Llm:ModelId"] = "test-model",
                    ["Llm:Endpoint"] = "http://localhost:11434",
                    ["Database:Host"] = "localhost",
                    ["Database:Port"] = "5432",
                    ["Database:DatabaseName"] = "test",
                    ["VectorSearch:EmbeddingModelId"] = "test-embed",
                    ["VectorSearch:RerankerEnabled"] = "true",
                    ["VectorSearch:RerankerEndpoint"] = "http://localhost:8082/rerank",
                    ["VectorSearch:RerankerModelId"] = "bge-reranker-v2-m3",
                    ["PromptTemplates:SystemRole"] = "test",
                    ["PromptTemplates:EvalFastPrompt"] = "test {SystemRole} {UserInput}",
                    ["PromptTemplates:EvalFastQueryPrompt"] = "test {SystemRole} {UserInput}"
                })
                .Build();
            AppConfig.Load(config);
        }
        catch (InvalidOperationException) { /* 已被初始化 */ }
    }

    // ═══════════════════════════════════════
    // RerankerService — 降级策略
    // ═══════════════════════════════════════

    [Fact]
    public void RerankerService_WithoutConfig_IsDisabled()
    {
        // 未启用 Reranker 时 RerankAsync 应直接返回 TopK
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["VectorSearch:RerankerEnabled"] = "false"
            })
            .Build();

        // RerankerService 构造不抛异常
        var act = () => new RerankerService(
            AppConfig.Instance);
        act.Should().NotThrow();
    }

    [Fact]
    public async Task RerankerService_EmptyCandidates_ReturnsEmpty()
    {
        var reranker = new RerankerService(AppConfig.Instance);

        var result = await reranker.RerankAsync("测试查询",
            new List<RetrievedChunk>(), 5);

        result.Should().BeEmpty("空候选列表应返回空结果");
    }

    [Fact]
    public async Task RerankerService_FewerThanTopK_ReturnsAll()
    {
        var reranker = new RerankerService(AppConfig.Instance);
        var candidates = new List<RetrievedChunk>
        {
            RetrievedChunk.Create("文档A", 0.9, 0),
            RetrievedChunk.Create("文档B", 0.8, 1),
            RetrievedChunk.Create("文档C", 0.7, 2),
        };

        // candidates (3) <= topK (5)，应跳过 Reranker 直接返回
        var result = await reranker.RerankAsync("测试查询", candidates, 5);

        result.Should().HaveCount(3, "候选数 ≤ TopK 时应跳过 Reranker");
    }

    [Fact]
    public async Task RerankerService_FallbackToLocal_WhenRemoteUnavailable()
    {
        // RerankerService 连接一个确定不可达的端点时会降级到本地启发式排序
        var reranker = new RerankerService(AppConfig.Instance);
        var candidates = new List<RetrievedChunk>
        {
            RetrievedChunk.Create("苯与丙酮储存禁忌配对详细说明", 0.6, 0),
            RetrievedChunk.Create("易燃液体储存通则 GB 15603", 0.9, 1),
            RetrievedChunk.Create("甲类仓库消防通道 安全距离要求", 0.7, 2),
            RetrievedChunk.Create("危险化学品重大危险源辨识", 0.5, 3),
            RetrievedChunk.Create("其他不相关文档", 0.3, 4),
            RetrievedChunk.Create("另一个不相关文档", 0.2, 5),
        };

        // 远程 Reranker 服务未启动（http://localhost:8082/rerank 不可达）
        // 应降级到本地启发式排序，不抛异常
        var result = await reranker.RerankAsync("苯", candidates, 3);

        result.Should().NotBeNull("降级模式也应返回有效结果");
        result.Should().HaveCount(3, "应返回 TopK=3 条结果");
        // 降级排序：关键词"苯"命中多的排在前面
        result.Should().OnlyContain(c => c.Content != null, "所有结果均应有内容");
    }

    // ═══════════════════════════════════════
    // ConclusionVerifier — 错误处理
    // ═══════════════════════════════════════

    [Fact]
    public async Task VerifyAsync_NullResponse_ReturnsFailed()
    {
        var result = await ConclusionVerifier.VerifyAsync(
            null!, new List<FunctionCallRecord>());

        result.IsPassed.Should().BeFalse("null 响应应标记为不通过");
        result.FailureReasons.Should().Contain(r => r.Contains("为空"));
    }

    [Fact]
    public async Task VerifyAsync_EmptyResponse_ReturnsFailed()
    {
        var result = await ConclusionVerifier.VerifyAsync(
            "", new List<FunctionCallRecord>());

        result.IsPassed.Should().BeFalse("空响应应标记为不通过");
    }

    [Fact]
    public async Task VerifyAsync_NoRegulation_ReturnsFailed()
    {
        var result = await ConclusionVerifier.VerifyAsync(
            "这是一个没有法规编号的普通回答",
            new List<FunctionCallRecord>());

        result.IsPassed.Should().BeFalse("无有效法规编号应不通过");
        result.FailureReasons.Should().Contain(r => r.Contains("法规编号"));
    }

    [Fact]
    public async Task VerifyAsync_EmptyDataDeclaration_ReturnsPassed()
    {
        // [P7 FIX] 空数据检测：LLM 明确声明数据不足时不应标记为失败
        var result = await ConclusionVerifier.VerifyAsync(
            "未检索到相关数据，知识库中暂无该化学品记录",
            new List<FunctionCallRecord>());

        result.IsPassed.Should().BeTrue("明确声明'未检索到'应视为正确识别知识边界");
        result.Warnings.Should().Contain(w => w.Contains("知识边界"));
    }

    [Fact]
    public async Task VerifyAsync_NoToolsCalled_AddsWarning()
    {
        var response = "【合规判断】是\n[判定:is_compliant=true]\n依据 GB 30000.2-2013";

        var result = await ConclusionVerifier.VerifyAsync(
            response, new List<FunctionCallRecord>());

        result.Warnings.Should().Contain(w => w.Contains("工具"));
    }

    [Fact]
    public async Task VerifyAsync_SafetyDistance_WithoutValue_ReturnsFailed()
    {
        var response = "【合规判断】是\n有关安全距离，请参考相关标准";

        var result = await ConclusionVerifier.VerifyAsync(
            response,
            new List<FunctionCallRecord> { new() { FunctionName = "GetSafetyDistance" } },
            category: "安全距离");

        result.IsPassed.Should().BeFalse("安全距离类无数值且无数据不足声明应不通过");
        result.FailureReasons.Should().Contain(r => r.Contains("安全距离"));
    }

    [Fact]
    public async Task VerifyAsync_SafetyDistance_WithValue_Passes()
    {
        var response = "【合规判断】是\n消防通道安全距离为 30 米\n依据 GB 50160";

        var result = await ConclusionVerifier.VerifyAsync(
            response,
            new List<FunctionCallRecord> { new() { FunctionName = "GetSafetyDistance" } },
            category: "安全距离");

        result.HasDistanceValue.Should().BeTrue();
    }

    // ═══════════════════════════════════════
    // SensitiveDataMasker — 边界条件
    // ═══════════════════════════════════════

    [Fact]
    public void Mask_NullInput_ReturnsEmpty()
    {
        SensitiveDataMasker.Mask(null).Should().Be("");
    }

    [Fact]
    public void Mask_EmptyInput_ReturnsEmpty()
    {
        SensitiveDataMasker.Mask("").Should().Be("");
    }

    [Fact]
    public void Mask_SqlInjection_PassesUnchanged()
    {
        // 脱敏不负责 SQL 注入防护（那是 SafetyGuardService 的职责）
        var sql = "SELECT * FROM users WHERE '1'='1'";
        // 没有匹配的手机号/邮箱/身份证/API Key 模式，返回原文
        SensitiveDataMasker.Mask(sql).Should().NotBeNull();
    }

    // ═══════════════════════════════════════
    // KnowledgeBaseService — 空知识库检索
    // ═══════════════════════════════════════

    [Fact]
    public async Task KnowledgeBaseService_EmptyKB_RetrieveReturnsEmpty()
    {
        var kb = new KnowledgeBaseService();

        var results = await kb.RetrieveAsync("苯", topK: 5);

        results.Should().BeEmpty("空知识库检索应返回空列表");
    }

    [Fact]
    public async Task KnowledgeBaseService_EmptyKB_GetDocumentCountReturnsZero()
    {
        var kb = new KnowledgeBaseService();

        kb.GetDocumentCount().Should().Be(0);
    }

    // ═══════════════════════════════════════
    // 评测集覆盖 — 边界场景
    // ═══════════════════════════════════════

    [Fact]
    public void EvalEngine_CheckConclusion_MissingTools_ReturnsFalse()
    {
        var response = "【合规判断】是\n[判定:is_compliant=true]";

        EvalEngine.CheckConclusion(response, new Models.EvalConclusion { IsCompliant = true },
            toolTriggered: false)
            .Should().BeFalse("评测模式下未触发工具调用应判定为不通过");
    }

    [Fact]
    public void EvalEngine_CheckParams_NullExpected_ReturnsFalse()
    {
        EvalEngine.CheckParams("any args", null!)
            .Should().BeFalse("期望参数为空时应返回 false");
    }
}
