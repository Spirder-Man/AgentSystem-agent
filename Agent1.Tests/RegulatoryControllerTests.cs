using System;
using System.Reflection;
using System.Threading.Tasks;
using Agent1.Api.Controllers;
using Agent1.Models;
using Agent1.Modules;
using Agent1.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// P5-3b: RegulatoryController 边界与模型测试。
///
/// 覆盖:
///   - RegulatoryController.Audit: 空查询验证
///   - RegulatoryAuditRequest 模型
/// </summary>
public class RegulatoryControllerTests
{
    private static RegulatoryController CreateController(IModuleFactory? factory = null)
    {
        factory ??= Mock.Of<IModuleFactory>();
        var logger = Mock.Of<ILogger<RegulatoryController>>();
        return new RegulatoryController(factory, logger);
    }

    [Fact]
    public async Task Audit_EmptyQuery_ReturnsBadRequest()
    {
        var controller = CreateController();
        var result = await controller.Audit(new RegulatoryAuditRequest(""));
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Audit_WhitespaceQuery_ReturnsBadRequest()
    {
        var controller = CreateController();
        var result = await controller.Audit(new RegulatoryAuditRequest("   "));
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Audit_NullQuery_ReturnsBadRequest()
    {
        var controller = CreateController();
        var result = await controller.Audit(new RegulatoryAuditRequest(null!));
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Audit_ModuleThrowsException_Returns500()
    {
        var mockFactory = new Mock<IModuleFactory>();
        mockFactory.Setup(f => f.CreateModule(ModuleType.RegulatoryAudit))
            .Throws(new InvalidOperationException("模块构造失败"));

        var controller = CreateController(mockFactory.Object);
        var result = await controller.Audit(new RegulatoryAuditRequest("核查内容"));

        result.Should().BeOfType<ObjectResult>();
        var objResult = (ObjectResult)result;
        objResult.StatusCode.Should().Be(500);
    }

    [Fact]
    public void RegulatoryAuditRequest_HasQueryProperty()
    {
        var request = new RegulatoryAuditRequest("测试核查清单");
        request.Query.Should().Be("测试核查清单");
    }
}
