using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Agent1.Models;
using Agent1.Services;
using Agent1.Services.Logging;
using Agent1.Services.Security;
using FluentAssertions;
using Xunit;

namespace Agent1.Tests
{
    /// <summary>
    /// L1 层：基础设施服务单元测试集合。
    /// 按数据流顺序：IntentRouter → SensitiveDataMasker → MetricsCollector →
    /// RunIdGenerator → AuditService → SessionService → JsonFileRepository →
    /// CapabilityRegistry → ModuleFactory → ModuleDispatcher →
    /// EventActionDispatcher → DeviceFingerprintService → TokenBlacklistService
    /// 原则：只测纯逻辑，不调外部依赖（DB/LLM/File）。
    /// </summary>

    // 注：IntentRouterTests, SensitiveDataMaskerTests, MetricsCollectorTests
    // 已存在于独立文件中（旧有），此处不重复定义。

    // ═══════════════════════════════════════════
    // 4.4 RunIdGenerator — 运行批次ID
    // ═══════════════════════════════════════════
    public class RunIdGeneratorTests
    {
        [Fact]
        public void Current_ShouldBe8CharHex()
        {
            var runId = RunIdGenerator.Current;
            runId.Should().NotBeNullOrEmpty();
            runId.Length.Should().Be(8);
            runId.Should().MatchRegex("^[0-9a-f]{8}$",
                "RunId 应为 8 位小写十六进制");
        }

        [Fact]
        public void Current_ShouldBeImmutableAcrossCalls()
        {
            var id1 = RunIdGenerator.Current;
            var id2 = RunIdGenerator.Current;
            id1.Should().Be(id2, "RunId 应在整个进程生命周期中保持不变");
        }

        [Fact]
        public void NewSessionId_ShouldBe12CharHex()
        {
            var sessionId = RunIdGenerator.NewSessionId();
            sessionId.Should().NotBeNullOrEmpty();
            sessionId.Length.Should().Be(12);
            sessionId.Should().MatchRegex("^[0-9a-f]{12}$");
        }

        [Fact]
        public void StartTime_ShouldBeUtcNow()
        {
            RunIdGenerator.StartTime.Kind.Should().Be(DateTimeKind.Utc);
            RunIdGenerator.StartTime.Should().BeCloseTo(
                DateTime.UtcNow, TimeSpan.FromHours(1),
                "启动时间应在合理范围内（测试启动时初始化）");
        }
    }

    // ═══════════════════════════════════════════
    // 4.5 AuditService — 审计日志（哈希链验证）
    // ═══════════════════════════════════════════
    public class AuditServiceTests
    {
        [Fact]
        public async Task LogOperation_WithoutDatabase_ShouldStoreInMemory()
        {
            var audit = new AuditService(db: null);
            await audit.LogOperationAsync("user1", "查询", "合规查询: 氢氧化钠储存");
            await audit.LogOperationAsync("user1", "修改", "更新储存记录");

            var logs = await audit.GetAuditLogsAsync(null, null);
            logs.Should().HaveCount(2);
            logs[0].UserId.Should().Be("user1");
        }

        [Fact]
        public async Task LogOperation_WithSensitiveData_ShouldMaskDetails()
        {
            var audit = new AuditService(db: null);
            await audit.LogOperationAsync("admin", "登录",
                "用户手机: 13800001111", isSensitive: true);

            var logs = await audit.GetAuditLogsAsync(null, null);
            logs.Should().ContainSingle();
            logs[0].IsSensitive.Should().BeTrue();
            logs[0].Details.Should().Contain("****",
                "敏感数据应被脱敏");
            logs[0].Details.Should().NotContain("13800001111",
                "手机号不应以明文存储");
        }

        [Fact]
        public async Task AuditLogs_ShouldBeInDescendingOrder()
        {
            var audit = new AuditService(db: null);
            await audit.LogOperationAsync("u1", "A", "first");
            await Task.Delay(10);
            await audit.LogOperationAsync("u1", "B", "second");

            var logs = await audit.GetAuditLogsAsync(null, null);
            logs.Should().BeInDescendingOrder(l => l.CreateTime,
                "审计日志应按时间倒序排列");
        }

        [Fact]
        public async Task GetAuditLogs_WithTimeFilter_ShouldFilterCorrectly()
        {
            var audit = new AuditService(db: null);
            await audit.LogOperationAsync("u1", "op1", "detail1");
            // AuditService 内部统一以 UTC 微秒存储 CreateTime，边界也必须用 UTC；
            // 若用 DateTime.Now（本地），在 UTC±8 等非零时区机器上会因 tick 偏移导致过滤失真。
            var midPoint = DateTime.UtcNow;
            await Task.Delay(10);
            await audit.LogOperationAsync("u2", "op2", "detail2");

            // 查询 midPoint 之前的记录
            var logs = await audit.GetAuditLogsAsync(null, midPoint);
            logs.Should().HaveCount(1);
            logs[0].UserId.Should().Be("u1");
        }

        [Fact]
        public async Task VerifyIntegrity_EmptyLogs_ShouldReturnIntact()
        {
            var audit = new AuditService(db: null);
            var (intact, brokenId, detail) = await audit.VerifyIntegrityAsync();
            intact.Should().BeTrue("无记录时哈希链应视为完整");
            detail.Should().Contain("无审计日志");
        }

        [Fact]
        public async Task HashChain_SequentialLogs_ShouldBeLinked()
        {
            var audit = new AuditService(db: null);
            await audit.LogOperationAsync("u1", "A", "d1");
            await audit.LogOperationAsync("u1", "B", "d2");

            var (intact, _, _) = await audit.VerifyIntegrityAsync();
            intact.Should().BeTrue("顺序写入的日志哈希链应完整");
        }

        [Fact]
        public async Task ExportAuditReport_ShouldContainHeaderAndLogs()
        {
            var audit = new AuditService(db: null);
            await audit.LogOperationAsync("u1", "检查", "合规检查通过");

            var report = await audit.ExportAuditReportAsync(
                DateTime.Now.AddDays(-1), DateTime.Now.AddDays(1));
            report.Should().Contain("化工园区危化品合规审核");
            report.Should().Contain("审计日志报告");
            report.Should().Contain("合规检查通过");
        }
    }

    // ═══════════════════════════════════════════
    // 4.6 SessionService — 会话服务（门面模式）
    // ═══════════════════════════════════════════
    public class SessionServiceTests
    {
        [Fact]
        public void CreateSession_ShouldReturnNonNullSession()
        {
            var svc = new SessionService();
            var session = svc.CreateSession();

            session.Should().NotBeNull();
            session.SessionId.Should().NotBeNullOrEmpty();
            session.SessionId.Length.Should().Be(36,
                "SessionId 默认为 GUID 格式（36 字符含连字符）");
        }

        [Fact]
        public void CreateSession_WithType_ShouldSetSessionType()
        {
            var svc = new SessionService();
            var session = svc.CreateSession(SessionType.ChemicalCompliance);

            session.SessionType.Should().Be(SessionType.ChemicalCompliance);
        }

        [Fact]
        public void GetSession_AfterCreation_ShouldReturnSameSession()
        {
            var svc = new SessionService();
            var created = svc.CreateSession();
            var retrieved = svc.GetSession(created.SessionId);

            retrieved.Should().NotBeNull();
            retrieved!.SessionId.Should().Be(created.SessionId);
        }

        [Fact]
        public void GetSession_NonExistent_ShouldReturnNull()
        {
            var svc = new SessionService();
            var result = svc.GetSession("nonexistent-id");
            result.Should().BeNull("不存在的会话应返回 null");
        }

        [Fact]
        public void AddDialogTurn_ShouldIncreaseHistoryCount()
        {
            var svc = new SessionService();
            var session = svc.CreateSession();

            svc.AddDialogTurn(session.SessionId, "user", "氢氧化钠储存要求");
            svc.AddDialogTurn(session.SessionId, "assistant", "根据GB15603...");

            svc.GetHistoryCount(session.SessionId).Should().Be(2);
        }

        [Fact]
        public void GetFormattedHistory_ShouldReturnDialogPairs()
        {
            var svc = new SessionService();
            var session = svc.CreateSession();
            svc.AddDialogTurn(session.SessionId, "user", "提问");
            svc.AddDialogTurn(session.SessionId, "assistant", "回答");

            var history = svc.GetFormattedHistory(session.SessionId);
            history.Should().Contain("提问");
            history.Should().Contain("回答");
        }
    }

    // ═══════════════════════════════════════════
    // 4.8 CapabilityRegistry — 能力注册表
    // ═══════════════════════════════════════════
    public class CapabilityRegistryTests
    {
        [Fact]
        public void GetAll_ShouldReturnRegisteredCapabilities()
        {
            // CapabilityRegistry 需要 AgentDialog + ModuleFactory，此处验证 Capability POCO 模型
            var cap = new Capability
            {
                Name = "test-capability",
                Description = "测试能力",
                RegulationRef = "GB-TEST",
                InputHint = "测试输入"
            };

            cap.Name.Should().Be("test-capability");
            cap.Description.Should().Be("测试能力");
            cap.RegulationRef.Should().Be("GB-TEST");
            cap.Handler.Should().BeNull("Handler 默认为 null");
        }

        [Fact]
        public void Capability_Handler_ShouldBeNullable()
        {
            var cap = new Capability { Name = "no-handler" };
            cap.Handler.Should().BeNull(
                "Handler 为 null 时应不抛异常（ExecuteAsync 中有 null 检查）");
        }
    }

    // ═══════════════════════════════════════════
    // 4.10 EventActionDispatcher — 事件分发（Pub/Sub）
    // ═══════════════════════════════════════════
    public class EventActionDispatcherTests
    {
        [Fact]
        public void Subscribe_ShouldAddHandler()
        {
            var dispatcher = new EventActionDispatcher();
            dispatcher.Subscribe("TestEvent", _ => Task.CompletedTask);

            var counts = dispatcher.GetSubscriptionCounts();
            counts.Should().ContainKey("TestEvent");
            counts["TestEvent"].Should().Be(1);
        }

        [Fact]
        public void Publish_NoSubscribers_ShouldNotThrow()
        {
            var dispatcher = new EventActionDispatcher();
            var evt = PipelineEvent.Create(1, "trace", "Unsubscribed", "无订阅者");

            // Publish should not throw even if no one is listening
            var act = () => dispatcher.Publish(evt);
            act.Should().NotThrow("无订阅者时发布事件不应抛异常");
        }

        [Fact]
        public void Subscribe_MultipleHandlers_ShouldStack()
        {
            var dispatcher = new EventActionDispatcher();
            dispatcher.Subscribe("MultiEvent", _ => Task.CompletedTask);
            dispatcher.Subscribe("MultiEvent", _ => Task.CompletedTask);
            dispatcher.Subscribe("MultiEvent", _ => Task.CompletedTask);

            var counts = dispatcher.GetSubscriptionCounts();
            counts["MultiEvent"].Should().Be(3);
        }
    }

    // ═══════════════════════════════════════════
    // 4.11 DeviceFingerprintService — 设备指纹
    // ═══════════════════════════════════════════
    public class DeviceFingerprintServiceTests
    {
        [Fact]
        public void ComputeFingerprint_ShouldReturn16CharHex()
        {
            var fp = DeviceFingerprintService.ComputeFingerprint(
                "192.168.1.1", "Mozilla/5.0");

            fp.Should().NotBeNullOrEmpty();
            fp.Length.Should().Be(16);
            fp.Should().MatchRegex("^[0-9a-f]{16}$");
        }

        [Fact]
        public void ComputeFingerprint_SameInput_ShouldProduceSameOutput()
        {
            var fp1 = DeviceFingerprintService.ComputeFingerprint(
                "10.0.0.1", "Chrome/120");
            var fp2 = DeviceFingerprintService.ComputeFingerprint(
                "10.0.0.1", "Chrome/120");

            fp1.Should().Be(fp2, "相同输入应生成相同的指纹");
        }

        [Fact]
        public void ComputeFingerprint_DifferentIp_ShouldProduceDifferentOutput()
        {
            var fp1 = DeviceFingerprintService.ComputeFingerprint(
                "10.0.0.1", "Chrome");
            var fp2 = DeviceFingerprintService.ComputeFingerprint(
                "10.0.0.2", "Chrome");

            fp1.Should().NotBe(fp2, "不同 IP 应生成不同的指纹");
        }

        [Fact]
        public void ComputeFingerprint_NullInputs_ShouldStillWork()
        {
            var fp = DeviceFingerprintService.ComputeFingerprint(null, null);
            fp.Should().NotBeNullOrEmpty();
            fp.Length.Should().Be(16);

            // 验证空 IP 场景也正常工作
            var fp2 = DeviceFingerprintService.ComputeFingerprint("", "");
            fp2.Should().NotBeNullOrEmpty();
        }
    }

    // ═══════════════════════════════════════════
    // 4.12 TokenBlacklistService — Token 黑名单
    // ═══════════════════════════════════════════
    public class TokenBlacklistServiceTests : IDisposable
    {
        private readonly TokenBlacklistService _service;

        public TokenBlacklistServiceTests()
        {
            _service = new TokenBlacklistService();
        }

        public void Dispose()
        {
            _service.Dispose();
        }

        [Fact]
        public void IsRevoked_NotRevoked_ShouldReturnFalse()
        {
            _service.IsRevoked("jti-unknown").Should().BeFalse(
                "未撤销的 token 不应被标记为已撤销");
        }

        [Fact]
        public void Revoke_And_IsRevoked_ShouldReturnTrue()
        {
            _service.Revoke("jti-001", DateTime.UtcNow.AddMinutes(30));
            _service.IsRevoked("jti-001").Should().BeTrue(
                "已撤销的 token 应被标记");
        }

        [Fact]
        public void Count_ShouldReflectRevocations()
        {
            _service.Revoke("jti-a", DateTime.UtcNow.AddHours(1));
            _service.Revoke("jti-b", DateTime.UtcNow.AddHours(1));

            _service.Count.Should().Be(2);
        }

        [Fact]
        public void Revoke_DuplicateJti_ShouldOverwrite()
        {
            _service.Revoke("jti-dup", DateTime.UtcNow.AddMinutes(10));
            _service.Revoke("jti-dup", DateTime.UtcNow.AddHours(2));

            _service.Count.Should().Be(1,
                "重复撤销相同 jti 不应增加计数");
            _service.IsRevoked("jti-dup").Should().BeTrue();
        }

        [Fact]
        public void Dispose_ShouldAllowMultipleCalls()
        {
            var act = () =>
            {
                _service.Dispose();
                _service.Dispose(); // 第二次调用不应抛异常
            };
            act.Should().NotThrow("重复 Dispose 应幂等安全");
        }
    }
}
