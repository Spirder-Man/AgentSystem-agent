using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// API 集成测试 — 使用 WebApplicationFactory 内存内启动 Agent1.Api，
/// 验证 Controller、Middleware、JWT 认证全链路。
///
/// 运行方式:
///   不需要真实数据库/LLM 连接 — 仅验证 HTTP 流水线
///   dotnet test --filter "Category=ApiIntegration"
/// </summary>
[Trait("Category", "ApiIntegration")]
internal class ApiIntegrationTests : IClassFixture<WebApplicationFactory<Agent1.Api.Program>>
{
    private readonly WebApplicationFactory<Agent1.Api.Program> _factory;

    public ApiIntegrationTests(WebApplicationFactory<Agent1.Api.Program> factory)
    {
        _factory = factory;
    }

    // ═══════════════════════════════════════
    // 健康检查端点（无需认证）
    // ═══════════════════════════════════════

    [Fact]
    public async Task HealthEndpoint_ReturnsSuccess()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable },
            "健康检查端点应可访问");
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsJson()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");

        response.Content.Headers.ContentType?.MediaType
            .Should().Contain("json", "健康检查应返回 JSON");
    }

    [Fact]
    public async Task MetricsEndpoint_ReturnsPlainText()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/metrics");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "Prometheus /metrics 端点应可访问");
    }

    // ═══════════════════════════════════════
    // 认证端点
    // ═══════════════════════════════════════

    [Fact]
    public async Task AuthLogin_WithEmptyBody_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/Auth/login", new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "空用户名密码应返回 400");
    }

    [Fact]
    public async Task AuthLogin_WithInvalidCredentials_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/Auth/login", new
        {
            username = "nonexistent",
            password = "wrongpassword"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "无效凭据应返回 401");
    }

    [Fact]
    public async Task AuthLogin_WithWhitespaceCredentials_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/Auth/login", new
        {
            username = "   ",
            password = "   "
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "空白凭据应返回 400");
    }

    [Fact]
    public async Task AuthRefresh_WithoutToken_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/Auth/refresh", new
        {
            refreshToken = ""
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "空 RefreshToken 应返回 400");
    }

    // ═══════════════════════════════════════
    // 未认证访问受保护端点
    // ═══════════════════════════════════════

    [Fact]
    public async Task ComplianceCheck_Unauthenticated_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/Compliance/check", new
        {
            query = "测试查询"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "未认证的合规审核请求应返回 401");
    }

    [Fact]
    public async Task ComplianceSummary_Unauthenticated_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/Compliance/summary");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "未认证的合规总览请求应返回 401");
    }

    [Fact]
    public async Task InspectionPlans_Unauthenticated_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/Inspection/plans");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "未认证的巡检计划请求应返回 401");
    }

    [Fact]
    public async Task Tickets_Unauthenticated_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/Tickets");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "未认证的工单请求应返回 401");
    }

    // ═══════════════════════════════════════
    // 速率限制（多次请求触发 429）
    // ═══════════════════════════════════════

    [Fact]
    public async Task RateLimiting_MultipleHealthRequests_DoesNotRateLimitHealthEndpoint()
    {
        // 健康检查端点 /health 不在限流路径下，多次请求不应触发 429
        var client = _factory.CreateClient();

        for (int i = 0; i < 20; i++)
        {
            var response = await client.GetAsync("/health");
            response.StatusCode.Should().BeOneOf(
                new[] { HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable });
        }
    }

    [Fact]
    public async Task RateLimiting_MultipleUnauthenticatedApiRequests_ReturnsConsistentStatus()
    {
        // 未认证请求应先返回 401（认证在限流之前），而非 429
        var client = _factory.CreateClient();

        for (int i = 0; i < 10; i++)
        {
            var response = await client.GetAsync("/api/Tickets");
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
                "未认证请求应在限流前被 JWT 中间件拦截");
        }
    }

    // ═══════════════════════════════════════
    // 响应头验证
    // ═══════════════════════════════════════

    [Fact]
    public async Task HealthEndpoint_HasContentTypeHeader()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.Content.Headers.ContentType.Should().NotBeNull();
    }

    [Fact]
    public async Task AuthLogin_BadRequest_HasJsonContentType()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/Auth/login", new { });

        response.Content.Headers.ContentType?.MediaType
            .Should().Contain("json");
    }

    // ═══════════════════════════════════════
    // 404 处理
    // ═══════════════════════════════════════

    [Fact]
    public async Task UnknownEndpoint_ReturnsNotFound()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/nonexistent/endpoint");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "不存在的端点应返回 404");
    }
}
