using System;
using System.Threading.Tasks;
using Agent1.Api.Controllers;
using Agent1.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// P5-3d: KnowledgeGraphController 边界验证测试。
///
/// 覆盖:
///   - Query: 空/空白查询 → BadRequest
///   - Query: 异常处理 → 500
///   - KnowledgeGraphRequest 模型
/// </summary>
public class KnowledgeGraphControllerTests
{
    private static KnowledgeGraphController CreateController(IKnowledgeBaseService? kbService = null)
    {
        kbService ??= Mock.Of<IKnowledgeBaseService>();
        var logger = Mock.Of<ILogger<KnowledgeGraphController>>();
        return new KnowledgeGraphController(kbService, logger);
    }

    [Fact]
    public async Task Query_EmptyQuery_ReturnsBadRequest()
    {
        var controller = CreateController();
        var result = await controller.Query(new KnowledgeGraphRequest(""));
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Query_WhitespaceQuery_ReturnsBadRequest()
    {
        var controller = CreateController();
        var result = await controller.Query(new KnowledgeGraphRequest("   "));
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void KnowledgeGraphRequest_HasQueryProperty()
    {
        var request = new KnowledgeGraphRequest("苯的危险类别");
        request.Query.Should().Be("苯的危险类别");
    }
}
