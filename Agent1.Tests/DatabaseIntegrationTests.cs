using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Agent1.Config;
using Agent1.Models;
using Agent1.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// 数据库集成测试 — 连接真实 PostgreSQL + pgvector 验证全链路 CRUD。
/// 
/// 运行方式:
///   本地:  设置环境变量后 dotnet test --filter "Category=Integration"
///   CI:    GitHub Actions 自动启动 pgvector/pgvector:pg16 服务容器
/// 
/// 前提条件:
///   - PostgreSQL 16 + pgvector 扩展已运行
///   - init_database.sql 已执行建表
///   - 环境变量: DB_HOST / DB_PORT / DB_NAME / DB_USERNAME / DB_PASSWORD
/// </summary>
[Trait("Category", "Integration")]
public class DatabaseIntegrationTests : IAsyncLifetime
{
    private DatabaseService _db = null!;
    private AppConfig _config = null!;

    /// <summary>测试前: 创建 DatabaseService 实例 + 建表</summary>
    public async Task InitializeAsync()
    {
        // 从环境变量构建最小配置
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Host"] = Environment.GetEnvironmentVariable("DB_HOST") ?? "localhost",
                ["Database:Port"] = Environment.GetEnvironmentVariable("DB_PORT") ?? "5432",
                ["Database:DatabaseName"] = Environment.GetEnvironmentVariable("DB_NAME") ?? "chemical_park_ai_agent",
                ["Database:Username"] = Environment.GetEnvironmentVariable("DB_USERNAME") ?? "postgres",
                ["Database:Password"] = Environment.GetEnvironmentVariable("DB_PASSWORD") ?? "7758521",
                // 以下为 AppConfig.Validate() 必需字段（集成测试不关心具体值）
                ["Llm:ModelId"] = "test-model",
                ["Llm:Endpoint"] = "http://localhost:11434",
                ["VectorSearch:EmbeddingModelId"] = "test-embed",
                ["PromptTemplates:SystemRole"] = "test",
                ["PromptTemplates:EvalFastPrompt"] = "test {SystemRole} {UserInput}",
                ["PromptTemplates:EvalFastQueryPrompt"] = "test {SystemRole} {UserInput}"
            })
            .Build();

        try { AppConfig.Load(configuration); }
        catch (InvalidOperationException) { /* 已被其他测试初始化 */ }

        _config = AppConfig.Instance;
        _db = new DatabaseService(_config);

        // 确保表存在
        var connected = await _db.TestConnectionAsync();
        if (!connected)
            throw new InvalidOperationException(
                $"无法连接 PostgreSQL: {_config.Database.Host}:{_config.Database.Port}/{_config.Database.DatabaseName}");

        await _db.InitializeDatabaseAsync();
    }

    /// <summary>测试后: 清理测试数据</summary>
    public async Task DisposeAsync()
    {
        try { await _db.ClearChemicalDocumentsAsync(); } catch { /* 静默清理 */ }
    }

    // ═══════════════════════════════════════
    // 基础连接测试
    // ═══════════════════════════════════════

    [Fact]
    public async Task TestConnection_WhenPostgresRunning_ReturnsTrue()
    {
        var result = await _db.TestConnectionAsync();
        result.Should().BeTrue("PostgreSQL 应处于运行状态");
    }

    [Fact]
    public async Task GetDatabaseInfo_ReturnsValidInfo()
    {
        var info = await _db.GetDatabaseInfoAsync();

        info.Should().Contain("数据库连接信息");
        info.Should().Contain(_config.Database.DatabaseName);
    }

    [Fact]
    public async Task GetTableNames_AfterInit_ContainsCoreTables()
    {
        var tables = await _db.GetTableNamesAsync();

        tables.Should().Contain("sessions");
        tables.Should().Contain("audit_logs");
        tables.Should().Contain("chemical_documents");
        tables.Should().Contain("search_logs");
    }

    // ═══════════════════════════════════════
    // 文档 CRUD 测试
    // ═══════════════════════════════════════

    [Fact]
    public async Task AddChemicalDocument_IncreasesDocumentCount()
    {
        var before = await _db.GetChemicalDocumentCountAsync();

        var record = new ChemicalDocumentRecord
        {
            Content = "GB 30000.7-2013 易燃液体: 闪点 ≤ 60°C 的液体",
            RegulationType = "国标",
            Priority = "高",
            SourceFile = "GB30000.7-2013.pdf",
            PageNumber = 1,
            Embedding = null // 集成测试不测向量检索
        };
        await _db.AddChemicalDocumentAsync(record);

        var after = await _db.GetChemicalDocumentCountAsync();
        after.Should().Be(before + 1, "新增一条文档后计数应 +1");
    }

    [Fact]
    public async Task AddChemicalDocumentsBatch_AllPersisted()
    {
        var batch = new List<ChemicalDocumentRecord>
        {
            new() { Content = "GB 30000.2-2013 爆炸物分类与标签规范", RegulationType = "国标", Priority = "高", PageNumber = 1 },
            new() { Content = "GB 30000.3-2013 易燃气体储存安全要求", RegulationType = "国标", Priority = "高", PageNumber = 1 },
            new() { Content = "GB 30000.7-2013 易燃液体运输管理规定", RegulationType = "国标", Priority = "高", PageNumber = 1 }
        };

        await _db.AddChemicalDocumentsBatchAsync(batch);

        var count = await _db.GetChemicalDocumentCountAsync();
        count.Should().BeGreaterOrEqualTo(3, "批量入库 3 条后计数应 ≥ 3");
    }

    [Fact]
    public async Task ClearChemicalDocuments_ResetsCount()
    {
        // 先插入
        await _db.AddChemicalDocumentAsync(new ChemicalDocumentRecord
        {
            Content = "测试数据-待清理", RegulationType = "测试", Priority = "低", PageNumber = 1
        });

        // 清空
        await _db.ClearChemicalDocumentsAsync();

        var count = await _db.GetChemicalDocumentCountAsync();
        count.Should().Be(0, "清空后文档计数应为 0");
    }

    [Fact]
    public async Task GetAllChemicalDocumentTexts_ReturnsCorrectFields()
    {
        await _db.AddChemicalDocumentAsync(new ChemicalDocumentRecord
        {
            Content = "苯 CAS:71-43-2 闪点:-11°C",
            RegulationType = "国标",
            Priority = "高",
            SourceFile = "test-benzene.pdf",
            PageNumber = 5
        });

        var docs = await _db.GetAllChemicalDocumentTextsAsync();

        docs.Should().NotBeEmpty();
        docs.Should().Contain(d =>
            d.Content.Contains("苯") &&
            d.RegulationType == "国标" &&
            d.Priority == "高" &&
            d.SourceFile == "test-benzene.pdf");
    }

    [Fact]
    public async Task DocumentRecord_PageNumber_PersistedCorrectly()
    {
        // P2-5 回归: 验证 PageNumber 正确写入数据库
        var record = new ChemicalDocumentRecord
        {
            Content = "GB 50160-2008 石油化工企业设计防火标准 第3章 防火间距",
            RegulationType = "国标",
            Priority = "高",
            PageNumber = 42
        };
        await _db.AddChemicalDocumentAsync(record);

        var docs = await _db.GetAllChemicalDocumentsWithEmbeddingsAsync();
        var saved = docs.FirstOrDefault(d => d.Content.Contains("GB 50160"));

        saved.Should().NotBeNull("刚写入的文档应能查到");
        saved!.PageNumber.Should().Be(42, "PageNumber 应正确持久化");
    }

    // ═══════════════════════════════════════
    // 审计日志测试
    // ═══════════════════════════════════════

    [Fact]
    public async Task AddAuditLog_PersistsCorrectly()
    {
        await _db.AddAuditLogAsync(
            userId: "test-user",
            operation: "IntegrationTest",
            details: "集成测试审计日志",
            ipAddress: "127.0.0.1");

        var logs = await _db.GetAuditLogsAsync(
            startTime: DateTime.UtcNow.AddMinutes(-5),
            endTime: DateTime.UtcNow.AddMinutes(1),
            userId: "test-user");

        logs.Should().NotBeEmpty("刚写入的审计日志应能查到");
        logs.Should().Contain(l =>
            l.UserId == "test-user" &&
            l.Operation == "IntegrationTest" &&
            l.Details.Contains("集成测试审计日志"));
    }

    // ═══════════════════════════════════════
    // 并发安全测试
    // ═══════════════════════════════════════

    [Fact]
    public async Task ConcurrentDocumentInsert_AllPersisted()
    {
        var tasks = new Task[10];
        for (int i = 0; i < 10; i++)
        {
            var idx = i;
            tasks[i] = Task.Run(async () =>
            {
                await _db.AddChemicalDocumentAsync(new ChemicalDocumentRecord
                {
                    Content = $"并发测试文档数据 #{idx:D2} - 化工园区危化品合规审查",
                    RegulationType = "并发测试",
                    Priority = "低",
                    PageNumber = idx
                });
            });
        }
        await Task.WhenAll(tasks);

        var count = await _db.GetChemicalDocumentCountAsync();
        count.Should().BeGreaterOrEqualTo(10, "10 个并发写入应全部成功");
    }

    // ═══════════════════════════════════════
    // 知识库数量统计（与其他模块的集成验证）
    // ═══════════════════════════════════════

    [Fact]
    public async Task GetChemicalDocumentCount_InitiallyZero_AfterCleanup()
    {
        await _db.ClearChemicalDocumentsAsync();
        var count = await _db.GetChemicalDocumentCountAsync();
        count.Should().Be(0, "清空后应为 0");
    }
}
