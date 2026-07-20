using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using Agent1.Models;
using Agent1.Modules;
using Agent1.Services;
using FluentAssertions;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// P5-2a: ComplianceCheckModule.BuildCompliancePrompt 纯逻辑测试。
///
/// BuildCompliancePrompt 是 private 实例方法，不依赖任何实例字段，
/// 仅拼接 Prompt 字符串。通过反射调用进行边界验证。
/// </summary>
public class ComplianceCheckModuleTests
{
    // ═══════════════════════════════════════
    // BuildCompliancePrompt: Prompt 模板构建
    // ═══════════════════════════════════════

    [Fact]
    public void BuildPrompt_EmptyReferences_StillContainsStructure()
    {
        var prompt = CallBuildCompliancePrompt("消防通道检查", new List<RetrievedChunk>());

        prompt.Should().Contain("消防通道检查");
        prompt.Should().Contain("参考法规");
        prompt.Should().Contain("合规审核");
        prompt.Should().Contain("是否合规");
    }

    [Fact]
    public void BuildPrompt_WithReferences_IncludesTruncatedContent()
    {
        var chunks = new List<RetrievedChunk>
        {
            new() { Content = "GB 15603 危险化学品贮存通则规定了禁忌物料不得同库贮存，氧化剂与易燃液体应严格隔离。" }
        };

        var prompt = CallBuildCompliancePrompt("氧化剂存储检查", chunks);

        prompt.Should().Contain("GB 15603");
        prompt.Should().Contain("氧化剂存储检查");
        prompt.Should().Contain("禁忌物料");
    }

    [Fact]
    public void BuildPrompt_MoreThan3Refs_OnlyFirst3Included()
    {
        var chunks = new List<RetrievedChunk>
        {
            new() { Content = "法规1内容" },
            new() { Content = "法规2内容" },
            new() { Content = "法规3内容" },
            new() { Content = "法规4内容不应出现" }
        };

        var prompt = CallBuildCompliancePrompt("测试", chunks);

        prompt.Should().Contain("法规1内容");
        prompt.Should().Contain("法规2内容");
        prompt.Should().Contain("法规3内容");
        prompt.Should().NotContain("法规4内容不应出现");
    }

    [Fact]
    public void BuildPrompt_LongReference_TruncatedAt400Chars()
    {
        var chunks = new List<RetrievedChunk>
        {
            new() { Content = new string('X', 500) }
        };

        var prompt = CallBuildCompliancePrompt("测试", chunks);

        prompt.Should().Contain("..."); // truncated marker
        prompt.Should().NotContain(new string('X', 450)); // shouldn't show full
    }

    [Fact]
    public void BuildPrompt_ContainsRequiredSections()
    {
        var prompt = CallBuildCompliancePrompt("安全检查", new List<RetrievedChunk>());

        prompt.Should().Contain("是否合规");
        prompt.Should().Contain("法规依据");
        prompt.Should().Contain("整改建议");
    }

    [Fact]
    public void BuildPrompt_UserInput_AlwaysPresent()
    {
        var input = "甲类仓库消防通道宽度是否符合GB 50016要求";
        var prompt = CallBuildCompliancePrompt(input, new List<RetrievedChunk>());

        prompt.Should().Contain(input);
    }

    // ═══════════════════════════════════════
    // Reflection Helpers
    // ═══════════════════════════════════════

    private static string CallBuildCompliancePrompt(string userInput, List<RetrievedChunk> references)
    {
        // BuildCompliancePrompt 不依赖任何实例字段
        var instance = FormatterServices.GetUninitializedObject(typeof(ComplianceCheckModule));
        var method = typeof(ComplianceCheckModule).GetMethod("BuildCompliancePrompt",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (string)method.Invoke(instance, new object[] { userInput, references })!;
    }
}
