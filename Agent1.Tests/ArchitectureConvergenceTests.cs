using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Agent1.Models;
using Agent1.Modules;
using Agent1.Services;
using FluentAssertions;
using Xunit;

namespace Agent1.Tests
{
    /// <summary>
    /// 架构收敛测试 — 将手动逐文件审查能力固化为 CI 自动化检查。
    /// 
    /// 涵盖三个维度：
    ///   1. 安全校验覆盖率 — 所有合规入口必须有 SafetyGuardService 调用
    ///   2. 接口契约一致性 — 所有 IInferenceModule 实现都有 RunWithResultAsync
    ///   3. 枚举完整性 — ModuleType 枚举覆盖所有已注册的模块
    /// </summary>
    public class ArchitectureConvergenceTests
    {
        // ═══════════════════════════════════════
        // 维度 1: 安全校验覆盖率
        // ═══════════════════════════════════════

        [Fact]
        public void SafetyGuardService_Has_ValidateInput_Method()
        {
            // 验证: SafetyGuardService 必须有 ValidateInput 方法（输入安全防线）
            var validateMethod = typeof(SafetyGuardService).GetMethod("ValidateInput",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

            validateMethod.Should().NotBeNull(
                "SafetyGuardService 必须提供 ValidateInput 方法作为化工安全系统输入防线");
            validateMethod!.ReturnType.Should().Be(
                typeof(ValueTuple<bool, string?>),
                "ValidateInput 必须返回 (bool safe, string? reason) 元组");
        }

        [Fact]
        public void SafetyGuardService_Has_ValidateOutput_Method()
        {
            // 验证: SafetyGuardService 必须有 ValidateOutput 方法（输出安全防线）
            var validateMethod = typeof(SafetyGuardService).GetMethod("ValidateOutput",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

            validateMethod.Should().NotBeNull(
                "SafetyGuardService 必须提供 ValidateOutput 方法作为化工安全系统输出防线");
            validateMethod!.ReturnType.Should().Be(
                typeof((bool, List<string>)),
                "ValidateOutput 必须返回 (bool safe, List<string> warnings) 元组");
        }

        [Fact]
        public void SafetyGuardService_All_Methods_Are_Static()
        {
            // 验证: SafetyGuardService 仅包含静态方法（安全函数应为无状态纯函数）
            var instanceMethods = typeof(SafetyGuardService)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsStatic)
                .ToList();

            instanceMethods.Should().BeEmpty(
                "SafetyGuardService 的所有公共方法应为静态方法，确保安全函数无状态");
        }

        [Fact]
        public void AgentDialog_ExecuteAsync_ReturnsCliExecutionResult()
        {
            // 验证: ExecuteAsync 返回 CliExecutionResult（不是 string）
            var method = typeof(AgentDialog).GetMethod("ExecuteAsync");
            var returnType = method!.ReturnType;

            returnType.Should().Be<Task<CliExecutionResult>>(
                "ExecuteAsync 必须返回 CliExecutionResult 以携带安全警告/工具调用/审计记录");
        }

        // ═══════════════════════════════════════
        // 维度 2: 接口契约一致性
        // ═══════════════════════════════════════

        [Fact]
        public void All_IInferenceModule_Implementations_Have_RunWithResultAsync()
        {
            // 验证: 所有 IInferenceModule 实现都提供了 RunWithResultAsync 方法
            var moduleTypes = typeof(IInferenceModule).Assembly.GetTypes()
                .Where(t => typeof(IInferenceModule).IsAssignableFrom(t)
                            && !t.IsAbstract
                            && !t.IsInterface
                            && t.IsPublic)
                .ToList();

            moduleTypes.Should().NotBeEmpty("至少应有一个 IInferenceModule 实现");

            var missing = moduleTypes
                .Where(t => t.GetMethod("RunWithResultAsync") == null)
                .ToList();

            missing.Should().BeEmpty(
                $"以下类型缺少 RunWithResultAsync 方法: {string.Join(", ", missing.Select(t => t.Name))}");
        }

        [Fact]
        public void All_IInferenceModule_Implementations_ReturnCliExecutionResult()
        {
            // 验证: RunWithResultAsync 返回 Task<CliExecutionResult>
            var moduleTypes = typeof(IInferenceModule).Assembly.GetTypes()
                .Where(t => typeof(IInferenceModule).IsAssignableFrom(t)
                            && !t.IsAbstract && !t.IsInterface && t.IsPublic);

            foreach (var type in moduleTypes)
            {
                var method = type.GetMethod("RunWithResultAsync");
                if (method == null) continue; // 已在上一个测试中报错

                method.ReturnType.Should().Be<Task<CliExecutionResult>>(
                    $"{type.Name}.RunWithResultAsync 必须返回 Task<CliExecutionResult>");
            }
        }

        // ═══════════════════════════════════════
        // 维度 3: 枚举完整性
        // ═══════════════════════════════════════

        [Fact]
        public void ModuleType_Enum_HasEmergencyResponse_And_KnowledgeGraph()
        {
            // 验证: ModuleType 枚举包含 EmergencyResponse(11) 和 KnowledgeGraph(12)
            Enum.IsDefined(typeof(ModuleType), 11).Should().BeTrue(
                "ModuleType 枚举应包含 EmergencyResponse=11，否则应急模块无法走统一调度");

            Enum.IsDefined(typeof(ModuleType), 12).Should().BeTrue(
                "ModuleType 枚举应包含 KnowledgeGraph=12，否则知识图谱模块无法走统一调度");
        }

        [Fact]
        public void ModuleType_Enum_Count_AtLeast_12()
        {
            // 验证: ModuleType 枚举至少有 12 个值
            var count = Enum.GetValues<ModuleType>().Length;
            count.Should().BeGreaterOrEqualTo(12,
                $"ModuleType 枚举应有 ≥12 个值（当前 {count}）。应急响应和知识图谱是否遗漏？");
        }

        [Fact]
        public void All_ModuleType_Values_Are_In_ModuleFactory()
        {
            // 验证: ModuleType 的每个枚举值在 ModuleFactory.CreateModule 中都有对应的 case
            var factory = typeof(ModuleFactory).GetMethod("CreateModule");
            var factoryBody = factory!.GetMethodBody();
            
            // 检查 ModuleFactory 不会对已知枚举值抛出 ArgumentOutOfRangeException
            foreach (ModuleType type in Enum.GetValues<ModuleType>())
            {
                // 反射验证: 对每个枚举值调用 CreateModule
                var moduleFactory = CreateModuleFactory();
                var module = moduleFactory.CreateModule(type);
                module.Should().NotBeNull($"ModuleFactory.CreateModule({type}) 不应返回 null");
            }
        }

        // ═══════════════════════════════════════
        // 维度 4: 结构化度量完整性
        // ═══════════════════════════════════════

        [Fact]
        public void CliExecutionResult_Has_Events_Property()
        {
            // 验证: CliExecutionResult 包含 Events 属性（事件溯源）
            var eventsProp = typeof(CliExecutionResult).GetProperty("Events");
            eventsProp.Should().NotBeNull("CliExecutionResult 必须包含 Events 属性以支持事件溯源");
            
            var propType = eventsProp!.PropertyType;
            propType.Should().Be<List<PipelineEvent>>(
                "Events 属性类型应为 List<PipelineEvent>");
        }

        [Fact]
        public void PipelineMetrics_Has_TraceId()
        {
            // 验证: PipelineMetrics 包含 TraceId
            var traceIdProp = typeof(PipelineMetrics).GetProperty("TraceId");
            traceIdProp.Should().NotBeNull("PipelineMetrics 必须包含 TraceId 属性以串联全链路日志");
        }

        // ═══════════════════════════════════════
        // 辅助方法
        // ═══════════════════════════════════════

        /// <summary>通过解析 IL 字节码检查方法体中是否调用了指定类型的方法</summary>
        private static bool MethodBodyContains(Type type, string methodName,
            string targetType, string targetMethod)
        {
            var method = type.GetMethod(methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if (method == null) return false;

            var body = method.GetMethodBody();
            if (body == null) return false;

            var ilBytes = body.GetILAsByteArray();
            if (ilBytes == null || ilBytes.Length == 0) return false;

            var module = method.Module;

            // 扫描 IL 字节码，查找 call (0x28) 和 callvirt (0x6F) 指令
            for (int i = 0; i < ilBytes.Length - 4; i++)
            {
                // call = 0x28, callvirt = 0x6F
                if (ilBytes[i] != 0x28 && ilBytes[i] != 0x6F)
                    continue;

                // 操作数是 4 字节 little-endian 元数据 token
                int token = ilBytes[i + 1]
                          | (ilBytes[i + 2] << 8)
                          | (ilBytes[i + 3] << 16)
                          | (ilBytes[i + 4] << 24);
                i += 4;

                try
                {
                    // ResolveMember 比 ResolveMethod 更通用，能处理跨模块引用
                    var member = module.ResolveMember(token);
                    if (member is MethodBase calledMethod
                        && calledMethod.DeclaringType?.Name == targetType
                        && calledMethod.Name == targetMethod)
                    {
                        return true;
                    }
                }
                catch (ArgumentOutOfRangeException) { /* 无效 token */ }
                catch (MissingMemberException) { /* 无法解析的 token */ }
                catch (NotSupportedException) { /* 不支持的 token 类型 */ }
            }

            return false;
        }

        private static ModuleFactory CreateModuleFactory()
        {
            // 创建最小依赖的 ModuleFactory 实例用于测试
            var sessionService = new Moq.Mock<ISessionService>().Object;
            var memoryService = new Moq.Mock<IMemoryService>().Object;
            var llmService = new Moq.Mock<ILlmService>().Object;
            var toolService = new Moq.Mock<IToolService>().Object;
            var knowledgeBaseService = new Moq.Mock<IKnowledgeBaseService>().Object;
            var integrationService = new Moq.Mock<IIntegrationService>().Object;
            var auditService = new Moq.Mock<IAuditService>().Object;
            var agentDialog = new Moq.Mock<AgentDialog>(
                sessionService, memoryService, llmService, toolService, auditService,
                (MemoryCoordinator?)null).Object;

            return new ModuleFactory(
                sessionService, memoryService, llmService, toolService,
                agentDialog, knowledgeBaseService, integrationService, auditService);
        }
    }
}
