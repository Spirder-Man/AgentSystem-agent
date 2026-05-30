
using Agent1.Modules;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Agent1.Services
{
    public class AgentDialog
    {
        private readonly ISessionService _sessionService;
        private readonly IMemoryService _memoryService;
        private readonly ILlmService _llmService;
        private readonly IToolService _toolService;

        public AgentDialog(
            ISessionService sessionService,
            IMemoryService memoryService,
            ILlmService llmService,
            IToolService toolService)
        {
            _sessionService = sessionService;
            _memoryService = memoryService;
            _llmService = llmService;
            _toolService = toolService;
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
        /// Phase 2b: 真正的多轮 ReAct 循环 — Thought→Action→Observation 可迭代多轮
        /// </summary>
        private async Task<string> ExecuteChemicalComplianceAsync(string input, PipelineContext context)
        {
            const int maxRounds = 3;
            var accumulatedObservations = new List<string>();

            for (int round = 1; round <= maxRounds; round++)
            {
                Console.WriteLine($"\n   【ReAct Round {round}/{maxRounds}】");

                // Step 1: Thought — LLM 判断需要什么工具（同时考虑之前轮次的 Observation）
                var observationContext = accumulatedObservations.Count > 0
                    ? "\n【前面轮次的工具调用结果】\n" + string.Join("\n---\n", accumulatedObservations)
                    : "";

                var plan = await _toolService.AnalyzeAndPlanToolsAsync(input, context.History + observationContext);

                if (plan.ToolNames.Count == 0)
                {
                    // LLM 认为不需要更多工具了 → 生成最终结论
                    Console.WriteLine("   → LLM 判断信息足够，生成结论");
                    break;
                }

                // Step 2: Action — 执行工具
                Console.WriteLine($"   → 本轮调用工具: {string.Join(", ", plan.ToolNames)}");
                var roundResults = await _toolService.ExecuteToolsAsync(plan, input);

                // Step 3: Observation — 记录结果，进入下一轮
                foreach (var kv in roundResults)
                {
                    var obs = $"[{kv.Key}] {kv.Value}";
                    accumulatedObservations.Add(obs);
                    Console.WriteLine($"   ✓ {kv.Key} 完成");
                }
            }

            // 生成最终结论（基于所有轮次的 Observation）
            Console.WriteLine("\n   【结论生成】基于多轮工具数据输出合规建议");
            Console.ForegroundColor = ConsoleColor.Blue;

            var toolSummary = accumulatedObservations.Count > 0
                ? string.Join("\n", accumulatedObservations)
                : "（本次未调用工具，以下结论基于通用知识）";

            var conclusionPrompt = $@"【角色】化工园区危化品合规审核专家
【对话历史】
{context.History}
【当前问题】{input}
【多轮工具调用结果】
{toolSummary}
【要求】
1. 严格基于工具返回的真实数据，禁止编造任何信息
2. 判断是否合规，引用具体法规条款
3. 指出违规点和对应的整改措施
4. 输出格式清晰易读，分条目列出";

            var answer = await _llmService.InvokeStreamWithRetryAsync(conclusionPrompt, ConsoleColor.Blue, "合规结论");
            Console.ResetColor();

            _memoryService.ExtractAndStoreKeyFacts(input, answer);
            return answer;
        }

        private async Task<string> ExecuteGeneralChatAsync(string input, PipelineContext context)
        {
            var userName = !string.IsNullOrWhiteSpace(context.UserProfile.UserName) 
                ? context.UserProfile.UserName 
                : "用户";
            var assistantName = !string.IsNullOrWhiteSpace(context.UserProfile.AssistantName) 
                ? context.UserProfile.AssistantName 
                : "助手";
            
            var prompt = $@"你是友好的AI助手，名字叫{assistantName}。

对话历史：
{context.History}

用户说：{input}

用户的名字可能是{userName}。

请直接回答用户，不要思考标记。";
            
            Console.WriteLine("\n💬 正在生成回复...");
            var answer = await _llmService.InvokeStreamWithRetryAsync(prompt, ConsoleColor.Blue, "简单对话");
            
            _memoryService.ExtractAndStoreKeyFacts(input, answer);
            
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

