
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

        public async Task<CliExecutionResult> ExecuteAsync(string userInput, SessionContext session)
        {
            var sw = Stopwatch.StartNew();
            var traceId = Guid.NewGuid().ToString("N")[..8];
            _eventSeq = 0;
            _currentEvents = new List<PipelineEvent>();
            var metrics = new PipelineMetrics
            {
                TraceId = traceId,
                InputLength = userInput.Length
            };

            RecordEvent(traceId, "PipelineStart", $"流水线启动: {userInput.Truncate(60)}",
                new Dictionary<string, object> { ["InputLength"] = userInput.Length });

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

                if (preResult.HasDirectAnswer)
                {
                    Console.WriteLine($"   → 记忆直接回答（跳过推理）");
                    return (preResult.DirectAnswer, new List<FunctionCallRecord>(), new List<string>());
                }

                // 将长期记忆上下文注入 context
                if (preResult.LongTermContext.Count > 0)
                {
                    context.History = preResult.ShortTermContext + "\n\n【长期记忆（跨会话）】\n" +
                        string.Join("\n", preResult.LongTermContext) + "\n\n" + context.History;
                }
            }

            // 兜底：原有短期记忆关键词匹配
            var memoryAnswer = _memoryService.TryAnswerFromMemory(input);
            if (!string.IsNullOrWhiteSpace(memoryAnswer))
            {
                Console.WriteLine("   → 使用记忆回答");
                Console.WriteLine("\n🧠 从记忆中找到答案！");
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.WriteLine(memoryAnswer);
                Console.ResetColor();
                _memoryService.ExtractAndStoreKeyFacts(input, memoryAnswer);
                return (memoryAnswer, new List<FunctionCallRecord>(), new List<string>());
            }

            if (intent == IntentType.ChemicalCompliance)
            {
                Console.WriteLine("   → 执行化工合规业务");
                return await ExecuteChemicalComplianceAsync(input, context);
            }
            else
            {
                Console.WriteLine("   → 执行通用对话业务");
                var answer = await ExecuteGeneralChatAsync(input, context);
                return (answer, new List<FunctionCallRecord>(), new List<string>());
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
            var t = AppConfig.Instance.PromptTemplates;
            var history = t.HistoryTemplate.Replace("{History}", context.History ?? "");
            var question = t.CurrentQuestionTemplate.Replace("{UserInput}", input);
            var prompt = $"{t.SystemRole}\n\n{history}\n\n{question}\n\n{t.OutputTemplate}";

            Console.WriteLine("\n   【SK Auto Function Calling 模式】");
            Console.ForegroundColor = ConsoleColor.Blue;
            var answer = await _llmService.InvokeStreamWithRetryAsync(prompt, ConsoleColor.Blue, "化工合规");
            Console.ResetColor();
            Console.WriteLine();

            // Phase 2a 验证: 从 LlmService 诊断记录同步工具调用
            var llmService = _llmService as LlmService;
            var toolCalls = new List<FunctionCallRecord>();
            if (llmService != null && llmService.LastFunctionCalls.Count > 0)
            {
                toolCalls = new List<FunctionCallRecord>(llmService.LastFunctionCalls);
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

            _memoryService.ExtractAndStoreKeyFacts(input, answer);

            // Phase 4.1: 记忆协调器后推理（异步，不阻塞响应）
            if (_memoryCoordinator != null)
            {
                var userId = context.UserProfile.UserName ?? "default";
                _ = _memoryCoordinator.PostInferenceAsync(context.Session.SessionId, userId, input, answer, LastToolResults);
            }

            return (answer, toolCalls, new List<string>());
        }

        private async Task<string> ExecuteGeneralChatAsync(string input, PipelineContext context)
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
            
            _memoryService.ExtractAndStoreKeyFacts(input, answer);

            // Phase 4.1: 后推理
            if (_memoryCoordinator != null)
            {
                var userId = context.UserProfile.UserName ?? "default";
                _ = _memoryCoordinator.PostInferenceAsync(context.Session.SessionId, userId, input, answer);
            }

            return answer;
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

