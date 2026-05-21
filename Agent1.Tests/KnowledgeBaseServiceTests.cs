using System.Threading.Tasks;
using Agent1.Services;
using Xunit;
using FluentAssertions;

public class KnowledgeBaseServiceTests
{
    [Fact]
    public async Task AddDocuments_IncreasesDocumentCount_And_RetrieveAsync_Completes()
    {
        var kb = new KnowledgeBaseService();

        var docs = new[]
        {
            "主轴 实时 温度：195℃",
            "温度 阈值：<= 180℃",
            "轴承 故障 案例"
        };

        await kb.AddDocumentsAsync(docs);

        kb.GetDocumentCount().Should().Be(3);

        var results = await kb.RetrieveAsync("主轴 温度", topK: 3);
        results.Should().NotBeNull();
    }
}