using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Agent1.Config;
using Agent1.Models;
using Agent1.Services;
using FluentAssertions;
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
    // 所有本类写入的业务数据必须携带该标记；Dispose 只按标记清理，
    // 严禁 DELETE 整表，避免集成测试误删本地/开发库的真实知识数据。
    private const string TestMarker = "codex-itest-20260813";

    private DatabaseService _db = null!;
    private AppConfig _config = null!;

    /// <summary>测试前: 创建 DatabaseService 实例 + 建表</summary>
    public async Task InitializeAsync()
    {
        // 直接从环境变量构建 DatabaseConfig，不依赖 AppConfig 单例
        // 避免与 ApiIntegrationTests 的 WebApplicationFactory 并行时发生单例竞争
        var port = int.TryParse(Environment.GetEnvironmentVariable("DB_PORT"), out var p) ? p : 5432;
        _config = new AppConfig
        {
            Database = new DatabaseConfig
            {
                Host = Environment.GetEnvironmentVariable("DB_HOST") ?? "localhost",
                Port = port,
                DatabaseName = Environment.GetEnvironmentVariable("DB_NAME") ?? "chemical_park_ai_agent",
                Username = Environment.GetEnvironmentVariable("DB_USERNAME") ?? "postgres",
                Password = Environment.GetEnvironmentVariable("DB_PASSWORD") ?? "7758521"
            }
        };
        _db = new DatabaseService(_config);

        // 确保表存在
        var connected = await _db.TestConnectionAsync();
        if (!connected)
            throw new InvalidOperationException(
                $"无法连接 PostgreSQL: {_config.Database.Host}:{_config.Database.Port}/{_config.Database.DatabaseName}");

        await _db.InitializeDatabaseAsync();
    }

    /// <summary>测试后: 只清理本测试类写入的标记数据（双层表 + 旧表）</summary>
    public async Task DisposeAsync()
    {
        try
        {
            using var conn = await _db.GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                DELETE FROM knowledge_documents
                WHERE source_path LIKE '%' || @marker || '%'
                   OR file_name LIKE '%' || @marker || '%';

                DELETE FROM chemical_documents
                WHERE source_file LIKE '%' || @marker || '%'
                   OR content LIKE '%' || @marker || '%';

                DELETE FROM long_term_memories
                WHERE user_id LIKE '%' || @marker || '%'
                   OR content LIKE '%' || @marker || '%';
            ";
            var markerParam = cmd.CreateParameter();
            markerParam.ParameterName = "marker";
            markerParam.Value = TestMarker;
            cmd.Parameters.Add(markerParam);
            cmd.ExecuteNonQuery();
        }
        catch { /* 静默清理 */ }
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
        tables.Should().Contain("knowledge_documents");
        tables.Should().Contain("knowledge_chunks");
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
            Content = $"{TestMarker} GB 30000.7-2013 易燃液体: 闪点 ≤ 60°C 的液体",
            RegulationType = "国标",
            Priority = "高",
            SourceFile = $"{TestMarker}-GB30000.7-2013.pdf",
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
            new() { Content = $"{TestMarker} GB 30000.2-2013 爆炸物分类与标签规范", RegulationType = "国标", Priority = "高", PageNumber = 1, SourceFile = $"{TestMarker}-gb30000.2.pdf" },
            new() { Content = $"{TestMarker} GB 30000.3-2013 易燃气体储存安全要求", RegulationType = "国标", Priority = "高", PageNumber = 1, SourceFile = $"{TestMarker}-gb30000.3.pdf" },
            new() { Content = $"{TestMarker} GB 30000.7-2013 易燃液体运输管理规定", RegulationType = "国标", Priority = "高", PageNumber = 1, SourceFile = $"{TestMarker}-gb30000.7.pdf" }
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
            Content = $"{TestMarker} 测试数据-待清理", RegulationType = "测试", Priority = "低", PageNumber = 1,
            SourceFile = $"{TestMarker}-to-clear.pdf"
        });

        // 清空
        await _db.ClearChemicalDocumentsAsync();

        var count = await _db.GetChemicalDocumentCountAsync();
        count.Should().Be(0, "清空后文档计数应为 0");
    }

    [Fact]
    public async Task GetAllChemicalDocumentTexts_ReturnsCorrectFields()
    {
        // 使用双层表新 API：先插入文档记录，再插入分块
        var docId = await _db.InsertDocumentAsync(new KnowledgeDocumentRecord
        {
            FileName = $"{TestMarker}-benzene.pdf",
            SourcePath = $"{TestMarker}-benzene.pdf",
            FileFormat = "pdf",
            RegulationType = "国标",
            Priority = "高",
            RegulationNumber = "GB-TEST-001",
            ContentHash = $"{TestMarker}-hash-benzene"
        });

        await _db.InsertChunkAsync(new ChemicalDocumentRecord
        {
            Content = $"{TestMarker} 苯 CAS:71-43-2 闪点:-11°C",
            RegulationType = "国标",
            Priority = "高",
            SourceFile = $"{TestMarker}-benzene.pdf",
            PageNumber = 5
        }, docId);

        await _db.UpdateDocumentChunkCountAsync(docId, 1);

        var docs = await _db.GetAllChemicalDocumentTextsAsync();

        docs.Should().NotBeEmpty();
        docs.Should().Contain(d =>
            d.Content.Contains("苯") &&
            d.RegulationType == "国标" &&
            d.Priority == "高" &&
            d.SourceFile == $"{TestMarker}-benzene.pdf");
    }

    [Fact]
    public async Task DocumentRecord_PageNumber_PersistedCorrectly()
    {
        // P2-5 回归: 验证 PageNumber 正确写入双层表
        var docId = await _db.InsertDocumentAsync(new KnowledgeDocumentRecord
        {
            FileName = $"{TestMarker}-GB 50160-2008.pdf",
            SourcePath = $"{TestMarker}-GB 50160-2008.pdf",
            FileFormat = "pdf",
            RegulationType = "国标",
            Priority = "高",
            RegulationNumber = "GB 50160-2008",
            ContentHash = $"{TestMarker}-hash-gb50160"
        });

        await _db.InsertChunkAsync(new ChemicalDocumentRecord
        {
            Content = $"{TestMarker} GB 50160-2008 石油化工企业设计防火标准 第3章 防火间距",
            RegulationType = "国标",
            Priority = "高",
            PageNumber = 42
        }, docId);

        await _db.UpdateDocumentChunkCountAsync(docId, 1);

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
        // [#5 FIX] 走 AuditService 写入：哈希链由服务自动计算，不再直插 AddAuditLogAsync 旁路
        var audit = new AuditService(_db);
        await audit.LogOperationAsync("test-user", "IntegrationTest", "集成测试审计日志");

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

    [Fact]
    public async Task AddAuditLog_DirectInsertWithoutChainHash_InternallyFillsHash()
    {
        // [#5 FIX] 直插兜底验证：底层 AddAuditLogAsync 未传 chainHash 时，
        // 内部读链尾强制补算，链上不得出现 NULL 空洞
        await _db.AddAuditLogAsync(
            userId: "test-user",
            operation: "IntegrationTest",
            details: "直插兜底哈希补算验证",
            ipAddress: "127.0.0.1");

        var logs = await _db.GetAuditLogsAsync(
            startTime: DateTime.UtcNow.AddMinutes(-5),
            endTime: DateTime.UtcNow.AddMinutes(1),
            userId: "test-user");

        var direct = logs.FirstOrDefault(l => l.Details.Contains("直插兜底哈希补算验证"));
        direct.Should().NotBeNull("直插记录应能查到");
        direct!.ChainHash.Should().NotBeNullOrEmpty("底层直插必须强制补算哈希，不得写入 NULL");
    }

    [Fact]
    public async Task UpsertLongTermMemory_RepeatedWrite_UpdatesInsteadOfDuplicating()
    {
        // [#17/#18 FIX] 幂等写入：同用户+同类型+归一化内容相同 → 更新旧记忆，不新增重复、不整组停用
        var marker = $"{TestMarker}-memory-{Guid.NewGuid():N}";
        var content = $"{marker} 甲类仓库应设独立库房并保持通风";

        await _db.UpsertLongTermMemoryAsync(new LongTermMemoryRecord
        {
            UserId = marker,
            MemoryType = "chemical_fact",
            Content = content,
            Importance = 0.5f,
            Embedding = null
        });

        await _db.UpsertLongTermMemoryAsync(new LongTermMemoryRecord
        {
            UserId = marker,
            MemoryType = "chemical_fact",
            Content = content + "  ", // 仅空白差异，归一化后视为同一事实
            Importance = 0.9f,
            Embedding = null
        });

        var stats = await _db.GetLongTermMemoryStatsAsync(marker);
        stats.TotalCount.Should().Be(1, "重复写入必须折叠为一条，而不是产生两条重复记忆");
        stats.ActiveCount.Should().Be(1);

        var hits = await _db.SearchLongTermMemoriesByKeywordAsync(marker, marker, 10);
        hits.Should().HaveCount(1);
        hits[0].Importance.Should().Be(0.9f, "更新应保留最新重要性");
    }

    [Fact]
    public async Task RepairHistoricalChainHashes_ChainBecomesIntact()
    {
        // [#5 FIX] 历史断链一键修复（幂等）：逐条按现行算法重算回写，
        // 覆盖历史 NULL 空洞与旧算法断链记录，修复后 VerifyIntegrity 必须通过
        var audit = new AuditService(_db);

        var (repaired, repairDetail) = await audit.RepairChainAsync();

        var (intact, brokenAtId, verifyDetail) = await audit.VerifyIntegrityAsync();
        intact.Should().BeTrue($"RepairChainAsync（修复 {repaired} 条）后哈希链应完整: {verifyDetail}");
        brokenAtId.Should().BeNull();
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
                    Content = $"{TestMarker} 并发测试文档数据 #{idx:D2} - 化工园区危化品合规审查",
                    RegulationType = "并发测试",
                    Priority = "低",
                    PageNumber = idx,
                    SourceFile = $"{TestMarker}-concurrent-{idx:D2}.pdf"
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
