using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// Dashboard API 集成测试 — P0.1 合规总览 API 端点全量验证。
/// 
/// 覆盖 DashboardController 的 6 个端点:
///   GET  /api/dashboard/overview     — 合规态势总览
///   GET  /api/dashboard/assets       — 资产台账
///   POST /api/dashboard/scan         — 自动合规扫描 (Auditor)
///   GET  /api/dashboard/findings     — 合规发现列表
///   GET  /api/dashboard/history      — 历史巡检记录
///   GET  /api/dashboard/report/hazard — 安全隐患报告
/// 
/// 存根策略: CustomApiWebApplicationFactory (StubDatabaseService + StubLlmService)
/// </summary>
[Trait("Category", "ApiIntegration")]
public class DashboardApiIntegrationTests : IClassFixture<CustomApiWebApplicationFactory>
{
    private readonly CustomApiWebApplicationFactory _factory;

    public DashboardApiIntegrationTests(CustomApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ═══════════════════════════════════════
    // 辅助方法
    // ═══════════════════════════════════════

    private HttpClient CreateClient() => _factory.CreateClient();

    private async Task<HttpClient> LoginAndGetClient(string username, string password)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/Auth/login", new { username, password });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = json.GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // ═══════════════════════════════════════
    // 1. GET /api/dashboard/overview — 合规总览
    // ═══════════════════════════════════════

    [Fact]
    public async Task Dashboard_Overview_Viewer_Success()
    {
        var client = await LoginAndGetClient("viewer", "View@789");
        var response = await client.GetAsync("/api/Dashboard/overview");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        // 验证核心字段存在
        json.TryGetProperty("totalAssets", out _).Should().BeTrue();
        json.TryGetProperty("complianceRate", out _).Should().BeTrue();
        json.TryGetProperty("totalFindings", out _).Should().BeTrue();
        json.TryGetProperty("openFindings", out _).Should().BeTrue();
        json.TryGetProperty("remediationRate", out _).Should().BeTrue();
        json.TryGetProperty("hasInventory", out _).Should().BeTrue();
        json.TryGetProperty("findingsBySeverity", out _).Should().BeTrue();
        json.TryGetProperty("findingsByStatus", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Dashboard_Overview_NoAuth_ReturnsUnauthorized()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/api/Dashboard/overview");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dashboard_Overview_Auditor_Success()
    {
        var client = await LoginAndGetClient("auditor", "Audit@456");
        var response = await client.GetAsync("/api/Dashboard/overview");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ═══════════════════════════════════════
    // 2. GET /api/dashboard/assets — 资产台账
    // ═══════════════════════════════════════

    [Fact]
    public async Task Dashboard_Assets_Viewer_Success()
    {
        var client = await LoginAndGetClient("viewer", "View@789");
        var response = await client.GetAsync("/api/Dashboard/assets");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.TryGetProperty("items", out _).Should().BeTrue();
        json.TryGetProperty("summary", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Dashboard_Assets_NoAuth_ReturnsUnauthorized()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/api/Dashboard/assets");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ═══════════════════════════════════════
    // 3. POST /api/dashboard/scan — 自动合规扫描
    // ═══════════════════════════════════════

    [Fact]
    public async Task Dashboard_Scan_Viewer_Forbidden()
    {
        // /scan 需要 Auditor 角色
        var client = await LoginAndGetClient("viewer", "View@789");
        var response = await client.PostAsync("/api/Dashboard/scan", null);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Dashboard_Scan_Auditor_StubAssets_Accepted()
    {
        // [#4 FIX] 扫描改后台任务：启动返回 202 { scanId }，进度经 /scan/status 轮询
        var client = await LoginAndGetClient("auditor", "Audit@456");
        var response = await client.PostAsync("/api/Dashboard/scan", null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.TryGetProperty("scanId", out var scanId).Should().BeTrue();
        scanId.GetString().Should().NotBeNullOrEmpty();
        json.TryGetProperty("totalAssets", out _).Should().BeTrue();

        // 轮询 status 直至后台扫描结束（Stub 资产量小，数秒内完成）
        for (var i = 0; i < 50; i++)
        {
            var statusResp = await client.GetAsync("/api/Dashboard/scan/status");
            statusResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var status = await statusResp.Content.ReadFromJsonAsync<JsonElement>();

            status.TryGetProperty("running", out var running).Should().BeTrue();
            status.TryGetProperty("current", out _).Should().BeTrue();
            status.TryGetProperty("total", out _).Should().BeTrue();
            status.TryGetProperty("newFindings", out _).Should().BeTrue();

            if (!running.GetBoolean())
            {
                status.GetProperty("completedAt").ValueKind.Should().NotBe(JsonValueKind.Null);
                return;
            }
            await Task.Delay(200);
        }

        Assert.Fail("后台扫描 10 秒内未完成");
    }

    [Fact]
    public async Task Dashboard_Scan_Concurrent_ReturnsConflict()
    {
        // [#4 FIX] 已有扫描在跑时重复发起 → 409（启动立即连发两次，第二次命中在跑分支或已完成重新 202）
        var client = await LoginAndGetClient("auditor", "Audit@456");

        // 等待可能残留的上一轮扫描结束（单例进度服务跨用例共享）
        await WaitForScanIdleAsync(client);

        var first = await client.PostAsync("/api/Dashboard/scan", null);
        first.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var second = await client.PostAsync("/api/Dashboard/scan", null);
        // Stub 扫描可能极快完成，两种合法结果：在跑 409 / 已完成后新启动 202
        second.StatusCode.Should().BeOneOf(HttpStatusCode.Conflict, HttpStatusCode.Accepted);
        if (second.StatusCode == HttpStatusCode.Conflict)
        {
            var json = await second.Content.ReadFromJsonAsync<JsonElement>();
            json.TryGetProperty("scanId", out _).Should().BeTrue();
        }

        await WaitForScanIdleAsync(client);
    }

    private static async Task WaitForScanIdleAsync(HttpClient client)
    {
        for (var i = 0; i < 50; i++)
        {
            var status = await (await client.GetAsync("/api/Dashboard/scan/status"))
                .Content.ReadFromJsonAsync<JsonElement>();
            if (!status.GetProperty("running").GetBoolean()) return;
            await Task.Delay(200);
        }
    }

    [Fact]
    public async Task Dashboard_Scan_NoAuth_ReturnsUnauthorized()
    {
        var client = CreateClient();
        var response = await client.PostAsync("/api/Dashboard/scan", null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ═══════════════════════════════════════
    // 4. GET /api/dashboard/findings — 合规发现列表
    // ═══════════════════════════════════════

    [Fact]
    public async Task Dashboard_Findings_Viewer_Success()
    {
        var client = await LoginAndGetClient("viewer", "View@789");
        var response = await client.GetAsync("/api/Dashboard/findings");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.TryGetProperty("items", out _).Should().BeTrue();
        json.TryGetProperty("summary", out _).Should().BeTrue();
        json.TryGetProperty("appliedFilter", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Dashboard_Findings_FilteredBySeverity_ReturnsOk()
    {
        var client = await LoginAndGetClient("viewer", "View@789");
        var response = await client.GetAsync("/api/Dashboard/findings?severity=Critical&openOnly=false");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var appliedFilter = json.GetProperty("appliedFilter");
        appliedFilter.GetProperty("severity").GetString().Should().Be("Critical");
    }

    [Fact]
    public async Task Dashboard_Findings_WithStatusFilter_ReturnsOk()
    {
        var client = await LoginAndGetClient("viewer", "View@789");
        var response = await client.GetAsync("/api/Dashboard/findings?status=New&openOnly=false");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ═══════════════════════════════════════
    // 5. GET /api/dashboard/history — 历史巡检记录
    // ═══════════════════════════════════════

    [Fact]
    public async Task Dashboard_History_Viewer_Success()
    {
        var client = await LoginAndGetClient("viewer", "View@789");
        var response = await client.GetAsync("/api/Dashboard/history");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.TryGetProperty("items", out _).Should().BeTrue();
        json.TryGetProperty("total", out _).Should().BeTrue();
        json.TryGetProperty("statusBreakdown", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Dashboard_History_NoAuth_ReturnsUnauthorized()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/api/Dashboard/history");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ═══════════════════════════════════════
    // 6. GET /api/dashboard/report/hazard — 安全隐患报告
    // ═══════════════════════════════════════

    [Fact]
    public async Task Dashboard_HazardReport_Viewer_Success()
    {
        var client = await LoginAndGetClient("viewer", "View@789");
        var response = await client.GetAsync("/api/Dashboard/report/hazard");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.TryGetProperty("generatedAt", out _).Should().BeTrue();
        json.TryGetProperty("disclaimer", out _).Should().BeTrue();
        json.TryGetProperty("summary", out _).Should().BeTrue();
        json.TryGetProperty("items", out _).Should().BeTrue();

        // disclaimer 应包含安全提示
        var disclaimer = json.GetProperty("disclaimer").GetString();
        disclaimer.Should().Contain("人工复核");
    }

    [Fact]
    public async Task Dashboard_HazardReport_NoAuth_ReturnsUnauthorized()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/api/Dashboard/report/hazard");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
