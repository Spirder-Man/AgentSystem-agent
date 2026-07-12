using System.ComponentModel.DataAnnotations;
using Agent1.Services.Monitoring;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agent1.Api.Controllers;

/// <summary>
/// 告警通道 API — 测试告警邮件发送。
/// 覆盖控制台功能：#20 测试告警邮件。
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "Auditor")]
public class AlertsController : ControllerBase
{
    private readonly AlertDispatcher _alertDispatcher;
    private readonly ILogger<AlertsController> _logger;

    public AlertsController(AlertDispatcher alertDispatcher, ILogger<AlertsController> logger)
    {
        _alertDispatcher = alertDispatcher;
        _logger = logger;
    }

    /// <summary>
    /// 发送测试告警邮件 — 验证 SMTP 告警通道是否正常运作。
    /// 支持自定义收件人，默认使用配置中的收件人列表。
    /// </summary>
    [HttpPost("test")]
    public async Task<IActionResult> SendTestAlert([FromBody] TestAlertRequest request)
    {
        try
        {
            var title = string.IsNullOrWhiteSpace(request.Title)
                ? "🧪 Agent1 告警通道测试"
                : request.Title;

            var message = $"这是一封测试邮件，用于验证告警通道是否正常运作。\n\n"
                + $"发送时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n"
                + $"机器名: {Environment.MachineName}\n"
                + $"触发用户: {User.Identity?.Name ?? "unknown"}\n"
                + $"自定义消息: {request.Message ?? "(无)"}\n\n"
                + $"如果你收到这封邮件，说明告警通道已成功打通 ✅";

            await _alertDispatcher.SendAlertAsync(title, message, AlertLevel.Info);

            _logger.LogInformation("测试告警已发送 → 标题: {Title}, 触发用户: {User}",
                title, User.Identity?.Name);

            return Ok(new
            {
                sent = true,
                title,
                timestamp = DateTime.Now.ToString("O"),
                note = "请检查收件箱确认收到"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "测试告警发送失败");
            return StatusCode(500, new { sent = false, error = $"发送失败: {ex.Message}" });
        }
    }
}

public record TestAlertRequest(
    string? Title,
    string? Message
);
