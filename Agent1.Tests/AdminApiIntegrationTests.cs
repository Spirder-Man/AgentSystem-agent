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
/// Admin API 集成测试 — 数据库验证端点。
/// 
/// 覆盖 AdminController 的 2 个端点:
///   GET /api/admin/db/info     — 数据库基本信息
///   GET /api/admin/db/validate  — 数据库连接验证
/// 
/// 权限: 仅 Admin 角色可访问。
/// </summary>
[Trait("Category", "ApiIntegration")]
public class AdminApiIntegrationTests : IClassFixture<CustomApiWebApplicationFactory>
{
    private readonly CustomApiWebApplicationFactory _factory;

    public AdminApiIntegrationTests(CustomApiWebApplicationFactory factory)
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
    // 2. GET /api/admin/db/validate
    // ═══════════════════════════════════════

    [Fact]
    public async Task Admin_DbValidate_Admin_Success()
    {
        var client = await LoginAndGetClient("admin", "Admin@123");
        var response = await client.GetAsync("/api/Admin/db/validate");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.TryGetProperty("connected", out _).Should().BeTrue();
        json.TryGetProperty("server", out _).Should().BeTrue();
        json.TryGetProperty("tables", out _).Should().BeTrue();
        json.TryGetProperty("elapsedMs", out _).Should().BeTrue();
    }

    // ═══════════════════════════════════════
    // 1. GET /api/admin/db/info
    // ═══════════════════════════════════════

    [Fact]
    public async Task Admin_DbInfo_Admin_Success()
    {
        var client = await LoginAndGetClient("admin", "Admin@123");
        var response = await client.GetAsync("/api/Admin/db/info");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.TryGetProperty("info", out _).Should().BeTrue();
        json.TryGetProperty("tables", out _).Should().BeTrue();
    }

    // ═══════════════════════════════════════
    // 权限测试
    // ═══════════════════════════════════════

    [Fact]
    public async Task Admin_DbInfo_NoAuth_ReturnsUnauthorized()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/api/Admin/db/info");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Admin_DbValidate_NoAuth_ReturnsUnauthorized()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/api/Admin/db/validate");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Admin_DbInfo_Viewer_Forbidden()
    {
        var client = await LoginAndGetClient("viewer", "View@789");
        var response = await client.GetAsync("/api/Admin/db/info");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_DbValidate_Viewer_Forbidden()
    {
        var client = await LoginAndGetClient("viewer", "View@789");
        var response = await client.GetAsync("/api/Admin/db/validate");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_DbInfo_Auditor_Forbidden()
    {
        var client = await LoginAndGetClient("auditor", "Audit@456");
        var response = await client.GetAsync("/api/Admin/db/info");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_DbValidate_Auditor_Forbidden()
    {
        var client = await LoginAndGetClient("auditor", "Audit@456");
        var response = await client.GetAsync("/api/Admin/db/validate");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
