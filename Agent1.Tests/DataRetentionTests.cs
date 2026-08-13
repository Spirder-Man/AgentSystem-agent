using System;
using System.Linq;
using System.Threading.Tasks;
using Agent1.Config;
using Agent1.Models;
using Agent1.Services;
using FluentAssertions;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// 数据留存断言测试（测试第六维度）—— 2026-08-11 数据丢失事件教训。
///
/// 背景：2026-07-30 真实 E2E 评测写入 PostgreSQL 的数据，8-3 被外部清库操作删除，
/// 五维度测试全部通过却无人察觉 —— 因为既有测试只断言"API 返回值"，
/// 从未断言"数据在数据库中真实留存"。测试期间数据存在、测试后无人查库，丢失无感知。
///
/// 本测试模拟 E2E 结束后的"独立视角查库"：
///   阶段1：以被测系统身份写入业务数据（审计日志 / 知识文档）；
///   阶段2：用全新连接 + 全新服务实例（模拟另一个进程/重启后）从磁盘读取，
///          断言写入的行真实落盘。任何依赖内存缓存、Stub、同连接可见性的
///          实现都会在此暴露。
///
/// 运行方式: dotnet test --filter "Category=Integration"
/// 前提条件: PostgreSQL 16 + pgvector 已运行、init_database.sql 已建表、
///           环境变量 DB_HOST / DB_PORT / DB_NAME / DB_USERNAME / DB_PASSWORD
/// </summary>
[Trait("Category", "Integration")]
public class DataRetentionTests : IAsyncLifetime
{
    private const string RetentionMarker = "retention-check-20260811";

    private DatabaseService _writer = null!; // 模拟 E2E 中被测系统（写方）
    private AppConfig _config = null!;

    /// <summary>测试前：连接真实 PostgreSQL 并确保表存在</summary>
    public async Task InitializeAsync()
    {
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
        _writer = new DatabaseService(_config);

        var connected = await _writer.TestConnectionAsync();
        if (!connected)
            throw new InvalidOperationException(
                $"无法连接 PostgreSQL: {_config.Database.Host}:{_config.Database.Port}/{_config.Database.DatabaseName}");

        await _writer.InitializeDatabaseAsync();
    }

    /// <summary>测试后：仅清理本测试写入的留存数据（按标记删除，不误删业务数据）</summary>
    public async Task DisposeAsync()
    {
        try
        {
            using var conn = await _writer.GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                $"DELETE FROM knowledge_chunks WHERE content LIKE '%{RetentionMarker}%';" +
                $"DELETE FROM knowledge_documents WHERE source_path LIKE '%{RetentionMarker}%';" +
                $"DELETE FROM audit_logs WHERE details LIKE '%{RetentionMarker}%';";
            cmd.ExecuteNonQuery();
        }
        catch { /* 静默清理 */ }
    }

    // ═══════════════════════════════════════
    // 第六维度：业务流水表（audit_logs）留存断言
    // ═══════════════════════════════════════

    [Fact]
    public async Task E2E_WriteAuditLog_RowVisibleToIndependentReader()
    {
        // 阶段1：模拟 E2E 中被测系统写入审计日志（[#5 FIX] 走 AuditService，哈希链由服务自动计算）
        var audit = new AuditService(_writer);
        await audit.LogOperationAsync(
            userId: "retention-user",
            operation: "E2E_Retention_Check",
            details: $"E2E 数据留存断言 {RetentionMarker}");

        // 阶段2：模拟 E2E 结束后独立进程查库（全新实例 = 全新连接，只能读磁盘）
        var reader = new DatabaseService(_config);
        var logs = await reader.GetAuditLogsAsync(
            startTime: DateTime.UtcNow.AddMinutes(-5),
            endTime: DateTime.UtcNow.AddMinutes(1),
            userId: "retention-user");

        logs.Should().NotBeEmpty("E2E 写入的审计日志必须在数据库中留存（独立连接可查）");
        logs.Should().Contain(l =>
            l.Details.Contains(RetentionMarker) &&
            l.Operation == "E2E_Retention_Check");
    }

    // ═══════════════════════════════════════
    // 第六维度：知识数据表（knowledge_documents / knowledge_chunks）留存断言
    // ═══════════════════════════════════════

    [Fact]
    public async Task E2E_WriteKnowledgeDocument_RowVisibleToIndependentReader()
    {
        // 阶段1：模拟 E2E 中知识管道写入（文档 + 分块）
        var docId = await _writer.InsertDocumentAsync(new KnowledgeDocumentRecord
        {
            FileName = $"retention-{RetentionMarker}.pdf",
            SourcePath = $"retention-{RetentionMarker}.pdf",
            FileFormat = "pdf",
            RegulationType = "国标",
            Priority = "高",
            ContentHash = $"hash-{RetentionMarker}"
        });

        await _writer.InsertChunkAsync(new ChemicalDocumentRecord
        {
            Content = $"数据留存断言分块 {RetentionMarker}",
            RegulationType = "国标",
            Priority = "高",
            SourceFile = $"retention-{RetentionMarker}.pdf",
            PageNumber = 1
        }, docId);

        await _writer.UpdateDocumentChunkCountAsync(docId, 1);

        // 阶段2：模拟 E2E 后独立进程查库
        var reader = new DatabaseService(_config);
        var docs = await reader.GetAllChemicalDocumentTextsAsync();

        docs.Should().Contain(d =>
            d.SourceFile == $"retention-{RetentionMarker}.pdf" &&
            d.Content.Contains(RetentionMarker),
            "E2E 写入的知识文档必须在数据库中留存（独立连接可查）");
    }
}
