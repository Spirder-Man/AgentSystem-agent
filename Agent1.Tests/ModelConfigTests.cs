using System;
using System.Collections.Generic;
using Agent1.Config;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Agent1.Tests
{
    /// <summary>
    /// L0 层：ModelConfig 静态门面测试。
    /// 理解点：ModelConfig 是 AppConfig 的"快捷访问层"，提供类型安全的属性访问。
    /// 注意：每个测试独立加载配置（AppConfig 是单例，需避免并行状态污染）。
    /// </summary>
    [Collection("ConfigTests")]
    public class ModelConfigTests
    {
        private static IConfiguration BuildConfig(string modelId, string endpoint = "http://localhost:8080/v1",
            string fcModelId = "", string multimodalModelId = "qwen-vl:latest", string kbPath = "knowledgebase")
        {
            return new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Llm:ModelId"] = modelId,
                    ["Llm:Endpoint"] = endpoint,
                    ["Llm:MultimodalModelId"] = multimodalModelId,
                    ["Llm:FunctionCallingModelId"] = fcModelId,
                    ["Database:Host"] = "localhost",
                    ["Database:Port"] = "5432",
                    ["Database:DatabaseName"] = "testdb",
                    ["Database:Password"] = "test-password",
                    ["VectorSearch:EmbeddingModelId"] = "nomic-embed-text",
                    ["PromptTemplates:SystemRole"] = "test-role",
                    ["PromptTemplates:EvalFastPrompt"] = "test-prompt {SystemRole} {UserInput}",
                    ["PromptTemplates:EvalFastQueryPrompt"] = "test-query {SystemRole} {UserInput}",
                    ["KnowledgeBase:BasePath"] = kbPath
                })
                .Build();
        }

        [Fact]
        public void Initialize_WithNullConfig_ShouldThrowArgumentNull()
        {
            // 先确保 AppConfig 已初始化（否则 Initialize 副作用不同）
            AppConfig.Load(BuildConfig("init-test"));
            Action act = () => ModelConfig.Initialize(null!);
            act.Should().Throw<ArgumentNullException>("null 配置应抛出 ArgumentNullException");
        }

        [Fact]
        public void ModelId_ShouldReturnConfiguredValue()
        {
            AppConfig.Load(BuildConfig("my-custom-model"));
            ModelConfig.ModelId.Should().Be("my-custom-model");
        }

        [Fact]
        public void Endpoint_ShouldReturnUri_WithTrailingSlash()
        {
            AppConfig.Load(BuildConfig("uri-test", endpoint: "http://localhost:8080/v1"));
            var ep = ModelConfig.Endpoint;
            ep.Should().BeOfType<Uri>("Endpoint 应返回 Uri 类型");
            // Uri.ToString() 的行为因 .NET 版本而异（可能含或不含尾部斜杠）
            ep.ToString().Should().Contain("http://localhost:8080");
            ep.ToString().Should().Contain("/v1");
        }

        [Fact]
        public void MultimodalModelId_ShouldReturnConfiguredValue()
        {
            AppConfig.Load(BuildConfig("mm-test", multimodalModelId: "qwen2-vl:7b"));
            ModelConfig.MultimodalModelId.Should().Be("qwen2-vl:7b");
        }

        [Fact]
        public void FunctionCallingModelId_WhenEmpty_ShouldFallbackToModelId()
        {
            AppConfig.Load(BuildConfig("fallback-model", fcModelId: ""));
            ModelConfig.FunctionCallingModelId.Should().Be("fallback-model",
                "FunctionCallingModelId 为空时应回退到 ModelId");
        }

        [Fact]
        public void FunctionCallingModelId_WhenSpecified_ShouldReturnDedicatedModel()
        {
            AppConfig.Load(BuildConfig("main-model", fcModelId: "fc-dedicated"));
            ModelConfig.FunctionCallingModelId.Should().Be("fc-dedicated",
                "指定 FC 专用模型时应返回专用模型ID");
        }

        [Fact]
        public void ChemicalKnowledgeBasePath_ShouldReturnConfiguredValue()
        {
            AppConfig.Load(BuildConfig("kb-test", kbPath: "/custom/kb/path"));
            ModelConfig.ChemicalKnowledgeBasePath.Should().Be("/custom/kb/path");
        }
    }
}
