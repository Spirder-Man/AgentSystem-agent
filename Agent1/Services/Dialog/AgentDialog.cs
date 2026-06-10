
using Agent1.Config;
using Agent1.Models;
using Agent1.Modules;
using System;
using System.Collections.Generic;
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

        /// <summary>最近一次化工合规执行的工具结果（供 Reflection 验证层使用）</summary>
        public Dictionary<string, string> LastToolResults { get; private set; } = new();
        /// <summary>最近一次工具规划结果</summary>
        public ToolPlan? LastToolPlan { get; private set; }

        public AgentDialog(
            ISessionService sessionService,
            IMemoryService memoryService,
            ILlmService llmService,
            IToolService toolService,
            MemoryCoordinator? memoryCoordinator = null)
        {
            _sessionService = sessionService;
            _memoryService = memoryService;
            _llmService = llmService;
            _toolService = toolService;
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

        public async Task<string> ExecuteAsync(string userInput, SessionContext session)
        {
            Console.WriteLine("\n═══════ 统一线性流水线启动 ═══════");
            
            var processedInput = await PreprocessAsync(userInput);
            Console.WriteLine($"[1/6] 预处理完成");
            
            var intent = RouteIntent(processedInput);
            Console.WriteLine($"[2/6] 意图归类完成: {intent}");
            
            var context = await LoadContextAsync(session, intent);
            Console.WriteLine($"[3/6] 上下文加载完成");
            
            var result = await ExecuteBusinessAsync(processedInput, context, intent);
            Console.WriteLine($"[4/6] 业务执行完成");
            
            await SaveSessionAsync(session, userInput, result);
            Console.WriteLine($"[5/6] 会话保存完成");
            
            var finalOutput = FormatOutput(result);
            Console.WriteLine($"[6/6] 结果输出完成");
            
            Console.WriteLine("═══════ 流水线结束 ═══════\n");
            
            return finalOutput;
        }

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

        private async Task<string> ExecuteBusinessAsync(string input, PipelineContext context, IntentType intent)
        {
            // Phase 4.1: 记忆协调器预推理
            if (_memoryCoordinator != null)
            {
                var userId = context.UserProfile.UserName ?? "default";
                var preResult = await _memoryCoordinator.PreInferenceAsync(context.Session.SessionId, userId, input);

                if (preResult.HasDirectAnswer)
                {
                    Console.WriteLine($"   → 记忆直接回答（跳过推理）");
                    return preResult.DirectAnswer;
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
                return memoryAnswer;
            }

            if (intent == IntentType.ChemicalCompliance)
            {
                Console.WriteLine("   → 执行化工合规业务");
                return await ExecuteChemicalComplianceAsync(input, context);
            }
            else
            {
                Console.WriteLine("   → 执行通用对话业务");
                return await ExecuteGeneralChatAsync(input, context);
            }
        }

        /// <summary>
        /// Phase 2a: SK Auto Function Calling — LLM 自主决定调用哪些工具
        /// 工具选择和执行由 Semantic Kernel 自动处理，不再需要手动 ReAct 循环
        /// Phase 2a 验证: 调用完成后从 LlmService.LastFunctionCalls 回填 LastToolResults
        ///   以保持与 ReflectionVerifier / RunReflectionStreamTools 的向后兼容
        /// </summary>
        private async Task<string> ExecuteChemicalComplianceAsync(string input, PipelineContext context)
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

            // Phase 2a 验证: 从 LlmService 诊断记录同步 LastToolResults
            // ReflectionVerifier.VerifySystemHealth 依赖此属性检查工具链完整性
            var llmService = _llmService as LlmService;
            if (llmService != null && llmService.LastFunctionCalls.Count > 0)
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

            _memoryService.ExtractAndStoreKeyFacts(input, answer);

            // Phase 4.1: 记忆协调器后推理（异步，不阻塞响应）
            if (_memoryCoordinator != null)
            {
                var userId = context.UserProfile.UserName ?? "default";
                _ = _memoryCoordinator.PostInferenceAsync(context.Session.SessionId, userId, input, answer, LastToolResults);
            }

            return answer;
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
    }
}

