using System.Collections.Concurrent;
using Agent1.Services.Logging;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Agent1.Services.Monitoring;

/// <summary>
/// 邮件告警通道 — 使用 MailKit + SMTP 发送 HTML 格式告警邮件。
/// 
/// 配置来源：appsettings.json 或环境变量
///   - Alerting:Email:Enabled       (bool)
///   - Alerting:Email:SmtpHost      (string)
///   - Alerting:Email:SmtpPort      (int, 默认 587)
///   - Alerting:Email:SenderEmail   (string)
///   - Alerting:Email:SenderPassword(string, 建议通过环境变量注入)
///   - Alerting:Email:RecipientEmails(string, 逗号分隔)
/// 
/// 安全机制：
///   - 端口 465 → SslOnConnect；端口 587 → StartTls
///   - 同类告警 60s 内防抖（由 AlertDispatcher 统一管理）
/// </summary>
public class EmailAlertService : IAlertService
{
    private readonly string _smtpHost;
    private readonly int _smtpPort;
    private readonly string _senderEmail;
    private readonly string _senderPassword;
    private readonly List<string> _recipientEmails;
    private readonly bool _enabled;

    public bool IsEnabled => _enabled;

    public EmailAlertService(
        string smtpHost,
        int smtpPort,
        string senderEmail,
        string senderPassword,
        List<string> recipientEmails,
        bool enabled = true)
    {
        _smtpHost = smtpHost;
        _smtpPort = smtpPort;
        _senderEmail = senderEmail;
        _senderPassword = senderPassword;
        _recipientEmails = recipientEmails;
        _enabled = enabled && !string.IsNullOrWhiteSpace(smtpHost) &&
                   !string.IsNullOrWhiteSpace(senderEmail) &&
                   recipientEmails.Count > 0;
    }

    public async Task SendAlertAsync(string title, string message, AlertLevel level)
    {
        if (!_enabled)
            return;

        using var email = new MimeMessage();
        email.From.Add(new MailboxAddress("Agent1 告警系统", _senderEmail));
        foreach (var recipient in _recipientEmails)
        {
            email.To.Add(MailboxAddress.Parse(recipient));
        }

        email.Subject = $"[Agent1 {level.ToString().ToUpper()}] {title}";

        // HTML 格式正文
        var levelColor = level switch
        {
            AlertLevel.Critical => "#dc3545",
            AlertLevel.Warning => "#fd7e14",
            _ => "#17a2b8"
        };
        var levelLabel = level switch
        {
            AlertLevel.Critical => "严重",
            AlertLevel.Warning => "警告",
            _ => "通知"
        };

        email.Body = new TextPart("html")
        {
            Text = $"""
            <html>
            <body style="font-family: 'Microsoft YaHei', sans-serif; max-width: 600px; margin: 0 auto;">
                <div style="background: {levelColor}; color: white; padding: 12px 20px; border-radius: 8px 8px 0 0;">
                    <h2 style="margin: 0;">⚠️ Agent1 — {levelLabel}告警</h2>
                </div>
                <div style="border: 1px solid #e0e0e0; border-top: none; padding: 20px; border-radius: 0 0 8px 8px;">
                    <h3>{System.Net.WebUtility.HtmlEncode(title)}</h3>
                    <pre style="background: #f8f9fa; padding: 12px; border-radius: 4px; white-space: pre-wrap;">{System.Net.WebUtility.HtmlEncode(message)}</pre>
                    <p style="color: #6c757d; font-size: 12px; margin-top: 16px;">
                        发送时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss} | RunId: {RunIdGenerator.Current}
                    </p>
                </div>
            </body>
            </html>
            """
        };

        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(_smtpHost, _smtpPort,
            _smtpPort == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls);
        await smtp.AuthenticateAsync(_senderEmail, _senderPassword);
        await smtp.SendAsync(email);
        await smtp.DisconnectAsync(true);
    }
}
