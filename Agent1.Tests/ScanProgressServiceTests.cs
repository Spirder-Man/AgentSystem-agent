using System.Linq;
using System.Threading.Tasks;
using Agent1.Api.Services;
using FluentAssertions;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// [#4 FIX] ScanProgressService 单元测试 — 后台扫描进度跟踪的状态机语义。
/// 本地无 WebApplicationFactory 环境也可验证 202/409 判定的核心逻辑（TryStart 原子性）。
/// </summary>
public class ScanProgressServiceTests
{
    [Fact]
    public void TryStart_Idle_ReturnsTrueWithScanId()
    {
        var svc = new ScanProgressService();

        var ok = svc.TryStart(10, out var scanId);

        ok.Should().BeTrue();
        scanId.Should().NotBeNullOrEmpty();
        var s = svc.GetStatus();
        s.Running.Should().BeTrue();
        s.Total.Should().Be(10);
        s.Current.Should().Be(0);
        s.StartedAt.Should().NotBeNull();
        s.CompletedAt.Should().BeNull();
    }

    [Fact]
    public void TryStart_AlreadyRunning_ReturnsFalseWithExistingScanId()
    {
        var svc = new ScanProgressService();
        svc.TryStart(5, out var firstId);

        var ok = svc.TryStart(5, out var secondId);

        ok.Should().BeFalse("已有扫描在跑时应拒绝重复启动（Controller 据此返回 409）");
        secondId.Should().Be(firstId, "冲突响应应携带在跑扫描的 scanId");
    }

    [Fact]
    public void Report_UpdatesProgressSnapshot()
    {
        var svc = new ScanProgressService();
        svc.TryStart(10, out _);

        svc.Report(3, 10, 2);

        var s = svc.GetStatus();
        s.Current.Should().Be(3);
        s.Total.Should().Be(10);
        s.NewFindings.Should().Be(2);
        s.Running.Should().BeTrue();
    }

    [Fact]
    public void Complete_StopsRunningAndAllowsRestart()
    {
        var svc = new ScanProgressService();
        svc.TryStart(4, out var firstId);
        svc.Report(4, 4, 1);

        svc.Complete(newFindings: 1);

        var s = svc.GetStatus();
        s.Running.Should().BeFalse();
        s.Current.Should().Be(4, "完成时 current 应对齐 total");
        s.NewFindings.Should().Be(1);
        s.CompletedAt.Should().NotBeNull();
        s.Error.Should().BeNull();

        // 完成后可再次启动新扫描
        svc.TryStart(8, out var secondId).Should().BeTrue();
        secondId.Should().NotBe(firstId);
        svc.GetStatus().Total.Should().Be(8);
    }

    [Fact]
    public void Fail_ExposesErrorAndAllowsRestart()
    {
        var svc = new ScanProgressService();
        svc.TryStart(4, out _);

        svc.Fail("LLM 服务不可用");

        var s = svc.GetStatus();
        s.Running.Should().BeFalse();
        s.Error.Should().Be("LLM 服务不可用");
        s.CompletedAt.Should().NotBeNull();

        svc.TryStart(4, out _).Should().BeTrue("失败后不应死锁，可重新发起扫描");
        svc.GetStatus().Error.Should().BeNull("重新启动应清空上次错误");
    }

    [Fact]
    public async Task TryStart_ConcurrentCalls_OnlyOneWins()
    {
        var svc = new ScanProgressService();
        var results = new bool[20];

        await Task.WhenAll(
            Enumerable.Range(0, 20).Select(i => Task.Run(() =>
            {
                results[i] = svc.TryStart(10, out _);
            })));

        results.Should().ContainSingle(r => r, "并发启动只允许一个赢家（409 语义的原子性保证）");
    }
}
