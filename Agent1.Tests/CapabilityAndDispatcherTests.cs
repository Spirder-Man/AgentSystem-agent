// ============================================================
// Phase 3 覆盖率爬坡 — 基础设施服务纯逻辑测试
//
// CapabilityRegistry — 原子能力注册表（Register/Get/MatchByInput/GetAll/ExecuteAsync）
// ModuleDispatcher — 模块调度器（ExecuteModuleAsync lazy init + ListModules）
//
// 这些类之前 0% 覆盖率，纯逻辑 + Mock 依赖
// ============================================================

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Agent1.Models;
using Agent1.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace Agent1.Tests;

public class CapabilityRegistryNewTests
{
    private readonly Mock<IModuleFactory> _mockFactory;
    private readonly StubAgentDialog _stubDialog;

    public CapabilityRegistryNewTests()
    {
        _mockFactory = new Mock<IModuleFactory>();
        _stubDialog = new StubAgentDialog();
    }

    private CapabilityRegistry CreateRegistry()
        => new CapabilityRegistry(_stubDialog, _mockFactory.Object);

    // 存根 AgentDialog：仅提供 ExecuteAsync 签名，不依赖真实 LLM/DB
    private class StubAgentDialog : AgentDialog
    {
        public StubAgentDialog() : base(
            new Mock<ISessionService>().Object,
            new Mock<IMemoryService>().Object,
            new Mock<ILlmService>().Object,
            new Mock<IToolService>().Object,
            new Mock<IAuditService>().Object)
        { }
    }

    [Fact]
    public void Get_ExistingCapability_ShouldReturnCapability()
    {
        var registry = CreateRegistry();
        var cap = registry.Get("storage-compliance");

        cap.Should().NotBeNull();
        cap!.Name.Should().Be("storage-compliance");
        cap.Description.Should().Contain("储存");
        cap.RegulationRef.Should().Be("GB 15603-1995");
    }

    [Fact]
    public void Get_NonExistingCapability_ShouldReturnNull()
    {
        var registry = CreateRegistry();
        var cap = registry.Get("non-existent-capability");
        cap.Should().BeNull();
    }

    [Fact]
    public void GetAll_ShouldReturnMultipleCapabilities()
    {
        var registry = CreateRegistry();
        var all = registry.GetAll();

        all.Should().HaveCountGreaterOrEqualTo(6);
        all.Should().Contain(c => c.Name == "storage-compliance");
        all.Should().Contain(c => c.Name == "safety-distance");
        all.Should().Contain(c => c.Name == "hazard-category");
    }

    [Theory]
    [InlineData("储存", "storage-compliance")]
    [InlineData("距离", "safety-distance")]
    [InlineData("应急", "emergency-plan")]
    [InlineData("标签", "ghs-label-check")]
    public void MatchByInput_KeywordMatch_ShouldReturnMatchingCapability(string query, string expectedName)
    {
        var registry = CreateRegistry();
        var matches = registry.MatchByInput(query);

        matches.Should().NotBeEmpty();
        matches.Should().Contain(c => c.Name == expectedName);
    }

    [Fact]
    public void MatchByInput_NoMatch_ShouldNotBeNull()
    {
        var registry = CreateRegistry();
        var matches = registry.MatchByInput("完全不相关的查询xyz123");

        matches.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_RegisteredHandler_ShouldReturnResult()
    {
        var registry = CreateRegistry();
        var session = new SessionContext();

        // 注册自定义能力（带简单 Handler）
        registry.Register(new Capability
        {
            Name = "test-cap",
            Description = "测试能力",
            InputHint = "test",
            Handler = async (query, ctx) => CliExecutionResult.Ok("结果: " + query)
        });

        var result = await registry.ExecuteAsync("test-cap", "苯储存", session);

        result.Success.Should().BeTrue();
        result.DisplayOutput.Should().Contain("苯储存");
    }

    [Fact]
    public async Task ExecuteAsync_UnknownCapability_ShouldThrow()
    {
        var registry = CreateRegistry();
        var session = new SessionContext();

        var act = () => registry.ExecuteAsync("unknown-cap", "query", session);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*未注册的能力*");
    }

    [Fact]
    public void Register_CustomCapability_ShouldBeRetrievable()
    {
        var registry = CreateRegistry();
        var custom = new Capability
        {
            Name = "custom-check",
            Description = "自定义检查",
            RegulationRef = "GB 99999",
            InputHint = "test"
        };

        registry.Register(custom);
        var retrieved = registry.Get("custom-check");

        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("custom-check");
        retrieved.RegulationRef.Should().Be("GB 99999");
    }
}

public class ModuleDispatcherNewTests
{
    private readonly Mock<IModuleFactory> _mockFactory;
    private readonly Mock<IInferenceModule> _mockModule;

    public ModuleDispatcherNewTests()
    {
        _mockFactory = new Mock<IModuleFactory>();
        _mockModule = new Mock<IInferenceModule>();
        _mockModule.Setup(m => m.Name).Returns("测试模块");
        _mockModule.Setup(m => m.Description).Returns("测试描述");
        _mockModule.Setup(m => m.RunAsync()).Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task ExecuteModuleAsync_FirstCall_ShouldCreateAndCacheModule()
    {
        _mockFactory.Setup(f => f.CreateModule(ModuleType.CoTSolid)).Returns(_mockModule.Object);
        var dispatcher = new ModuleDispatcher(_mockFactory.Object);

        await dispatcher.ExecuteModuleAsync(ModuleType.CoTSolid);

        _mockFactory.Verify(f => f.CreateModule(ModuleType.CoTSolid), Times.Once);
        _mockModule.Verify(m => m.RunAsync(), Times.Once);
    }

    [Fact]
    public async Task ExecuteModuleAsync_CachedModule_ShouldReuseWithoutFactoryCall()
    {
        _mockFactory.Setup(f => f.CreateModule(ModuleType.ReActSolid)).Returns(_mockModule.Object);
        var dispatcher = new ModuleDispatcher(_mockFactory.Object);

        await dispatcher.ExecuteModuleAsync(ModuleType.ReActSolid);
        await dispatcher.ExecuteModuleAsync(ModuleType.ReActSolid);

        _mockFactory.Verify(f => f.CreateModule(ModuleType.ReActSolid), Times.Once);
        _mockModule.Verify(m => m.RunAsync(), Times.Exactly(2));
    }

    [Fact]
    public void ListModules_ShouldOutputAvailableModules()
    {
        _mockFactory.Setup(f => f.GetAvailableModules()).Returns(new[] { ModuleType.CoTSolid, ModuleType.ReActSolid });
        _mockFactory.Setup(f => f.CreateModule(It.IsAny<ModuleType>())).Returns(_mockModule.Object);
        var dispatcher = new ModuleDispatcher(_mockFactory.Object);

        var act = () => dispatcher.ListModules();
        act.Should().NotThrow();
    }
}
