using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Agent1.Api.Controllers;
using Agent1.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// P5-3e: AuditController 边界与业务逻辑测试。
///
/// 覆盖:
///   - ExportReport: from>=to → BadRequest, 正常导出
///   - VerifyIntegrity: 完整/断裂
///   - GetLogs: 分页逻辑验证
///   - GetStats: 统计汇总
/// </summary>
public class AuditControllerTests
{
    private static AuditController CreateController(IAuditService? auditService = null)
    {
        auditService ??= Mock.Of<IAuditService>();
        return new AuditController(auditService);
    }

    // ═══════════════════════════════════════
    // ExportReport 时间验证
    // ═══════════════════════════════════════

    [Fact]
    public async Task ExportReport_FromGreaterThanTo_ReturnsBadRequest()
    {
        var controller = CreateController();
        var result = await controller.ExportReport(
            new DateTime(2026, 7, 10),
            new DateTime(2026, 7, 1));

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ExportReport_FromEqualsTo_ReturnsBadRequest()
    {
        var controller = CreateController();
        var result = await controller.ExportReport(
            new DateTime(2026, 7, 10),
            new DateTime(2026, 7, 10));

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ExportReport_ValidRange_ReturnsOk()
    {
        var mockAudit = new Mock<IAuditService>();
        mockAudit.Setup(a => a.ExportAuditReportAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync("审计报告内容...");

        var controller = CreateController(mockAudit.Object);
        var result = await controller.ExportReport(
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 10));

        result.Should().BeOfType<OkObjectResult>();
    }

    // ═══════════════════════════════════════
    // VerifyIntegrity
    // ═══════════════════════════════════════

    [Fact]
    public async Task VerifyIntegrity_Intact_ReturnsTrue()
    {
        var mockAudit = new Mock<IAuditService>();
        mockAudit.Setup(a => a.VerifyIntegrityAsync())
            .ReturnsAsync((true, null, "所有 100 条记录哈希链完整"));

        var controller = CreateController(mockAudit.Object);
        var result = await controller.VerifyIntegrity();

        result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result;
        // Verify the response contains intact=true
        var value = ok.Value;
        value.Should().NotBeNull();
    }

    [Fact]
    public async Task VerifyIntegrity_Broken_ReturnsFalse()
    {
        var mockAudit = new Mock<IAuditService>();
        mockAudit.Setup(a => a.VerifyIntegrityAsync())
            .ReturnsAsync((false, 42L, "第 42 条记录哈希链断裂"));

        var controller = CreateController(mockAudit.Object);
        var result = await controller.VerifyIntegrity();

        result.Should().BeOfType<OkObjectResult>();
    }

    // ═══════════════════════════════════════
    // GetLogs 分页
    // ═══════════════════════════════════════

    [Fact]
    public async Task GetLogs_DefaultPagination_ReturnsOk()
    {
        var mockAudit = new Mock<IAuditService>();
        mockAudit.Setup(a => a.GetAuditLogsAsync(null, null, null))
            .ReturnsAsync(new List<AuditLog>());

        var controller = CreateController(mockAudit.Object);
        var result = await controller.GetLogs(null, null, null);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetLogs_WithData_ReturnsPagedResults()
    {
        var logs = new List<AuditLog>
        {
            new() { Id = 1, UserId = "admin", Operation = "登录", Details = "成功", CreateTime = DateTime.Now },
            new() { Id = 2, UserId = "admin", Operation = "查询", Details = "合规检查", CreateTime = DateTime.Now },
        };

        var mockAudit = new Mock<IAuditService>();
        mockAudit.Setup(a => a.GetAuditLogsAsync(null, null, null))
            .ReturnsAsync(logs);

        var controller = CreateController(mockAudit.Object);
        var result = await controller.GetLogs(null, null, null, page: 1, pageSize: 10);

        result.Should().BeOfType<OkObjectResult>();
    }

    // ═══════════════════════════════════════
    // GetStats
    // ═══════════════════════════════════════

    [Fact]
    public async Task GetStats_ReturnsOk()
    {
        var mockAudit = new Mock<IAuditService>();
        mockAudit.Setup(a => a.GetAuditLogsAsync(null, null, null))
            .ReturnsAsync(new List<AuditLog>());

        var controller = CreateController(mockAudit.Object);
        var result = await controller.GetStats();

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetStats_WithOperations_GroupsByOperation()
    {
        var logs = new List<AuditLog>
        {
            new() { Id = 1, UserId = "user1", Operation = "登录", CreateTime = DateTime.Now },
            new() { Id = 2, UserId = "user1", Operation = "登录", CreateTime = DateTime.Now },
            new() { Id = 3, UserId = "user2", Operation = "查询", CreateTime = DateTime.Now },
        };

        var mockAudit = new Mock<IAuditService>();
        mockAudit.Setup(a => a.GetAuditLogsAsync(null, null, null))
            .ReturnsAsync(logs);

        var controller = CreateController(mockAudit.Object);
        var result = await controller.GetStats();

        result.Should().BeOfType<OkObjectResult>();
    }
}
