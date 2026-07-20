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
/// P5-3c: KnowledgeBaseController 边界验证测试。
///
/// 覆盖:
///   - SetSearchMode: 无效模式 → BadRequest
///   - RagTest: 空查询 → BadRequest
///   - 请求模型
/// </summary>
public class KnowledgeBaseControllerTests
{
    private static KnowledgeBaseController CreateController()
    {
        var logger = Mock.Of<ILogger<KnowledgeBaseController>>();
        return new KnowledgeBaseController(
            null!, // ChemicalRAG - unused in validation paths
            null!, // IKnowledgeBaseService - unused in validation paths
            logger);
    }

    [Fact]
    public void SetSearchMode_InvalidMode_ReturnsBadRequest()
    {
        var controller = CreateController();
        var result = controller.SetSearchMode(new SearchModeRequest("InvalidMode"));
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task RagTest_EmptyQuery_ReturnsBadRequest()
    {
        var controller = CreateController();
        var result = await controller.RagTest(new RagTestRequest(""));
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task RagTest_WhitespaceQuery_ReturnsBadRequest()
    {
        var controller = CreateController();
        var result = await controller.RagTest(new RagTestRequest("   "));
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void SearchModeRequest_DefaultValues()
    {
        var request = new SearchModeRequest("Hybrid");
        request.Mode.Should().Be("Hybrid");
    }

    [Fact]
    public void RagTestRequest_DefaultTopK_Is5()
    {
        var request = new RagTestRequest("测试");
        request.TopK.Should().Be(5);
    }

    [Fact]
    public void RagTestRequest_CustomTopK_OverridesDefault()
    {
        var request = new RagTestRequest("测试", 10);
        request.TopK.Should().Be(10);
    }
}
