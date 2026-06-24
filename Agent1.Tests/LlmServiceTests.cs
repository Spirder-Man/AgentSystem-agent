using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Agent1.Config;
using Agent1.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// LlmService 单元测试 — 熔断器 + Thinking 控制。
/// 通过反射访问私有成员，不依赖真实的 LLM 连接。
/// </summary>
public class LlmServiceTests
{
    private static readonly ConstructorInfo? _llmServiceCtor;
    private static readonly MethodInfo? _checkCircuitBreaker;
    private static readonly MethodInfo? _recordCircuitSuccess;
    private static readonly MethodInfo? _recordCircuitFailure;
    private static readonly FieldInfo? _consecutiveFailuresField;
    private static readonly FieldInfo? _circuitOpenTimeField;
    private static readonly PropertyInfo? _enableThinkingProp;
    private static readonly PropertyInfo? _circuitBreakDurationProp;

    static LlmServiceTests()
    {
        // 初始化最小 AppConfig
        try
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Llm:ModelId"] = "test-model",
                    ["Llm:Endpoint"] = "http://localhost:11434",
                    ["Llm:MaxRetries"] = "2",
                    ["Llm:RetryDelayMs"] = "10",
                    ["Database:Host"] = "localhost",
                    ["Database:Port"] = "5432",
                    ["Database:DatabaseName"] = "test",
                    ["VectorSearch:EmbeddingModelId"] = "test-embed",
                    ["VectorSearch:EmbeddingTimeoutSeconds"] = "5",
                    ["VectorSearch:MaxConcurrentEmbeddings"] = "2",
                    ["PromptTemplates:SystemRole"] = "test",
                    ["PromptTemplates:EvalFastPrompt"] = "test {SystemRole} {UserInput}",
                    ["PromptTemplates:EvalFastQueryPrompt"] = "test {SystemRole} {UserInput}"
                })
                .Build();
            AppConfig.Load(config);
        }
        catch (InvalidOperationException) { /* 已被初始化 */ }

        // 反射绑定 — 编译期不依赖 private API
        var type = typeof(LlmService);
        var bf = BindingFlags.NonPublic | BindingFlags.Instance;
        var bfAll = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        _llmServiceCtor = type.GetConstructor(
            BindingFlags.Public | BindingFlags.Instance, null,
            new[] { typeof(Lazy<IKnowledgeBaseService>) }, null);

        _checkCircuitBreaker = type.GetMethod("CheckCircuitBreaker", bf);
        _recordCircuitSuccess = type.GetMethod("RecordCircuitSuccess", bf);
        _recordCircuitFailure = type.GetMethod("RecordCircuitFailure", bf);
        _consecutiveFailuresField = type.GetField("_consecutiveFailures", bf);
        _circuitOpenTimeField = type.GetField("_circuitOpenTime", bf);
        _enableThinkingProp = type.GetProperty("EnableThinking", bfAll);
        _circuitBreakDurationProp = type.GetProperty("CircuitBreakDuration",
            BindingFlags.NonPublic | BindingFlags.Static);
    }

    /// <summary>创建 LlmService 实例（通过构造函数注入 Lazy）</summary>
    private static LlmService CreateLlmService()
    {
        var lazyKb = new Lazy<IKnowledgeBaseService>(() =>
            new Moq.Mock<IKnowledgeBaseService>().Object);
        return (LlmService)_llmServiceCtor!.Invoke(new object[] { lazyKb })!;
    }

    // ═══════════════════════════════════════
    // 熔断器 — 正常状态
    // ═══════════════════════════════════════

    [Fact]
    public void CircuitBreaker_InitialState_DoesNotThrow()
    {
        var svc = CreateLlmService();

        var act = () => _checkCircuitBreaker!.Invoke(svc, null);

        act.Should().NotThrow("初始状态熔断器应为关闭状态");
    }

    [Fact]
    public void CircuitBreaker_InitialFailureCount_IsZero()
    {
        var svc = CreateLlmService();

        var count = (int)_consecutiveFailuresField!.GetValue(svc)!;
        var openTime = _circuitOpenTimeField!.GetValue(svc);

        count.Should().Be(0, "初始失败计数应为 0");
        openTime.Should().BeNull("初始状态电路打开时间应为 null");
    }

    // ═══════════════════════════════════════
    // 熔断器 — 失败累积
    // ═══════════════════════════════════════

    [Fact]
    public void CircuitBreaker_OneFailure_IncrementsCount()
    {
        var svc = CreateLlmService();

        _recordCircuitFailure!.Invoke(svc, null);

        var count = (int)_consecutiveFailuresField!.GetValue(svc)!;
        count.Should().Be(1, "第1次失败后计数应为 1");
    }

    [Fact]
    public void CircuitBreaker_TwoFailures_DoesNotOpen()
    {
        var svc = CreateLlmService();

        _recordCircuitFailure!.Invoke(svc, null); // 1
        _recordCircuitFailure!.Invoke(svc, null); // 2

        var count = (int)_consecutiveFailuresField!.GetValue(svc)!;
        count.Should().Be(2);
        var act = () => _checkCircuitBreaker!.Invoke(svc, null);
        act.Should().NotThrow("连续 2 次失败不应触发熔断（阈值为 3）");
    }

    [Fact]
    public void CircuitBreaker_ThreeFailures_OpensCircuit()
    {
        var svc = CreateLlmService();

        _recordCircuitFailure!.Invoke(svc, null); // 1
        _recordCircuitFailure!.Invoke(svc, null); // 2
        _recordCircuitFailure!.Invoke(svc, null); // 3

        var count = (int)_consecutiveFailuresField!.GetValue(svc)!;
        var openTime = _circuitOpenTimeField!.GetValue(svc);

        count.Should().Be(3, "连续 3 次失败后计数为 3");
        openTime.Should().NotBeNull("连续 3 次失败后熔断器应打开");
    }

    [Fact]
    public void CircuitBreaker_WhenOpen_ThrowsCircuitBreakerOpenException()
    {
        var svc = CreateLlmService();

        // 触发 3 次失败打开熔断
        _recordCircuitFailure!.Invoke(svc, null);
        _recordCircuitFailure!.Invoke(svc, null);
        _recordCircuitFailure!.Invoke(svc, null);

        var act = () => _checkCircuitBreaker!.Invoke(svc, null);
        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<CircuitBreakerOpenException>()
            .WithMessage("*熔断*");
    }

    // ═══════════════════════════════════════
    // 熔断器 — 半开与恢复
    // ═══════════════════════════════════════

    [Fact]
    public void CircuitBreaker_AfterCooldown_EntersHalfOpen()
    {
        var svc = CreateLlmService();

        // 打开熔断器
        _recordCircuitFailure!.Invoke(svc, null);
        _recordCircuitFailure!.Invoke(svc, null);
        _recordCircuitFailure!.Invoke(svc, null);

        // 模拟冷却期已过（直接设置 _circuitOpenTime 为 31 秒前）
        _circuitOpenTimeField!.SetValue(svc, DateTime.UtcNow.AddSeconds(-31));

        // CheckCircuitBreaker 应将计数重置并允许通过
        var act = () => _checkCircuitBreaker!.Invoke(svc, null);
        act.Should().NotThrow("冷却期已过应允许试探请求");

        var count = (int)_consecutiveFailuresField!.GetValue(svc)!;
        count.Should().Be(0, "半开状态下计数应已重置");
        _circuitOpenTimeField!.GetValue(svc).Should().BeNull("半开状态下 openTime 应为 null");
    }

    [Fact]
    public void CircuitBreaker_SuccessResetsCounter()
    {
        var svc = CreateLlmService();

        _recordCircuitFailure!.Invoke(svc, null); // 1
        _recordCircuitFailure!.Invoke(svc, null); // 2

        _recordCircuitSuccess!.Invoke(svc, null);  // 成功应重置

        var count = (int)_consecutiveFailuresField!.GetValue(svc)!;
        count.Should().Be(0, "成功后失败计数应重置为 0");
        _circuitOpenTimeField!.GetValue(svc).Should().BeNull("成功后 openTime 应为 null");
    }

    [Fact]
    public void CircuitBreaker_FiveFailures_FourthAndFifthStillThrow()
    {
        var svc = CreateLlmService();

        _recordCircuitFailure!.Invoke(svc, null); // 1
        _recordCircuitFailure!.Invoke(svc, null); // 2
        _recordCircuitFailure!.Invoke(svc, null); // 3 → 打开

        // 第4、5次也记录失败
        _recordCircuitFailure!.Invoke(svc, null); // 4
        _recordCircuitFailure!.Invoke(svc, null); // 5

        var act = () => _checkCircuitBreaker!.Invoke(svc, null);
        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<CircuitBreakerOpenException>();
    }

    // ═══════════════════════════════════════
    // 熔断器 — 线程安全（基本验证）
    // ═══════════════════════════════════════

    [Fact]
    public async Task CircuitBreaker_ConcurrentFailures_CountIsConsistent()
    {
        var svc = CreateLlmService();
        var tasks = new Task[10];

        for (int i = 0; i < 10; i++)
        {
            tasks[i] = Task.Run(() => _recordCircuitFailure!.Invoke(svc, null));
        }
        await Task.WhenAll(tasks);

        var count = (int)_consecutiveFailuresField!.GetValue(svc)!;
        count.Should().Be(10, "10 个并发 RecordCircuitFailure 应全部计入");
        _circuitOpenTimeField!.GetValue(svc).Should().NotBeNull("超过 3 次后熔断器应打开");
    }

    // ═══════════════════════════════════════
    // Thinking 控制
    // ═══════════════════════════════════════

    [Fact]
    public void EnableThinking_DefaultValue_IsFalse()
    {
        var svc = CreateLlmService();

        var enabled = (bool)_enableThinkingProp!.GetValue(svc)!;

        enabled.Should().BeFalse(
            "默认应关闭 Thinking 模式以获取快速响应（Function Calling/评测/对话场景）");
    }

    [Fact]
    public void EnableThinking_CanToggle()
    {
        var svc = CreateLlmService();

        _enableThinkingProp!.SetValue(svc, true);
        ((bool)_enableThinkingProp.GetValue(svc)!).Should().BeTrue("设置为 true");

        _enableThinkingProp.SetValue(svc, false);
        ((bool)_enableThinkingProp.GetValue(svc)!).Should().BeFalse("重置为 false");
    }

    // ═══════════════════════════════════════
    // 熔断器常量
    // ═══════════════════════════════════════

    [Fact]
    public void CircuitBreakDuration_Is30Seconds()
    {
        // 通过反射获取静态只读属性的值（不需要实例）
        var duration = _circuitBreakDurationProp!.GetValue(null);

        duration.Should().Be(TimeSpan.FromSeconds(30),
            "熔断器冷却时间应为 30 秒");
    }

    [Fact]
    public void MaxConsecutiveFailures_Is3()
    {
        var field = typeof(LlmService).GetField("MaxConsecutiveFailures",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

        // const 字段通过类型获取（static）
        var value = (int)field!.GetValue(null)!;

        value.Should().Be(3, "默认熔断阈值为 3 次连续失败");
    }
}
