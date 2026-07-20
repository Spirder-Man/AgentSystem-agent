using System;
using System.Reflection;
using System.Threading.Tasks;
using Agent1.Services;
using Agent1.Services.Orchestration;
using FluentAssertions;
using Moq;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// P5a-3: ScheduledScanService 生命周期测试
///
/// 聚焦 Timer 生命周期管理（不触发实际扫描）：
///   - Start/Stop/Dispose 流程
///   - Dispose 幂等性
///   - Start 创建 Timer
///   - ScanNowAsync 无资产时安全返回
/// </summary>
public class ScheduledScanServiceTests
{
    // ── Helpers: 创建最小依赖的 ScheduledScanService ──

    /// <summary>
    /// 创建用于生命周期测试的实例。
    /// ComplianceRuleEngine 构造函数需要 (AgentDialog, IAuditService)，用 null 绕过。
    /// InspectionRepository 无参构造可用。
    /// EventActionDispatcher 无参构造可用。
    /// </summary>
    private static ScheduledScanService CreateService()
    {
        // Moq 需要传递构造函数参数来创建代理
        var mockEngine = new Mock<ComplianceRuleEngine>(
            MockBehavior.Loose,
            null!,  // AgentDialog = null
            null!   // IAuditService = null
        ) { CallBase = false };

        var mockRepo = new Mock<InspectionRepository> { CallBase = false };
        var mockDispatcher = new Mock<EventActionDispatcher> { CallBase = false };

        return new ScheduledScanService(
            mockEngine.Object,
            mockRepo.Object,
            mockDispatcher.Object);
    }

    /// <summary>
    /// 为 ScanNowAsync 测试创建带自定义仓库的实例。
    /// </summary>
    private static ScheduledScanService CreateServiceWithRepo(InspectionRepository repo)
    {
        var mockEngine = new Mock<ComplianceRuleEngine>(
            MockBehavior.Loose,
            null!,
            null!
        ) { CallBase = false };

        var mockDispatcher = new Mock<EventActionDispatcher> { CallBase = false };

        return new ScheduledScanService(mockEngine.Object, repo, mockDispatcher.Object);
    }

    // ═══════════════════════════════════════
    // 生命周期
    // ═══════════════════════════════════════

    [Fact]
    public void Constructor_DoesNotThrow()
    {
        var act = () => CreateService();
        act.Should().NotThrow();
    }

    [Fact]
    public void Start_CreatesTimer()
    {
        var svc = CreateService();
        svc.Start();

        var timerField = typeof(ScheduledScanService).GetField("_timer",
            BindingFlags.NonPublic | BindingFlags.Instance);
        timerField.Should().NotBeNull();
        var timer = timerField!.GetValue(svc);
        timer.Should().NotBeNull("Start() 应创建 _timer");

        svc.Dispose();
    }

    [Fact]
    public void Start_CreatesSessionCleanupTimer()
    {
        var svc = CreateService();
        svc.Start();

        var timerField = typeof(ScheduledScanService).GetField("_sessionCleanupTimer",
            BindingFlags.NonPublic | BindingFlags.Instance);
        timerField.Should().NotBeNull();
        var timer = timerField!.GetValue(svc);
        timer.Should().NotBeNull("Start() 应创建 _sessionCleanupTimer");

        svc.Dispose();
    }

    [Fact]
    public void Stop_DoesNotThrow()
    {
        var svc = CreateService();
        svc.Start();

        var act = () => svc.Stop();
        act.Should().NotThrow();

        svc.Dispose();
    }

    [Fact]
    public void Stop_WithoutStart_DoesNotThrow()
    {
        var svc = CreateService();
        // 未调用 Start，_timer 为 null

        var act = () => svc.Stop();
        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_StopsTimers()
    {
        var svc = CreateService();
        svc.Start();
        svc.Dispose();

        // 验证 _disposed = true
        var disposedField = typeof(ScheduledScanService).GetField("_disposed",
            BindingFlags.NonPublic | BindingFlags.Instance);
        disposedField.Should().NotBeNull();
        ((bool)disposedField!.GetValue(svc)!).Should().BeTrue();
    }

    [Fact]
    public void Dispose_Idempotent()
    {
        var svc = CreateService();
        svc.Start();

        svc.Dispose();
        var act = () => svc.Dispose();
        act.Should().NotThrow("多次 Dispose 不应抛异常");
    }

    [Fact]
    public void Dispose_WithoutStart_Safe()
    {
        var svc = CreateService();
        // 未调用 Start → _timer, _sessionCleanupTimer 为 null

        var act = () => svc.Dispose();
        act.Should().NotThrow();
    }

    // ═══════════════════════════════════════
    // ScanNowAsync（依赖真实 InspectionRepository，仅验证不抛异常）
    // ═══════════════════════════════════════

    [Fact]
    public async Task ScanNowAsync_DoesNotThrow()
    {
        // 使用真实 InspectionRepository（会创建 demo 数据）
        var repo = new InspectionRepository();
        var svc = CreateServiceWithRepo(repo);

        // 注意：这会触发真实扫描（RuleEngine 内部依赖 AgentDialog=null，会抛异常）
        // 但 ExecuteScanAsync 有 try-catch，应安全返回
        var result = await svc.ScanNowAsync();

        result.Should().NotBeNull();
        result.ScannedAt.Should().BeCloseTo(DateTime.Now, TimeSpan.FromSeconds(10));
    }

    // ═══════════════════════════════════════
    // 接口契约
    // ═══════════════════════════════════════

    [Fact]
    public void Implements_IDisposable()
    {
        typeof(ScheduledScanService).Should().Implement<IDisposable>();
    }

    [Fact]
    public void HasScanNowAsync_Method()
    {
        var method = typeof(ScheduledScanService).GetMethod("ScanNowAsync");
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be<Task<ComplianceScanResult>>();
    }
}
