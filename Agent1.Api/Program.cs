using Agent1;
using Agent1.Api.Middleware;
using Agent1.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Text;

// ═══════════════════════════════════════════════════
// Phase 1: 配置外部化 — appsettings.json + 环境变量
// ═══════════════════════════════════════════════════
var builder = WebApplication.CreateBuilder(args);

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables()
    .Build();

AppConfig.Load(configuration);

// 启动前校验
var configErrors = AppConfig.Instance.Validate();
if (configErrors.Count > 0)
{
    Console.WriteLine("配置校验失败:");
    foreach (var err in configErrors)
        Console.WriteLine($"   - {err}");
    return 1;
}

// ═══════════════════════════════════════════════════
// 结构化日志 — Serilog
// ═══════════════════════════════════════════════════
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/agent1-api-.log", rollingInterval: RollingInterval.Day)
    .WriteTo.Seq(Environment.GetEnvironmentVariable("SEQ_URL") ?? "http://localhost:5341")
    .CreateLogger();

builder.Host.UseSerilog();

// ═══════════════════════════════════════════════════
// 依赖注入
// ═══════════════════════════════════════════════════
builder.Services.AddSingleton(AppConfig.Instance);
builder.Services.AddSingleton<IDatabaseService, DatabaseService>();
builder.Services.AddSingleton<ISessionService, SessionService>();
builder.Services.AddSingleton<IMemoryService, MemoryService>();

// ILlmService 延迟绑定 (打破循环依赖)
builder.Services.AddSingleton<LlmService>(sp => new LlmService(null!));
builder.Services.AddSingleton<ILlmService>(sp => sp.GetRequiredService<LlmService>());

builder.Services.AddSingleton<IToolService>(sp =>
{
    var llm = sp.GetRequiredService<ILlmService>();
    var kb = sp.GetRequiredService<IKnowledgeBaseService>();
    return new ToolService(llm, kb, AppConfig.Instance.ChemicalTool?.Tools);
});
builder.Services.AddSingleton<AgentDialog>();
builder.Services.AddSingleton<IKnowledgeBaseService>(sp =>
{
    var db = sp.GetRequiredService<IDatabaseService>();
    var llm = sp.GetRequiredService<ILlmService>();
    return new HybridKnowledgeBaseService(db, llm, AppConfig.Instance);
});
builder.Services.AddSingleton<IIntegrationService, IntegrationService>();
builder.Services.AddSingleton<IAuditService, AuditService>();
builder.Services.AddSingleton<IModuleFactory, ModuleFactory>();
builder.Services.AddSingleton<ModuleDispatcher>();

// ═══════════════════════════════════════════════════
// JWT 认证 (Task 2)
// ═══════════════════════════════════════════════════
var jwtSection = configuration.GetSection("Jwt");
var jwtKey = jwtSection["Key"] ?? Environment.GetEnvironmentVariable("JWT_KEY") ?? "Agent1-Dev-Key-Change-In-Production-2026";
if (string.IsNullOrEmpty(jwtSection["Key"]) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JWT_KEY")))
{
    Console.WriteLine("警告: JWT Key 未配置，使用默认开发密钥。生产环境请设置 JWT_KEY 环境变量。");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSection["Issuer"] ?? "Agent1",
            ValidAudience = jwtSection["Audience"] ?? "Agent1.Api",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", policy => policy.RequireRole("admin"));
    options.AddPolicy("Auditor", policy => policy.RequireRole("admin", "auditor"));
    options.AddPolicy("Viewer", policy => policy.RequireRole("admin", "auditor", "viewer"));
});

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "化工合规 AI Agent API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new()
    {
        Description = "JWT Authorization header. Example: \"Bearer {token}\"",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new()
    {
        {
            new()
            {
                Reference = new() { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddControllers();

var app = builder.Build();

// ═══════════════════════════════════════════════════
// DI 容器解析 + 初始化
// ═══════════════════════════════════════════════════
using (var scope = app.Services.CreateScope())
{
    var sp = scope.ServiceProvider;
    var databaseService = sp.GetRequiredService<IDatabaseService>();
    var knowledgeBaseService = sp.GetRequiredService<IKnowledgeBaseService>();
    var llmSvc = sp.GetRequiredService<LlmService>();

    // 延迟注入 RAG 知识库
    llmSvc.SetKnowledgeBaseService(knowledgeBaseService);

    // 数据库初始化
    var logger = sp.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("正在测试数据库连接...");
    if (await databaseService.TestConnectionAsync())
    {
        logger.LogInformation("数据库连接成功");
        await databaseService.InitializeDatabaseAsync();
    }
    else
    {
        logger.LogWarning("数据库连接失败，请检查配置");
    }

    // 预加载知识库
    var chemicalRAG = new ChemicalRAG(AppConfig.Instance.KnowledgeBase.BasePath, knowledgeBaseService);
    await chemicalRAG.LoadKnowledgeBaseAsync();
    logger.LogInformation("知识库加载完成 ({Count} 条)", knowledgeBaseService.GetDocumentCount());
}

// ═══════════════════════════════════════════════════
// 中间件管线
// ═══════════════════════════════════════════════════

// 全局异常处理（最外层中间件，捕获所有未处理异常）
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseMiddleware<RequestMetricsMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// ── 本地辅助函数 ──
static async Task<bool> CheckOllamaAsync(ILlmService llm)
{
    try
    {
        var testResult = await llm.GetEmbeddingAsync("health check");
        return testResult != null;
    }
    catch
    {
        return false;
    }
}


// ═══════════════════════════════════════════════════
// 健康检查端点 (Task 9c: 增强 DB + Ollama + KB)
// ═══════════════════════════════════════════════════
app.MapGet("/health", async (IDatabaseService db, ILlmService llm, IKnowledgeBaseService kb) =>
{
    var checks = new Dictionary<string, object>();
    bool degraded = false;

    // 1. 数据库检查
    var dbOk = await db.TestConnectionAsync();
    checks["database"] = dbOk ? "connected" : "disconnected";
    if (!dbOk) degraded = true;

    // 2. Ollama 模型检查 (快速 ping)
    var ollamaOk = await CheckOllamaAsync(llm);
    checks["ollama"] = ollamaOk ? "reachable" : "unreachable";
    if (!ollamaOk) degraded = true;

    // 3. 知识库文档数
    var docCount = kb.GetDocumentCount();
    checks["knowledge_base_docs"] = docCount;

    // 4. 指标摘要
    var metrics = MetricsCollector.GetSnapshot();
    checks["llm_calls"] = metrics.LlmCallCount;
    checks["llm_error_rate"] = metrics.LlmCallCount > 0
        ? $"{(double)metrics.LlmErrorCount / metrics.LlmCallCount * 100:F1}%"
        : "N/A";

    var health = new
    {
        status = degraded ? "degraded" : "healthy",
        timestamp = DateTime.UtcNow.ToString("o"),
        version = "2.4.0",
        checks
    };

    return degraded ? Results.Json(health, statusCode: 503) : Results.Ok(health);
});

// K8s readiness probe
app.MapGet("/health/ready", async (IDatabaseService db) =>
{
    var dbOk = await db.TestConnectionAsync();
    return dbOk ? Results.Ok(new { ready = true }) : Results.Json(new { ready = false }, statusCode: 503);
});

// K8s liveness probe
app.MapGet("/health/live", () => Results.Ok(new { alive = true }));

// Prometheus 指标端点 (Task 9d)
app.MapGet("/metrics", () => Results.Content(MetricsCollector.ToPrometheusFormat(), "text/plain; version=0.0.4"));


Console.WriteLine("化工合规 AI Agent API 已启动");
app.Run();
return 0;
