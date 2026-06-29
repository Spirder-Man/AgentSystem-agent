using System;
using System.Collections.Generic;
using Agent1.Models;
using Agent1.Services;
using FluentAssertions;
using Xunit;

namespace Agent1.Tests
{
    /// <summary>
    /// L0 层：CliExecutionResult 统一输出契约测试。
    /// 理解点：这个类是模块输出到 CLI/API/审计的统一载体，工厂方法体现了两条代码路径。
    /// </summary>
    public class CliExecutionResultTests
    {
        [Fact]
        public void Ok_ShouldCreateSuccessResult()
        {
            var result = CliExecutionResult.Ok("操作成功", new { Id = 1 });

            result.Success.Should().BeTrue("Ok工厂方法创建的应标记为成功");
            result.DisplayOutput.Should().Be("操作成功");
            result.StructuredResult.Should().NotBeNull("应包含结构化结果");
            result.Warnings.Should().BeEmpty("成功的操作不应有警告");
            result.ToolCalls.Should().BeEmpty("未进行工具调用时列表为空");
            result.Events.Should().BeEmpty("未注入事件时列表为空");
        }

        [Fact]
        public void Ok_WithoutStructuredResult_ShouldHaveNullStructuredResult()
        {
            var result = CliExecutionResult.Ok("仅展示文本");

            result.Success.Should().BeTrue();
            result.DisplayOutput.Should().Be("仅展示文本");
            result.StructuredResult.Should().BeNull("未传入结构化结果时应为null");
        }

        [Fact]
        public void Blocked_ShouldCreateFailedResult_WithWarning()
        {
            var result = CliExecutionResult.Blocked("检测到Prompt注入");

            result.Success.Should().BeFalse("安全拦截应标记为失败");
            result.DisplayOutput.Should().Contain("拦截", "输出应包含拦截信息");
            result.DisplayOutput.Should().Contain("Prompt注入", "输出应包含拦截原因");
            result.Warnings.Should().ContainSingle("应有一条警告");
            result.Warnings[0].Should().Be("检测到Prompt注入");
            result.AuditRecord.Should().Be("安全拦截: 检测到Prompt注入",
                "审计记录应包含拦截原因");
        }

        [Fact]
        public void Blocked_ShouldHaveEmptyToolCallsAndEvents()
        {
            var result = CliExecutionResult.Blocked("SQL注入");

            result.ToolCalls.Should().BeEmpty("安全拦截不应执行工具调用");
            result.Events.Should().BeEmpty("安全拦截不应生成事件");
        }

        [Fact]
        public void Properties_ShouldBeIndependentlySettable()
        {
            var result = new CliExecutionResult
            {
                Success = true,
                DisplayOutput = "custom output",
                Intent = IntentType.ChemicalCompliance,
                MatchedRouteKeyword = "储存",
                AuditRecord = "audit-001",
                ToolCalls = new List<FunctionCallRecord>
                {
                    new() { FunctionName = "CheckHazardCategory", Success = true }
                },
                Events = new List<PipelineEvent>
                {
                    PipelineEvent.Create(1, "trace-001", "InputReceived", "用户输入已接收")
                }
            };

            result.Intent.Should().Be(IntentType.ChemicalCompliance);
            result.MatchedRouteKeyword.Should().Be("储存",
                "MatchedRouteKeyword 记录触发路由的关键词");
            result.AuditRecord.Should().Be("audit-001");
            result.ToolCalls.Should().HaveCount(1);
            result.ToolCalls[0].FunctionName.Should().Be("CheckHazardCategory");
            result.Events.Should().HaveCount(1);
            result.Events[0].TraceId.Should().Be("trace-001");
        }
    }

    /// <summary>
    /// L0 层：PipelineEvent 事件溯源单元测试。
    /// 理解点：C# record 的不可变性 + init-only 属性如何在事件溯源中保证数据完整性。
    /// </summary>
    public class PipelineEventTests
    {
        [Fact]
        public void Create_ShouldProduceImmutableEvent()
        {
            var evt = PipelineEvent.Create(
                eventId: 1,
                traceId: "abc12345",
                eventType: "InputReceived",
                description: "收到用户输入",
                data: new Dictionary<string, object> { ["length"] = 42 }
            );

            evt.EventId.Should().Be(1);
            evt.TraceId.Should().Be("abc12345");
            evt.EventType.Should().Be("InputReceived");
            evt.Description.Should().Be("收到用户输入");
            evt.Data.Should().ContainKey("length");
            evt.Data["length"].Should().Be(42);
            evt.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1),
                "时间戳应为创建时的UTC时间");
        }

        [Fact]
        public void Create_WithNullData_ShouldUseEmptyDictionary()
        {
            var evt = PipelineEvent.Create(1, "trace", "Test", "desc");

            evt.Data.Should().NotBeNull("即使传入null也应使用空字典");
            evt.Data.Should().BeEmpty();
        }

        [Fact]
        public void Record_IsImmutable_InitOnlyProperties()
        {
            // C# record 的 init-only 属性在对象创建后不可修改。
            // 此测试验证编译期约束：外部代码无法修改已创建的事件。
            var evt = PipelineEvent.Create(1, "trace", "Test", "original");

            // 验证属性已正确设置
            evt.Description.Should().Be("original");
            evt.EventId.Should().Be(1);

            // 注：由于 init-only 属性，编译器不会允许以下代码：
            // evt.EventId = 2;  // 编译错误
            // 这就是事件溯源所需的不变性保证。
        }

        [Fact]
        public void MultipleEvents_WithSameTraceId_FormAuditChain()
        {
            var traceId = "audit-chain-01";
            var events = new List<PipelineEvent>
            {
                PipelineEvent.Create(1, traceId, "PipelineStart", "流水线启动"),
                PipelineEvent.Create(2, traceId, "IntentRouted", "意图路由: ChemicalCompliance"),
                PipelineEvent.Create(3, traceId, "BusinessExecuted", "业务执行完成"),
                PipelineEvent.Create(4, traceId, "PipelineComplete", "流水线完成")
            };

            events.Should().HaveCount(4);
            events.Should().AllSatisfy(e => e.TraceId.Should().Be(traceId),
                "同一请求的所有事件应有相同 TraceId");
            events.Should().BeInAscendingOrder(e => e.EventId,
                "事件应按 EventId 升序排列形成审计链");
        }
    }

    /// <summary>
    /// L0 层：PipelineMetrics 性能指标模型测试。
    /// 理解点：6+2 步流水线的时间指标如何结构化采集。
    /// </summary>
    public class PipelineMetricsTests
    {
        [Fact]
        public void DefaultInstance_ShouldHaveZeroTimings()
        {
            var metrics = new PipelineMetrics();

            metrics.TraceId.Should().BeEmpty("默认 TraceId 为空");
            metrics.InputLength.Should().Be(0);
            metrics.PreprocessMs.Should().Be(0);
            metrics.RouteMs.Should().Be(0);
            metrics.LoadContextMs.Should().Be(0);
            metrics.ExecuteBusinessMs.Should().Be(0);
            metrics.SaveSessionMs.Should().Be(0);
            metrics.FormatOutputMs.Should().Be(0);
            metrics.TotalMs.Should().Be(0);
            metrics.ToolCallCount.Should().Be(0);
            metrics.OutputLength.Should().Be(0);
            metrics.WarningCount.Should().Be(0);
        }

        [Fact]
        public void AllTimingProperties_ShouldBeIndependentlySettable()
        {
            var metrics = new PipelineMetrics
            {
                TraceId = "perf-001",
                InputLength = 256,
                PreprocessMs = 5,
                SafetyCheckInputMs = 3,
                RouteMs = 2,
                LoadContextMs = 15,
                ExecuteBusinessMs = 1200,
                SafetyCheckOutputMs = 8,
                SaveSessionMs = 10,
                FormatOutputMs = 3,
                TotalMs = 1246,
                ToolCallCount = 2,
                OutputLength = 512,
                Intent = "ChemicalCompliance",
                MatchedKeyword = "储存",
                WarningCount = 0
            };

            metrics.TraceId.Should().Be("perf-001");
            metrics.ExecuteBusinessMs.Should().Be(1200,
                "业务执行通常是耗时最长的步骤");
            metrics.ToolCallCount.Should().Be(2);
            metrics.Intent.Should().Be("ChemicalCompliance");
        }

        [Fact]
        public void ToProperties_ShouldReturnAllKeyMetrics()
        {
            var metrics = new PipelineMetrics
            {
                TraceId = "props-001",
                ExecuteBusinessMs = 500,
                TotalMs = 600
            };

            var props = metrics.ToProperties();

            props.Should().NotBeNull();
            props.Should().ContainKey("TraceId");
            props["TraceId"].Should().Be("props-001");
            props.Should().ContainKey("ExecuteBusinessMs");
            props.Should().ContainKey("TotalMs");
        }
    }
}
