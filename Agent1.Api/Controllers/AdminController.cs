using System.Diagnostics;
using Agent1.Config;
using Agent1.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agent1.Api.Controllers;

/// <summary>
/// 系统管理 API — 数据库验证、服务诊断等运维端点。
/// 映射控制台功能：#2 数据库验证 (DatabaseValidationCommand)。
/// 仅 admin 角色可访问。
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "Admin")]
public class AdminController : ControllerBase
{
    private readonly IDatabaseService _db;
    private readonly ILogger<AdminController> _logger;

    public AdminController(IDatabaseService db, ILogger<AdminController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// 获取数据库基本信息 — 服务器地址、数据库名、表列表。
    /// </summary>
    [HttpGet("db/info")]
    public async Task<IActionResult> GetDatabaseInfo()
    {
        try
        {
            var info = await _db.GetDatabaseInfoAsync();
            var tables = await _db.GetTableNamesAsync();

            return Ok(new
            {
                info,
                tables,
                retrievedAt = DateTime.UtcNow.ToString("o")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取数据库信息失败");
            return StatusCode(500, new { error = $"数据库查询失败: {ex.Message}" });
        }
    }

    /// <summary>
    /// 数据库连接验证 — 测试连接 + 返回完整诊断信息。
    /// 对应 CLI 的 DatabaseValidationCommand.ExecuteAsync()。
    /// </summary>
    [HttpGet("db/validate")]
    public async Task<IActionResult> ValidateDatabase()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var connected = await _db.TestConnectionAsync();
            var info = await _db.GetDatabaseInfoAsync();
            var tables = await _db.GetTableNamesAsync();
            sw.Stop();

            _logger.LogInformation(
                "数据库验证完成: 连接={Connected}, 表数={TableCount}, 耗时={Elapsed}ms",
                connected, tables.Count, sw.ElapsedMilliseconds);

            return Ok(new
            {
                connected,
                server = new
                {
                    host = AppConfig.Instance.Database.Host,
                    port = AppConfig.Instance.Database.Port,
                    database = AppConfig.Instance.Database.DatabaseName,
                    user = AppConfig.Instance.Database.Username
                },
                info,
                tableCount = tables.Count,
                tables,
                elapsedMs = sw.ElapsedMilliseconds,
                verifiedAt = DateTime.UtcNow.ToString("o")
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "数据库验证失败");
            return StatusCode(500, new
            {
                connected = false,
                error = $"验证失败: {ex.Message}",
                elapsedMs = sw.ElapsedMilliseconds
            });
        }
    }
}
