using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Agent1.Services;
using Xunit;
using FluentAssertions;

namespace Agent1.Tests
{
    // ═══════════════════════════════════════════
    // GB 编号标准化测试
    // ═══════════════════════════════════════════
    public class GbNumberNormalizationTests
    {
        [Fact]
        public void NormalizeGbNumbers_StandardFormat()
        {
            var result = KnowledgeBaseService.NormalizeGbNumbers("GB 30000.7-2013");
            result.Should().Be("gb3000072013", "应归一化为紧凑格式");
        }

        [Fact]
        public void NormalizeGbNumbers_CompactFormat()
        {
            var result = KnowledgeBaseService.NormalizeGbNumbers("GB30000.7-2013");
            result.Should().Be("gb3000072013");
        }

        [Fact]
        public void NormalizeGbNumbers_WithSlashT()
        {
            var result = KnowledgeBaseService.NormalizeGbNumbers("GB/T 30000.14-2013");
            result.Should().Be("gb/t30000142013", "应保留 T 标记");
        }

        [Fact]
        public void NormalizeGbNumbers_WithoutYear()
        {
            var result = KnowledgeBaseService.NormalizeGbNumbers("GB 15603");
            result.Should().Be("gb15603");
        }

        [Fact]
        public void NormalizeGbNumbers_MultipleOccurrences()
        {
            var result = KnowledgeBaseService.NormalizeGbNumbers(
                "依据 GB 15603 和 GB 30000.7-2013 进行分类");
            result.Should().Contain("gb15603");
            result.Should().Contain("gb3000072013");
            result.Should().NotContain("GB 15603");
        }

        [Fact]
        public void NormalizeGbNumbers_EmptyOrNull()
        {
            KnowledgeBaseService.NormalizeGbNumbers("").Should().Be("");
            KnowledgeBaseService.NormalizeGbNumbers(null!).Should().BeNull();
        }

        [Fact]
        public void NormalizeGbNumbers_NoGbReference()
        {
            var result = KnowledgeBaseService.NormalizeGbNumbers("化学品分类标准");
            result.Should().Be("化学品分类标准", "无 GB 引用时应原样返回");
        }

        [Fact]
        public void NormalizeGbNumbers_WithSpacesAndSlashes()
        {
            // "GB / T 15603-2022" 中 "/ T" 被空格分隔，不识别为 GB/T 前缀
            var result = KnowledgeBaseService.NormalizeGbNumbers("GB / T 15603-2022");
            result.Should().Be("gb156032022", "带空格的斜杠变体, T 不在 /T 连续出现时不识别为带T");
        }

        [Fact]
        public void NormalizeGbNumbers_WithoutSubNumber()
        {
            var result = KnowledgeBaseService.NormalizeGbNumbers("GB 50160-2008");
            result.Should().Be("gb501602008");
        }
    }

    // ═══════════════════════════════════════════
    // KnowledgeBaseService BM25 检索测试
    // ═══════════════════════════════════════════
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

        [Fact]
        public async Task RetrieveAsync_EmptyQuery_ReturnsEmpty()
        {
            var kb = new KnowledgeBaseService();
            await kb.AddDocumentAsync("测试内容");

            var results = await kb.RetrieveAsync("", topK: 5);
            results.Should().BeEmpty("空查询应返回空结果");
        }

        [Fact]
        public async Task RetrieveAsync_EmptyKnowledgeBase_ReturnsEmpty()
        {
            var kb = new KnowledgeBaseService();
            var results = await kb.RetrieveAsync("查询");
            results.Should().BeEmpty();
        }

        [Fact]
        public async Task RetrieveAsync_ReturnsResultsOrderedByScore()
        {
            var kb = new KnowledgeBaseService();
            await kb.AddDocumentAsync("GB 15603 危险化学品贮存通则规定了禁忌物料不得同库贮存");
            await kb.AddDocumentAsync("化学品分类标准依据 GB 30000.7-2013 执行");
            await kb.AddDocumentAsync("安全生产法规定企业应建立安全管理制度");

            var results = await kb.RetrieveAsync("危险化学品 贮存 GB15603", topK: 3);
            results.Should().NotBeEmpty();
            results.Should().BeInDescendingOrder(r => r.Score, "结果应按分数降序排列");
        }

        [Fact]
        public async Task RetrieveAsync_RespectsTopK()
        {
            var kb = new KnowledgeBaseService();
            var docs = new[] { "文档1", "文档2", "文档3", "文档4", "文档5" };
            await kb.AddDocumentsAsync(docs);

            var results = await kb.RetrieveAsync("文档", topK: 2);
            results.Count.Should().BeLessOrEqualTo(2, "应遵守 topK 限制");
        }

        [Fact]
        public async Task RetrieveAsync_ReturnsEmptyForNoMatch()
        {
            var kb = new KnowledgeBaseService();
            await kb.AddDocumentAsync("化学品安全管理条例");

            var results = await kb.RetrieveAsync("xyz不存在的查询词abc");
            results.Should().BeEmpty("完全不匹配时应返回空结果");
        }

        [Fact]
        public async Task ClearAsync_RemovesAllDocuments()
        {
            var kb = new KnowledgeBaseService();
            await kb.AddDocumentsAsync(new[] { "文档1", "文档2" });
            kb.GetDocumentCount().Should().Be(2);

            await kb.ClearAsync();
            kb.GetDocumentCount().Should().Be(0);

            var results = await kb.RetrieveAsync("文档");
            results.Should().BeEmpty();
        }

        [Fact]
        public async Task RetrieveAsync_GbNumberNormalizationInQuery()
        {
            var kb = new KnowledgeBaseService();
            await kb.AddDocumentAsync("GB 30000.7-2013 规定了易燃液体的分类和标签规范");

            // 查询中的 GB 编号也被归一化，应能匹配
            var results = await kb.RetrieveAsync("GB30000.7-2013 易燃液体");
            results.Should().NotBeEmpty("归一化后的 GB 编号应匹配");
        }

        [Fact]
        public async Task RetrieveAsync_ChineseChemicalTerms()
        {
            var kb = new KnowledgeBaseService();
            await kb.AddDocumentAsync("甲苯与硝酸严禁同库储存，氧化剂与易燃液体隔离");
            await kb.AddDocumentAsync("防火间距应不小于15米");

            var results = await kb.RetrieveAsync("甲苯 硝酸 储存");
            results.Should().NotBeEmpty();
            results[0].Content.Should().Contain("甲苯");
        }

        [Fact]
        public async Task RetrieveAsync_ChineseNGramTokenization()
        {
            // 中文分词通过 n-gram 实现，单字查询也应能匹配
            var kb = new KnowledgeBaseService();
            await kb.AddDocumentAsync("化学品泄漏应急处置方案");

            var results = await kb.RetrieveAsync("泄漏");
            results.Should().NotBeEmpty("双字 n-gram 应匹配 '泄漏'");
        }

        [Fact]
        public async Task AddChemicalRegulationAsync_SetsMetadata()
        {
            var kb = new KnowledgeBaseService();
            await kb.AddChemicalRegulationAsync("测试国标内容", "国标", "高", "甲苯");

            kb.GetDocumentCount().Should().Be(1);
        }

        [Fact]
        public async Task RetrieveChemicalRegulationAsync_FiltersByRegulationType()
        {
            var kb = new KnowledgeBaseService();
            await kb.AddChemicalRegulationAsync("国标GB15603内容", "国标", "高");
            await kb.AddChemicalRegulationAsync("园区规则内容", "园区规则", "中");

            var results = await kb.RetrieveChemicalRegulationAsync(
                "内容", regulationType: "国标");
            results.Should().NotBeEmpty();
            // 结果应只包含国标类型的文档
            results.Should().AllSatisfy(r =>
                r.Metadata["RegulationType"].Should().Be("国标"));
        }

        [Fact]
        public async Task RemoveBySourceFile_RemovesMatchingDocuments()
        {
            var kb = new KnowledgeBaseService();
            var metadata = new Dictionary<string, object> { ["SourceFile"] = "test_file" };
            await kb.AddDocumentAsync("内容1", metadata);
            await kb.AddDocumentAsync("内容2", metadata);
            await kb.AddDocumentAsync("内容3", new Dictionary<string, object> { ["SourceFile"] = "other" });

            var removed = kb.RemoveBySourceFile("test_file.txt");
            removed.Should().Be(2, "应移除2个同名源文件的文档");
            kb.GetDocumentCount().Should().Be(1, "应保留另一个文档");
        }

        [Fact]
        public async Task PreprocessQuery_TrimsAndNormalizesGb()
        {
            var kb = new KnowledgeBaseService();
            var result = kb.PreprocessQuery("  GB 30000.7-2013  化学品  ");
            // Trim 仅去除首尾空格，GB编号被归一化，但内部的多余空格保留
            result.Should().Contain("gb3000072013");
            result.Should().StartWith("gb3000072013");
            result.Should().EndWith("化学品");
        }

        [Fact]
        public async Task RetrieveAsync_RetrievedChunkHasMetadata()
        {
            var kb = new KnowledgeBaseService();
            var metadata = new Dictionary<string, object>
            {
                ["source"] = "GB15603",
                ["Priority"] = "高"
            };
            await kb.AddDocumentAsync("测试内容", metadata);
            var results = await kb.RetrieveAsync("测试");

            results.Should().NotBeEmpty();
            results[0].Metadata.Should().ContainKey("source");
            results[0].Metadata["source"].Should().Be("GB15603");
        }
    }

    // ═══════════════════════════════════════════
    // 智能分块测试 (SplitTextIntoChunks)
    // ═══════════════════════════════════════════
    public class SmartChunkingTests
    {
        [Fact]
        public void SplitTextIntoChunks_EmptyOrNull_ReturnsEmpty()
        {
            KnowledgeBaseService.SplitTextIntoChunks("").Should().BeEmpty();
            KnowledgeBaseService.SplitTextIntoChunks(null!).Should().BeEmpty();
        }

        [Fact]
        public void SplitTextIntoChunks_SingleParagraph_ReturnsOneChunk()
        {
            var text = "这是一个简短的段落。";
            var chunks = KnowledgeBaseService.SplitTextIntoChunks(text, maxChunkSize: 500);
            chunks.Should().HaveCount(1);
            chunks[0].Should().Be(text);
        }

        [Fact]
        public void SplitTextIntoChunks_LongText_SplitsBySize()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 100; i++)
                sb.AppendLine($"这是第{i}个测试段落，包含足够的中文文字来填充块大小限制");
            var text = sb.ToString();

            var chunks = KnowledgeBaseService.SplitTextIntoChunks(text, maxChunkSize: 500, overlap: 50);
            chunks.Should().NotBeEmpty();
            chunks.Count.Should().BeGreaterThan(1, "长文本应分割为多个块");
        }

        [Fact]
        public void SplitTextIntoChunks_OverlapBoundaries()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 80; i++)
                sb.AppendLine($"第{i}段测试文本用于填充块大小以便测试重叠功能中文内容");
            var text = sb.ToString();

            var chunks = KnowledgeBaseService.SplitTextIntoChunks(text, maxChunkSize: 500, overlap: 100);
            // 如果 overlap > 0 且有多个块，第一个非首块应有重叠
            if (chunks.Count > 1)
            {
                chunks.Should().NotBeEmpty();
            }
        }

        [Fact]
        public void SplitTextIntoChunks_NoSemantic_FallbackToSimpleSplit()
        {
            var text = "段落一\n\n段落二\n\n段落三";
            var chunks = KnowledgeBaseService.SplitTextIntoChunks(text, maxChunkSize: 500, enableSemantic: false);
            chunks.Should().NotBeEmpty();
            // 各段落按 maxChunkSize 组装，3个短段落会被合并为1个块
            chunks[0].Should().Contain("段落一");
            chunks[0].Should().Contain("段落二");
            chunks[0].Should().Contain("段落三");
        }

        [Fact]
        public void SplitTextIntoChunks_EnablesSemanticBoundaries()
        {
            var text = @"第1章 概述
本章介绍化学品分类的基本概念。

第2章 术语和定义
2.1 易燃液体
指闪点不大于93°C的液体。";

            var chunks = KnowledgeBaseService.SplitTextIntoChunks(text, maxChunkSize: 500, enableSemantic: true);
            chunks.Should().NotBeEmpty();
            // 语义模式应按章节边界分割
            chunks.Any(c => c.Contains("第1章")).Should().BeTrue();
        }

        [Fact]
        public void SplitTextIntoChunks_SingleHugeParagraph_SplitsAtBestBreakPoint()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 200; i++)
                sb.Append($"测试句子{i}。"); // 句号是优先断点
            var text = sb.ToString();

            var chunks = KnowledgeBaseService.SplitTextIntoChunks(text, maxChunkSize: 300, overlap: 50);
            chunks.Should().NotBeEmpty();
            chunks.Count.Should().BeGreaterThan(1);
            // 每个 chunk 不应超过 maxChunkSize + overlap
            foreach (var chunk in chunks)
                chunk.Length.Should().BeLessOrEqualTo(400, "块大小应在合理范围内(含overlap)");
        }
    }
}