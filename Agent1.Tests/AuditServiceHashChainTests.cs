using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Agent1.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// [P0 #3] 审计哈希链完整性测试。
///
/// 覆盖修复点：
/// - 参与哈希计算的时间与入库时间一致（UTC/微秒归一）→ 写入后可通过验证。
/// - 服务重启后从 DB 尾条恢复链头 → 跨实例新增记录仍连续，不断链。
/// - 篡改 detail 后能被检测到断裂位置。
///
/// 用内存 Mock&lt;IDatabaseService&gt; 模拟 DB：存储 createTime 原值并按 id 倒序返回尾条哈希，
/// 与真实 PostgreSQL 显式写入 created_at 的行为等价。
/// </summary>
public class AuditServiceHashChainTests
{
    /// <summary>构建一个带内存存储的 IDatabaseService Mock，模拟审计日志持久化。</summary>
    private static Mock<IDatabaseService> BuildInMemoryDb(List<AuditLog> store)
    {
        var db = new Mock<IDatabaseService>();
        long idSeq = 0;

        db.Setup(x => x.AddAuditLogAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<DateTime?>()))
            .Returns((string u, string op, string d, string? ip, string? ch, DateTime? ct) =>
            {
                store.Add(new AuditLog
                {
                    Id = ++idSeq,
                    UserId = u,
                    Operation = op,
                    Details = d,
                    ChainHash = ch,
                    CreateTime = ct ?? DateTime.UtcNow
                });
                return Task.CompletedTask;
            });

        db.Setup(x => x.GetAuditLogsAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<string?>()))
            .Returns(() => Task.FromResult(store.OrderByDescending(l => l.CreateTime).ToList()));

        db.Setup(x => x.GetLastAuditChainHashAsync())
            .Returns(() => Task.FromResult(store.OrderByDescending(l => l.Id).FirstOrDefault()?.ChainHash));

        return db;
    }

    [Fact]
    public async Task WriteThenVerify_ChainIsIntact()
    {
        var store = new List<AuditLog>();
        var db = BuildInMemoryDb(store);
        var audit = new AuditService(db.Object);

        await audit.LogOperationAsync("admin", "合规审核", "查询A");
        await audit.LogOperationAsync("admin", "合规审核", "查询B");
        await audit.LogOperationAsync("auditor", "法规审计", "导出C");

        var (intact, brokenAtId, detail) = await audit.VerifyIntegrityAsync();

        intact.Should().BeTrue(detail);
        brokenAtId.Should().BeNull();
        store.Should().HaveCount(3);
    }

    [Fact]
    public async Task RestartRecoversChainHead_NewRecordsStayLinked()
    {
        var store = new List<AuditLog>();
        var db = BuildInMemoryDb(store);

        // 第一次运行：写入两条
        var first = new AuditService(db.Object);
        await first.LogOperationAsync("admin", "合规审核", "查询A");
        await first.LogOperationAsync("admin", "合规审核", "查询B");

        // 模拟服务重启：新实例共享同一 DB，_lastChainHash 应从 DB 尾条恢复
        var second = new AuditService(db.Object);
        await second.LogOperationAsync("admin", "合规审核", "查询C");

        var (intact, brokenAtId, detail) = await second.VerifyIntegrityAsync();

        intact.Should().BeTrue("重启后应从 DB 恢复链头，新记录不应链接到 GENESIS 而断链: " + detail);
        brokenAtId.Should().BeNull();
        store.Should().HaveCount(3);
    }

    [Fact]
    public async Task TamperedDetail_IsDetected()
    {
        var store = new List<AuditLog>();
        var db = BuildInMemoryDb(store);
        var audit = new AuditService(db.Object);

        await audit.LogOperationAsync("admin", "合规审核", "查询A");
        await audit.LogOperationAsync("admin", "合规审核", "查询B");

        // 篡改第一条的详情，哈希不再匹配
        store[0].Details = "被篡改的内容";

        var (intact, brokenAtId, _) = await audit.VerifyIntegrityAsync();

        intact.Should().BeFalse();
        brokenAtId.Should().Be(store[0].Id);
    }

    [Fact]
    public async Task CreatedAtParticipatesInHash_MatchesStoredTime()
    {
        var store = new List<AuditLog>();
        var db = BuildInMemoryDb(store);
        var audit = new AuditService(db.Object);

        await audit.LogOperationAsync("admin", "合规审核", "查询A");

        // 入库 created_at 为 UTC 且截断到微秒（tick 为 10 的倍数）
        var stored = store.Single();
        stored.CreateTime.Kind.Should().Be(DateTimeKind.Utc);
        (stored.CreateTime.Ticks % 10).Should().Be(0);

        // 验证通过说明参与哈希的时间与入库时间一致
        var (intact, _, detail) = await audit.VerifyIntegrityAsync();
        intact.Should().BeTrue(detail);
    }
}
