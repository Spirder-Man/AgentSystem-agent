
using Agent1.Config;
using Agent1.Models;
using Agent1.Modules;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Agent1.Services
{
    public class AgentDialog
    {
        private readonly ISessionService _sessionService;
        private readonly IMemoryService _memoryService;
        private readonly ILlmService _llmService;
        private readonly IToolService _toolService;
        private readonly MemoryCoordinator? _memoryCoordinator;
        private readonly IAuditService _auditService;

        /// <summary>最近一次化工合规执行的工具结果（供 Reflection 验证层使用）</summary>
        [Obsolete("请使用 ExecuteAsync 返回的 CliExecutionResult.ToolCalls。LastToolResults 仅保留向后兼容。")]
        public Dictionary<string, string> LastToolResults { get; private set; } = new();
        /// <summary>最近一次工具规划结果</summary>
        [Obsolete("请使用 ExecuteAsync 返回的 CliExecutionResult。LastToolPlan 仅保留向后兼容。")]
        public ToolPlan? LastToolPlan { get; private set; }

        public AgentDialog(
            ISessionService sessionService,
            IMemoryService memoryService,
            ILlmService llmService,
            IToolService toolService,
            IAuditService auditService,
            MemoryCoordinator? memoryCoordinator = null)
        {
            _sessionService = sessionService;
            _memoryService = memoryService;
            _llmService = llmService;
            _toolService = toolService;
            _auditService = auditService;
            _memoryCoordinator = memoryCoordinator;
        }

        public SessionContext CreateSession(SessionType type)
        {
            return _sessionService.CreateSession(type);
        }

        public string GetFormattedHistory(string sessionId)
        {
            return _sessionService.GetFormattedHistory(sessionId);
        }

        public void ClearMemory()
        {
            _memoryService.ClearMemory();
        }
        /// <summary>
        /// 执行对话
        /// </summary>
        /// <param name="userInput">用户输入</param>
        /// <param name="session">会话上下文</param>
        /// <returns>对话执行结果</returns>
        public async Task<CliExecutionResult> ExecuteAsync(string userInput, SessionContext session)
        {
            // 开始执行对话
            var sw = Stopwatch.StartNew();
            // 生成跟踪 ID
            var traceId = Guid.NewGuid().ToString("N")[..8];
            // 重置事件序列号和事件列表
            _eventSeq = 0;
            _currentEvents = new List<PipelineEvent>();
            // 创建指标对象
            var metrics = new PipelineMetrics
            {
                TraceId = traceId,
                InputLength = userInput.Length
            };
            // 记录流水线启动事件
            RecordEvent(traceId, "PipelineStart", $"流水线启动: {userInput.Truncate(60)}",
                new Dictionary<string, object> { ["InputLength"] = userInput.Length });
            // 记录流水线启动事件
            Serilog.Log.Information("[Pipeline] 开始 | TraceId={TraceId} | 输入长度={Len} | 输入={Input}",
                traceId, userInput.Length, userInput.Truncate(80));

            Console.WriteLine("\n═══════ 统一线性流水线启动 ═══════");
            
            // [1/6] 预处理
            var t0 = sw.ElapsedMilliseconds;
            var processedInput = await PreprocessAsync(userInput);
            metrics.PreprocessMs = sw.ElapsedMilliseconds - t0;
            Console.WriteLine($"[1/6] 预处理完成");
            RecordEvent(traceId, "Preprocess", "预处理完成",
                new Dictionary<string, object> { ["ElapsedMs"] = metrics.PreprocessMs });

            // [安全检测] Prompt 注入检测
            var t0s = sw.ElapsedMilliseconds;
            var (safe, reason) = SafetyGuardService.ValidateInput(processedInput);
            metrics.SafetyCheckInputMs = sw.ElapsedMilliseconds - t0s;
            if (!safe)
            {
                Console.WriteLine($"❌ 输入被安全拦截: {reason}");
                Serilog.Log.Warning("[Pipeline] 安全拦截 | TraceId={TraceId} | 原因={Reason}",
                    traceId, reason);
                _ = _auditService.LogOperationAsync("system", "SecurityBlock",
                    $"输入拦截: {reason} | 输入: {processedInput.Truncate(100)}");
                return CliExecutionResult.Blocked(reason);
            }
            
            // [2/6] 意图路由
            var t1 = sw.ElapsedMilliseconds;
            var intent = RouteIntent(processedInput);
            metrics.RouteMs = sw.ElapsedMilliseconds - t1;
            metrics.Intent = intent.ToString();
            metrics.MatchedKeyword = IntentRouter.LastMatchedKeyword;
            Console.WriteLine($"[2/6] 意图归类完成: {intent}");
            RecordEvent(traceId, "IntentRouted", $"意图归类: {intent}",
                new Dictionary<string, object> { ["Intent"] = intent.ToString(), ["MatchedKeyword"] = IntentRouter.LastMatchedKeyword ?? "" });
            
            // [3/6] 上下文加载
            var t2 = sw.ElapsedMilliseconds;
            var context = await LoadContextAsync(session, intent);
            metrics.LoadContextMs = sw.ElapsedMilliseconds - t2;
            Console.WriteLine($"[3/6] 上下文加载完成");
            RecordEvent(traceId, "ContextLoaded", "上下文加载完成",
                new Dictionary<string, object> { ["ElapsedMs"] = metrics.LoadContextMs });
            
            // [4/6] 业务执行
            var t3 = sw.ElapsedMilliseconds;
            var (result, toolCalls, warnings) = await ExecuteBusinessWithResultAsync(processedInput, context, intent);
            metrics.ExecuteBusinessMs = sw.ElapsedMilliseconds - t3;
            metrics.ToolCallCount = toolCalls.Count;
            metrics.OutputLength = result.Length;
            Console.WriteLine($"[4/6] 业务执行完成 ({metrics.ExecuteBusinessMs}ms)");
            RecordEvent(traceId, "BusinessExecuted", $"业务执行完成: {metrics.ExecuteBusinessMs}ms",
                new Dictionary<string, object> { ["ElapsedMs"] = metrics.ExecuteBusinessMs, ["ToolCallCount"] = toolCalls.Count, ["OutputLength"] = result.Length });
            foreach (var tc in toolCalls)
                RecordEvent(traceId, "ToolCalled", $"工具调用: {tc.FunctionName}",
                    new Dictionary<string, object> { ["Function"] = tc.FunctionName, ["Success"] = tc.Success });

            // [安全检测] 输出高危断言检测
            var t4s = sw.ElapsedMilliseconds;
            var (outputSafe, outputWarnings) = SafetyGuardService.ValidateOutput(result);
            metrics.SafetyCheckOutputMs = sw.ElapsedMilliseconds - t4s;
            metrics.WarningCount = outputWarnings.Count;
            var allWarnings = new List<string>(warnings);
            allWarnings.AddRange(outputWarnings);
            var displayOutput = result;
            if (!outputSafe && intent == IntentType.ChemicalCompliance)
            {
                displayOutput = result + "\n\n⚠️ 安全复核提醒:\n" + string.Join("\n", outputWarnings);
            }
            RecordEvent(traceId, "SafetyCheckOutput", $"输出安全检测: {(outputSafe ? "通过" : $"{outputWarnings.Count}条警告")}",
                new Dictionary<string, object> { ["Passed"] = outputSafe, ["Warnings"] = outputWarnings.Count });
            
            // [5/6] 会话保存
            var t4 = sw.ElapsedMilliseconds;
            await SaveSessionAsync(session, userInput, result);
            metrics.SaveSessionMs = sw.ElapsedMilliseconds - t4;
            Console.WriteLine($"[5/6] 会话保存完成");
            RecordEvent(traceId, "SessionSaved", "会话保存完成",
                new Dictionary<string, object> { ["ElapsedMs"] = metrics.SaveSessionMs });
            
            // [6/6] 输出格式化
            var t5 = sw.ElapsedMilliseconds;
            var finalOutput = FormatOutput(displayOutput);
            metrics.FormatOutputMs = sw.ElapsedMilliseconds - t5;
            Console.WriteLine($"[6/6] 结果输出完成");
            RecordEvent(traceId, "OutputFormatted", "输出格式化完成",
                new Dictionary<string, object> { ["ElapsedMs"] = metrics.FormatOutputMs });

            metrics.TotalMs = sw.ElapsedMilliseconds;
            Console.WriteLine("═══════ 流水线结束 ═══════\n");

            Serilog.Log.Information(
                "[Pipeline] 完成 | TraceId={TraceId} | 总耗时={TotalMs}ms | " +
                "意图={Intent} | 工具调用={ToolCount} | 安全警告={WarnCount} | " +
                "路由={RouteMs}ms | 上下文={ContextMs}ms | 执行={ExecMs}ms | 输入安全={SafetyInMs}ms | 输出安全={SafetyOutMs}ms",
                traceId, metrics.TotalMs, metrics.Intent,
                metrics.ToolCallCount, metrics.WarningCount,
                metrics.RouteMs, metrics.LoadContextMs, metrics.ExecuteBusinessMs,
                metrics.SafetyCheckInputMs, metrics.SafetyCheckOutputMs);

            // [P0 安全加固] 审计日志
            if (intent == IntentType.ChemicalCompliance)
            {
                _ = _auditService.LogOperationAsync("system", "ChemicalCompliance",
                    $"合规查询: {userInput.Truncate(80)} | TraceId={traceId} | 工具调用: {toolCalls.Count}个 | 安全警告: {allWarnings.Count}条 | 总耗时={metrics.TotalMs}ms",
                    isSensitive: true);
            }

            RecordEvent(traceId, "PipelineComplete", $"流水线完成: 总耗时={metrics.TotalMs}ms",
                new Dictionary<string, object> { ["TotalMs"] = metrics.TotalMs, ["EventCount"] = _eventSeq });
            // 返回结果
            return new CliExecutionResult
            {
                Success = true,
                DisplayOutput = finalOutput,
                StructuredResult = metrics,
                Warnings = allWarnings,
                Intent = intent,
                MatchedRouteKeyword = IntentRouter.LastMatchedKeyword,
                ToolCalls = toolCalls,
                Events = new List<PipelineEvent>(_currentEvents),
                AuditRecord = intent == IntentType.ChemicalCompliance
                    ? $"合规查询完成, TraceId={traceId}, 工具调用 {toolCalls.Count} 个, 总耗时={metrics.TotalMs}ms, 事件={_eventSeq}条"
                    : "简单对话完成"
            };
        }
        /// <summary>
        /// Phase 1: 预处理输入
        /// </summary>
        private Task<string> PreprocessAsync(string input)
        {
            return Task.FromResult(input.Trim());
        }

        private IntentType RouteIntent(string input)
        {
            return IntentRouter.Route(input);
        }

        private Task<PipelineContext> LoadContextAsync(SessionContext session, IntentType intent)
        {
            // Phase 1.1: 切换到当前会话的作用域
            _memoryService.SetSession(session.SessionId);

            var history = _sessionService.GetFormattedHistory(session.SessionId, 10);
            var memory = _memoryService.GetKeyFacts();
            var userProfile = _memoryService.GetUserProfile();
            
            return Task.FromResult(new PipelineContext
            {
                Session = session,
                History = history,
                Memory = memory,
                UserProfile = userProfile,
                Intent = intent
            });
        }

        private async Task<(string result, List<FunctionCallRecord> toolCalls, List<string> warnings)> ExecuteBusinessWithResultAsync(string input, PipelineContext context, IntentType intent)
        {
            // Phase 4.1: 记忆协调器预推理
            if (_memoryCoordinator != null)
            {
                var userId = context.UserProfile.UserName ?? "default";
                var preResult = await _memoryCoordinator.PreInferenceAsync(context.Session.SessionId, userId, input);

                // [BUG FIX] 仅当缓存命中且来源为真实工具调用时才跳过推理
                // 此前不检查 HasDirectAnswer 的来源质量，导致"未找到/建议查阅"等兜底文本被缓存后
                // 直接返回，跳过 LLM。当知识库后续更新了数据后，用户仍会得到旧的兜底回答。
                if (preResult.HasDirectAnswer)
                {
                    if (!preResult.IsCacheHit && !preResult.IsMemoryHit)
                    {
                        // 非缓存/非记忆来源的直接回答（如长期记忆精确匹配），允许跳过
                        Console.WriteLine($"   → 记忆直接回答（跳过推理）");
                        return (preResult.DirectAnswer, new List<FunctionCallRecord>(), new List<string>());
                    }
                    else if (preResult.HasToolCallsForThisAnswer)
                    {
                        // 缓存/记忆命中了，且确认来源是工具调用结果（非兜底）
                        Console.WriteLine($"   → 记忆直接回答（来源=工具调用，跳过推理）");
                        return (preResult.DirectAnswer, new List<FunctionCallRecord>(), new List<string>());
                    }
                    else
                    {
                        // 缓存/记忆命中但无工具调用来源标记，不可信，继续走 LLM
                        Console.WriteLine($"   ⚠️ 缓存命中但来源不可信（无工具调用），继续推理");
                    }
                }

                // 将长期记忆上下文注入 context
                if (preResult.LongTermContext.Count > 0)
                {
                    context.History = preResult.ShortTermContext + "\n\n【长期记忆（跨会话）】\n" +
                        string.Join("\n", preResult.LongTermContext) + "\n\n" + context.History;
                }
            }

            // [BUG FIX] 原有短期记忆关键词匹配 — 不再作为跳过推理的依据
            // 此前 TryAnswerFromMemory 直接返回 KeyFacts 缓存值而不检查其质量（可能是兜底文本），
            // 现在降级为"上下文提示"：将匹配到的历史事实注入推理但不跳过 LLM
            var memoryAnswer = _memoryService.TryAnswerFromMemory(input);
            if (!string.IsNullOrWhiteSpace(memoryAnswer))
            {
                Console.WriteLine("   → 记忆上下文注入（不跳过推理）");
                // 将历史记忆作为上下文前缀注入 context.History，让 LLM 参考而非盲信
                context.History = $"【历史记忆（供参考，请结合当前知识库核实）】\n{memoryAnswer}\n\n" + context.History;
            }

            if (intent == IntentType.ChemicalCompliance)
            {
                Console.WriteLine("   → 执行化工合规业务");
                return await ExecuteChemicalComplianceAsync(input, context);
            }
            else
            {
                Console.WriteLine("   → 执行通用对话业务");
                var (answer, toolCalls) = await ExecuteGeneralChatAsync(input, context);
                return (answer, toolCalls, new List<string>());
            }
        }

        // [保留向后兼容] ExecuteBusinessAsync 委托给 ExecuteBusinessWithResultAsync
        private async Task<string> ExecuteBusinessAsync(string input, PipelineContext context, IntentType intent)
            => (await ExecuteBusinessWithResultAsync(input, context, intent)).result;

        /// <summary>
        /// Phase 2a: SK Auto Function Calling — LLM 自主决定调用哪些工具
        /// 工具选择和执行由 Semantic Kernel 自动处理，不再需要手动 ReAct 循环
        /// 返回值: (回答文本, 工具调用记录, 安全警告)
        /// </summary>
        private async Task<(string answer, List<FunctionCallRecord> toolCalls, List<string> warnings)> ExecuteChemicalComplianceAsync(string input, PipelineContext context)
        {
            var t = AppConfig.Instance.PromptTemplates;// 获取提示模板
            var history = t.HistoryTemplate.Replace("{History}", context.History ?? "");// 替换历史记录
            var question = t.CurrentQuestionTemplate.Replace("{UserInput}", input);// 替换用户输入
            var prompt = $"{t.SystemRole}\n\n{history}\n\n{question}\n\n{t.OutputTemplate}";// 组合成完整提示   

            Console.WriteLine("\n   【SK Auto Function Calling 模式】");
            Console.ForegroundColor = ConsoleColor.Blue;
            // 提示用户输入化工合规问题
            var answer = await _llmService.InvokeStreamWithRetryAsync(prompt, ConsoleColor.Blue, "化工合规");
            Console.ResetColor();
            Console.WriteLine();

            // Phase 2a 验证: 从 LlmService 诊断记录同步工具调用
            var llmService = _llmService as LlmService;
            var toolCalls = new List<FunctionCallRecord>();// 工具调用记录
            // 从 LlmService 诊断记录同步工具调用
            if (llmService != null && llmService.LastFunctionCalls.Count > 0)
            {
                // 复制工具调用记录
                toolCalls = new List<FunctionCallRecord>(llmService.LastFunctionCalls);
                // 填充工具调用结果
                LastToolResults = new Dictionary<string, string>();
                // 遍历工具调用记录
                foreach (var fc in llmService.LastFunctionCalls)
                {
                    LastToolResults[fc.FunctionName] = fc.Result ?? "(无返回)";
                }
                LastToolPlan = new ToolPlan
                {
                    NeedsTools = true,
                    ToolNames = llmService.LastFunctionCalls.Select(fc => fc.FunctionName).ToList()
                };
            }
            // 如果没有工具调用，则初始化工具调用结果和计划
            else
            {
                LastToolResults = new Dictionary<string, string>();
                LastToolPlan = new ToolPlan { NeedsTools = false };
            }
            // 提取并存储关键事实 input 和 answer 是提示模板的输入和输出
            _memoryService.ExtractAndStoreKeyFacts(input, answer);

            // Phase 4.1: 记忆协调器后推理（异步，不阻塞响应）
            if (_memoryCoordinator != null)
            {
                var userId = context.UserProfile.UserName ?? "default";
                _ = _memoryCoordinator.PostInferenceAsync(context.Session.SessionId, userId, input, answer, LastToolResults);
            }
            // 返回结果：回答文本, 工具调用记录, 安全警告
            return (answer, toolCalls, new List<string>());
        }
        /// <summary>
        /// Phase 2b: 通用对话业务
        /// </summary>
        /// <summary>
        /// Phase 2b: 通用对话业务。
        /// 返回值: (回答文本, 工具调用记录) — 即使 SimpleChat 意图下 SK 仍可能自动调用
        /// GetCurrentTime/Calculate 等系统工具，必须收集工具调用记录以保持统计口径一致。
        /// </summary>
        private async Task<(string answer, List<FunctionCallRecord> toolCalls)> ExecuteGeneralChatAsync(string input, PipelineContext context)
        {
            var t = AppConfig.Instance.PromptTemplates;
            var assistantName = !string.IsNullOrWhiteSpace(context.UserProfile.AssistantName) 
                ? context.UserProfile.AssistantName 
                : "助手";
            var role = t.SimpleChatRole.Replace("{AssistantName}", assistantName);
            var history = t.HistoryTemplate.Replace("{History}", context.History ?? "");
            var userName = !string.IsNullOrWhiteSpace(context.UserProfile.UserName) 
                ? context.UserProfile.UserName 
                : "用户";
            var question = t.SimpleChatQuestionTemplate
                .Replace("{UserInput}", input)
                .Replace("{UserName}", userName);
            var prompt = $"{role}\n\n{history}\n\n{question}";
            
            Console.WriteLine("\n💬 正在生成回复...");
            var answer = await _llmService.InvokeStreamWithRetryAsync(prompt, ConsoleColor.Blue, "简单对话");
            
            // [BUG FIX] 收集 SK Auto FC 在 SimpleChat 路径中调用的系统工具（GetCurrentTime/Calculate）
            // 此前硬编码返回空列表导致 metrics.ToolCallCount=0 与实际 [SK诊断] 输出矛盾
            var toolCalls = new List<FunctionCallRecord>();
            if (_llmService is LlmService llmService && llmService.LastFunctionCalls.Count > 0)
            {
                toolCalls = new List<FunctionCallRecord>(llmService.LastFunctionCalls);
                // 同步到 LastToolResults 供后续 PostInferenceAsync 使用
                var toolResults = new Dictionary<string, string>();
                foreach (var fc in llmService.LastFunctionCalls)
                    toolResults[fc.FunctionName] = fc.Result ?? "(无返回)";
            }

            _memoryService.ExtractAndStoreKeyFacts(input, answer);

            // Phase 4.1: 后推理
            if (_memoryCoordinator != null)
            {
                var userId = context.UserProfile.UserName ?? "default";
                _ = _memoryCoordinator.PostInferenceAsync(context.Session.SessionId, userId, input, answer);
            }

            return (answer, toolCalls);
        }

        /// <summary>
        /// 评测快速通道: 跳过流水线/会话/记忆/流式输出，直接非流式调用 LLM。
        /// 用于 50 条批量评测场景，节省约 40% 单次请求时间。
        /// 工具调用记录回填到 LastToolResults / LastFunctionCalls 供评测器检查。
        /// </summary>
        public async Task<string> ExecuteEvalFastAsync(string userInput)
        {
            // Phase 1.1: 评测通道使用独立会话
            var evalSessionId = $"eval_{DateTime.Now:yyyyMMddHHmmss}";
            _memoryService.SetSession(evalSessionId);

            var t = AppConfig.Instance.PromptTemplates;
            var prompt = t.EvalFastPrompt
                .Replace("{SystemRole}", t.SystemRole)
                .Replace("{UserInput}", userInput);

            return await ExecuteEvalInternalAsync(prompt, "评测");
        }

        /// <summary>
        /// 评测快速通道 (信息查询意图): 使用 EvalFastQueryPrompt，禁止合规判断，仅提取事实。
        /// </summary>
        public async Task<string> ExecuteEvalFastQueryAsync(string userInput)
        {
            // Phase 1.1: 评测通道使用独立会话
            var evalSessionId = $"eval_query_{DateTime.Now:yyyyMMddHHmmss}";
            _memoryService.SetSession(evalSessionId);

            var t = AppConfig.Instance.PromptTemplates;
            var prompt = t.EvalFastQueryPrompt
                .Replace("{SystemRole}", t.SystemRole)
                .Replace("{UserInput}", userInput);

            return await ExecuteEvalInternalAsync(prompt, "评测(信息查询)");
        }

        /// <summary>评测快速通道内部实现</summary>
        private async Task<string> ExecuteEvalInternalAsync(string prompt, string stageName)
        {
            Console.Write("   [非流式] 调用中... ");
            var llmService = _llmService as LlmService;
            string answer;

            if (llmService != null)
            {
                answer = await llmService.InvokeNonStreamingWithRetryAsync(prompt, stageName);

                // 同步工具调用记录供评测器检查
                if (llmService.LastFunctionCalls.Count > 0)
                {
                    LastToolResults = new Dictionary<string, string>();
                    foreach (var fc in llmService.LastFunctionCalls)
                    {
                        LastToolResults[fc.FunctionName] = fc.Result ?? "(无返回)";
                    }
                    LastToolPlan = new ToolPlan
                    {
                        NeedsTools = true,
                        ToolNames = llmService.LastFunctionCalls.Select(fc => fc.FunctionName).ToList()
                    };
                }
                else
                {
                    LastToolResults = new Dictionary<string, string>();
                    LastToolPlan = new ToolPlan { NeedsTools = false };
                }
            }
            else
            {
                // 降级到流式
                answer = await _llmService.InvokeStreamWithRetryAsync(prompt, ConsoleColor.Blue, "化工合规");
            }

            Console.WriteLine("完成");
            return answer;
        }
        /// <summary>
        /// 保存会话记录
        /// </summary>
        private Task SaveSessionAsync(SessionContext session, string input, string result)
        {
            _sessionService.AddDialogTurn(session.SessionId, "User", input);
            _sessionService.AddDialogTurn(session.SessionId, "Assistant", result);
            return Task.CompletedTask;
        }

        private string FormatOutput(string result)
        {
            return result;
        }

        // ── 事件溯源辅助方法 ──

        private int _eventSeq;
        private List<PipelineEvent> _currentEvents = new();

        /// <summary>记录一条流水线事件到内存列表和持久化存储</summary>
        private void RecordEvent(string traceId, string eventType, string description,
            Dictionary<string, object>? data = null)
        {
            _eventSeq++;
            var evt = PipelineEvent.Create(_eventSeq, traceId, eventType, description, data);
            _currentEvents.Add(evt);
        }
    }
}

