using Agent1;
using Agent1.Config;
using Agent1.Services;
using Agent1.Services.Orchestration;
using Agent1.Services.Logging;
using Agent1.Services.Logging.Sinks;
using Agent1.Services.Monitoring;
using Agent1.Services.Security;
using Agent1.Api.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Agent1.Services.Logging.Enrichers;
using Agent1.Services.Logging.Filters;
using System.Text;

// ═══════════════════════════════════════════════════
// Phase 1: 配置外部化 — appsettings.json + 环境变量
// ═══════════════════════════════════════════════════
var builder = WebApplication.CreateBuilder(args);

// ═══ 监听端口：Docker 通过 ASPNETCORE_URLS 环境变量控制，本地开发默认 5000 ═══
var listenPort = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://0.0.0.0:5000";
builder.WebHost.UseUrls(listenPort);

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
// P2: 启动崩溃日志 — 确保启动阶段异常不静默丢失
// ═══════════════════════════════════════════════════
try
{
// ═══════════════════════════════════════════════════
// 结构化日志 — Serilog（P0 整改: 配置驱动 + Enricher 流水线 + Filter 层）
// ═══════════════════════════════════════════════════
// P0-3: Serilog SelfLog — 记录框架自身异常到独立文件
Serilog.Debugging.SelfLog.Enable(msg =>
{
    try { File.AppendAllText("logs/serilog-self.log", $"{DateTime.UtcNow:O} {msg}{Environment.NewLine}"); }
    catch { /* 静默丢弃 */ }
});

// [P1] 提前创建 AlertDispatcher（Serilog 配置在 DI 之前，需独立实例）
var alertDispatcher = new AlertDispatcher();
alertDispatcher.Register(new ConsoleAlertService());

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(configuration)          // 从 appsettings.json Serilog 节读取
    .Enrich.With<EnvironmentEnricher>()              // MachineName / ProcessId / OSVersion
    .Enrich.With<RunIdEnricher>()                    // RunId / StartTime
    .Enrich.With<SessionEnricher>()                  // SessionId（默认 "none"）
    .Enrich.With<ThreadEnricher>()                   // ThreadId
    .Filter.With<KeywordLogFilter>()                 // 拦截敏感关键词日志
    .WriteTo.Console()
    .WriteTo.File("logs/agent1-api-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7)                   // 保留最近 7 天
    .WriteTo.Sink(new AlertSink(alertDispatcher))    // Critical 日志 → 告警分发
    .WriteTo.Seq(Environment.GetEnvironmentVariable("SEQ_URL") ?? "http://localhost:5341")
    .CreateLogger();

builder.Host.UseSerilog();

// ═══════════════════════════════════════════════════
// 依赖注入
// ═══════════════════════════════════════════════════
builder.Services.AddSingleton(AppConfig.Instance);
// LLM 并发保护: CPU 推理最多 2 个并发
builder.Services.AddSingleton(new SemaphoreSlim(2, 2));
builder.Services.AddSingleton<IDatabaseService, DatabaseService>();
builder.Services.AddSingleton<ISessionService, SessionService>();
builder.Services.AddSingleton<IMemoryService>(sp =>
{
    var llm = sp.GetRequiredService<ILlmService>();
    return new MemoryService(llm);
});

// [P0 Lazy<T>] 循环依赖破解 — Lazy<T> 延迟解析，替代 null! + SetKnowledgeBaseService
builder.Services.AddSingleton<LlmService>(sp => new LlmService(
    new Lazy<IKnowledgeBaseService>(() => sp.GetRequiredService<IKnowledgeBaseService>())));
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
builder.Services.AddSingleton<IAuditService>(sp =>
{
    var db = sp.GetRequiredService<IDatabaseService>();
    return new AuditService(db);
});
builder.Services.AddSingleton<IModuleFactory, ModuleFactory>();
builder.Services.AddSingleton<ModuleDispatcher>();
builder.Services.AddSingleton<ResponseCacheService>();

// [P0 编排API] 业务编排层服务 — 支撑 Inspection/Ticket/ComplianceSummary API
builder.Services.AddSingleton<InspectionOrchestrator>();
builder.Services.AddSingleton<DeterministicRuleEngine>();
builder.Services.AddSingleton<ComplianceRuleEngine>();
builder.Services.AddSingleton<InspectionRepository>();

// [P1 安全加固] Token 黑名单 — 登出时撤销 Access Token
builder.Services.AddSingleton<TokenBlacklistService>();

// [P1] 告警系统 — 注册提前创建的 AlertDispatcher（已含 ConsoleAlertService）
// 补充注册邮件通道（需在 AppConfig.Load 之后，配置已就绪）
var emailCfg = AppConfig.Instance.Alerting.Email;
if (emailCfg.Enabled && !string.IsNullOrWhiteSpace(emailCfg.SmtpHost) && emailCfg.RecipientEmails.Count > 0)
{
    var emailService = new EmailAlertService(
        emailCfg.SmtpHost,
        emailCfg.SmtpPort,
        emailCfg.SenderEmail,
        emailCfg.SenderPassword,
        emailCfg.RecipientEmails,
        enabled: emailCfg.Enabled);
    alertDispatcher.Register(emailService);
    Console.WriteLine($"📧 邮件告警已启用 → {emailCfg.SmtpHost}:{emailCfg.SmtpPort} (收件人:{emailCfg.RecipientEmails.Count}人)");
}
else
{
    Console.WriteLine("⚠️ 邮件告警未启用 — 请设置 ALERT_RECIPIENT_EMAILS 环境变量");
}
builder.Services.AddSingleton(alertDispatcher);

// Phase 2: 长期记忆服务
builder.Services.AddSingleton<ILongTermMemoryService>(sp =>
{
    var db = sp.GetRequiredService<IDatabaseService>();
    var llm = sp.GetRequiredService<ILlmService>();
    return new LongTermMemoryService(db, llm);
});

// Phase 4.1: 记忆协调器
builder.Services.AddSingleton<MemoryCoordinator>(sp =>
{
    var shortMem = sp.GetRequiredService<IMemoryService>();
    var longMem = sp.GetRequiredService<ILongTermMemoryService>();
    var cache = sp.GetRequiredService<ResponseCacheService>();
    var audit = sp.GetRequiredService<IAuditService>();
    return new MemoryCoordinator(shortMem, longMem, cache, audit);
});

// [P3] 知识库增量更新 — 注册 ChemicalRAG 供 API 端点使用
builder.Services.AddSingleton(sp =>
{
    var db = sp.GetRequiredService<IDatabaseService>();
    var kb = sp.GetRequiredService<IKnowledgeBaseService>();
    return new ChemicalRAG(AppConfig.Instance.KnowledgeBase.BasePath, kb, db);
});

// ═══════════════════════════════════════════════════
// JWT 认证 (Task 2)
// ═══════════════════════════════════════════════════
var jwtSection = configuration.GetSection("Jwt");
var jwtKey = jwtSection["Key"];
if (string.IsNullOrEmpty(jwtKey))
    jwtKey = Environment.GetEnvironmentVariable("JWT_KEY");
if (string.IsNullOrEmpty(jwtKey))
{
    if (builder.Environment.IsProduction())
    {
        Console.WriteLine("致命错误: 生产环境必须设置 JWT_KEY 环境变量或 Jwt:Key 配置项");
        Environment.Exit(1);
    }
    jwtKey = "Agent1-Dev-Key-Change-In-Production-2026";
    Console.WriteLine("警告: JWT Key 未配置，使用默认开发密钥。生产环境请设置 JWT_KEY 环境变量。");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // 保留 token 中的原始 claim 类型（不自动映射到微软默认类型）
        options.MapInboundClaims = false;

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)) { KeyId = "agent1" };
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSection["Issuer"] ?? "Agent1",
            ValidAudience = jwtSection["Audience"] ?? "Agent1.Api",
            IssuerSigningKey = signingKey,
            RoleClaimType = System.Security.Claims.ClaimTypes.Role,
            NameClaimType = System.Security.Claims.ClaimTypes.Name
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

// ═══════════════════════════════════════════════════
// CORS 配置 (Task 3.1): 默认允许 localhost，生产通过环境变量配置
// ═══════════════════════════════════════════════════
var corsOrigins = (Environment.GetEnvironmentVariable("CORS_ORIGINS") ?? "http://localhost:3000,http://localhost:5173")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(corsOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// ═══════════════════════════════════════════════════
// OpenTelemetry 分布式追踪 (Task 4.2)
// 导出目标: OTEL_EXPORTER_OTLP_ENDPOINT 环境变量（默认 http://localhost:4317）
// ═══════════════════════════════════════════════════
var otelEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") ?? "http://localhost:4317";
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(
        serviceName: "agent1-api",
        serviceVersion: "2.5.0"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter(o => o.Endpoint = new Uri(otelEndpoint)))
    .WithMetrics(m => m
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter(o => o.Endpoint = new Uri(otelEndpoint)));

// OpenTelemetry 日志桥接（Serilog → OTLP）
builder.Logging.AddOpenTelemetry(o =>
{
    o.IncludeFormattedMessage = true;
    o.IncludeScopes = true;
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

    // [P0 Lazy<T>] SetKnowledgeBaseService 已废弃 — Lazy<T> 自动完成延迟注入

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
    var chemicalRAG = new ChemicalRAG(AppConfig.Instance.KnowledgeBase.BasePath, knowledgeBaseService, databaseService);
    await chemicalRAG.LoadKnowledgeBaseAsync();
    logger.LogInformation("知识库加载完成 ({Count} 条)", knowledgeBaseService.GetDocumentCount());

    // 缓存预热：从评测集占位 50 条热点查询
    var responseCache = sp.GetRequiredService<ResponseCacheService>();
    var evalSetPath = Path.Combine(AppContext.BaseDirectory, AppConfig.Instance.Evaluation.EvalSetPath);
    responseCache.WarmupFromEvalSet(evalSetPath);
}

// ═══════════════════════════════════════════════════
// 中间件管线
// ═══════════════════════════════════════════════════

// HTTPS 重定向 + HSTS（生产环境启用，开发环境跳过）
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
    app.UseHsts();
}

// 全局异常处理（必须是最外层中间件，捕获所有下游中间件的未处理异常）
app.UseMiddleware<GlobalExceptionMiddleware>();

// 请求 ID 透传
app.UseMiddleware<RequestIdMiddleware>();

// 速率限制：100次/分钟/IP+端点 组合限流
app.UseMiddleware<RateLimitingMiddleware>(100, 60);
app.UseMiddleware<RequestMetricsMiddleware>();

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors();

app.UseAuthentication();

// [P1 安全加固] Token 黑名单检查 — 必须在认证之后、授权之前
app.UseMiddleware<TokenBlacklistMiddleware>();

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

// 缓存统计端点
app.MapGet("/cache/stats", (ResponseCacheService cache) =>
{
    var stats = cache.GetStats();
    return Results.Ok(new
    {
        stats.EntryCount,
        stats.TotalHits,
        OldestEntry = stats.OldestEntry != DateTime.MinValue ? stats.OldestEntry.ToString("o") : "N/A",
        NewestEntry = stats.NewestEntry != DateTime.MinValue ? stats.NewestEntry.ToString("o") : "N/A"
    });
});

// 缓存清除端点
app.MapPost("/cache/clear", (ResponseCacheService cache) =>
{
    cache.Clear();
    return Results.Ok(new { message = "缓存已清除" });
});

// ═══════════════════════════════════════════════════
// [P1] 告警测试端点 — 验证 SMTP 邮件通道是否打通
// ═══════════════════════════════════════════════════
app.MapPost("/alert/test", async (AlertDispatcher dispatcher, ILogger<Program> logger) =>
{
    try
    {
        await dispatcher.SendAlertAsync(
            "🧪 Agent1 告警通道测试",
            $"这是一封测试邮件，用于验证告警通道是否正常运作。\n\n" +
            $"发送时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
            $"RunId: {RunIdGenerator.Current}\n" +
            $"机器名: {Environment.MachineName}\n\n" +
            $"如果你收到这封邮件，说明告警通道已成功打通 ✅",
            AlertLevel.Info);

        logger.LogInformation("告警测试邮件已发送");
        return Results.Ok(new
        {
            message = "测试告警已发送，请检查邮箱 lcy.050801@qq.com",
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            runId = RunIdGenerator.Current
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "告警测试发送失败");
        return Results.Json(new { error = $"发送失败: {ex.Message}" }, statusCode: 500);
    }
});

// ═══════════════════════════════════════════════════
// [P3] 知识库增量更新端点 — 仅处理新增/修改/删除的文件
// ═══════════════════════════════════════════════════
app.MapPost("/knowledgebase/incremental-update", async (ChemicalRAG chemicalRAG, IKnowledgeBaseService kb) =>
{
    await chemicalRAG.LoadKnowledgeBaseIncrementalAsync();
    return Results.Ok(new { message = "增量更新完成", documentCount = kb.GetDocumentCount() });
});

// ═══════════════════════════════════════════════════
// Phase 1.4: 短期记忆统计端点
// ═══════════════════════════════════════════════════
app.MapGet("/memory/stats", (IMemoryService memory, string? sessionId) =>
{
    if (!string.IsNullOrWhiteSpace(sessionId))
        memory.SetSession(sessionId);
    var stats = memory.GetSessionStats();
    return Results.Ok(stats);
});

// 记忆清除端点（按 sessionId）
app.MapPost("/memory/clear", (IMemoryService memory, string? sessionId) =>
{
    if (!string.IsNullOrWhiteSpace(sessionId))
        memory.SetSession(sessionId);
    memory.ClearMemory();
    return Results.Ok(new { message = "会话记忆已清除", sessionId = sessionId ?? "default" });
});

// ═══════════════════════════════════════════════════
// Phase 2-3: 长期记忆端点
// ═══════════════════════════════════════════════════
app.MapGet("/memory/long-term/stats", async (ILongTermMemoryService ltm, string? userId) =>
{
    var stats = await ltm.GetStatsAsync(userId ?? "default");
    return Results.Ok(stats);
});

app.MapGet("/memory/long-term/search", async (ILongTermMemoryService ltm, string? userId, string? q) =>
{
    if (string.IsNullOrWhiteSpace(q))
        return Results.BadRequest(new { error = "搜索关键词 q 不能为空" });
    var results = await ltm.SearchByKeywordAsync(userId ?? "default", q, 10);
    return Results.Ok(new { keyword = q, count = results.Count, results });
});

app.MapPost("/memory/long-term/cleanup", async (ILongTermMemoryService ltm, int? retentionDays) =>
{
    var deleted = await ltm.CleanupAsync(retentionDays ?? 180);
    return Results.Ok(new { deleted, message = $"已清理 {deleted} 条过期记忆" });
});


// ═══════════════════════════════════════════════════
// 优雅关闭 (Task 3.2)
// ═══════════════════════════════════════════════════
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    Console.WriteLine("收到停止信号，正在优雅关闭...");
    Log.Information("应用程序正在关闭，等待现有请求完成（最多30秒）...");
    Log.CloseAndFlush();
});

Console.WriteLine("化工合规 AI Agent API 已启动");

// P2: 启动配置摘要（脱敏后输出关键配置项）
Log.Information("[配置摘要] LLM={ModelId} DB={DbHost}:{DbPort}/{DbName} Vector={IndexType} Search={SearchMode} RunId={RunId}",
    AppConfig.Instance.Llm.ModelId,
    AppConfig.Instance.Database.Host,
    AppConfig.Instance.Database.Port,
    AppConfig.Instance.Database.DatabaseName,
    AppConfig.Instance.VectorSearch.IndexType,
    AppConfig.Instance.KnowledgeBase.SearchMode,
    RunIdGenerator.Current);

app.Run();
return 0;
}
catch (Exception ex)
{
    // P2: 启动崩溃日志 — 最外层 try-catch 确保启动期异常可追溯
    var crashLog = Path.Combine(AppContext.BaseDirectory, "startup-crash.log");
    try
    {
        File.AppendAllText(crashLog,
            $"[{DateTime.UtcNow:O}] 启动崩溃{Environment.NewLine}" +
            $"类型: {ex.GetType().FullName}{Environment.NewLine}" +
            $"消息: {ex.Message}{Environment.NewLine}" +
            $"堆栈: {ex.StackTrace}{Environment.NewLine}");
    }
    catch { /* 连崩溃日志都写不了，尽力了 */ }
    Console.Error.WriteLine($"致命错误: {ex.Message}");
    return 1;
}
