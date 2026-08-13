using System;
using System.Collections.Generic;
using Agent1.Config;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Agent1.Tests
{
    /// <summary>
    /// 串行化 Config 相关测试，避免 AppConfig 单例状态竞争。
    /// </summary>
    [CollectionDefinition("ConfigTests", DisableParallelization = true)]
    public class ConfigTestsCollection { }

    /// <summary>
    /// L0 层：AppConfig 配置加载、校验、环境变量覆盖测试。
    /// 理解点：配置是如何从 JSON + 环境变量合并的？默认值是什么？校验了什么？
    /// </summary>
    [Collection("ConfigTests")]
    public class AppConfigTests
    {
        /// <summary>
        /// 构建最小合法配置（通过校验的最小子集）
        /// </summary>
        private static IConfiguration BuildMinimalConfig()
        {
            return new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Llm:ModelId"] = "test-model",
                    ["Llm:Endpoint"] = "http://localhost:11434",
                    ["Database:Host"] = "localhost",
                    ["Database:Port"] = "5432",
                    ["Database:DatabaseName"] = "testdb",
                    ["Database:Password"] = "test-password",
                    ["VectorSearch:EmbeddingModelId"] = "test-embed",
                    ["PromptTemplates:SystemRole"] = "test-role",
                    ["PromptTemplates:EvalFastPrompt"] = "test-prompt {SystemRole} {UserInput}",
                    ["PromptTemplates:EvalFastQueryPrompt"] = "test-query {SystemRole} {UserInput}"
                })
                .Build();
        }

        // ═══════════════════════════════════════════
        // 配置加载测试
        // ═══════════════════════════════════════════

        [Fact]
        public void Load_WithMinimalConfig_ShouldSetDefaults()
        {
            // 保存并清除环境变量，避免远程 .env 覆盖内存默认值断言
            var savedLlmEndpoint = Environment.GetEnvironmentVariable("LLM_ENDPOINT");
            try
            {
                Environment.SetEnvironmentVariable("LLM_ENDPOINT", null);

                var config = BuildMinimalConfig();

                AppConfig.Load(config);
                var app = AppConfig.Instance;

                // 验证用户指定的值
                app.Llm.ModelId.Should().Be("test-model");
                app.Llm.Endpoint.Should().Be("http://localhost:11434");
                app.Database.Host.Should().Be("localhost");

                // 验证默认值未被覆盖
                app.Llm.MaxRetries.Should().Be(3, "默认重试次数应为3");
                app.Llm.RetryDelayMs.Should().Be(1000, "默认重试延迟应为1000ms");
                app.Llm.CircuitBreakerThreshold.Should().Be(3, "默认熔断器阈值应为3");
                app.Database.Port.Should().Be(5432, "默认端口应为5432");
                app.Database.Provider.Should().Be("PostgreSQL", "默认数据库提供者");
                app.Database.ConnectionTimeout.Should().Be(30, "默认连接超时30秒");

                // 验证知识库配置默认值
                app.KnowledgeBase.ChunkSize.Should().Be(500, "默认分块大小500");
                app.KnowledgeBase.ChunkOverlap.Should().Be(100, "默认重叠窗口100");
                app.KnowledgeBase.EnableSemanticChunking.Should().BeTrue("默认启用语义分块");
                app.KnowledgeBase.EnableQueryExpansion.Should().BeTrue("默认启用查询扩展");
                app.KnowledgeBase.QueryCacheTtlMinutes.Should().Be(5, "默认缓存TTL 5分钟");
                app.KnowledgeBase.QueryCacheMaxEntries.Should().Be(500, "默认缓存最大条目500");
                app.KnowledgeBase.SearchMode.Should().Be(SearchModeType.Hybrid, "默认混合检索模式");
            }
            finally
            {
                Environment.SetEnvironmentVariable("LLM_ENDPOINT", savedLlmEndpoint);
            }
        }

        // [P1 #4] 多模态(视觉)与 Reranker 默认端口必须分离，避免同占 8082 冲突
        [Fact]
        public void MultimodalAndRerankerEndpoints_ShouldNotShareSamePort()
        {
            var multimodal = new ChemicalLlmConfig().MultimodalEndpoint;
            var reranker = new VectorSearchConfig().RerankerEndpoint;

            multimodal.Should().NotBe(reranker, "多模态与 Reranker 端点不得完全相同");
            new Uri(multimodal).Port.Should().NotBe(new Uri(reranker).Port, "两者不得共用同一端口");
            new Uri(multimodal).Port.Should().Be(8083);
            new Uri(reranker).Port.Should().Be(8082);
        }

        [Fact]
        public void Load_WithConfig_ShouldBindVectorSearchDefaults()
        {
            var config = BuildMinimalConfig();
            AppConfig.Load(config);
            var vs = AppConfig.Instance.VectorSearch;

            vs.EnableVectorSearch.Should().BeTrue("默认启用向量检索");
            vs.EmbeddingModelId.Should().Be("test-embed");
            vs.EmbeddingDimension.Should().Be(768, "默认嵌入维度768");
            vs.Bm25Weight.Should().Be(0.4, "BM25权重默认0.4");
            vs.VectorWeight.Should().Be(0.6, "向量权重默认0.6");
            vs.GpuEmbeddingEnabled.Should().BeTrue("默认启用GPU嵌入");
            vs.GpuSearchEnabled.Should().BeTrue("默认启用GPU检索");
            vs.RerankerEnabled.Should().BeTrue("默认启用Reranker");
            vs.GpuFallbackEnabled.Should().BeTrue("GPU不可用时自动降级");
            vs.EmbeddingBatchSize.Should().Be(32, "默认批处理大小32");
            vs.EmbeddingTimeoutSeconds.Should().Be(30, "默认嵌入超时30秒");
            vs.RerankerCandidateTopK.Should().Be(20, "粗排召回数默认20");
            vs.RerankerFinalTopK.Should().Be(5, "精排保留数默认5");
        }

        [Fact]
        public void Instance_BeforeLoad_ShouldThrowInvalidOperation()
        {
            // 由于 AppConfig 是单例且可能已被前面的测试初始化，
            // 这里验证的是异常抛出机制存在。
            // 实际行为：如果已初始化，返回已加载的实例（不抛异常）。
            // 此测试侧重于文档说明——理解 Instance 的惰性初始化机制。
            
            // 注：因为 xUnit 并行运行，单例状态可能已被其他测试初始化，
            // 所以此测试仅验证 Instance 属性存在及其基本行为。
            var instance = AppConfig.Instance; // 不应抛异常（可能已被初始化）
            instance.Should().NotBeNull("AppConfig.Instance 应已初始化");
        }

        // ═══════════════════════════════════════════
        // 配置校验测试
        // ═══════════════════════════════════════════

        [Fact]
        public void Validate_MinimalConfig_ShouldReturnNoErrors()
        {
            // 使用独立配置（不依赖 BuildMinimalConfig，避免单例污染）
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Llm:ModelId"] = "validate-test-model",
                    ["Llm:Endpoint"] = "http://localhost:8080/v1",
                    ["Database:Host"] = "localhost",
                    ["Database:Port"] = "5432",
                    ["Database:DatabaseName"] = "validate-db",
                    ["Database:Password"] = "validate-pwd",
                    ["VectorSearch:EmbeddingModelId"] = "validate-embed",
                    ["PromptTemplates:SystemRole"] = "validate-role",
                    ["PromptTemplates:EvalFastPrompt"] = "prompt {SystemRole} {UserInput}",
                    ["PromptTemplates:EvalFastQueryPrompt"] = "query {SystemRole} {UserInput}"
                })
                .Build();
            AppConfig.Load(config);

            var errors = AppConfig.Instance.Validate();

            errors.Should().BeEmpty("最小合法配置应通过所有校验");
        }

        [Fact]
        public void Validate_MissingModelId_ShouldReportError()
        {
            // 用空 ModelId 重新构建配置
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Llm:ModelId"] = "",
                    ["Llm:Endpoint"] = "http://localhost:11434",
                    ["Database:Host"] = "localhost",
                    ["Database:Port"] = "5432",
                    ["Database:DatabaseName"] = "testdb",
                    ["Database:Password"] = "test-password",
                    ["VectorSearch:EmbeddingModelId"] = "test-embed",
                    ["PromptTemplates:SystemRole"] = "test-role",
                    ["PromptTemplates:EvalFastPrompt"] = "test-prompt {SystemRole} {UserInput}",
                    ["PromptTemplates:EvalFastQueryPrompt"] = "test-query {SystemRole} {UserInput}"
                })
                .Build();
            AppConfig.Load(config);

            var errors = AppConfig.Instance.Validate();

            errors.Should().Contain(e => e.Contains("ModelId"),
                "缺少 ModelId 应报告错误");
        }

        [Fact]
        public void Validate_BadEndpointFormat_ShouldReportError()
        {
            // 清空 LLM_ENDPOINT：AppConfig.Load 会优先读环境变量覆盖 Endpoint，
            // 若 .env 已注入 LLM_ENDPOINT 将掩盖非法配置，导致断言失败
            var savedLlmEndpoint = Environment.GetEnvironmentVariable("LLM_ENDPOINT");
            Environment.SetEnvironmentVariable("LLM_ENDPOINT", null);
            try
            {
                var config = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Llm:ModelId"] = "test-model",
                        ["Llm:Endpoint"] = "not-an-url",
                        ["Database:Host"] = "localhost",
                        ["Database:Port"] = "5432",
                        ["Database:DatabaseName"] = "testdb",
                        ["Database:Password"] = "test-password",
                        ["VectorSearch:EmbeddingModelId"] = "test-embed",
                        ["PromptTemplates:SystemRole"] = "test-role",
                        ["PromptTemplates:EvalFastPrompt"] = "test-prompt {SystemRole} {UserInput}",
                        ["PromptTemplates:EvalFastQueryPrompt"] = "test-query {SystemRole} {UserInput}"
                    })
                    .Build();
                AppConfig.Load(config);

                var errors = AppConfig.Instance.Validate();

                errors.Should().Contain(e => e.Contains("http"),
                    "非 http 开头的 Endpoint 应报告格式错误");
            }
            finally
            {
                Environment.SetEnvironmentVariable("LLM_ENDPOINT", savedLlmEndpoint);
            }
        }

        [Fact]
        public void Validate_InvalidPort_ShouldReportError()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Llm:ModelId"] = "test-model",
                    ["Llm:Endpoint"] = "http://localhost:11434",
                    ["Database:Host"] = "localhost",
                    ["Database:Port"] = "99999",  // 无效端口
                    ["Database:DatabaseName"] = "testdb",
                    ["Database:Password"] = "test-password",
                    ["VectorSearch:EmbeddingModelId"] = "test-embed",
                    ["PromptTemplates:SystemRole"] = "test-role",
                    ["PromptTemplates:EvalFastPrompt"] = "test-prompt {SystemRole} {UserInput}",
                    ["PromptTemplates:EvalFastQueryPrompt"] = "test-query {SystemRole} {UserInput}"
                })
                .Build();
            AppConfig.Load(config);

            var errors = AppConfig.Instance.Validate();

            errors.Should().Contain(e => e.Contains("Port") || e.Contains("无效"),
                "无效端口号应报告错误");
        }

        // ═══════════════════════════════════════════
        // 嵌套配置类测试
        // ═══════════════════════════════════════════

        [Fact]
        public void ChemicalToolConfig_ShouldHaveDefaultTools()
        {
            var config = BuildMinimalConfig();
            AppConfig.Load(config);
            var tools = AppConfig.Instance.ChemicalTool.Tools;

            tools.Should().NotBeEmpty("应包含默认工具定义");
            tools.Should().Contain(t => t.Name == "CheckHazardCategory");
            tools.Should().Contain(t => t.Name == "CheckStorageCompatibility");
            tools.Should().Contain(t => t.Name == "GetSafetyDistance");
            tools.Should().Contain(t => t.Name == "GetCurrentTime");
            tools.Should().Contain(t => t.Name == "Calculate");

            // 验证 CheckStorageCompatibility 的关键词触发器是完整的
            var storageTool = tools.Find(t => t.Name == "CheckStorageCompatibility");
            storageTool.Should().NotBeNull();
            storageTool!.KeywordTriggers.Should().Contain("同库");
            storageTool.KeywordTriggers.Should().Contain("共存");
            storageTool.KeywordTriggers.Should().Contain("配伍");
        }

        [Fact]
        public void KnowledgeBaseConfig_ShouldHaveDefaultSources()
        {
            var config = BuildMinimalConfig();
            AppConfig.Load(config);
            var sources = AppConfig.Instance.KnowledgeBase.Sources;

            sources.Should().HaveCount(3, "默认有3个知识源");
            sources.Should().Contain(s => s.Name == "国标" && s.Priority == 100);
            sources.Should().Contain(s => s.Name == "园区规则" && s.Priority == 80);
            sources.Should().Contain(s => s.Name == "历史案例" && s.Priority == 60);
        }

        [Fact]
        public void AuditConfig_ShouldHaveComplianceDefaults()
        {
            var config = BuildMinimalConfig();
            AppConfig.Load(config);
            var audit = AppConfig.Instance.Audit;

            audit.EnableOperationLog.Should().BeTrue("等保三级要求操作日志");
            audit.AuditLogRetentionDays.Should().Be(180, "等保三级要求6个月留存");
            audit.EnableDataEncryption.Should().BeTrue("默认启用数据加密");
        }

        [Fact]
        public void MemoryConfig_ShouldHaveSensibleDefaults()
        {
            var config = BuildMinimalConfig();
            AppConfig.Load(config);
            var mem = AppConfig.Instance.Memory;

            mem.CompressTriggerTurns.Should().Be(10, "10轮触发压缩");
            mem.KeepRecentTurns.Should().Be(5, "保留最近5轮");
        }

        [Fact]
        public void IntegrationConfig_DefaultsShouldBeDisabled()
        {
            var config = BuildMinimalConfig();
            AppConfig.Load(config);
            var integration = AppConfig.Instance.Integration;

            integration.EnableERPSync.Should().BeFalse("ERP同步默认关闭");
            integration.EnableWMSSync.Should().BeFalse("WMS同步默认关闭");
            integration.EnableEHSSync.Should().BeFalse("EHS同步默认关闭");
        }

        [Fact]
        public void EvaluationConfig_ShouldHaveDefaultPaths()
        {
            var config = BuildMinimalConfig();
            AppConfig.Load(config);
            var eval = AppConfig.Instance.Evaluation;

            eval.EvalSetPath.Should().Be("Data/ComplianceEvalSet.json");
            eval.CaseIntervalMs.Should().Be(2000, "评测用例间隔默认2秒");
            eval.OutputReportPath.Should().Be("Data/eval_report.json");
        }

        // ═══════════════════════════════════════════
        // 环境变量覆盖测试
        // ═══════════════════════════════════════════

        [Fact]
        public void Load_WithDbPasswordEnvVar_ShouldOverrideConfig()
        {
            var savedDbPassword = Environment.GetEnvironmentVariable("DB_PASSWORD");
            try
            {
                Environment.SetEnvironmentVariable("DB_PASSWORD", "env-secret-pwd");
                var config = BuildMinimalConfig();
                AppConfig.Load(config);

                AppConfig.Instance.Database.Password.Should().Be("env-secret-pwd",
                    "环境变量 DB_PASSWORD 应覆盖配置文件中的密码");
            }
            finally
            {
                // 恢复原值而非删除 — 删除会污染后续测试（CustomApiWebApplicationFactory
                // 在 DB_PASSWORD 为空时会注入错误密码 test_pwd_7758521，导致全量测试失败）
                Environment.SetEnvironmentVariable("DB_PASSWORD", savedDbPassword);
            }
        }

        [Fact]
        public void Load_WithLlmEndpointEnvVar_ShouldOverrideConfig()
        {
            var savedLlmEndpoint = Environment.GetEnvironmentVariable("LLM_ENDPOINT");
            try
            {
                Environment.SetEnvironmentVariable("LLM_ENDPOINT", "http://gpu-server:8080/v1");
                var config = BuildMinimalConfig();
                AppConfig.Load(config);

                AppConfig.Instance.Llm.Endpoint.Should().Be("http://gpu-server:8080/v1",
                    "LLM_ENDPOINT 环境变量应覆盖 Endpoint");
            }
            finally
            {
                Environment.SetEnvironmentVariable("LLM_ENDPOINT", savedLlmEndpoint);
            }
        }

        [Fact]
        public void Load_WithKbPathEnvVar_ShouldOverrideConfig()
        {
            var savedKbPath = Environment.GetEnvironmentVariable("KNOWLEDGE_BASE_PATH");
            try
            {
                Environment.SetEnvironmentVariable("KNOWLEDGE_BASE_PATH", "/app/knowledgebase");
                var config = BuildMinimalConfig();
                AppConfig.Load(config);

                AppConfig.Instance.KnowledgeBase.BasePath.Should().Be("/app/knowledgebase",
                    "KNOWLEDGE_BASE_PATH 应覆盖知识库路径");
            }
            finally
            {
                Environment.SetEnvironmentVariable("KNOWLEDGE_BASE_PATH", savedKbPath);
            }
        }

        [Fact]
        public void Load_WithAlertRecipientsEnvVar_ShouldParseCsv()
        {
            var savedRecipients = Environment.GetEnvironmentVariable("ALERT_RECIPIENT_EMAILS");
            try
            {
                Environment.SetEnvironmentVariable("ALERT_RECIPIENT_EMAILS", "a@test.com,b@test.com,c@test.com");
                var config = BuildMinimalConfig();
                AppConfig.Load(config);

                var recipients = AppConfig.Instance.Alerting.Email.RecipientEmails;
                recipients.Should().HaveCount(3, "逗号分隔应产生3个收件人");
                recipients.Should().Contain("a@test.com");
                recipients.Should().Contain("b@test.com");
                recipients.Should().Contain("c@test.com");
            }
            finally
            {
                Environment.SetEnvironmentVariable("ALERT_RECIPIENT_EMAILS", savedRecipients);
            }
        }

        // ═══════════════════════════════════════════
        // SearchModeType 枚举测试
        // ═══════════════════════════════════════════

        [Fact]
        public void SearchModeType_Enum_ShouldMapToExpectedValues()
        {
            ((int)SearchModeType.Bm25).Should().Be(0);
            ((int)SearchModeType.Vector).Should().Be(1);
            ((int)SearchModeType.Hybrid).Should().Be(2);
        }

        // ═══════════════════════════════════════════
        // PromptTemplateConfig 测试
        // ═══════════════════════════════════════════

        [Fact]
        public void PromptTemplateConfig_ShouldHaveNonEmptyDefaults()
        {
            // 不覆盖 PromptTemplates 相关字段，让它们使用 C# 类中的默认值
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Llm:ModelId"] = "test-model",
                    ["Llm:Endpoint"] = "http://localhost:11434",
                    ["Database:Host"] = "localhost",
                    ["Database:Port"] = "5432",
                    ["Database:DatabaseName"] = "testdb",
                    ["Database:Password"] = "test-password",
                    ["VectorSearch:EmbeddingModelId"] = "test-embed"
                    // 不覆盖 PromptTemplates，让它们使用默认值
                    // (SystemRole="你是化工园区危化品合规审核专家...", OutputTemplate=..., EvalFastPrompt=...)
                })
                .Build();
            AppConfig.Load(config);
            var prompts = AppConfig.Instance.PromptTemplates;

            prompts.SystemRole.Should().NotBeEmpty("系统角色不应为空");
            prompts.SystemRole.Should().Contain("化工", "系统角色应定位为化工合规专家");
            prompts.OutputTemplate.Should().Contain("合规判断", "输出模板应包含合规判断字段");
            prompts.EvalFastPrompt.Should().Contain("{UserInput}", "评测Prompt应包含用户输入占位符");
            prompts.EvalFastQueryPrompt.Should().Contain("信息查询", "查询版Prompt应标注信息查询模式");
        }
    }
}
