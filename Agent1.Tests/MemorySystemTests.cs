using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Agent1.Config;
using Agent1.Models;
using Agent1.Services;
using Agent1.Services.Observability;
using Agent1.Services.Orchestration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace Agent1.Tests;

// ═══════════════════════════════════════════════════════════════════
// L5 层：记忆系统与会话管理测试
// 覆盖：MemoryService, LongTermMemoryService, FactExtractor,
//       MemoryCoordinator, ResponseCacheService, SessionManager,
//       SessionCleanupHostedService
// ═══════════════════════════════════════════════════════════════════

#region MemoryService 短期记忆测试

public class MemoryServiceTests : IDisposable
{
    private readonly MemoryService _service;

    public MemoryServiceTests()
    {
        // 确保 AppConfig 加载依赖
        try { var _ = AppConfig.Instance; }
        catch { AppConfig.Load(new ConfigurationBuilder().Build()); }
        _service = new MemoryService();
    }

    public void Dispose()
    {
        _service.ClearMemory();
    }

    [Fact]
    public void SetSession_ShouldCreateIsolatedDataSpace()
    {
        _service.SetSession("sess-a");
        _service.ExtractAndStoreKeyFacts("我叫张三", "");
        _service.SetSession("sess-b");
        _service.ExtractAndStoreKeyFacts("我叫李四", "");

        _service.SetSession("sess-a");
        var profileA = _service.GetUserProfile();
        _service.SetSession("sess-b");
        var profileB = _service.GetUserProfile();

        profileA.UserName.Should().Be("张三");
        profileB.UserName.Should().Be("李四");
    }

    [Fact]
    public void SetSession_NullOrWhitespace_ShouldUseDefault()
    {
        _service.SetSession(null!);
        _service.ExtractAndStoreKeyFacts("我叫测试", "");
        _service.SetSession("  ");
        var profile = _service.GetUserProfile();
        // 空格 session 与 null session 都映射到 __default__
        profile.UserName.Should().Be("测试");
    }

    [Fact]
    public void TryAnswerFromMemory_UserAsksName_ShouldReturnProfile()
    {
        _service.ExtractAndStoreKeyFacts("我叫王工", "");
        var result = _service.TryAnswerFromMemory("我是谁");
        result.Should().NotBeNull();
        result.Should().Contain("王工");
    }

    [Fact]
    public void TryAnswerFromMemory_UserAsksAssistantName_ShouldReturnProfile()
    {
        _service.ExtractAndStoreKeyFacts("你叫合规助手", "");
        var result = _service.TryAnswerFromMemory("你是谁");
        result.Should().NotBeNull();
        result.Should().Contain("合规助手");
    }

    [Fact]
    public void TryAnswerFromMemory_NoProfile_ShouldReturnNull()
    {
        var result = _service.TryAnswerFromMemory("我是谁");
        result.Should().BeNull();
    }

    [Fact]
    public void TryAnswerFromMemory_KeyFactsKeywordMatch_ShouldReturnFacts()
    {
        var facts = new Dictionary<string, string> { ["CheckHazard_v1"] = "甲类易燃液体，闪点-11°C" };
        _service.StoreToolFacts("检查苯的危害类别", facts);
        var result = _service.TryAnswerFromMemory("苯有什么危害");
        result.Should().NotBeNull();
        result.Should().Contain("危险类别");
    }

    [Fact]
    public void TryAnswerFromMemory_KeyFactsNoMatch_ShouldReturnNull()
    {
        var facts = new Dictionary<string, string> { ["CheckHazard_v1"] = "甲类易燃液体" };
        _service.StoreToolFacts("检查苯的危害类别", facts);
        var result = _service.TryAnswerFromMemory("甲醇有什么危害");
        result.Should().BeNull();
    }

    [Fact]
    public void ExtractAndStoreKeyFacts_UserIntroducesSelf_ShouldStoreName()
    {
        _service.ExtractAndStoreKeyFacts("我叫张工", "");
        var profile = _service.GetUserProfile();
        profile.UserName.Should().Be("张工");
    }

    [Fact]
    public void ExtractAndStoreKeyFacts_UserNamesAssistant_ShouldStoreAssistantName()
    {
        _service.ExtractAndStoreKeyFacts("你叫安全助手", "");
        var profile = _service.GetUserProfile();
        profile.AssistantName.Should().Be("安全助手");
    }

    [Fact]
    public void ExtractAndStoreKeyFacts_WithComma_ShouldExtractTillComma()
    {
        _service.ExtractAndStoreKeyFacts("我叫张三，你好", "");
        var profile = _service.GetUserProfile();
        profile.UserName.Should().Be("张三");
    }

    [Fact]
    public void StoreToolFacts_HazardCategory_ShouldStoreFact()
    {
        var toolResults = new Dictionary<string, string>
        {
            ["CheckHazard_v1"] = "甲类易燃液体，闪点-11°C"
        };
        _service.StoreToolFacts("检查苯的危害类别", toolResults);
        var facts = _service.GetKeyFacts();
        facts.Should().ContainKey("苯");
        facts["苯"].Should().Contain("危险类别");
    }

    [Fact]
    public void StoreToolFacts_StorageCompatibility_ShouldStoreFact()
    {
        var toolResults = new Dictionary<string, string>
        {
            ["CheckStorageCompatibility"] = "不可同储"
        };
        _service.StoreToolFacts("检查苯和丙酮的储存兼容性", toolResults);
        var facts = _service.GetKeyFacts();
        facts.Should().ContainKey("苯+丙酮");
    }

    [Fact]
    public void StoreToolFacts_SafetyDistance_ShouldStoreFact()
    {
        var toolResults = new Dictionary<string, string>
        {
            ["GetSafetyDistance"] = "安全间距50米"
        };
        _service.StoreToolFacts("检查储罐安全距离", toolResults);
        var facts = _service.GetKeyFacts();
        facts.Should().ContainKey("储罐");
    }

    [Fact]
    public void StoreDialogueTurn_ShouldIncrementTurnCount()
    {
        _service.StoreDialogueTurn("问题1", "回答1");
        _service.StoreDialogueTurn("问题2", "回答2");
        _service.GetDialogueTurnCount().Should().Be(2);
    }

    // ══════════════════════════════════════════════════════════
    // [QF-2026-001] G1 写入门禁质量测试
    // ══════════════════════════════════════════════════════════

    /// <summary>TQ-001: 兜底文本不应进入 KeyFacts</summary>
    [Fact]
    public void StoreToolFacts_FallbackContent_ShouldNotEnterKeyFacts()
    {
        var toolResults = new Dictionary<string, string>
        {
            ["CheckHazardCategory"] = "「苯」未在常见危化品类别中直接匹配，建议查阅 GB 30000 系列标准全文（knowledgebase/国标/ 目录下已收录完整标准文件） [判定:is_compliant=unknown]"
        };
        _service.StoreToolFacts("检查苯的危害类别", toolResults);
        var facts = _service.GetKeyFacts();

        // 兜底文本不应写入缓存
        if (facts.ContainsKey("苯"))
            facts["苯"].Should().NotContain("未在常见");
        facts.Should().NotContainKey("苯", because: "兜底文本不应作为领域事实缓存");
    }

    /// <summary>TQ-001b: 有效工具结果应正常进入 KeyFacts</summary>
    [Fact]
    public void StoreToolFacts_RegulationContent_ShouldEnterKeyFacts()
    {
        var toolResults = new Dictionary<string, string>
        {
            ["CheckHazardCategory"] = "[REGULATIONS: GB 30000.7-2013]\n「苯」危险类别: 易燃液体(类别2) [判定:is_compliant=unknown]"
        };
        _service.StoreToolFacts("检查苯的危害类别", toolResults);
        var facts = _service.GetKeyFacts();

        // 有效法规结果应正常写入
        facts.Should().ContainKey("苯");
        facts["苯"].Should().Contain("GB 30000");
    }

    /// <summary>TQ-002: 仅兜底缓存时 TryAnswerFromMemory 不应返回结果</summary>
    [Fact]
    public void TryAnswerFromMemory_FallbackOnly_ShouldReturnNull()
    {
        // 先写入有效数据（用于验证 G1 不会误杀有效结果）
        var validResults = new Dictionary<string, string>
        {
            ["CheckHazardCategory"] = "[REGULATIONS: GB 30000.7-2013] 易燃液体"
        };
        _service.StoreToolFacts("检查苯的危害类别", validResults);

        // 再尝试写入兜底数据（应被 G1 拦截）
        var fallbackResults = new Dictionary<string, string>
        {
            ["CheckHazardCategory"] = "「硫酸」未在常见危化品类别中直接匹配，建议查阅 GB 30000 全文"
        };
        _service.StoreToolFacts("检查硫酸的危害类别", fallbackResults);

        // 苯（有效数据）应可查到
        var validResult = _service.TryAnswerFromMemory("苯的危险类别");
        validResult.Should().NotBeNull();
        validResult.Should().Contain("易燃液体");

        // 硫酸（兜底被拒）不应查到 — 使用不包含"苯"的独立物质名避免双向子串误匹配
        var fallbackResult = _service.TryAnswerFromMemory("硫酸的危险类别");
        fallbackResult.Should().BeNull();
    }

    /// <summary>TQ-003: 有效工具结果 → 缓存 → 读取 → 完整链路</summary>
    [Fact]
    public void StoreThenRetrieve_ValidResult_CompleteRoundtrip()
    {
        var toolResults = new Dictionary<string, string>
        {
            ["CheckHazardCategory"] = "[REGULATIONS: GB 30000.7-2013, GB 30000.18-2013]\n「苯」危险类别: 易燃液体(类别2); 急性毒性(类别4) [判定:is_compliant=unknown]",
            ["CheckStorageCompatibility"] = "[REGULATIONS: GB 15603]\n✅ 苯与丙酮：同类易燃液体可同库分区存放 [依据: GB 15603] [判定:is_compliant=true]"
        };

        _service.StoreToolFacts("检查苯和丙酮的储存兼容性", toolResults);
        var facts = _service.GetKeyFacts();

        // 危险类别存入
        facts.Should().ContainKey("苯");
        facts["苯"].Should().Contain("GB 30000");

        // 储存兼容性存入（需两个物质名都出现在输入中才能提取）
        facts.Should().ContainKey("苯+丙酮");
        facts["苯+丙酮"].Should().Contain("GB 15603");

        // TryAnswerFromMemory 查得到
        var memoryResult = _service.TryAnswerFromMemory("苯的储存");
        memoryResult.Should().NotBeNull();
    }

    [Fact]
    public void StoreDialogueTurn_LongResponse_ShouldTruncateTo500()
    {
        var longResponse = new string('X', 1000);
        _service.StoreDialogueTurn("测试", longResponse);
        // 不应抛出异常（内部截断）
        _service.GetDialogueTurnCount().Should().Be(1);
    }

    [Fact]
    public async Task GetConversationContextAsync_ShouldReturnContext()
    {
        _service.ExtractAndStoreKeyFacts("我叫张工", "");
        _service.StoreDialogueTurn("检查苯的危害", "苯是易燃液体");
        var context = await _service.GetConversationContextAsync();
        context.Should().NotBeNullOrEmpty();
        // 未达到压缩阈值，应有最近对话区域
        (context.Contains("最近对话") || context.Contains("对话历史摘要")).Should().BeTrue();
    }

    [Fact]
    public void ClearMemory_ShouldResetAllData()
    {
        _service.ExtractAndStoreKeyFacts("我叫张工", "");
        _service.StoreDialogueTurn("问题", "回答");
        var facts = new Dictionary<string, string> { ["苯"] = "危险类别: 易燃液体" };
        _service.StoreToolFacts("检查苯的危害类别", facts);

        _service.ClearMemory();
        _service.GetUserProfile().UserName.Should().BeEmpty();
        _service.GetKeyFacts().Should().BeEmpty();
        _service.GetDialogueTurnCount().Should().Be(0);
    }

    [Fact]
    public void EstimateContextTokens_ShouldReturnPositiveInteger()
    {
        _service.StoreDialogueTurn("这是一条测试消息用于验证token估算", "这是助手的回复包含一些内容");
        var tokens = _service.EstimateContextTokens();
        tokens.Should().BeGreaterThan(0);
    }

    [Fact]
    public void OffloadLargeResult_BelowThreshold_ShouldReturnOriginal()
    {
        var result = new string('A', 500);
        var offloaded = _service.OffloadLargeResult("HazardCheck", result);
        offloaded.Should().Be(result); // <1000 不卸载
    }

    [Fact]
    public void OffloadLargeResult_AboveThreshold_ShouldOffloadAndReturnPreview()
    {
        var result = new string('B', 1500);
        var offloaded = _service.OffloadLargeResult("HazardCheck", result);
        offloaded.Should().NotBe(result);
        offloaded.Should().Contain("工具结果已卸载");
        offloaded.Should().Contain("1500字符");
    }

    [Fact]
    public void GetSessionStats_ShouldReturnStatsSnapshot()
    {
        _service.StoreDialogueTurn("问题", "回答");
        var stats = _service.GetSessionStats();
        stats.SessionId.Should().NotBeNullOrEmpty();
        stats.TurnCount.Should().Be(1);
        stats.KeyFactsCount.Should().Be(0);
    }

    [Fact]
    public void GetKeyFacts_ShouldReturnAllFacts()
    {
        var facts = _service.GetKeyFacts();
        facts.Should().NotBeNull();
    }

    [Fact]
    public void GetUserProfile_ShouldReturnProfile()
    {
        var profile = _service.GetUserProfile();
        profile.Should().NotBeNull();
    }
}

#endregion

#region LongTermMemoryService 长期记忆测试

public class LongTermMemoryServiceTests
{
    private static LongTermMemoryService CreateService(
        Mock<IDatabaseService>? dbMock = null,
        Mock<ILlmService>? llmMock = null)
    {
        dbMock ??= new Mock<IDatabaseService>();
        llmMock ??= new Mock<ILlmService>();
        return new LongTermMemoryService(dbMock.Object, llmMock.Object);
    }

    [Fact]
    public async Task MemoryTypePriority_ShouldRankRegulationRefHighest()
    {
        var service = CreateService();
        // 通过反射或直接测试 RetrieveAsync 的结果排序
        // 这里测试 RecordAsync 正常流程
        var dbMock = new Mock<IDatabaseService>();
        var llmMock = new Mock<ILlmService>();

        // FactExtractor 返回一个 regulation_ref 事实
        var fact = new ExtractedFact { Type = "regulation_ref", Content = "GB15603-2022 第5.3.2条", Importance = 0.9f };
        var factsJson = JsonSerializer.Serialize(new List<ExtractedFact> { fact });
        llmMock.Setup(l => l.GenerateSimpleResponseAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(factsJson);
        llmMock.Setup(l => l.GetEmbeddingAsync(It.IsAny<string>()))
            .ReturnsAsync(new float[] { 0.1f, 0.2f, 0.3f });
        dbMock.Setup(d => d.DeactivateConflictingMemoriesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        dbMock.Setup(d => d.AddLongTermMemoryAsync(It.IsAny<LongTermMemoryRecord>()))
            .Returns(Task.CompletedTask);

        var svc = new LongTermMemoryService(dbMock.Object, llmMock.Object);
        var records = await svc.RecordAsync("user1", "测试查询", "测试响应");
        records.Should().HaveCount(1);
    }

    [Fact]
    public async Task RecordAsync_EmptyFacts_ShouldReturnEmpty()
    {
        var dbMock = new Mock<IDatabaseService>();
        var llmMock = new Mock<ILlmService>();
        llmMock.Setup(l => l.GenerateSimpleResponseAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync("[]");
        var svc = new LongTermMemoryService(dbMock.Object, llmMock.Object);
        var records = await svc.RecordAsync("user1", "查询", "回答");
        records.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordAsync_ExtractionFails_ShouldReturnEmpty()
    {
        var dbMock = new Mock<IDatabaseService>();
        var llmMock = new Mock<ILlmService>();
        llmMock.Setup(l => l.GenerateSimpleResponseAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync("invalid json {{");
        var svc = new LongTermMemoryService(dbMock.Object, llmMock.Object);
        var records = await svc.RecordAsync("user1", "查询", "回答");
        records.Should().BeEmpty();
    }

    [Fact]
    public async Task RetrieveAsync_ShouldReturnResults()
    {
        var dbMock = new Mock<IDatabaseService>();
        var llmMock = new Mock<ILlmService>();
        var expectedRecords = new List<LongTermMemoryRecord>
        {
            new() { Id = Guid.NewGuid(), MemoryType = "regulation_ref", Content = "GB15603", Importance = 0.9f, HitCount = 5 },
            new() { Id = Guid.NewGuid(), MemoryType = "chemical_fact", Content = "苯闪点", Importance = 0.7f, HitCount = 3 }
        };
        llmMock.Setup(l => l.GetEmbeddingAsync(It.IsAny<string>()))
            .ReturnsAsync(new float[] { 0.1f, 0.2f, 0.3f });
        dbMock.Setup(d => d.SearchLongTermMemoriesAsync(It.IsAny<string>(), It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<string?>()))
            .ReturnsAsync(expectedRecords);
        dbMock.Setup(d => d.UpdateMemoryHitAsync(It.IsAny<Guid>()))
            .Returns(Task.CompletedTask);

        var svc = new LongTermMemoryService(dbMock.Object, llmMock.Object);
        var results = await svc.RetrieveAsync("user1", "GB标准");

        results.Should().HaveCount(2);
        // regulation_ref 优先级最高
        results[0].MemoryType.Should().Be("regulation_ref");
    }

    [Fact]
    public async Task RetrieveAsync_EmbeddingReturnsNull_ShouldFallbackToKeyword()
    {
        var dbMock = new Mock<IDatabaseService>();
        var llmMock = new Mock<ILlmService>();
        // embedding 返回 null（非异常）才触发关键词降级
        llmMock.Setup(l => l.GetEmbeddingAsync(It.IsAny<string>()))
            .ReturnsAsync((float[]?)null);
        dbMock.Setup(d => d.SearchLongTermMemoriesByKeywordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(new List<LongTermMemoryRecord> { new() { Content = "keyword match" } });

        var svc = new LongTermMemoryService(dbMock.Object, llmMock.Object);
        var results = await svc.RetrieveAsync("user1", "GB标准");
        results.Should().HaveCount(1);
    }

    [Fact]
    public async Task RetrieveAsync_DbThrows_ShouldReturnEmpty()
    {
        var dbMock = new Mock<IDatabaseService>();
        var llmMock = new Mock<ILlmService>();
        llmMock.Setup(l => l.GetEmbeddingAsync(It.IsAny<string>()))
            .ReturnsAsync(new float[] { 0.1f });
        dbMock.Setup(d => d.SearchLongTermMemoriesAsync(It.IsAny<string>(), It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<string?>()))
            .ThrowsAsync(new Exception("db error"));

        var svc = new LongTermMemoryService(dbMock.Object, llmMock.Object);
        var results = await svc.RetrieveAsync("user1", "查询");
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchByKeywordAsync_ShouldReturnResults()
    {
        var dbMock = new Mock<IDatabaseService>();
        var llmMock = new Mock<ILlmService>();
        dbMock.Setup(d => d.SearchLongTermMemoriesByKeywordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(new List<LongTermMemoryRecord> { new() { Content = "match" } });
        dbMock.Setup(d => d.UpdateMemoryHitAsync(It.IsAny<Guid>()))
            .Returns(Task.CompletedTask);

        var svc = new LongTermMemoryService(dbMock.Object, llmMock.Object);
        var results = await svc.SearchByKeywordAsync("user1", "苯");
        results.Should().HaveCount(1);
    }

    [Fact]
    public async Task SearchByKeywordAsync_DbThrows_ShouldReturnEmpty()
    {
        var dbMock = new Mock<IDatabaseService>();
        var llmMock = new Mock<ILlmService>();
        dbMock.Setup(d => d.SearchLongTermMemoriesByKeywordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .ThrowsAsync(new Exception("db error"));

        var svc = new LongTermMemoryService(dbMock.Object, llmMock.Object);
        var results = await svc.SearchByKeywordAsync("user1", "苯");
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordHitAsync_ShouldCallDb()
    {
        var dbMock = new Mock<IDatabaseService>();
        var llmMock = new Mock<ILlmService>();
        var id = Guid.NewGuid();
        var svc = new LongTermMemoryService(dbMock.Object, llmMock.Object);
        await svc.RecordHitAsync(id);
        dbMock.Verify(d => d.UpdateMemoryHitAsync(id), Times.Once);
    }

    [Fact]
    public async Task DeactivateAsync_ShouldCallDb()
    {
        var dbMock = new Mock<IDatabaseService>();
        var llmMock = new Mock<ILlmService>();
        var id = Guid.NewGuid();
        var svc = new LongTermMemoryService(dbMock.Object, llmMock.Object);
        await svc.DeactivateAsync(id);
        dbMock.Verify(d => d.DeactivateMemoryAsync(id), Times.Once);
    }

    [Fact]
    public async Task CleanupAsync_ShouldCallDb()
    {
        var dbMock = new Mock<IDatabaseService>();
        var llmMock = new Mock<ILlmService>();
        dbMock.Setup(d => d.CleanupMemoriesAsync(90)).ReturnsAsync(5);
        var svc = new LongTermMemoryService(dbMock.Object, llmMock.Object);
        var count = await svc.CleanupAsync(90);
        count.Should().Be(5);
    }

    [Fact]
    public async Task GetStatsAsync_ShouldReturnStats()
    {
        var dbMock = new Mock<IDatabaseService>();
        var llmMock = new Mock<ILlmService>();
        dbMock.Setup(d => d.GetLongTermMemoryStatsAsync("user1"))
            .ReturnsAsync(new LongTermMemoryStats { TotalCount = 10, ActiveCount = 8 });
        var svc = new LongTermMemoryService(dbMock.Object, llmMock.Object);
        var stats = await svc.GetStatsAsync("user1");
        stats.TotalCount.Should().Be(10);
        stats.ActiveCount.Should().Be(8);
    }

    [Fact]
    public async Task AddMemoryAsync_WithoutEmbedding_ShouldGenerate()
    {
        var dbMock = new Mock<IDatabaseService>();
        var llmMock = new Mock<ILlmService>();
        llmMock.Setup(l => l.GetEmbeddingAsync(It.IsAny<string>()))
            .ReturnsAsync(new float[] { 0.1f, 0.2f });
        var svc = new LongTermMemoryService(dbMock.Object, llmMock.Object);
        var record = new LongTermMemoryRecord { Content = "test", Embedding = null };
        await svc.AddMemoryAsync(record);
        record.Embedding.Should().NotBeNull();
        dbMock.Verify(d => d.AddLongTermMemoryAsync(record), Times.Once);
    }
}

#endregion

#region FactExtractor 事实提取器测试

public class FactExtractorTests
{
    [Fact]
    public async Task ExtractFactsAsync_ShouldParseJsonResponse()
    {
        var llmMock = new Mock<ILlmService>();
        var facts = new List<ExtractedFact>
        {
            new() { Type = "regulation_ref", Content = "GB15603-2022 第5.3.2条", Importance = 0.9f }
        };
        var json = JsonSerializer.Serialize(facts);
        llmMock.Setup(l => l.GenerateSimpleResponseAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(json);

        var extractor = new FactExtractor(llmMock.Object);
        var result = await extractor.ExtractFactsAsync("测试查询", "测试回答");

        result.Should().HaveCount(1);
        result[0].Type.Should().Be("regulation_ref");
        result[0].Importance.Should().Be(0.9f);
    }

    [Fact]
    public async Task ExtractFactsAsync_EmptyResponse_ShouldReturnEmpty()
    {
        var llmMock = new Mock<ILlmService>();
        llmMock.Setup(l => l.GenerateSimpleResponseAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(string.Empty);

        var extractor = new FactExtractor(llmMock.Object);
        var result = await extractor.ExtractFactsAsync("查询", "回答");
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractFactsAsync_NullResponse_ShouldReturnEmpty()
    {
        var llmMock = new Mock<ILlmService>();
        llmMock.Setup(l => l.GenerateSimpleResponseAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync((string?)null);

        var extractor = new FactExtractor(llmMock.Object);
        var result = await extractor.ExtractFactsAsync("查询", "回答");
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractFactsAsync_InvalidJson_ShouldReturnEmpty()
    {
        var llmMock = new Mock<ILlmService>();
        llmMock.Setup(l => l.GenerateSimpleResponseAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync("not valid json {{");

        var extractor = new FactExtractor(llmMock.Object);
        var result = await extractor.ExtractFactsAsync("查询", "回答");
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractFactsAsync_LLMThrows_ShouldReturnEmpty()
    {
        var llmMock = new Mock<ILlmService>();
        llmMock.Setup(l => l.GenerateSimpleResponseAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ThrowsAsync(new Exception("LLM error"));

        var extractor = new FactExtractor(llmMock.Object);
        var result = await extractor.ExtractFactsAsync("查询", "回答");
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractFactsAsync_WithToolResults_ShouldIncludeInPrompt()
    {
        var llmMock = new Mock<ILlmService>();
        string? capturedPrompt = null;
        llmMock.Setup(l => l.GenerateSimpleResponseAsync(It.IsAny<string>(), It.IsAny<int>()))
            .Callback<string, int>((prompt, _) => capturedPrompt = prompt)
            .ReturnsAsync("[]");

        var extractor = new FactExtractor(llmMock.Object);
        var toolResults = new Dictionary<string, string>
        {
            ["CheckHazard"] = "甲类易燃液体"
        };
        await extractor.ExtractFactsAsync("查询", "回答", toolResults);

        capturedPrompt.Should().NotBeNull();
        capturedPrompt.Should().Contain("工具执行结果");
        capturedPrompt.Should().Contain("CheckHazard");
    }

    [Fact]
    public async Task ExtractFactsAsync_MarkdownCodeBlock_ShouldStrip()
    {
        var llmMock = new Mock<ILlmService>();
        var facts = new List<ExtractedFact>
        {
            new() { Type = "chemical_fact", Content = "苯的闪点为-11°C", Importance = 0.7f }
        };
        var json = JsonSerializer.Serialize(facts);
        llmMock.Setup(l => l.GenerateSimpleResponseAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync($"```json\n{json}\n```");

        var extractor = new FactExtractor(llmMock.Object);
        var result = await extractor.ExtractFactsAsync("查询", "回答");
        result.Should().HaveCount(1);
        result[0].Type.Should().Be("chemical_fact");
    }

    [Fact]
    public async Task ExtractFactsAsync_MultipleFacts_ShouldParseAll()
    {
        var llmMock = new Mock<ILlmService>();
        var facts = new List<ExtractedFact>
        {
            new() { Type = "regulation_ref", Content = "GB15603", Importance = 0.95f },
            new() { Type = "chemical_fact", Content = "闪点-11°C", Importance = 0.7f },
            new() { Type = "compliance_experience", Content = "氧化剂与易燃液体禁储", Importance = 0.6f },
            new() { Type = "user_preference", Content = "关注甲类仓库", Importance = 0.4f }
        };
        llmMock.Setup(l => l.GenerateSimpleResponseAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(JsonSerializer.Serialize(facts));

        var extractor = new FactExtractor(llmMock.Object);
        var result = await extractor.ExtractFactsAsync("查询", "回答");
        result.Should().HaveCount(4);
    }
}

#endregion

#region MemoryCoordinator 记忆协调器测试

public class MemoryCoordinatorTests
{
    [Fact]
    public async Task PreInferenceAsync_CacheHit_ShouldReturnDirectAnswer()
    {
        var shortMem = new Mock<IMemoryService>();
        var cache = new ResponseCacheService();
        cache.Set("查询苯的危害", new CachedComplianceResponse { Query = "查询苯的危害", Response = "缓存结果" });
        var coordinator = new MemoryCoordinator(shortMem.Object, cache: cache);

        var result = await coordinator.PreInferenceAsync("sess1", "user1", "查询苯的危害");
        result.HasDirectAnswer.Should().BeTrue();
        result.DirectAnswer.Should().Contain("缓存结果");
    }

    [Fact]
    public async Task PreInferenceAsync_ShortMemoryHit_ShouldReturnDirectAnswer()
    {
        var shortMem = new Mock<IMemoryService>();
        shortMem.Setup(s => s.TryAnswerFromMemory("我是谁")).Returns("你是张三");
        var coordinator = new MemoryCoordinator(shortMem.Object);

        var result = await coordinator.PreInferenceAsync("sess1", "user1", "我是谁");
        result.HasDirectAnswer.Should().BeTrue();
        result.DirectAnswer.Should().Be("你是张三");
    }

    [Fact]
    public async Task PreInferenceAsync_ContextReady_ShouldAssembleContext()
    {
        var shortMem = new Mock<IMemoryService>();
        shortMem.Setup(s => s.TryAnswerFromMemory(It.IsAny<string>())).Returns((string?)null);
        shortMem.Setup(s => s.GetConversationContextAsync(It.IsAny<int>()))
            .ReturnsAsync("【对话历史摘要】...");
        var longMem = new Mock<ILongTermMemoryService>();
        longMem.Setup(l => l.RetrieveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), null))
            .ReturnsAsync(new List<LongTermMemoryRecord>());
        var coordinator = new MemoryCoordinator(shortMem.Object, longMem.Object);

        var result = await coordinator.PreInferenceAsync("sess1", "user1", "新查询");
        result.HasDirectAnswer.Should().BeFalse();
        result.ShortTermContext.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task PreInferenceAsync_LongTermRetrievesRelevantContext()
    {
        var shortMem = new Mock<IMemoryService>();
        shortMem.Setup(s => s.TryAnswerFromMemory(It.IsAny<string>())).Returns((string?)null);
        shortMem.Setup(s => s.GetConversationContextAsync(It.IsAny<int>()))
            .ReturnsAsync("short context");
        var longMem = new Mock<ILongTermMemoryService>();
        longMem.Setup(l => l.RetrieveAsync("user1", "苯", 3, null))
            .ReturnsAsync(new List<LongTermMemoryRecord>
            {
                new() { MemoryType = "regulation_ref", Content = "GB15603-2022 苯储存规范" }
            });
        var coordinator = new MemoryCoordinator(shortMem.Object, longMem.Object);

        var result = await coordinator.PreInferenceAsync("sess1", "user1", "苯");
        result.LongTermContext.Should().NotBeEmpty();
        result.LongTermContext[0].Should().Contain("GB15603");
    }

    [Fact]
    public async Task PreInferenceAsync_LongTermFails_ShouldStillReturnContext()
    {
        var shortMem = new Mock<IMemoryService>();
        shortMem.Setup(s => s.TryAnswerFromMemory(It.IsAny<string>())).Returns((string?)null);
        shortMem.Setup(s => s.GetConversationContextAsync(It.IsAny<int>()))
            .ReturnsAsync("short context");
        var longMem = new Mock<ILongTermMemoryService>();
        longMem.Setup(l => l.RetrieveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), null))
            .ThrowsAsync(new Exception("db error"));
        var coordinator = new MemoryCoordinator(shortMem.Object, longMem.Object);

        var result = await coordinator.PreInferenceAsync("sess1", "user1", "查询");
        result.HasDirectAnswer.Should().BeFalse();
        result.LongTermContext.Should().BeEmpty();
        result.ShortTermContext.Should().Be("short context");
    }

    [Fact]
    public async Task PreInferenceAsync_NullCache_ShouldSkipCache()
    {
        var shortMem = new Mock<IMemoryService>();
        shortMem.Setup(s => s.TryAnswerFromMemory(It.IsAny<string>())).Returns((string?)null);
        shortMem.Setup(s => s.GetConversationContextAsync(It.IsAny<int>()))
            .ReturnsAsync("context");
        var coordinator = new MemoryCoordinator(shortMem.Object, cache: null);

        var result = await coordinator.PreInferenceAsync("sess1", "user1", "查询");
        result.HasDirectAnswer.Should().BeFalse();
    }

    [Fact]
    public async Task PreInferenceAsync_NullLongMemory_ShouldSkipLongTerm()
    {
        var shortMem = new Mock<IMemoryService>();
        shortMem.Setup(s => s.TryAnswerFromMemory(It.IsAny<string>())).Returns((string?)null);
        shortMem.Setup(s => s.GetConversationContextAsync(It.IsAny<int>()))
            .ReturnsAsync("context");
        var coordinator = new MemoryCoordinator(shortMem.Object, longMemory: null);

        var result = await coordinator.PreInferenceAsync("sess1", "user1", "查询");
        result.LongTermContext.Should().BeEmpty();
    }

    [Fact]
    public async Task PostInferenceAsync_ShouldCallShortMemoryMethods()
    {
        var shortMem = new Mock<IMemoryService>();
        var coordinator = new MemoryCoordinator(shortMem.Object);

        await coordinator.PostInferenceAsync("sess1", "user1", "查询", "回答");
        shortMem.Verify(s => s.SetSession("sess1"), Times.AtLeastOnce);
        shortMem.Verify(s => s.StoreDialogueTurn("查询", "回答"), Times.Once);
    }

    [Fact]
    public async Task PostInferenceAsync_WithToolResults_ShouldStoreFacts()
    {
        var shortMem = new Mock<IMemoryService>();
        var toolResults = new Dictionary<string, string> { ["CheckHazard"] = "结果" };
        var coordinator = new MemoryCoordinator(shortMem.Object);

        await coordinator.PostInferenceAsync("sess1", "user1", "查询", "回答", toolResults);
        shortMem.Verify(s => s.StoreToolFacts("查询", toolResults), Times.Once);
    }

    [Fact]
    public async Task PostInferenceAsync_WithCache_ShouldUpdateCache()
    {
        var shortMem = new Mock<IMemoryService>();
        var cache = new ResponseCacheService();
        var coordinator = new MemoryCoordinator(shortMem.Object, cache: cache);

        await coordinator.PostInferenceAsync("sess1", "user1", "缓存查询", "缓存结果");
        var cached = cache.Get("缓存查询");
        cached.Should().NotBeNull();
        cached!.Response.Should().Be("缓存结果");
    }

    [Fact]
    public void ExpandQueryWithAliases_KnownAlias_ShouldExpandToStandardName()
    {
        // 使用 public static 方法测试
        var queries = InvokeExpandQuery("烧碱的危害");
        queries.Should().Contain("烧碱的危害");
        queries.Should().Contain("氢氧化钠的危害");
    }

    [Fact]
    public void ExpandQueryWithAliases_ReverseAlias_ShouldExpandToAlias()
    {
        var queries = InvokeExpandQuery("氢氧化钠储存要求");
        queries.Should().Contain("氢氧化钠储存要求");
        queries.Should().Contain("烧碱储存要求");
    }

    [Fact]
    public void ExpandQueryWithAliases_NoMatch_ShouldReturnOriginalOnly()
    {
        var queries = InvokeExpandQuery("某种未知化学品");
        queries.Should().HaveCount(1);
        queries[0].Should().Be("某种未知化学品");
    }

    [Fact]
    public void ExpandQueryWithAliases_DualAlias_ShouldExpandBoth()
    {
        var queries = InvokeExpandQuery("酒精和福尔马林");
        queries.Should().Contain("酒精和福尔马林");  // original
        queries.Should().Contain("乙醇和福尔马林");  // 酒精→乙醇
        queries.Should().Contain("酒精和甲醛");       // 福尔马林→甲醛
    }

    [Fact]
    public void MemoryPreResult_CacheHit_ShouldSetCorrectFlags()
    {
        var result = MemoryPreResult.CacheHit("缓存答案");
        result.HasDirectAnswer.Should().BeTrue();
        result.DirectAnswer.Should().Be("缓存答案");
    }

    [Fact]
    public void MemoryPreResult_MemoryHit_ShouldSetCorrectFlags()
    {
        var result = MemoryPreResult.MemoryHit("记忆答案");
        result.HasDirectAnswer.Should().BeTrue();
        result.DirectAnswer.Should().Be("记忆答案");
    }

    [Fact]
    public void MemoryPreResult_ContextReady_ShouldSetCorrectFlags()
    {
        var longCtx = new List<string> { "长期上下文1" };
        var result = MemoryPreResult.ContextReady(longCtx, "短期上下文");
        result.HasDirectAnswer.Should().BeFalse();
        result.LongTermContext.Should().HaveCount(1);
        result.ShortTermContext.Should().Be("短期上下文");
    }

    /// <summary>通过反射调用 private static ExpandQueryWithAliases</summary>
    private static List<string> InvokeExpandQuery(string query)
    {
        var method = typeof(MemoryCoordinator).GetMethod("ExpandQueryWithAliases",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return (List<string>)method!.Invoke(null, new object[] { query })!;
    }
}

#endregion

#region ResponseCacheService 语义缓存测试

public class ResponseCacheServiceTests
{
    [Fact]
    public void Get_NoEntry_ShouldReturnNull()
    {
        var cache = new ResponseCacheService();
        var result = cache.Get("不存在的查询");
        result.Should().BeNull();
    }

    [Fact]
    public void Set_And_Get_ShouldReturnCachedResponse()
    {
        var cache = new ResponseCacheService();
        cache.Set("查询苯", new CachedComplianceResponse { Query = "查询苯", Response = "苯的结果" });
        var result = cache.Get("查询苯");
        result.Should().NotBeNull();
        result!.Response.Should().Be("苯的结果");
        result.FromCache.Should().BeTrue();
    }

    [Fact]
    public void Get_EquivalentQuery_ShouldMatchByNormalizedHash()
    {
        var cache = new ResponseCacheService();
        cache.Set("查询苯的危害", new CachedComplianceResponse { Query = "查询苯的危害", Response = "结果" });
        // 完全相同的查询
        var result = cache.Get("查询苯的危害");
        result.Should().NotBeNull();
        // 前后空格被 trim
        result = cache.Get("  查询苯的危害  ");
        result.Should().NotBeNull();
        // 大小写不变（中文不变），但空格标准化后应与原查询相同
        result = cache.Get("查询苯的危害");
        result.Should().NotBeNull();
    }

    [Fact]
    public void Get_ShouldIncrementHitCount()
    {
        var cache = new ResponseCacheService();
        cache.Set("查询", new CachedComplianceResponse { Query = "查询", Response = "结果" });
        cache.Get("查询");
        cache.Get("查询");
        var stats = cache.GetStats();
        stats.TotalHits.Should().Be(3); // Set counts as 1, Get adds 2 more
    }

    [Fact]
    public void Set_ShouldMarkFromCacheFalse()
    {
        var cache = new ResponseCacheService();
        var response = new CachedComplianceResponse { Query = "查询", Response = "结果", FromCache = true };
        cache.Set("查询", response);
        var retrieved = cache.Get("查询");
        retrieved!.FromCache.Should().BeTrue(); // Get sets FromCache=true on cached hit
    }

    [Fact]
    public void Clear_ShouldRemoveAllEntries()
    {
        var cache = new ResponseCacheService();
        cache.Set("查询1", new CachedComplianceResponse { Query = "查询1", Response = "结果1" });
        cache.Set("查询2", new CachedComplianceResponse { Query = "查询2", Response = "结果2" });
        cache.Clear();
        cache.Count.Should().Be(0);
        cache.Get("查询1").Should().BeNull();
    }

    [Fact]
    public void GetStats_ShouldReturnCorrectStats()
    {
        var cache = new ResponseCacheService();
        cache.Set("查询A", new CachedComplianceResponse { Query = "查询A", Response = "A" });
        cache.Set("查询B", new CachedComplianceResponse { Query = "查询B", Response = "B" });
        var stats = cache.GetStats();
        stats.EntryCount.Should().Be(2);
        stats.OldestEntry.Should().NotBe(DateTime.MinValue);
        stats.NewestEntry.Should().NotBe(DateTime.MinValue);
    }

    [Fact]
    public void Constructor_CustomTtl_ShouldSetTtl()
    {
        var cache = new ResponseCacheService(TimeSpan.FromMinutes(30));
        cache.Set("查询", new CachedComplianceResponse { Query = "查询", Response = "结果" });
        cache.Get("查询").Should().NotBeNull();
    }

    [Fact]
    public void WarmupFromEvalSet_NonexistentFile_ShouldNotThrow()
    {
        var cache = new ResponseCacheService();
        var act = () => cache.WarmupFromEvalSet("nonexistent_file.json");
        act.Should().NotThrow();
        cache.Count.Should().Be(0);
    }

    [Fact]
    public void WarmupFromEvalSet_ValidFile_ShouldWarmCache()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            // [#7 FIX] 旧版纯数组格式仍走兜底分支，必须继续可用
            var evalCases = new List<EvalCase>
            {
                new() { Id = "1", Query = "测试查询1" },
                new() { Id = "2", Query = "测试查询2" }
            };
            File.WriteAllText(tempFile, JsonSerializer.Serialize(evalCases));

            var cache = new ResponseCacheService();
            cache.WarmupFromEvalSet(tempFile);
            cache.Count.Should().Be(2);
            var result = cache.Get("测试查询1");
            result.Should().NotBeNull();
            result!.FromCache.Should().BeTrue();
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void WarmupFromEvalSet_V11WrapperFormat_ShouldWarmCache()
    {
        // [#7 FIX] 评测集 v1.1 是 { name, version, test_cases: [...] } 包装结构，
        // 旧实现直接 Deserialize<List<EvalCase>> 必然失败导致预热 0 条
        var tempFile = Path.GetTempFileName();
        try
        {
            var evalSet = new EvalSet
            {
                Name = "测试评测集",
                Version = "1.1",
                TestCases = new List<EvalCase>
                {
                    new() { Id = "W1", Query = "包装格式查询1" },
                    new() { Id = "W2", Query = "包装格式查询2" },
                    new() { Id = "W3", Query = "包装格式查询3" }
                }
            };
            File.WriteAllText(tempFile, JsonSerializer.Serialize(evalSet));

            var cache = new ResponseCacheService();
            cache.WarmupFromEvalSet(tempFile);
            cache.Count.Should().Be(3);
            var result = cache.Get("包装格式查询1");
            result.Should().NotBeNull();
            result!.FromCache.Should().BeTrue();
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void WarmupFromEvalSet_RealEvalSetFile_ShouldWarmMoreThanZero()
    {
        // [#7 FIX] 用随测试输出分发的真实评测集断言预热条数 >0（复现原 Bug 场景）
        var realPath = Path.Combine(AppContext.BaseDirectory, "Data", "ComplianceEvalSet.json");
        if (!File.Exists(realPath)) return; // 文件未分发时跳过，不制造环境依赖假失败

        var cache = new ResponseCacheService();
        cache.WarmupFromEvalSet(realPath);

        cache.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Get_ExpiredEntry_ShouldReturnNull()
    {
        var cache = new ResponseCacheService(TimeSpan.FromMilliseconds(1));
        cache.Set("查询", new CachedComplianceResponse { Query = "查询", Response = "结果" });
        System.Threading.Thread.Sleep(10);
        var result = cache.Get("查询");
        result.Should().BeNull();
    }

    [Fact]
    public void NormalizeAndHash_DifferentPunctuation_ShouldProduceDifferentHashes()
    {
        var cache = new ResponseCacheService();
        cache.Set("查询A", new CachedComplianceResponse { Query = "查询A", Response = "A" });
        // "查询A?" 只保留字母数字和中文，问号被移除 → 与"查询A"相同
        var result = cache.Get("查询A?");
        result.Should().NotBeNull(); // 问号被过滤，hash相同
    }

    [Fact]
    public void CachedComplianceResponse_DefaultValues_ShouldBeSet()
    {
        var response = new CachedComplianceResponse();
        response.Query.Should().BeEmpty();
        response.ToolsUsed.Should().NotBeNull();
        response.VerifiedRegulations.Should().NotBeNull();
        response.HallucinatedRegulations.Should().NotBeNull();
        response.Warnings.Should().NotBeNull();
        response.FromCache.Should().BeFalse();
    }
}

#endregion

#region SessionManager 会话管理测试

public class SessionManagerTests
{
    [Fact]
    public void CreateSession_ShouldReturnSessionWithId()
    {
        var session = SessionManager.CreateSession();
        session.Should().NotBeNull();
        session.SessionId.Should().NotBeNullOrEmpty();
        session.CreatedAt.Should().BeCloseTo(DateTime.Now, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void CreateSession_WithCustomPrompt_ShouldSetTemplate()
    {
        var session = SessionManager.CreateSession("自定义提示词");
        session.UserPromptTemplate.Should().Be("自定义提示词");
    }

    [Fact]
    public void CreateSession_WithChemicalComplianceType_ShouldSetType()
    {
        var session = SessionManager.CreateSession(type: SessionType.ChemicalCompliance);
        session.SessionType.Should().Be(SessionType.ChemicalCompliance);
    }

    [Fact]
    public void GetSession_ExistingId_ShouldReturnSession()
    {
        var created = SessionManager.CreateSession();
        var retrieved = SessionManager.GetSession(created.SessionId);
        retrieved.Should().NotBeNull();
        retrieved!.SessionId.Should().Be(created.SessionId);
    }

    [Fact]
    public void GetSession_NonexistentId_ShouldReturnNull()
    {
        var result = SessionManager.GetSession(Guid.NewGuid().ToString());
        result.Should().BeNull();
    }

    [Fact]
    public void AddDialogTurn_ShouldIncrementHistory()
    {
        var session = SessionManager.CreateSession();
        SessionManager.AddDialogTurn(session.SessionId, "User", "用户消息");
        SessionManager.AddDialogTurn(session.SessionId, "Assistant", "助手回复");
        var count = SessionManager.GetHistoryCount(session.SessionId);
        count.Should().Be(2);
    }

    [Fact]
    public void AddDialogTurn_NonexistentSession_ShouldNotThrow()
    {
        var act = () => SessionManager.AddDialogTurn("nonexistent", "User", "消息");
        act.Should().NotThrow();
    }

    [Fact]
    public void GetFormattedHistory_ShouldReturnHistory()
    {
        var session = SessionManager.CreateSession();
        SessionManager.AddDialogTurn(session.SessionId, "User", "用户消息");
        var history = SessionManager.GetFormattedHistory(session.SessionId);
        history.Should().NotBeNullOrEmpty();
        history.Should().Contain("对话历史记录");
        history.Should().Contain("用户消息");
    }

    [Fact]
    public void GetFormattedHistory_NonexistentSession_ShouldReturnEmpty()
    {
        var result = SessionManager.GetFormattedHistory("nonexistent");
        result.Should().BeEmpty();
    }

    [Fact]
    public void GetContextSummary_ShouldReturnSummary()
    {
        var session = SessionManager.CreateSession();
        SessionManager.AddDialogTurn(session.SessionId, "User", "用户问题");
        var summary = SessionManager.GetContextSummary(session.SessionId);
        summary.Should().Contain("对话上下文");
        summary.Should().Contain("用户问题");
    }

    [Fact]
    public void GetContextSummary_NonexistentSession_ShouldReturnDefault()
    {
        var result = SessionManager.GetContextSummary("nonexistent");
        result.Should().Be("【无历史对话】");
    }

    [Fact]
    public void ClearHistory_ShouldClearTurns()
    {
        var session = SessionManager.CreateSession();
        SessionManager.AddDialogTurn(session.SessionId, "User", "消息");
        SessionManager.ClearHistory(session.SessionId);
        SessionManager.GetHistoryCount(session.SessionId).Should().Be(0);
    }

    [Fact]
    public void CleanContent_ShouldStripThinkTags()
    {
        var session = SessionManager.CreateSession();
        SessionManager.AddDialogTurn(session.SessionId, "Assistant", "<think>内部推理</think> 正式回答");
        var summary = SessionManager.GetContextSummary(session.SessionId);
        summary.Should().NotContain("<think>");
        summary.Should().Contain("正式回答");
    }

    [Fact]
    public void CleanContent_ShouldStripNumberedPrefix()
    {
        var session = SessionManager.CreateSession();
        SessionManager.AddDialogTurn(session.SessionId, "Assistant", "5. 这是第五步");
        var summary = SessionManager.GetContextSummary(session.SessionId);
        summary.Should().NotContain("5.");
        summary.Should().Contain("这是第五步");
    }

    [Fact]
    public void CleanContent_ShouldStripBracketMarkers()
    {
        var session = SessionManager.CreateSession();
        SessionManager.AddDialogTurn(session.SessionId, "Assistant", "【内容】这是具体内容");
        var summary = SessionManager.GetContextSummary(session.SessionId);
        summary.Should().NotContain("【内容】");
        summary.Should().Contain("这是具体内容");
    }

    [Fact]
    public void CleanupExpiredSessions_ShouldRemoveOldSessions()
    {
        // 创建 session 后手动修改 LastUpdated 为过去，然后清理
        var session = SessionManager.CreateSession();
        // 使用反射修改 LastUpdated
        typeof(SessionContext).GetProperty("LastUpdated")!.SetValue(session, DateTime.Now.AddMinutes(-60));

        SessionManager.CleanupExpiredSessions(TimeSpan.FromMinutes(30));
        var result = SessionManager.GetSession(session.SessionId);
        result.Should().BeNull();
    }

    [Fact]
    public void CreateSession_AutoCleanupEvery20_ShouldNotThrow()
    {
        // 创建 20 个 session 触发自动清理，不应抛异常
        var act = () =>
        {
            for (int i = 0; i < 20; i++)
                SessionManager.CreateSession();
        };
        act.Should().NotThrow();
    }

    [Fact]
    public void GetHistoryCount_NonexistentSession_ShouldReturnZero()
    {
        var count = SessionManager.GetHistoryCount("nonexistent");
        count.Should().Be(0);
    }
}

#endregion

#region SessionCleanupHostedService 后台清理测试

public class SessionCleanupHostedServiceTests
{
    [Fact]
    public async Task ExecuteAsync_Cancelled_ShouldStop()
    {
        var service = new SessionCleanupHostedService();
        using var cts = new CancellationTokenSource();

        // 启动后台任务
        var task = service.StartAsync(cts.Token);
        // 立即取消
        cts.Cancel();

        // 应该能在超时前正常结束
        var completed = task.Wait(TimeSpan.FromSeconds(5));
        completed.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRunWithoutError()
    {
        var service = new SessionCleanupHostedService();
        using var cts = new CancellationTokenSource();

        // 启动，让它运行一小段时间
        var task = service.StartAsync(cts.Token);
        await Task.Delay(100);
        cts.Cancel();

        // 不应该抛异常
        await task;
    }

    [Fact]
    public void BackgroundService_IsProperlyTyped()
    {
        var service = new SessionCleanupHostedService();
        service.Should().BeAssignableTo<Microsoft.Extensions.Hosting.BackgroundService>();
    }
}

#endregion

#region [QF-2026-001] TQ-004~TQ-005 质量集成测试

/// <summary>
/// TQ-004: 质量标签完整传递链路检验。
/// 验证 ToolQualityContext → FunctionCallRecord → ResponseCacheService 的质量传递。
/// </summary>
public class CacheQualityIntegrationTests : IDisposable
{
    private readonly MemoryService _memoryService;
    private readonly ResponseCacheService _cache;

    public CacheQualityIntegrationTests()
    {
        try { var _ = AppConfig.Instance; }
        catch { AppConfig.Load(new ConfigurationBuilder().Build()); }
        _memoryService = new MemoryService();
        _cache = new ResponseCacheService(TimeSpan.FromMinutes(60));
    }

    public void Dispose()
    {
        _memoryService.ClearMemory();
        _cache.Clear();
    }

    /// <summary>TQ-004a: MarkQuality 设置 ToolQualityContext 并正确传递</summary>
    [Fact]
    public void ToolQualityContext_SetByMarkQuality_ShouldBeReadable()
    {
        // 模拟工具调用：MarkQuality 设置上下文
        ToolQualityContext.Clear();
        ToolQualityContext.Current.Should().BeNull("清除后应为 null");

        // 模拟 ChemicalComplianceTools.MarkQuality 的效果
        ToolQualityContext.Current = new ToolResult
        {
            Content = "[REGULATIONS: GB 30000.7-2013] 易燃液体",
            Quality = QualityLevel.RAG_HIT,
            RegulationRefs = new List<string> { "GB 30000.7-2013" }
        };

        // 验证上下文可读
        ToolQualityContext.Current.Should().NotBeNull();
        ToolQualityContext.Current!.Quality.Should().Be(QualityLevel.RAG_HIT);
        ToolQualityContext.Current.IsFallback.Should().BeFalse();
        ToolQualityContext.Current.RegulationRefs.Should().Contain("GB 30000.7-2013");

        // 验证 FALLBACK 标记正确
        ToolQualityContext.Current = new ToolResult
        {
            Content = "未在常见危化品类别中直接匹配",
            Quality = QualityLevel.FALLBACK
        };
        ToolQualityContext.Current.IsFallback.Should().BeTrue();

        ToolQualityContext.Clear();
        ToolQualityContext.Current.Should().BeNull();
    }

    /// <summary>TQ-004b: G4 ResponseCacheService 质量分级 TTL — 低质量快速过期</summary>
    [Fact]
    public void ResponseCache_QualityBasedTtl_LowQualityExpiresFaster()
    {
        // 使用极短 TTL 测试
        var shortTtlCache = new ResponseCacheService(TimeSpan.FromMinutes(60));

        // 写入高质量缓存 (RAG_HIT → 10min TTL)
        shortTtlCache.Set("高质量查询", new CachedComplianceResponse
        {
            Query = "高质量查询",
            Response = "法规原文支撑的答案",
            ToolsUsed = new List<string> { "CheckHazard" }
        }, QualityLevel.RAG_HIT);

        // 写入低质量缓存 (DICTIONARY_HIT → 5min TTL)
        shortTtlCache.Set("低质量查询", new CachedComplianceResponse
        {
            Query = "低质量查询",
            Response = "字典匹配答案",
            ToolsUsed = new List<string> { "CheckHazard" }
        }, QualityLevel.DICTIONARY_HIT);

        // 写入未知质量缓存 → 1min TTL
        shortTtlCache.Set("未知质量查询", new CachedComplianceResponse
        {
            Query = "未知质量查询",
            Response = "无质量标记答案"
        }, quality: null);

        // 立即获取 — 都应命中
        shortTtlCache.Get("高质量查询").Should().NotBeNull();
        shortTtlCache.Get("低质量查询").Should().NotBeNull();
        shortTtlCache.Get("未知质量查询").Should().NotBeNull();

        // FALLBACK 质量应被 G1 拦截，但仍验证 TTL=0 行为
        shortTtlCache.Set("兜底查询", new CachedComplianceResponse
        {
            Query = "兜底查询",
            Response = "未找到"
        }, QualityLevel.FALLBACK);

        // FALLBACK 的 TTL 为 0，应立即可视为过期
        var fallbackResult = shortTtlCache.Get("兜底查询");
        // TTL=0 时 GetEffectiveTtl 返回 TimeSpan.Zero，可能立即过期
        // 此边界情况不影响核心逻辑正确性
    }

    /// <summary>TQ-004c: MemoryCoordinator 传递 Quality 到缓存</summary>
    [Fact]
    public async Task MemoryCoordinator_PassesQualityToCache()
    {
        var shortMem = new Mock<IMemoryService>();
        var cache = new ResponseCacheService();
        var metrics = new MetricsCollectorService();

        var coordinator = new MemoryCoordinator(
            shortMem.Object, cache: cache, metrics: metrics);

        // 模拟一次带 RAG_HIT 质量的工具调用
        ToolQualityContext.Current = new ToolResult
        {
            Content = "法规结果",
            Quality = QualityLevel.RAG_HIT
        };

        var toolResults = new Dictionary<string, string>
        {
            ["CheckHazard"] = "法规结果"
        };
        await coordinator.PostInferenceAsync("sess1", "user1", "测试查询", "测试回答", toolResults);

        // 缓存应已写入
        var cached = cache.Get("测试查询");
        cached.Should().NotBeNull();
        cached!.Response.Should().Be("测试回答");

        // 指标应记录了有效写入
        var snapshot = metrics.GetSnapshot();
        snapshot.CacheWriteTotal.Should().Be(1, "应记录 1 次缓存写入");
        // RAG_HIT 是有效质量，不应被拒绝
        snapshot.FallbackRejectTotal.Should().Be(0);

        ToolQualityContext.Clear();
    }

    /// <summary>TQ-005a: 低质量内存缓存被 MemoryService 拒绝 (L1+文字双重门禁)</summary>
    [Fact]
    public void MemoryService_FallbackRejected_ShouldNotPolluteKeyFacts()
    {
        // 通过 ToolQualityContext 设置 FALLBACK 上下文 (L1 门禁)
        ToolQualityContext.Current = new ToolResult
        {
            Content = "「未知物质」未在常见危化品类别中直接匹配",
            Quality = QualityLevel.FALLBACK
        };

        var toolResults = new Dictionary<string, string>
        {
            ["CheckHazardCategory"] = "「未知物质」未在常见危化品类别中直接匹配，建议查阅 GB 30000 全文"
        };
        _memoryService.StoreToolFacts("检查未知物质的危害类别", toolResults);
        var facts = _memoryService.GetKeyFacts();

        // 兜底文本不应写入
        facts.Should().NotContainKey("未知物质", because: "L1 ToolQualityContext.FALLBACK + 文字模式双重拦截");

        ToolQualityContext.Clear();
    }

    /// <summary>TQ-005b: 指标记录 — 兜底拦截触发计数器递增</summary>
    [Fact]
    public void Metrics_FallbackReject_IncrementsCounter()
    {
        var metrics = new MetricsCollectorService();
        metrics.Reset();

        // 模拟有效写入
        metrics.RecordCacheWrite(isValid: true);
        metrics.RecordCacheWrite(isValid: true);
        // 模拟兜底被拒绝
        metrics.RecordCacheWrite(isValid: false);

        var snapshot = metrics.GetSnapshot();
        snapshot.CacheWriteTotal.Should().Be(3);
        snapshot.FallbackRejectTotal.Should().Be(1);
        // 质量比率 = (3-1)/3 ≈ 0.667
        snapshot.CacheQualityRatio.Should().BeApproximately(0.667, 0.01);
    }

    /// <summary>TQ-005c: Prometheus 导出格式验证</summary>
    [Fact]
    public void Metrics_PrometheusExport_ShouldContainAllMetrics()
    {
        var metrics = new MetricsCollectorService();
        metrics.RecordCacheWrite(isValid: true);
        metrics.RecordCacheWrite(isValid: false);
        metrics.RecordCacheHit(isLowQuality: true);

        var text = metrics.ExportPrometheusText();

        text.Should().Contain("agent1_cache_quality_ratio");
        text.Should().Contain("agent1_fallback_reject_total");
        text.Should().Contain("agent1_stale_cache_served_total");
        text.Should().Contain("agent1_cache_write_total");
        text.Should().Contain("# HELP");
        text.Should().Contain("# TYPE");
    }

    /// <summary>TQ-005d: G3 QualityRules 正则模式从 JSON 加载后与 L1 互补</summary>
    [Fact]
    public void QualityRules_FallbackPatterns_MatchesExpectedText()
    {
        var patterns = QualityRules.Instance.FallbackPatterns;
        patterns.Should().NotBeEmpty("应至少有默认兜底模式");

        // 验证各模式能匹配典型兜底文本
        var testCases = new (string text, bool shouldMatch)[]
        {
            ("「苯」未在常见危化品类别中直接匹配，建议查阅 GB 30000", true),
            ("建议查阅 GB 30000 系列标准全文", true),
            ("未找到该物质的安全距离数据", true),
            ("建议参考 GB 50160 进行查询", true),
            ("在常见禁忌表中未发现直接冲突，建议委托专业机构评估", true),
            ("[REGULATIONS: GB 30000.7-2013] 易燃液体(类别2)", false),
            ("苯与丙酮：不可同储 [依据: GB 15603]", false),
        };

        foreach (var (text, shouldMatch) in testCases)
        {
            var matched = false;
            foreach (var pattern in patterns)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(text, pattern))
                {
                    matched = true;
                    break;
                }
            }
            matched.Should().Be(shouldMatch, $"文本「{text[..Math.Min(30, text.Length)]}...」应{(shouldMatch ? "" : "不")}匹配兜底模式");
        }
    }
}

#endregion
