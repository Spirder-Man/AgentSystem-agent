using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agent1.Services.Logging;
using Agent1.Services.Logging.Enrichers;
using Agent1.Services.Logging.Filters;
using Agent1.Services.Logging.Sinks;
using Agent1.Services.Monitoring;
using FluentAssertions;
using Moq;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;
using Xunit;

namespace Agent1.Tests;

// ============================================================================
// L7 层 — 可观测性与监控测试
// ============================================================================

/// <summary>
/// Simple ILogEventPropertyFactory for unit testing Serilog enrichers.
/// Avoids Moq expression tree issues with optional parameters.
/// </summary>
internal sealed class TestLogEventPropertyFactory : ILogEventPropertyFactory
{
    public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false)
    {
        return new LogEventProperty(name, new ScalarValue(value));
    }
}

#region Enricher Tests

public class EnvironmentEnricherTests
{
    private static LogEvent CreateLogEvent()
    {
        return new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            null,
            MessageTemplate.Empty,
            new List<LogEventProperty>());
    }

    private static ILogEventPropertyFactory CreatePropertyFactory()
    {
        return new TestLogEventPropertyFactory();
    }

    [Fact]
    public void Enrich_AddsMachineName()
    {
        var enricher = new EnvironmentEnricher();
        var logEvent = CreateLogEvent();
        var factory = CreatePropertyFactory();

        enricher.Enrich(logEvent, factory);

        logEvent.Properties.Should().ContainKey("MachineName");
        logEvent.Properties["MachineName"].Should().BeOfType<ScalarValue>();
        ((ScalarValue)logEvent.Properties["MachineName"]).Value.Should().Be(Environment.MachineName);
    }

    [Fact]
    public void Enrich_AddsProcessId()
    {
        var enricher = new EnvironmentEnricher();
        var logEvent = CreateLogEvent();
        var factory = CreatePropertyFactory();

        enricher.Enrich(logEvent, factory);

        logEvent.Properties.Should().ContainKey("ProcessId");
        ((ScalarValue)logEvent.Properties["ProcessId"]).Value.Should().Be(Environment.ProcessId);
    }

    [Fact]
    public void Enrich_AddsOSVersion()
    {
        var enricher = new EnvironmentEnricher();
        var logEvent = CreateLogEvent();
        var factory = CreatePropertyFactory();

        enricher.Enrich(logEvent, factory);

        logEvent.Properties.Should().ContainKey("OSVersion");
        ((ScalarValue)logEvent.Properties["OSVersion"]).Value.Should().Be(Environment.OSVersion.VersionString);
    }

    [Fact]
    public void Enrich_DoesNotOverwriteExistingProperties()
    {
        var enricher = new EnvironmentEnricher();
        var existingProp = new LogEventProperty("MachineName", new ScalarValue("existing-value"));
        var logEvent = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            null,
            MessageTemplate.Empty,
            new[] { existingProp });
        var factory = CreatePropertyFactory();

        enricher.Enrich(logEvent, factory);

        // AddPropertyIfAbsent should not overwrite
        ((ScalarValue)logEvent.Properties["MachineName"]).Value.Should().Be("existing-value");
    }
}

public class RunIdEnricherTests
{
    private static LogEvent CreateLogEvent()
    {
        return new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            null,
            MessageTemplate.Empty,
            new List<LogEventProperty>());
    }

    private static ILogEventPropertyFactory CreatePropertyFactory()
    {
        return new TestLogEventPropertyFactory();
    }

    [Fact]
    public void Enrich_AddsRunId()
    {
        var enricher = new RunIdEnricher();
        var logEvent = CreateLogEvent();
        var factory = CreatePropertyFactory();

        enricher.Enrich(logEvent, factory);

        logEvent.Properties.Should().ContainKey("RunId");
        var runId = ((ScalarValue)logEvent.Properties["RunId"]).Value as string;
        runId.Should().NotBeNullOrEmpty();
        runId.Should().Be(RunIdGenerator.Current);
    }

    [Fact]
    public void Enrich_AddsStartTime()
    {
        var enricher = new RunIdEnricher();
        var logEvent = CreateLogEvent();
        var factory = CreatePropertyFactory();

        enricher.Enrich(logEvent, factory);

        logEvent.Properties.Should().ContainKey("StartTime");
        var startTime = ((ScalarValue)logEvent.Properties["StartTime"]).Value as string;
        startTime.Should().NotBeNullOrEmpty();
        startTime.Should().Be(RunIdGenerator.StartTime.ToString("O"));
    }

    [Fact]
    public void Enrich_DoesNotOverwriteExistingRunId()
    {
        var enricher = new RunIdEnricher();
        const string existing = "existing-run-id";
        var logEvent = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            null,
            MessageTemplate.Empty,
            new[] { new LogEventProperty("RunId", new ScalarValue(existing)) });
        var factory = CreatePropertyFactory();

        enricher.Enrich(logEvent, factory);

        ((ScalarValue)logEvent.Properties["RunId"]).Value.Should().Be(existing);
    }
}

public class SessionEnricherTests
{
    private static LogEvent CreateLogEvent()
    {
        return new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            null,
            MessageTemplate.Empty,
            new List<LogEventProperty>());
    }

    private static ILogEventPropertyFactory CreatePropertyFactory()
    {
        return new TestLogEventPropertyFactory();
    }

    [Fact]
    public void Enrich_AddsDefaultSessionId()
    {
        var enricher = new SessionEnricher();
        var logEvent = CreateLogEvent();
        var factory = CreatePropertyFactory();

        enricher.Enrich(logEvent, factory);

        logEvent.Properties.Should().ContainKey("SessionId");
        ((ScalarValue)logEvent.Properties["SessionId"]).Value.Should().Be("none");
    }

    [Fact]
    public void Enrich_DoesNotOverwriteExistingSessionId()
    {
        var enricher = new SessionEnricher();
        const string existing = "session-abc-123";
        var logEvent = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            null,
            MessageTemplate.Empty,
            new[] { new LogEventProperty("SessionId", new ScalarValue(existing)) });
        var factory = CreatePropertyFactory();

        enricher.Enrich(logEvent, factory);

        ((ScalarValue)logEvent.Properties["SessionId"]).Value.Should().Be(existing);
    }
}

public class ThreadEnricherTests
{
    private static LogEvent CreateLogEvent()
    {
        return new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            null,
            MessageTemplate.Empty,
            new List<LogEventProperty>());
    }

    private static ILogEventPropertyFactory CreatePropertyFactory()
    {
        return new TestLogEventPropertyFactory();
    }

    [Fact]
    public void Enrich_AddsThreadId()
    {
        var enricher = new ThreadEnricher();
        var logEvent = CreateLogEvent();
        var factory = CreatePropertyFactory();

        enricher.Enrich(logEvent, factory);

        logEvent.Properties.Should().ContainKey("ThreadId");
        var threadId = ((ScalarValue)logEvent.Properties["ThreadId"]).Value;
        threadId.Should().Be(Environment.CurrentManagedThreadId);
    }

    [Fact]
    public void Enrich_DoesNotOverwriteExistingThreadId()
    {
        var enricher = new ThreadEnricher();
        const int existing = 999;
        var logEvent = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            null,
            MessageTemplate.Empty,
            new[] { new LogEventProperty("ThreadId", new ScalarValue(existing)) });
        var factory = CreatePropertyFactory();

        enricher.Enrich(logEvent, factory);

        ((ScalarValue)logEvent.Properties["ThreadId"]).Value.Should().Be(existing);
    }

    [Fact]
    public void Enrich_ThreadIdIsPositive()
    {
        var enricher = new ThreadEnricher();
        var logEvent = CreateLogEvent();
        var factory = CreatePropertyFactory();

        enricher.Enrich(logEvent, factory);

        var threadId = (int)((ScalarValue)logEvent.Properties["ThreadId"]).Value;
        threadId.Should().BeGreaterThan(0);
    }
}

#endregion

#region KeywordLogFilter Tests

public class KeywordLogFilterTests
{
    private static LogEvent CreateLogEvent(string message, Dictionary<string, object>? properties = null)
    {
        var template = new MessageTemplateParser().Parse(message);
        var logEventProperties = new List<LogEventProperty>();

        if (properties != null)
        {
            foreach (var kvp in properties)
            {
                logEventProperties.Add(new LogEventProperty(kvp.Key, new ScalarValue(kvp.Value)));
            }
        }

        return new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            null,
            template,
            logEventProperties);
    }

    [Fact]
    public void IsEnabled_AllowsNormalMessage()
    {
        var filter = new KeywordLogFilter();
        var logEvent = CreateLogEvent("化学品合规检查已完成");

        filter.IsEnabled(logEvent).Should().BeTrue();
    }

    [Fact]
    public void IsEnabled_FiltersPasswordInMessage()
    {
        var filter = new KeywordLogFilter();
        var logEvent = CreateLogEvent("用户登录 password=admin123");

        filter.IsEnabled(logEvent).Should().BeFalse();
    }

    [Fact]
    public void IsEnabled_FiltersTokenInMessage()
    {
        var filter = new KeywordLogFilter();
        var logEvent = CreateLogEvent("API调用 token=eyJhbGciOi...");

        filter.IsEnabled(logEvent).Should().BeFalse();
    }

    [Fact]
    public void IsEnabled_FiltersSecretInProperties()
    {
        var filter = new KeywordLogFilter();
        var logEvent = CreateLogEvent("配置加载完成", new Dictionary<string, object>
        {
            ["ApiSecret"] = "my-secret-key-value"
        });

        filter.IsEnabled(logEvent).Should().BeFalse();
    }

    [Fact]
    public void IsEnabled_FiltersApiKeyInProperties()
    {
        var filter = new KeywordLogFilter();
        // Filter checks property VALUES, not keys. Value must contain the sensitive keyword.
        var logEvent = CreateLogEvent("外部服务调用", new Dictionary<string, object>
        {
            ["Credential"] = "api_key=sk-abc123def456"
        });

        filter.IsEnabled(logEvent).Should().BeFalse();
    }

    [Fact]
    public void IsEnabled_FiltersConnectionStringInProperties()
    {
        var filter = new KeywordLogFilter();
        var logEvent = CreateLogEvent("数据库连接", new Dictionary<string, object>
        {
            ["ConnStr"] = "Server=localhost;connectionstring=data"
        });

        filter.IsEnabled(logEvent).Should().BeFalse();
    }

    [Fact]
    public void IsEnabled_IsCaseInsensitive()
    {
        var filter = new KeywordLogFilter();

        filter.IsEnabled(CreateLogEvent("PASSWORD=test")).Should().BeFalse();
        filter.IsEnabled(CreateLogEvent("Password=test")).Should().BeFalse();
        filter.IsEnabled(CreateLogEvent("TOKEN=test")).Should().BeFalse();
        filter.IsEnabled(CreateLogEvent("Token=test")).Should().BeFalse();
    }

    [Fact]
    public void IsEnabled_FiltersChineseSensitiveKeywords()
    {
        var filter = new KeywordLogFilter();

        filter.IsEnabled(CreateLogEvent("用户密码是123456")).Should().BeFalse();
        filter.IsEnabled(CreateLogEvent("系统密钥已更新")).Should().BeFalse();
        filter.IsEnabled(CreateLogEvent("访问令牌过期了")).Should().BeFalse();
    }

    [Fact]
    public void IsEnabled_AllowsSafeTechnicalTerms()
    {
        var filter = new KeywordLogFilter();

        filter.IsEnabled(CreateLogEvent("用户身份验证通过")).Should().BeTrue();
        filter.IsEnabled(CreateLogEvent("化学品储存安全检查完成")).Should().BeTrue();
        filter.IsEnabled(CreateLogEvent("GB15603-1995 标准匹配成功")).Should().BeTrue();
    }

    [Fact]
    public void IsEnabled_FiltersPrivateKeyInProperties()
    {
        var filter = new KeywordLogFilter();
        // Filter checks for "privatekey" as a substring (case-insensitive)
        var logEvent = CreateLogEvent("证书加载", new Dictionary<string, object>
        {
            ["KeyData"] = "-----BEGIN RSA PRIVATEKEY-----"
        });

        filter.IsEnabled(logEvent).Should().BeFalse();
    }

    [Fact]
    public void IsEnabled_FiltersAuthorizationHeader()
    {
        var filter = new KeywordLogFilter();
        var logEvent = CreateLogEvent("HTTP请求", new Dictionary<string, object>
        {
            ["Header"] = "authorization: Bearer xyz"
        });

        filter.IsEnabled(logEvent).Should().BeFalse();
    }

    [Fact]
    public void IsEnabled_AllowsNonStringScalarValues()
    {
        var filter = new KeywordLogFilter();
        var logEvent = CreateLogEvent("数值计算完成", new Dictionary<string, object>
        {
            ["Result"] = 42,
            ["Duration"] = 1.5
        });

        // Non-string scalar values shouldn't cause issues
        filter.IsEnabled(logEvent).Should().BeTrue();
    }
}

#endregion

#region AlertSink Tests

public class AlertSinkTests
{
    [Fact]
    public async Task Emit_FatalLevel_DispatchesAlert()
    {
        // Arrange: create dispatcher with mock alert service
        var mockService = new Mock<IAlertService>();
        mockService.Setup(s => s.IsEnabled).Returns(true);
        var dispatcher = new AlertDispatcher();
        dispatcher.Register(mockService.Object);

        var sink = new AlertSink(dispatcher);
        var logEvent = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Fatal,
            null,
            new MessageTemplateParser().Parse("LLM服务不可用"),
            new List<LogEventProperty>());

        // Act
        sink.Emit(logEvent);

        // Allow fire-and-forget to complete
        await Task.Delay(200);

        // Assert
        mockService.Verify(
            s => s.SendAlertAsync(
                It.Is<string>(t => t.Contains("LLM服务不可用")),
                It.IsAny<string>(),
                AlertLevel.Critical),
            Times.AtLeastOnce);
    }

    [Fact]
    public void Emit_ErrorLevel_DoesNotDispatch()
    {
        var mockService = new Mock<IAlertService>();
        mockService.Setup(s => s.IsEnabled).Returns(true);
        var dispatcher = new AlertDispatcher();
        dispatcher.Register(mockService.Object);

        var sink = new AlertSink(dispatcher);
        var logEvent = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Error,
            null,
            new MessageTemplateParser().Parse("某操作失败"),
            new List<LogEventProperty>());

        sink.Emit(logEvent);

        // Error < Fatal, should NOT trigger
        mockService.Verify(
            s => s.SendAlertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AlertLevel>()),
            Times.Never);
    }

    [Fact]
    public void Emit_WarningLevel_DoesNotDispatch()
    {
        var mockService = new Mock<IAlertService>();
        mockService.Setup(s => s.IsEnabled).Returns(true);
        var dispatcher = new AlertDispatcher();
        dispatcher.Register(mockService.Object);

        var sink = new AlertSink(dispatcher);
        var logEvent = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Warning,
            null,
            new MessageTemplateParser().Parse("连接池使用率 > 80%"),
            new List<LogEventProperty>());

        sink.Emit(logEvent);

        mockService.Verify(
            s => s.SendAlertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AlertLevel>()),
            Times.Never);
    }

    [Fact]
    public async Task Emit_WithException_StillDispatches()
    {
        var mockService = new Mock<IAlertService>();
        mockService.Setup(s => s.IsEnabled).Returns(true);
        var dispatcher = new AlertDispatcher();
        dispatcher.Register(mockService.Object);

        var sink = new AlertSink(dispatcher);
        var exception = new InvalidOperationException("数据库连接断开");
        var logEvent = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Fatal,
            exception,
            new MessageTemplateParser().Parse("数据库异常"),
            new List<LogEventProperty>());

        sink.Emit(logEvent);
        await Task.Delay(200);

        mockService.Verify(
            s => s.SendAlertAsync(
                It.IsAny<string>(),
                It.Is<string>(m => m.Contains("InvalidOperationException")),
                AlertLevel.Critical),
            Times.AtLeastOnce);
    }
}

#endregion

#region AlertDispatcher Tests

public class AlertDispatcherTests
{
    [Fact]
    public async Task SendAlertAsync_FanoutsToAllRegisteredServices()
    {
        var service1 = new Mock<IAlertService>();
        service1.Setup(s => s.IsEnabled).Returns(true);
        var service2 = new Mock<IAlertService>();
        service2.Setup(s => s.IsEnabled).Returns(true);

        var dispatcher = new AlertDispatcher();
        dispatcher.Register(service1.Object);
        dispatcher.Register(service2.Object);

        await dispatcher.SendAlertAsync("测试告警", "测试消息", AlertLevel.Warning);

        service1.Verify(s => s.SendAlertAsync("测试告警", "测试消息", AlertLevel.Warning), Times.Once);
        service2.Verify(s => s.SendAlertAsync("测试告警", "测试消息", AlertLevel.Warning), Times.Once);
    }

    [Fact]
    public async Task SendAlertAsync_SkipsDisabledServices()
    {
        var enabledService = new Mock<IAlertService>();
        enabledService.Setup(s => s.IsEnabled).Returns(true);
        var disabledService = new Mock<IAlertService>();
        disabledService.Setup(s => s.IsEnabled).Returns(false);

        var dispatcher = new AlertDispatcher();
        dispatcher.Register(enabledService.Object);
        dispatcher.Register(disabledService.Object);

        await dispatcher.SendAlertAsync("测试", "消息", AlertLevel.Info);

        enabledService.Verify(s => s.SendAlertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AlertLevel>()), Times.Once);
        disabledService.Verify(s => s.SendAlertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AlertLevel>()), Times.Never);
    }

    [Fact]
    public async Task SendAlertAsync_DebouncesWithin60Seconds()
    {
        var service = new Mock<IAlertService>();
        service.Setup(s => s.IsEnabled).Returns(true);
        var dispatcher = new AlertDispatcher();
        dispatcher.Register(service.Object);

        // First call should go through
        await dispatcher.SendAlertAsync("重复告警", "第一次", AlertLevel.Critical);
        service.Verify(s => s.SendAlertAsync("重复告警", "第一次", AlertLevel.Critical), Times.Once);

        // Second call with same title within 60s should be suppressed
        await dispatcher.SendAlertAsync("重复告警", "第二次", AlertLevel.Critical);
        service.Verify(s => s.SendAlertAsync("重复告警", "第二次", AlertLevel.Critical), Times.Never);
    }

    [Fact]
    public async Task SendAlertAsync_DifferentTitlesNotDebounced()
    {
        var service = new Mock<IAlertService>();
        service.Setup(s => s.IsEnabled).Returns(true);
        var dispatcher = new AlertDispatcher();
        dispatcher.Register(service.Object);

        await dispatcher.SendAlertAsync("告警A", "消息A", AlertLevel.Warning);
        await dispatcher.SendAlertAsync("告警B", "消息B", AlertLevel.Warning);

        service.Verify(s => s.SendAlertAsync("告警A", It.IsAny<string>(), It.IsAny<AlertLevel>()), Times.Once);
        service.Verify(s => s.SendAlertAsync("告警B", It.IsAny<string>(), It.IsAny<AlertLevel>()), Times.Once);
    }

    [Fact]
    public async Task SendAlertAsync_OneChannelFailureDoesNotAffectOthers()
    {
        var failingService = new Mock<IAlertService>();
        failingService.Setup(s => s.IsEnabled).Returns(true);
        failingService.Setup(s => s.SendAlertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AlertLevel>()))
            .ThrowsAsync(new InvalidOperationException("SMTP连接失败"));

        var workingService = new Mock<IAlertService>();
        workingService.Setup(s => s.IsEnabled).Returns(true);

        var dispatcher = new AlertDispatcher();
        dispatcher.Register(failingService.Object);
        dispatcher.Register(workingService.Object);

        // Should not throw despite one channel failing
        await dispatcher.Invoking(d => d.SendAlertAsync("测试", "消息", AlertLevel.Critical))
            .Should().NotThrowAsync();

        // Working service should still receive the alert
        workingService.Verify(s => s.SendAlertAsync("测试", "消息", AlertLevel.Critical), Times.Once);
    }

    [Fact]
    public async Task SendAlertAsync_NoRegisteredServices_DoesNotThrow()
    {
        var dispatcher = new AlertDispatcher();

        await dispatcher.Invoking(d => d.SendAlertAsync("测试", "消息", AlertLevel.Info))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendAlertAsync_AllLevelsSupported()
    {
        var service = new Mock<IAlertService>();
        service.Setup(s => s.IsEnabled).Returns(true);
        var dispatcher = new AlertDispatcher();
        dispatcher.Register(service.Object);

        await dispatcher.SendAlertAsync("Info", "msg", AlertLevel.Info);
        await dispatcher.SendAlertAsync("Warning", "msg", AlertLevel.Warning);
        await dispatcher.SendAlertAsync("Critical", "msg", AlertLevel.Critical);

        service.Verify(s => s.SendAlertAsync("Info", It.IsAny<string>(), AlertLevel.Info), Times.Once);
        service.Verify(s => s.SendAlertAsync("Warning", It.IsAny<string>(), AlertLevel.Warning), Times.Once);
        service.Verify(s => s.SendAlertAsync("Critical", It.IsAny<string>(), AlertLevel.Critical), Times.Once);
    }
}

#endregion

#region ConsoleAlertService Tests

public class ConsoleAlertServiceTests
{
    [Fact]
    public void IsEnabled_AlwaysReturnsTrue()
    {
        var service = new ConsoleAlertService();
        service.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task SendAlertAsync_ReturnsCompletedTask()
    {
        var service = new ConsoleAlertService();
        var task = service.SendAlertAsync("测试标题", "测试消息", AlertLevel.Info);

        await task;
        task.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task SendAlertAsync_DoesNotThrow_ForAllLevels()
    {
        var service = new ConsoleAlertService();

        await service.Invoking(s => s.SendAlertAsync("T", "M", AlertLevel.Info))
            .Should().NotThrowAsync();
        await service.Invoking(s => s.SendAlertAsync("T", "M", AlertLevel.Warning))
            .Should().NotThrowAsync();
        await service.Invoking(s => s.SendAlertAsync("T", "M", AlertLevel.Critical))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendAlertAsync_HandlesMultilineMessage()
    {
        var service = new ConsoleAlertService();
        var multilineMessage = "第一行\n第二行\n第三行";

        await service.Invoking(s => s.SendAlertAsync("多行测试", multilineMessage, AlertLevel.Warning))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendAlertAsync_HandlesEmptyMessage()
    {
        var service = new ConsoleAlertService();

        await service.Invoking(s => s.SendAlertAsync("空消息", "", AlertLevel.Info))
            .Should().NotThrowAsync();
    }
}

#endregion

#region EmailAlertService Tests

public class EmailAlertServiceTests
{
    [Fact]
    public void Constructor_WithValidConfig_IsEnabled()
    {
        var service = new EmailAlertService(
            smtpHost: "smtp.example.com",
            smtpPort: 587,
            senderEmail: "alert@example.com",
            senderPassword: "password123",
            recipientEmails: new List<string> { "admin@example.com" },
            enabled: true);

        service.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Constructor_WithEmptySmtpHost_IsDisabled()
    {
        var service = new EmailAlertService(
            smtpHost: "",
            smtpPort: 587,
            senderEmail: "alert@example.com",
            senderPassword: "pass",
            recipientEmails: new List<string> { "admin@example.com" },
            enabled: true);

        service.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void Constructor_WithNullSmtpHost_IsDisabled()
    {
        var service = new EmailAlertService(
            smtpHost: null!,
            smtpPort: 587,
            senderEmail: "alert@example.com",
            senderPassword: "pass",
            recipientEmails: new List<string> { "admin@example.com" },
            enabled: true);

        service.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void Constructor_WithEmptySenderEmail_IsDisabled()
    {
        var service = new EmailAlertService(
            smtpHost: "smtp.example.com",
            smtpPort: 587,
            senderEmail: "",
            senderPassword: "pass",
            recipientEmails: new List<string> { "admin@example.com" },
            enabled: true);

        service.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void Constructor_WithEmptyRecipients_IsDisabled()
    {
        var service = new EmailAlertService(
            smtpHost: "smtp.example.com",
            smtpPort: 587,
            senderEmail: "alert@example.com",
            senderPassword: "pass",
            recipientEmails: new List<string>(),
            enabled: true);

        service.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void Constructor_ExplicitlyDisabled_IsDisabled()
    {
        var service = new EmailAlertService(
            smtpHost: "smtp.example.com",
            smtpPort: 587,
            senderEmail: "alert@example.com",
            senderPassword: "pass",
            recipientEmails: new List<string> { "admin@example.com" },
            enabled: false);

        service.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task SendAlertAsync_WhenDisabled_DoesNotThrow()
    {
        var service = new EmailAlertService(
            smtpHost: "",
            smtpPort: 587,
            senderEmail: "",
            senderPassword: "",
            recipientEmails: new List<string>(),
            enabled: false);

        await service.Invoking(s => s.SendAlertAsync("测试", "消息", AlertLevel.Critical))
            .Should().NotThrowAsync();
    }

    [Fact]
    public void Constructor_DefaultEnabledIsTrue()
    {
        var service = new EmailAlertService(
            smtpHost: "smtp.example.com",
            smtpPort: 587,
            senderEmail: "alert@example.com",
            senderPassword: "pass",
            recipientEmails: new List<string> { "a@b.com" });

        service.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Constructor_Port465_StillEnabled()
    {
        var service = new EmailAlertService(
            smtpHost: "smtp.example.com",
            smtpPort: 465,
            senderEmail: "alert@example.com",
            senderPassword: "pass",
            recipientEmails: new List<string> { "admin@example.com" });

        service.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Constructor_MultipleRecipients_IsEnabled()
    {
        var service = new EmailAlertService(
            smtpHost: "smtp.example.com",
            smtpPort: 587,
            senderEmail: "alert@example.com",
            senderPassword: "pass",
            recipientEmails: new List<string> { "a@b.com", "c@d.com", "e@f.com" });

        service.IsEnabled.Should().BeTrue();
    }
}

#endregion

#region AlertLevel Enum Tests

public class AlertLevelTests
{
    [Fact]
    public void AlertLevel_HasThreeValues()
    {
        Enum.GetValues<AlertLevel>().Length.Should().Be(3);
    }

    [Fact]
    public void AlertLevel_ValuesAreCorrect()
    {
        Enum.GetValues<AlertLevel>().Should().Contain(new[] { AlertLevel.Info, AlertLevel.Warning, AlertLevel.Critical });
    }
}

#endregion
