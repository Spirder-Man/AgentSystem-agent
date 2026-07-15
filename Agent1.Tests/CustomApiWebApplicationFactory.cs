using System;
using System.Linq;
using System.Text.Json;
using Agent1.Services;
using Agent1.Services.Orchestration;
using Agent1.Tests.Stubs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Agent1.Tests;

/// <summary>
/// 自定义 WebApplicationFactory — 替换外部依赖（PostgreSQL / llama.cpp）为内存存根。
///
/// 使用方式：
///   var factory = new CustomApiWebApplicationFactory();
///   var client = factory.CreateClient();
///
/// 测试账号（由 AUTH_ACCOUNTS_JSON 环境变量注入）：
///   admin   / Admin@123  → role: admin
///   auditor / Audit@456  → role: auditor
///   viewer  / View@789   → role: viewer
/// </summary>
public class CustomApiWebApplicationFactory : WebApplicationFactory<Agent1.Api.Program>
{
    private readonly StubDatabaseService _stubDb = new();
    private readonly StubLlmService _stubLlm = new();

    public StubDatabaseService StubDb => _stubDb;
    public StubLlmService StubLlm => _stubLlm;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // 注入测试账号（明文密码，AuthController 会自动 BCrypt 升级）
        Environment.SetEnvironmentVariable("AUTH_ACCOUNTS_JSON", JsonSerializer.Serialize(new[]
        {
            new { Username = "admin",   Password = "Admin@123", Role = "admin" },
            new { Username = "auditor", Password = "Audit@456", Role = "auditor" },
            new { Username = "viewer",  Password = "View@789",  Role = "viewer" }
        }));

        builder.ConfigureServices(services =>
        {
            // ── 替换 IDatabaseService ──
            services.RemoveAll<IDatabaseService>();
            services.AddSingleton<IDatabaseService>(_stubDb);

            // ── 替换 ILlmService + LlmService ──
            services.RemoveAll<ILlmService>();
            services.RemoveAll<LlmService>();
            services.AddSingleton<ILlmService>(_stubLlm);
            // 同时注册为 LlmService 类型（供代码中 as LlmService 转型使用）
            // 注：StubLlmService 不继承 LlmService，转型返回 null 是安全的

            // ── 移除后台服务（避免测试中干扰） ──
            RemoveHostedService<SessionCleanupHostedService>(services);
        });
    }

    /// <summary>移除指定类型的 IHostedService 注册</summary>
    private static void RemoveHostedService<T>(IServiceCollection services) where T : class, IHostedService
    {
        var descriptors = services
            .Where(d => d.ServiceType == typeof(IHostedService) || d.ServiceType == typeof(T))
            .Where(d => d.ImplementationType == typeof(T) || d.ServiceType == typeof(T))
            .ToList();
        foreach (var d in descriptors)
            services.Remove(d);
    }
}
