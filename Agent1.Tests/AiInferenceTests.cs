using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Agent1.Config;
using Agent1.Models;
using Agent1.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace Agent1.Tests;

// ═══════════════════════════════════════════
// ReflectionVerifier 测试 — 代码级事实核查引擎
// ═══════════════════════════════════════════
public class ReflectionVerifierTests
{
    private readonly ReflectionVerifier _verifier;
    private readonly Mock<IKnowledgeBaseService> _kbMock = new();

    public ReflectionVerifierTests()
    {
        _verifier = new ReflectionVerifier(_kbMock.Object);

        // 最小 AppConfig 初始化
        try { AppConfig.Instance.ToString(); } catch
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Llm:ModelId"] = "test-model",
                    ["Llm:Endpoint"] = "http://localhost:11434",
                    ["Database:Host"] = "localhost",
                    ["Database:Port"] = "5432",
                    ["Database:DatabaseName"] = "testdb",
                    ["Database:Password"] = "pwd",
                    ["VectorSearch:EmbeddingModelId"] = "test-embed",
                    ["PromptTemplates:SystemRole"] = "test",
                    ["PromptTemplates:EvalFastPrompt"] = "test {SystemRole} {UserInput}",
                    ["PromptTemplates:EvalFastQueryPrompt"] = "test {SystemRole} {UserInput}"
                }).Build();
            AppConfig.Load(config);
        }
    }

    // ── SystemHealthReport 测试 ──

    [Fact]
    public void VerifySystemHealth_AllToolsExecuted_NoWarnings()
    {
        var toolResults = new Dictionary<string, string>
        {
            ["CheckHazardCategory"] = "爆炸物 → GB 30000.2-2013",
            ["CheckStorageCompatibility"] = "禁止同库储存"
        };
        var plan = new ToolPlan { NeedsTools = true, ToolNames = { "CheckHazardCategory", "CheckStorageCompatibility" } };

        var report = _verifier.VerifySystemHealth(toolResults, plan);
        report.ToolsPlanned.Should().Be(2);
        report.ToolsExecuted.Should().Be(2);
        report.ToolsCancelled.Should().Be(0);
        report.ToolWarnings.Should().BeEmpty();
    }

    [Fact]
    public void VerifySystemHealth_ToolChainIncomplete_DetectsMissing()
    {
        var toolResults = new Dictionary<string, string> { ["CheckHazardCategory"] = "结果" };
        var plan = new ToolPlan { NeedsTools = true, ToolNames = { "CheckHazardCategory", "CheckStorageCompatibility" } };

        var report = _verifier.VerifySystemHealth(toolResults, plan);
        report.ToolsPlanned.Should().Be(2);
        report.ToolsExecuted.Should().Be(1);
    }

    [Fact]
    public void VerifySystemHealth_EmptyResult_GeneratesWarning()
    {
        var toolResults = new Dictionary<string, string> { ["GetCurrentTime"] = "" };
        var report = _verifier.VerifySystemHealth(toolResults);
        report.ToolWarnings.Should().Contain(w => w.Contains("GetCurrentTime") && w.Contains("为空"));
    }

    [Fact]
    public void VerifySystemHealth_FailedTool_Detected()
    {
        var toolResults = new Dictionary<string, string> { ["CheckHazardCategory"] = "调用失败: 超时" };
        var report = _verifier.VerifySystemHealth(toolResults);
        report.ToolWarnings.Should().Contain(w => w.Contains("异常"));
    }

    [Fact]
    public void VerifySystemHealth_KnowledgeBaseMiss_GeneratesWarning()
    {
        var toolResults = new Dictionary<string, string> { ["CheckHazardCategory"] = "未在知识库中找到匹配的危化品" };
        var report = _verifier.VerifySystemHealth(toolResults);
        report.ToolWarnings.Should().Contain(w => w.Contains("未命中"));
    }

    [Fact]
    public void VerifySystemHealth_NullPlan_StillCountsCorrectly()
    {
        var toolResults = new Dictionary<string, string> { ["GetCurrentTime"] = "2024-01-01" };
        var report = _verifier.VerifySystemHealth(toolResults, null);
        report.ToolsPlanned.Should().Be(0, "null plan 时 plan 计数为 0");
        report.ToolsExecuted.Should().Be(1);
    }

    [Fact]
    public void VerifySystemHealth_CancelledTool_Detected()
    {
        var toolResults = new Dictionary<string, string> { ["CheckHazardCategory"] = "Operation cancelled by user" };
        var report = _verifier.VerifySystemHealth(toolResults);
        report.ToolsCancelled.Should().Be(1);
    }

    // ── BuildCorrectedPrompt 测试 ──

    [Fact]
    public void BuildCorrectedPrompt_ContainsAllSections()
    {
        var bizReport = new BusinessVerificationReport
        {
            Claims = { new ClaimVerification { ClaimedText = "GB15603-1995", FoundInSource = true, ClaimType = "国标" } },
            FactualPrecision = 1.0,
            RawConclusion = "合规"
        };
        var sysReport = new SystemHealthReport { ToolsPlanned = 2, ToolsExecuted = 2 };

        var prompt = _verifier.BuildCorrectedPrompt("查询", "原始结论", bizReport, sysReport);
        prompt.Should().Contain("代码核查报告");
        prompt.Should().Contain("原始结论");
        prompt.Should().Contain("合规判断");
        prompt.Should().Contain("法规依据");
        prompt.Should().Contain("违规点");
        prompt.Should().Contain("整改建议");
        prompt.Should().Contain("数据置信度");
    }

    [Fact]
    public void BuildCorrectedPrompt_WithHallucinations_IncludesDeleteInstruction()
    {
        var bizReport = new BusinessVerificationReport
        {
            Claims =
            {
                new ClaimVerification { ClaimedText = "GB15603-1995", FoundInSource = true, ClaimType = "国标" },
                new ClaimVerification { ClaimedText = "GB99999-0000", FoundInSource = false, ClaimType = "国标" }
            },
            FactualPrecision = 0.5,
            HallucinatedClaims = { "GB99999-0000" }
        };
        var sysReport = new SystemHealthReport();
        var prompt = _verifier.BuildCorrectedPrompt("查询", "结论", bizReport, sysReport);
        prompt.Should().Contain("✗");
    }

    // ── 模型测试 ──

    [Fact]
    public void ClaimVerification_ToString_Found_ShowsCheckmark()
    {
        var claim = new ClaimVerification { ClaimedText = "GB15603-1995", FoundInSource = true, EvidenceSnippet = "4.2.2条" };
        claim.ToString().Should().Contain("✓");
        claim.ToString().Should().Contain("4.2.2条");
    }

    [Fact]
    public void ClaimVerification_ToString_NotFound_ShowsWarning()
    {
        var claim = new ClaimVerification { ClaimedText = "GB99999", FoundInSource = false };
        claim.ToString().Should().Contain("✗");
        claim.ToString().Should().Contain("幻觉");
    }

    [Fact]
    public void BusinessVerificationReport_ToMarkdown_ContainsSummary()
    {
        var report = new BusinessVerificationReport
        {
            Claims = { new ClaimVerification { ClaimedText = "GB15603", FoundInSource = true, ClaimType = "国标" } },
            FactualPrecision = 1.0
        };
        var md = report.ToMarkdown();
        md.Should().Contain("事实核查报告");
        md.Should().Contain("事实精度");
        md.Should().Contain("100.0%");
    }

    [Fact]
    public void SystemHealthReport_ToMarkdown_CompleteChain_ReportsHealthy()
    {
        var report = new SystemHealthReport { ToolsPlanned = 2, ToolsExecuted = 2 };
        report.ToMarkdown().Should().Contain("工具链完整");
    }

    [Fact]
    public void SystemHealthReport_ToMarkdown_Cancelled_ShowsWarning()
    {
        var report = new SystemHealthReport { ToolsPlanned = 3, ToolsExecuted = 1, ToolsCancelled = 2 };
        report.ToMarkdown().Should().Contain("⚠️");
    }

    // StringExtensions.Truncate is internal, not accessible from tests
}

// ═══════════════════════════════════════════
// ParseToolCalls 测试 — 模型工具调用解析（CoT/RAG 共享逻辑）
// ═══════════════════════════════════════════
public class ParseToolCallsTests
{
    // CoT.ParseToolCalls
    [Fact]
    public void CoT_ParseToolCalls_TOOLSPrefix_ExtractsTools()
    {
        var output = @"思考过程...
TOOLS:CheckHazardCategory,CheckStorageCompatibility";
        var result = InvokeCoTParseToolCalls(output);
        result.Should().Contain("CheckHazardCategory");
        result.Should().Contain("CheckStorageCompatibility");
        result.Should().HaveCount(2);
    }

    [Fact]
    public void CoT_ParseToolCalls_ChineseToolPrefix_ExtractsTools()
    {
        var output = @"分析完成
【工具调用】:CheckHazardCategory";
        var result = InvokeCoTParseToolCalls(output);
        result.Should().Contain("CheckHazardCategory");
    }

    [Fact]
    public void CoT_ParseToolCalls_MultiplePrefixes_PicksLast()
    {
        var output = @"TOOLS:GetCurrentTime
更多分析...
TOOLS:CheckStorageCompatibility";
        var result = InvokeCoTParseToolCalls(output);
        result.Should().Contain("CheckStorageCompatibility");
        result.Should().NotContain("GetCurrentTime", "取最后一个 TOOLS: 前缀");
    }

    [Fact]
    public void CoT_ParseToolCalls_NoToolsMarked_ReturnsEmpty()
    {
        var output = "不需要调用任何工具";
        var result = InvokeCoTParseToolCalls(output);
        result.Should().BeEmpty();
    }

    [Fact]
    public void CoT_ParseToolCalls_FallbackScanTail_DetectsToolNames()
    {
        // 末尾扫描：末 200 字符中检测已知工具名
        var output = new string('x', 200) + "CheckHazardCategory";
        var result = InvokeCoTParseToolCalls(output);
        result.Should().Contain("CheckHazardCategory", "末尾扫描应检测到工具名");
    }

    [Fact]
    public void CoT_ParseToolCalls_FiltersEmptyAndNone()
    {
        var output = "TOOLS:无";
        var result = InvokeCoTParseToolCalls(output);
        result.Should().BeEmpty("无字应被过滤");
    }

    // RAG.ParseToolCalls
    [Fact]
    public void RAG_ParseToolCalls_MarkerPrefix_ExtractsTools()
    {
        var output = @"分析完成
需要调用的工具：CheckHazardCategory,CheckStorageCompatibility";
        var result = InvokeRagParseToolCalls(output);
        result.Should().Contain("CheckHazardCategory");
        result.Should().Contain("CheckStorageCompatibility");
    }

    [Fact]
    public void RAG_ParseToolCalls_ExplicitNone_ReturnsEmpty()
    {
        var output = "需要调用的工具：无";
        var result = InvokeRagParseToolCalls(output);
        result.Should().BeEmpty();
    }

    [Fact]
    public void RAG_ParseToolCalls_NoMarker_ReturnsEmpty()
    {
        var output = "这是一个普通的回答，沒有任何工具调用标记";
        var result = InvokeRagParseToolCalls(output);
        result.Should().BeEmpty();
    }

    [Fact]
    public void RAG_ParseToolCalls_NullInput_ReturnsEmpty()
    {
        var result = InvokeRagParseToolCalls(null!);
        result.Should().BeEmpty();
    }

    [Fact]
    public void RAG_ParseToolCalls_OnlyFirstLine_Detected()
    {
        var output = @"需要调用的工具：CheckHazardCategory
后面还有其他需要调用的工具：CheckStorageCompatibility";
        var result = InvokeRagParseToolCalls(output);
        result.Should().Contain("CheckHazardCategory");
        result.Should().NotContain("CheckStorageCompatibility", "只取标记行第一行");
    }

    [Fact]
    public void RAG_ParseToolCalls_DeduplicatesTools()
    {
        var output = "需要调用的工具：CheckHazardCategory,CheckHazardCategory,GetCurrentTime";
        var result = InvokeRagParseToolCalls(output);
        result.Should().HaveCount(2, "去重后只有两个不同工具");
    }

    // 反射辅助方法
    private static string[] InvokeCoTParseToolCalls(string output)
    {
        var cotType = typeof(CoT);
        var method = cotType.GetMethod("ParseToolCalls",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (method == null) return Array.Empty<string>();

        // CoT 构造函数需要多个依赖，用 FormatterServices 创建未初始化实例
        var cot = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(cotType);
        return (string[])method.Invoke(cot, new object[] { output })!;
    }

    private static string[] InvokeRagParseToolCalls(string? output)
    {
        var ragType = typeof(RAG);
        var method = ragType.GetMethod("ParseToolCalls",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (method == null) return Array.Empty<string>();

        var rag = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(ragType);
        return (string[])method.Invoke(rag, new object[] { output! })!;
    }
}

// ═══════════════════════════════════════════
// RAG 实体提取方法测试 — 从用户输入中提取物质名称/设施类型
// ═══════════════════════════════════════════
public class RagEntityExtractionTests
{
    [Theory]
    [InlineData("苯属于什么危险类别", "苯")]
    [InlineData("甲醇是什么类别", "甲醇")]
    [InlineData("乙醇的危化品", "乙醇")]
    [InlineData("甲苯是什么", "甲苯")]
    [InlineData("氧化剂是什么东西", "氧化剂东西")]  // "是什么"移除后留下"东西"
    [InlineData("苯能不能和丙酮一起储存", "苯和丙酮一起储存")] // "能不能"被移除
    public void ExtractSubstance_RemovesQuestionWords(string input, string expected)
    {
        var result = RAG.ExtractSubstanceStatic(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("苯和丙酮能同库储存吗", "苯", "丙酮能同库储存")]
    [InlineData("甲醇与乙醇储存", "甲醇", "乙醇储存")]
    [InlineData("硝酸、硫酸反应", "硝酸", "硫酸反应")]
    public void ExtractTwoSubstances_SplitsBySeparator(string input, string expectedA, string expectedB)
    {
        var (a, b) = RAG.ExtractTwoSubstancesStatic(input);
        a.Should().Be(expectedA);
        b.Should().Be(expectedB);
    }

    [Fact]
    public void ExtractTwoSubstances_SingleSubstance_ReturnsSingle()
    {
        var (a, b) = RAG.ExtractTwoSubstancesStatic("苯是什么危险类别");
        a.Should().NotBeNullOrEmpty();
        b.Should().BeEmpty();
    }

    [Theory]
    [InlineData("液化烃储罐安全距离", "液化烃储罐-储罐")]
    [InlineData("甲类仓库与明火点的安全距离", "甲类仓库-明火点")]
    [InlineData("甲类仓库与建筑间距", "甲类仓库-建筑")]
    [InlineData("储罐与建筑间距", "储罐-建筑")]
    [InlineData("储罐到消防通道距离", "储罐-消防通道")]
    [InlineData("储罐到厂区边界", "储罐-厂区边界")]
    [InlineData("储罐间距", "储罐-储罐")]
    [InlineData("任意查询", "任意查询")] // 兜底
    public void ExtractFacilityType_MapsKeywordToKey(string input, string expected)
    {
        var result = RAG.ExtractFacilityTypeStatic(input);
        result.Should().Be(expected);
    }
}

// ═══════════════════════════════════════════
// ChemicalRAG 测试 — 分块 + 优先级重排序
// ═══════════════════════════════════════════
public class ChemicalRagTests
{
    public ChemicalRagTests()
    {
        try { AppConfig.Instance.ToString(); } catch
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Llm:ModelId"] = "test-model",
                    ["Llm:Endpoint"] = "http://localhost:11434",
                    ["Database:Host"] = "localhost",
                    ["Database:Port"] = "5432",
                    ["Database:DatabaseName"] = "testdb",
                    ["Database:Password"] = "pwd",
                    ["VectorSearch:EmbeddingModelId"] = "test-embed",
                    ["PromptTemplates:SystemRole"] = "test",
                    ["PromptTemplates:EvalFastPrompt"] = "test {SystemRole} {UserInput}",
                    ["PromptTemplates:EvalFastQueryPrompt"] = "test {SystemRole} {UserInput}"
                }).Build();
            AppConfig.Load(config);
        }
    }

    // SplitTextIntoChunks 是 private 方法，通过反射调用
    [Fact]
    public void SplitTextIntoChunks_SingleParagraph_FitsInOneChunk()
    {
        var text = "这是一个短文本段落。";
        var chunks = InvokeSplitTextIntoChunks(text, 500);
        chunks.Should().HaveCount(1);
        chunks[0].Should().Contain("短文本");
    }

    [Fact]
    public void SplitTextIntoChunks_MultipleParagraphs_SplitsBySize()
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 50; i++)
            sb.AppendLine($"这是第{i}个较长的段落，包含足够多的文本内容来触发分块机制。");
        var text = sb.ToString();

        var chunks = InvokeSplitTextIntoChunks(text, 500);
        chunks.Should().NotBeEmpty();
        chunks.Count.Should().BeGreaterThan(1, "多段落长文本应被分块");
    }

    [Fact]
    public void SplitTextIntoChunks_EmptyText_ReturnsEmpty()
    {
        var chunks = InvokeSplitTextIntoChunks("", 500);
        chunks.Should().BeEmpty();
    }

    [Fact]
    public void SplitTextIntoChunks_WhitespaceOnly_ReturnsEmpty()
    {
        var chunks = InvokeSplitTextIntoChunks("   \r\n  \n  ", 500);
        chunks.Should().BeEmpty();
    }

    private static List<string> InvokeSplitTextIntoChunks(string text, int maxChunkSize)
    {
        var type = typeof(ChemicalRAG);
        var method = type.GetMethod("SplitTextIntoChunks",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (method == null) return new List<string>();

        var rag = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);
        return (List<string>)method.Invoke(rag, new object[] { text, maxChunkSize })!;
    }
}

// ═══════════════════════════════════════════
// P0-3: IntegrationService 测试 — 显式降级，禁止静默返回空数据。
// 所有未接入方法必须抛出 NotSupportedException 并包含系统名称提示。
// ═══════════════════════════════════════════
public class IntegrationServiceTests
{
    private readonly IntegrationService _service = new();

    [Fact]
    public async Task GetWarehouseRecords_ShouldThrowNotSupported()
    {
        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => _service.GetWarehouseRecordsAsync());
        ex.Message.Should().Contain("ERP");
        ex.Message.Should().Contain("仓储");
    }

    [Fact]
    public async Task GetWarehouseRecords_WithChemicalName_ShouldThrowNotSupported()
    {
        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => _service.GetWarehouseRecordsAsync("苯"));
        ex.Message.Should().Contain("ERP");
    }

    [Fact]
    public async Task GetEHSTickets_ShouldThrowNotSupported()
    {
        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => _service.GetEHSTicketsAsync());
        ex.Message.Should().Contain("EHS");
        ex.Message.Should().Contain("工单");
    }

    [Fact]
    public async Task GetEHSTickets_WithFilter_ShouldThrowNotSupported()
    {
        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => _service.GetEHSTicketsAsync(true));
        ex.Message.Should().Contain("EHS");
    }

    [Fact]
    public async Task SyncMethods_ShouldThrowNotSupported()
    {
        (await Assert.ThrowsAsync<NotSupportedException>(
            () => _service.SyncERPDataAsync())).Message.Should().Contain("ERP");

        (await Assert.ThrowsAsync<NotSupportedException>(
            () => _service.SyncWMSDataAsync())).Message.Should().Contain("WMS");

        (await Assert.ThrowsAsync<NotSupportedException>(
            () => _service.SyncEHSDataAsync())).Message.Should().Contain("EHS");
    }

    [Fact]
    public void Implements_IIntegrationService()
    {
        _service.Should().BeAssignableTo<IIntegrationService>();
    }

    [Fact]
    public async Task AllMethods_MessageShouldContainActionableGuidance()
    {
        // P0-3: 验证所有 5 个方法各自抛出正确的异常消息
        var ex1 = await Assert.ThrowsAsync<NotSupportedException>(
            () => _service.GetWarehouseRecordsAsync());
        ex1.Message.Should().Contain("ERP");

        var ex2 = await Assert.ThrowsAsync<NotSupportedException>(
            () => _service.GetEHSTicketsAsync());
        ex2.Message.Should().Contain("EHS");

        var ex3 = await Assert.ThrowsAsync<NotSupportedException>(
            () => _service.SyncWMSDataAsync());
        ex3.Message.Should().Contain("WMS");

        // 所有异常都应包含可操作指引
        ex1.Message.Should().Contain("请联系管理员");
        ex2.Message.Should().Contain("请联系管理员");
        ex3.Message.Should().Contain("请联系管理员");
    }
}

// ═══════════════════════════════════════════
// AgentDialog 预处理 + 格式化测试
// ═══════════════════════════════════════════
public class AgentDialogUnitTests
{
    public AgentDialogUnitTests()
    {
        try { AppConfig.Instance.ToString(); } catch
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Llm:ModelId"] = "test-model",
                    ["Llm:Endpoint"] = "http://localhost:11434",
                    ["Database:Host"] = "localhost",
                    ["Database:Port"] = "5432",
                    ["Database:DatabaseName"] = "testdb",
                    ["Database:Password"] = "pwd",
                    ["VectorSearch:EmbeddingModelId"] = "test-embed",
                    ["PromptTemplates:SystemRole"] = "test",
                    ["PromptTemplates:EvalFastPrompt"] = "test {SystemRole} {UserInput}",
                    ["PromptTemplates:EvalFastQueryPrompt"] = "test {SystemRole} {UserInput}"
                }).Build();
            AppConfig.Load(config);
        }
    }

    [Fact]
    public async Task Preprocess_TrimsInput()
    {
        var result = await InvokePreprocessAsync("  苯和丙酮安全吗  ");
        result.Should().Be("苯和丙酮安全吗");
    }

    [Fact]
    public async Task Preprocess_EmptyString_ReturnsEmpty()
    {
        var result = await InvokePreprocessAsync("");
        result.Should().Be("");
    }

    [Fact]
    public void FormatOutput_ReturnsInputUnchanged()
    {
        var result = InvokeFormatOutput("测试输出");
        result.Should().Be("测试输出");
    }

    private static async Task<string> InvokePreprocessAsync(string input)
    {
        var type = typeof(AgentDialog);
        var method = type.GetMethod("PreprocessAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (method == null) return "";

        var dialog = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);
        var task = (Task<string>)method.Invoke(dialog, new object[] { input })!;
        return await task;
    }

    private static string InvokeFormatOutput(string result)
    {
        var type = typeof(AgentDialog);
        var method = type.GetMethod("FormatOutput",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (method == null) return "";

        var dialog = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);
        return (string)method.Invoke(dialog, new object[] { result })!;
    }
}

// ═══════════════════════════════════════════
// MultimodalService 测试 — 结构测试（不依赖真实模型）
// ═══════════════════════════════════════════
public class MultimodalServiceTests
{
    [Fact]
    public void Constructor_DoesNotThrow()
    {
        try { AppConfig.Instance.ToString(); } catch
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Llm:ModelId"] = "test-model",
                    ["Llm:Endpoint"] = "http://localhost:11434",
                    ["Database:Host"] = "localhost",
                    ["Database:Port"] = "5432",
                    ["Database:DatabaseName"] = "testdb",
                    ["Database:Password"] = "pwd",
                    ["VectorSearch:EmbeddingModelId"] = "test-embed",
                    ["PromptTemplates:SystemRole"] = "test",
                    ["PromptTemplates:EvalFastPrompt"] = "test {SystemRole} {UserInput}",
                    ["PromptTemplates:EvalFastQueryPrompt"] = "test {SystemRole} {UserInput}"
                }).Build();
            AppConfig.Load(config);
        }
        ModelConfig.Initialize(AppConfig.Instance);

        var action = () => new MultimodalService();
        action.Should().NotThrow();
    }

    [Fact]
    public async Task AnalyzeImage_MissingFile_ReturnsErrorString()
    {
        ModelConfig.Initialize(AppConfig.Instance);
        using var service = new MultimodalService();
        var result = await service.AnalyzeImageAsync("nonexistent.jpg", "test prompt");
        result.Should().Contain("不存在");
    }

    [Fact]
    public async Task AnalyzeHazardLabel_MissingFile_ReturnsError()
    {
        ModelConfig.Initialize(AppConfig.Instance);
        using var service = new MultimodalService();
        var result = await service.AnalyzeHazardLabelAsync("nonexistent.jpg");
        result.Should().Contain("不存在");
    }
}
