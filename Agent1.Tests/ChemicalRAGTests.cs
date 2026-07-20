using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using Agent1.Services;
using FluentAssertions;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// P5-1b: ChemicalRAG 纯逻辑测试 — 无需文件系统依赖。
///
/// 通过反射测试 private 方法:
///   - SplitTextIntoChunks: 按段落分块 (maxChunkSize=500)
/// </summary>
public class ChemicalRAGTests
{
    // ═══════════════════════════════════════
    // SplitTextIntoChunks: 按段落分块
    // ═══════════════════════════════════════

    [Fact]
    public void SplitTextIntoChunks_EmptyText_ReturnsEmpty()
    {
        var chunks = CallSplitTextIntoChunks("", 500);
        chunks.Should().BeEmpty();
    }

    [Fact]
    public void SplitTextIntoChunks_SingleShortParagraph_ReturnsOneChunk()
    {
        var text = "简短段落";
        var chunks = CallSplitTextIntoChunks(text, 500);
        chunks.Should().HaveCount(1);
        chunks[0].Should().Be("简短段落");
    }

    [Fact]
    public void SplitTextIntoChunks_MultipleShortParagraphs_MergedIntoSingleChunk()
    {
        var text = "段落1\n段落2\n段落3";
        var chunks = CallSplitTextIntoChunks(text, 500);
        chunks.Should().HaveCount(1);
        chunks[0].Should().Contain("段落1");
        chunks[0].Should().Contain("段落2");
        chunks[0].Should().Contain("段落3");
    }

    [Fact]
    public void SplitTextIntoChunks_LongParagraphs_SplitIntoMultipleChunks()
    {
        var text = "";
        for (int i = 0; i < 50; i++)
            text += $"这是第{i}个测试段落，包含足够的文字来填充块大小限制的中文内容。\n";

        var chunks = CallSplitTextIntoChunks(text, 500);
        chunks.Should().NotBeEmpty();
        chunks.Count.Should().BeGreaterThan(1, "长文本应分割为多个块");
    }

    [Fact]
    public void SplitTextIntoChunks_EmptyLines_Filtered()
    {
        var text = "段落1\n\n  \n段落2\n";
        var chunks = CallSplitTextIntoChunks(text, 500);
        chunks.Should().HaveCount(1);
        chunks[0].Should().Contain("段落1");
        chunks[0].Should().Contain("段落2");
    }

    [Fact]
    public void SplitTextIntoChunks_ExactBoundary_NoEmptyChunk()
    {
        // 段落正好在边界处
        var text = new string('测', 400) + "\n" + new string('试', 200);
        var chunks = CallSplitTextIntoChunks(text, 500);

        chunks.Should().NotBeEmpty();
        // 不应有空块
        foreach (var chunk in chunks)
            chunk.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void SplitTextIntoChunks_SingleHugeParagraph_SplitsWhenExceeded()
    {
        // 单个段落超过 maxChunkSize，下一个段落开始新块
        var text = new string('A', 600) + "\n段落2";
        var chunks = CallSplitTextIntoChunks(text, 500);

        chunks.Should().HaveCount(2);
        chunks[0].Should().Contain("A");
        chunks[1].Should().Contain("段落2");
    }

    [Fact]
    public void SplitTextIntoChunks_HugeFirstParagraph_ThenSmall()
    {
        var text = "";
        for (int i = 0; i < 10; i++)
            text += $"第{i}个长段落包含足够中文文字来填充块大小限制测试内容。\n";
        text += "尾段";

        var chunks = CallSplitTextIntoChunks(text, 500);
        chunks.Should().NotBeEmpty();
        // 最后一个 chunk 应包含 "尾段"
        chunks[^1].Should().Contain("尾段");
    }

    // ═══════════════════════════════════════
    // ResolveKnowledgeBasePath: 知识库路径健壮解析（P0）
    // ═══════════════════════════════════════

    [Fact]
    public void ResolveKnowledgeBasePath_AbsoluteQualified_ReturnedAsIs()
    {
        var root = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "kb_" + Guid.NewGuid().ToString("N"))).FullName;
        Directory.CreateDirectory(Path.Combine(root, "国标"));
        try
        {
            var resolved = CallResolveKnowledgeBasePath(root);
            resolved.Should().Be(root);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ResolveKnowledgeBasePath_RelativePath_AnchoredToCwdAncestor()
    {
        // 模拟远程实况：CWD 在项目子目录（Agent1.Api/bin），真实知识库在上层仓库根。
        var root = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "kbroot_" + Guid.NewGuid().ToString("N"))).FullName;
        var kb = Directory.CreateDirectory(Path.Combine(root, "knowledgebase")).FullName;
        Directory.CreateDirectory(Path.Combine(kb, "国标"));
        var deep = Directory.CreateDirectory(Path.Combine(root, "Agent1.Api", "bin")).FullName;

        var prevCwd = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(deep);
            var resolved = CallResolveKnowledgeBasePath("knowledgebase");
            resolved.Should().Be(Path.GetFullPath(kb));
        }
        finally
        {
            Directory.SetCurrentDirectory(prevCwd);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ResolveKnowledgeBasePath_NoMatch_FallsBackToAbsolute()
    {
        var resolved = CallResolveKnowledgeBasePath("nonexistent_kb_dir_" + Guid.NewGuid().ToString("N"));
        Path.IsPathRooted(resolved).Should().BeTrue();
    }

    // ═══════════════════════════════════════
    // Reflection Helpers
    // ═══════════════════════════════════════

    private static string CallResolveKnowledgeBasePath(string configuredPath)
    {
        var method = typeof(ChemicalRAG).GetMethod("ResolveKnowledgeBasePath",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string)method.Invoke(null, new object[] { configuredPath })!;
    }

    private static List<string> CallSplitTextIntoChunks(string text, int maxChunkSize)
    {
        // SplitTextIntoChunks 是实例方法但不依赖任何实例字段
        var instance = FormatterServices.GetUninitializedObject(typeof(ChemicalRAG));
        var method = typeof(ChemicalRAG).GetMethod("SplitTextIntoChunks",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (List<string>)method.Invoke(instance, new object[] { text, maxChunkSize })!;
    }
}
