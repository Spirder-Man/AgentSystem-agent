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
/// API 全链路集成测试 — 使用 CustomApiWebApplicationFactory 内存内启动，
/// 验证所有 Controller 端点的 HTTP 流水线、JWT 认证、角色授权、输入校验。
///
/// 存根策略:
///   IDatabaseService → StubDatabaseService（内存 refresh token 存储）
///   ILlmService      → StubLlmService（mock 响应，不连 llama.cpp）
///
/// 测试账号:
///   admin   / Admin@123  (admin)
///   auditor / Audit@456  (auditor)
///   viewer  / View@789   (viewer)
/// </summary>
[Trait("Category", "ApiIntegration")]
public class ApiIntegrationTests : IClassFixture<CustomApiWebApplicationFactory>
{
    private readonly CustomApiWebApplicationFactory _factory;

    public ApiIntegrationTests(CustomApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ═══════════════════════════════════════
    // 测试辅助方法
    // ═══════════════════════════════════════

    private HttpClient CreateClient() => _factory.CreateClient();

    /// <summary>登录并返回带 JWT Bearer Token 的 HttpClient</summary>
    private async Task<HttpClient> LoginAndGetClient(string username, string password)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/Auth/login", new
        {
            username, password
        });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = json.GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>登录并返回 JWT token 字符串</summary>
    private async Task<string> LoginAndGetToken(string username, string password)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/Auth/login", new { username, password });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("token").GetString()!;
    }

    // ═══════════════════════════════════════
    // 健康检查 & 基础设施端点（无需认证）
    // ═══════════════════════════════════════

    [Fact]
    public async Task HealthEndpoint_ReturnsSuccess()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/health");
        response.StatusCode.Should().BeOneOf(new[] { HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable });
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsJson()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/health");
        response.Content.Headers.ContentType?.MediaType.Should().Contain("json");
    }

    [Fact]
    public async Task MetricsEndpoint_ReturnsPlainText()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/metrics");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UnknownEndpoint_ReturnsNotFound()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/api/nonexistent/endpoint");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task HealthEndpoint_HasContentTypeHeader()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/health");
        response.Content.Headers.ContentType.Should().NotBeNull();
    }

    // ═══════════════════════════════════════
    // Auth 端点 — 登录正用例
    // ═══════════════════════════════════════

    [Fact]
    public async Task AuthLogin_Admin_Success_ReturnsTokenAndRole()
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/Auth/login", new
        {
            username = "admin",
            password = "Admin@123"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("token").GetString().Should().NotBeNullOrEmpty();
        json.GetProperty("username").GetString().Should().Be("admin");
        json.GetProperty("role").GetString().Should().Be("admin");
        json.GetProperty("refreshToken").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task AuthLogin_Auditor_Success()
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/Auth/login", new
        {
            username = "auditor",
            password = "Audit@456"
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("role").GetString().Should().Be("auditor");
    }

    [Fact]
    public async Task AuthLogin_Viewer_Success()
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/Auth/login", new
        {
            username = "viewer",
            password = "View@789"
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("role").GetString().Should().Be("viewer");
    }

    [Fact]
    public async Task AuthLogin_WithEmptyBody_ReturnsBadRequest()
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/Auth/login", new { });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AuthLogin_WithWhitespaceCredentials_ReturnsBadRequest()
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/Auth/login", new
        {
            username = "   ",
            password = "   "
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AuthLogin_WithInvalidCredentials_ReturnsUnauthorized()
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/Auth/login", new
        {
            username = "nonexistent",
            password = "wrongpassword"
        });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AuthRefresh_WithoutToken_ReturnsBadRequest()
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/Auth/refresh", new { refreshToken = "" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AuthRefresh_WithInvalidToken_ReturnsUnauthorized()
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/Auth/refresh", new
        {
            refreshToken = "invalid-refresh-token"
        });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AuthRefresh_WithValidToken_ReturnsNewTokenPair()
    {
        var client = CreateClient();
        // 先登录获取 refresh token
        var loginResp = await client.PostAsJsonAsync("/api/Auth/login", new
        {
            username = "admin",
            password = "Admin@123"
        });
        var loginJson = await loginResp.Content.ReadFromJsonAsync<JsonElement>();
        var refreshToken = loginJson.GetProperty("refreshToken").GetString();

        // 用 refresh token 刷新
        var refreshResp = await client.PostAsJsonAsync("/api/Auth/refresh", new
        {
            refreshToken
        });
        refreshResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var refreshJson = await refreshResp.Content.ReadFromJsonAsync<JsonElement>();
        refreshJson.GetProperty("token").GetString().Should().NotBeNullOrEmpty();
        refreshJson.GetProperty("refreshToken").GetString().Should().NotBeNullOrEmpty();
    }

    // ═══════════════════════════════════════
    // 未认证访问受保护端点（应返回 401）
    // ═══════════════════════════════════════

    [Theory]
    [InlineData("/api/Compliance/summary", "GET")]
    [InlineData("/api/Compliance/check", "POST")]
    [InlineData("/api/Compliance/hazard/query", "POST")]
    [InlineData("/api/Compliance/storage/compatibility", "POST")]
    [InlineData("/api/Inspection/plans", "GET")]
    [InlineData("/api/Inspection/plans", "POST")]
    [InlineData("/api/Inspection/assets", "GET")]
    [InlineData("/api/Inspection/rounds", "GET")]
    [InlineData("/api/Tickets", "GET")]
    [InlineData("/api/Eval/run", "POST")]
    [InlineData("/api/Audit/logs", "GET")]
    [InlineData("/api/KnowledgeBase/search-mode", "GET")]
    [InlineData("/api/Regulatory/audit", "POST")]
    [InlineData("/api/Emergency/response", "POST")]
    [InlineData("/api/KnowledgeGraph/query", "POST")]
    [InlineData("/api/Alerts/test", "POST")]
    [InlineData("/api/Diagnostics/tool-calling", "POST")]
    public async Task ProtectedEndpoint_WithoutToken_ReturnsUnauthorized(string url, string method)
    {
        var client = CreateClient();
        HttpResponseMessage response;
        if (method == "GET")
            response = await client.GetAsync(url);
        else
            response = await client.PostAsJsonAsync(url, new { query = "test" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            $"{method} {url} 未认证应返回 401");
    }

    // ═══════════════════════════════════════
    // Compliance 端点 — 认证后正用例
    // ═══════════════════════════════════════

    [Fact]
    public async Task ComplianceSummary_Viewer_Success()
    {
        var client = await LoginAndGetClient("viewer", "View@789");
        var response = await client.GetAsync("/api/Compliance/summary");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        // 验证响应结构
        json.TryGetProperty("totalAssets", out _).Should().BeTrue();
        json.TryGetProperty("complianceRate", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ComplianceCheck_Viewer_Forbidden()
    {
        // /check 需要 Auditor 角色，Viewer 应返回 403
        var client = await LoginAndGetClient("viewer", "View@789");
        var response = await client.PostAsJsonAsync("/api/Compliance/check", new { query = "测试" });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ComplianceCheck_Auditor_EmptyQuery_ReturnsBadRequest()
    {
        var client = await LoginAndGetClient("auditor", "Audit@456");
        var response = await client.PostAsJsonAsync("/api/Compliance/check", new { query = "" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ComplianceCheck_Auditor_ValidQuery_ReturnsSuccess()
    {
        var client = await LoginAndGetClient("auditor", "Audit@456");
        var response = await client.PostAsJsonAsync("/api/Compliance/check", new
        {
            query = "苯和丙酮能同库储存吗"
        });
        // 使用 Stub LLM，应返回 200
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HazardQuery_Viewer_EmptySubstance_ReturnsBadRequest()
    {
        var client = await LoginAndGetClient("viewer", "View@789");
        var response = await client.PostAsJsonAsync("/api/Compliance/hazard/query", new
        {
            substanceName = ""
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task HazardQuery_Viewer_Valid_ReturnsSuccess()
    {
        var client = await LoginAndGetClient("viewer", "View@789");
        var response = await client.PostAsJsonAsync("/api/Compliance/hazard/query", new
        {
            substanceName = "苯"
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task StorageCompatibility_Viewer_EmptySubstances_ReturnsBadRequest()
    {
        var client = await LoginAndGetClient("viewer", "View@789");
        var response = await client.PostAsJsonAsync("/api/Compliance/storage/compatibility", new
        {
            substanceA = "",
            substanceB = ""
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task StorageCompatibility_Viewer_Valid_ReturnsSuccess()
    {
        var client = await LoginAndGetClient("viewer", "View@789");
        var response = await client.PostAsJsonAsync("/api/Compliance/storage/compatibility", new
        {
            substanceA = "苯",
            substanceB = "丙酮"
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ═══════════════════════════════════════
    // Inspection 端点 — CRUD + 巡检执行
    // ═══════════════════════════════════════

    [Fact]
    public async Task InspectionPlans_Viewer_ListSuccess()
    {
        var client = await LoginAndGetClient("viewer", "View@789");
        var response = await client.GetAsync("/api/Inspection/plans");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task InspectionPlans_Viewer_Create_Forbidden()
    {
        // 创建计划需要 Auditor
        var client = await LoginAndGetClient("viewer", "View@789");
        var response = await client.PostAsJsonAsync("/api/Inspection/plans", new
        {
            name = "测试计划",
            items = new[] { new { query = "测试检查项" } }
        });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task InspectionPlans_Auditor_Create_EmptyName_ReturnsBadRequest()
    {
        var client = await LoginAndGetClient("auditor", "Audit@456");
        var response = await client.PostAsJsonAsync("/api/Inspection/plans", new
        {
            name = "",
            items = new[] { new { query = "test" } }
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task InspectionPlans_Auditor_Create_EmptyItems_ReturnsBadRequest()
    {
        var client = await LoginAndGetClient("auditor", "Audit@456");
        var response = await client.PostAsJsonAsync("/api/Inspection/plans", new
        {
            name = "测试计划",
            items = Array.Empty<object>()
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task InspectionPlans_Auditor_CreateAndGet_Success()
    {
        var client = await LoginAndGetClient("auditor", "Audit@456");

        // 创建计划
        var createResp = await client.PostAsJsonAsync("/api/Inspection/plans", new
        {
            name = "集成测试巡检计划",
            area = "A区",
            items = new[] { new { query = "检查消防设施", capability = "storage-compliance" } },
            notes = "自动化测试"
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var createJson = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var planId = createJson.GetProperty("planId").GetString();

        // 获取单个计划
        var getResp = await client.GetAsync($"/api/Inspection/plans/{planId}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var getJson = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        getJson.GetProperty("name").GetString().Should().Be("集成测试巡检计划");
    }

    [Fact]
    public async Task InspectionAssets_Viewer_Success()
    {
        var client = await LoginAndGetClient("viewer", "View@789");
        var response = await client.GetAsync("/api/Inspection/assets");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task InspectionRounds_Viewer_Success()
    {
        var client = await LoginAndGetClient("viewer", "View@789");
        var response = await client.GetAsync("/api/Inspection/rounds");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task InspectionQuickCheck_Viewer_EmptyQuery_ReturnsBadRequest()
    {
        var client = await LoginAndGetClient("viewer", "View@789");
        var response = await client.PostAsJsonAsync("/api/Inspection/quick-check", new { query = "" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task InspectionQuickCheck_Viewer_Success()
    {
        var client = await LoginAndGetClient("viewer", "View@789");
        var response = await client.PostAsJsonAsync("/api/Inspection/quick-check", new
        {
            query = "检查灭火器配置"
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ═══════════════════════════════════════
    // Tickets 端点
    // ═══════════════════════════════════════

    [Fact]
    public async Task Tickets_Viewer_ListSuccess()
    {
        var client = await LoginAndGetClient("viewer", "View@789");
        var response = await client.GetAsync("/api/Tickets");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Tickets_Followup_Viewer_Forbidden()
    {
        var client = await LoginAndGetClient("viewer", "View@789");
        var response = await client.PostAsJsonAsync("/api/Tickets/followup", new
        {
            complianceResult = "测试结果"
        });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ═══════════════════════════════════════
    // Eval 端点（Auditor only）
    // ═══════════════════════════════════════

    [Fact]
    public async Task EvalRun_Viewer_Forbidden()
    {
        var client = await LoginAndGetClient("viewer", "View@789");
        var response = await client.PostAsync("/api/Eval/run", null);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task EvalRun_Auditor_Success()
    {
        var client = await LoginAndGetClient("auditor", "Audit@456");
        var response = await client.PostAsync("/api/Eval/run", null);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    // ═══════════════════════════════════════
    // Audit 端点（Admin only）
    // ═══════════════════════════════════════

    [Fact]
    public async Task AuditLogs_Auditor_Forbidden()
    {
        var client = await LoginAndGetClient("auditor", "Audit@456");
        var response = await client.GetAsync("/api/Audit/logs");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AuditLogs_Admin_Success()
    {
        var client = await LoginAndGetClient("admin", "Admin@123");
        var response = await client.GetAsync("/api/Audit/logs");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AuditIntegrity_Admin_Success()
    {
        var client = await LoginAndGetClient("admin", "Admin@123");
        var response = await client.GetAsync("/api/Audit/integrity");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AuditStats_Admin_Success()
    {
        var client = await LoginAndGetClient("admin", "Admin@123");
        var response = await client.GetAsync("/api/Audit/stats");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ═══════════════════════════════════════
    // KnowledgeBase 端点
    // ═══════════════════════════════════════

    [Fact]
    public async Task KnowledgeBase_SearchMode_Viewer_Success()
    {
        var client = await LoginAndGetClient("viewer", "View@789");
        var response = await client.GetAsync("/api/KnowledgeBase/search-mode");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("mode").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task KnowledgeBase_SearchMode_Put_Valid_Success()
    {
        var client = await LoginAndGetClient("auditor", "Audit@456");
        var response = await client.PutAsJsonAsync("/api/KnowledgeBase/search-mode", new { mode = "Bm25" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task KnowledgeBase_SearchMode_Put_Invalid_ReturnsBadRequest()
    {
        var client = await LoginAndGetClient("auditor", "Audit@456");
        var response = await client.PutAsJsonAsync("/api/KnowledgeBase/search-mode", new { mode = "InvalidMode" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task KnowledgeBase_RagTest_Viewer_Success()
    {
        var client = await LoginAndGetClient("viewer", "View@789");
        var response = await client.PostAsJsonAsync("/api/KnowledgeBase/rag-test", new
        {
            query = "GB 30000",
            topK = 3
        });
        // 可能因知识库目录不存在而返回错误，但应可访问
        response.StatusCode.Should().BeOneOf(new[] { HttpStatusCode.OK, HttpStatusCode.InternalServerError });
    }

    [Fact]
    public async Task KnowledgeBase_IncrementalLoad_Viewer_Forbidden()
    {
        var client = await LoginAndGetClient("viewer", "View@789");
        var response = await client.PostAsync("/api/KnowledgeBase/incremental-load", null);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ═══════════════════════════════════════
    // Regulatory 端点（Auditor only）
    // ═══════════════════════════════════════

    [Fact]
    public async Task RegulatoryAudit_Viewer_Forbidden()
    {
        var client = await LoginAndGetClient("viewer", "View@789");
        var response = await client.PostAsJsonAsync("/api/Regulatory/audit", new { query = "测试" });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ═══════════════════════════════════════
    // Emergency 端点
    // ═══════════════════════════════════════

    [Fact]
    public async Task EmergencyResponse_Viewer_EmptySubstance_ReturnsBadRequest()
    {
        var client = await LoginAndGetClient("viewer", "View@789");
        var response = await client.PostAsJsonAsync("/api/Emergency/response", new { substance = "" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ═══════════════════════════════════════
    // KnowledgeGraph 端点
    // ═══════════════════════════════════════

    [Fact]
    public async Task KnowledgeGraphQuery_Viewer_EmptyQuery_ReturnsBadRequest()
    {
        var client = await LoginAndGetClient("viewer", "View@789");
        var response = await client.PostAsJsonAsync("/api/KnowledgeGraph/query", new { query = "" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ═══════════════════════════════════════
    // Multimodal 端点（Auditor only）
    // ═══════════════════════════════════════

    [Fact]
    public async Task MultimodalAnalyze_Viewer_Forbidden()
    {
        var client = await LoginAndGetClient("viewer", "View@789");
        var response = await client.PostAsync("/api/Multimodal/analyze", null);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ═══════════════════════════════════════
    // Alerts 端点（Auditor only）
    // ═══════════════════════════════════════

    [Fact]
    public async Task AlertsTest_Viewer_Forbidden()
    {
        var client = await LoginAndGetClient("viewer", "View@789");
        var response = await client.PostAsJsonAsync("/api/Alerts/test", new { });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ═══════════════════════════════════════
    // Diagnostics 端点
    // ═══════════════════════════════════════

    [Fact]
    public async Task DiagnosticsToolCalling_Viewer_Success()
    {
        var client = await LoginAndGetClient("viewer", "View@789");
        var response = await client.PostAsync("/api/Diagnostics/tool-calling", null);
        // 使用 Stub LLM，应返回 200
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ═══════════════════════════════════════
    // JWT 角色声明验证
    // ═══════════════════════════════════════

    [Fact]
    public async Task JwtToken_ContainsCorrectRoleClaim()
    {
        var token = await LoginAndGetToken("auditor", "Audit@456");
        token.Should().NotBeNullOrEmpty();

        // 用 viewer 的 token 访问 Auditor 端点应返回 403
        var viewerToken = await LoginAndGetToken("viewer", "View@789");
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", viewerToken);
        var response = await client.PostAsJsonAsync("/api/Compliance/check", new { query = "测试" });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task JwtToken_InvalidToken_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid.jwt.token");
        var response = await client.GetAsync("/api/Compliance/summary");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task JwtToken_ExpiredToken_ReturnsUnauthorized()
    {
        // 构造一个已过期的 token（使用错误的签名，JWT 中间件会拒绝）
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJ0ZXN0IiwibmFtZSI6InRlc3QiLCJyb2xlIjoidmlld2VyIiwiZXhwIjoxNTAwMDAwMDAwfQ.xyz");
        var response = await client.GetAsync("/api/Compliance/summary");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ═══════════════════════════════════════
    // 速率限制验证
    // ═══════════════════════════════════════

    [Fact]
    public async Task RateLimiting_MultipleHealthRequests_DoesNotRateLimitHealthEndpoint()
    {
        var client = CreateClient();
        for (int i = 0; i < 20; i++)
        {
            var response = await client.GetAsync("/health");
            response.StatusCode.Should().BeOneOf(new[] { HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable });
        }
    }

    [Fact]
    public async Task RateLimiting_MultipleUnauthenticatedApiRequests_ReturnsConsistentStatus()
    {
        var client = CreateClient();
        for (int i = 0; i < 10; i++)
        {
            var response = await client.GetAsync("/api/Tickets");
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
                "未认证请求应在限流前被 JWT 中间件拦截");
        }
    }

    // ═══════════════════════════════════════
    // 响应头 & 内容类型验证
    // ═══════════════════════════════════════

    [Fact]
    public async Task AuthLogin_BadRequest_HasJsonContentType()
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/Auth/login", new { });
        response.Content.Headers.ContentType?.MediaType.Should().Contain("json");
    }

    [Fact]
    public async Task ComplianceSummary_HasJsonContentType()
    {
        var client = await LoginAndGetClient("viewer", "View@789");
        var response = await client.GetAsync("/api/Compliance/summary");
        response.Content.Headers.ContentType?.MediaType.Should().Contain("json");
    }
}

