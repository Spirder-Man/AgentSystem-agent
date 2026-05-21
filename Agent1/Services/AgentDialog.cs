
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

            if (intent == IntentType.IndustrialDiagnostic)
            {
                Console.WriteLine("   → 执行工业诊断业务");
                return await ExecuteIndustrialDiagnosticAsync(input, context);
            }
            else
            {
                Console.WriteLine("   → 执行通用对话业务");
                return await ExecuteGeneralChatAsync(input, context);
            }
        }

        private async Task<string> ExecuteIndustrialDiagnosticAsync(string input, PipelineContext context)
        {
            Console.WriteLine("\n   【Step 1 - Thought】ReAct推理 - 分析问题");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            
            string thoughtPrompt = $@"你是化工园区危化品合规审核专家，现在需要分析以下问题：{input}

可用工具：
1. CheckHazardCategory(危化品名称) - 查询危险类别及适用国标
2. CheckStorageCompatibility(危化品A, 危化品B) - 检查可否同库储存
3. GetSafetyDistance(设施类型) - 查询安全间距要求（储罐间距、消防通道等）
4. GetCurrentTime() - 获取当前时间
5. Calculate(表达式) - 数学计算

请输出你的思考过程，并明确说明需要调用哪些工具（只需列出工具名称，用逗号分隔）
格式要求：先输出思考内容，最后用【工具调用】: 工具1,工具2 格式列出需要调用的工具。";

            string thoughtResult = await _llmService.InvokeStreamWithRetryAsync(thoughtPrompt, ConsoleColor.DarkGray, "ReAct思考分析");
            Console.ResetColor();

            Console.WriteLine("\n   【Step 2 - Action】ReAct推理 - 调用工具");
            string[] toolsToCall = ParseToolCalls(thoughtResult);

            if (toolsToCall.Length == 0)
            {
                toolsToCall = new string[] { "GetSafetyDistance" };
            }

            Console.WriteLine("\n   【Step 3 - Observation】ReAct推理 - 获取工具数据");
            Console.ForegroundColor = ConsoleColor.Green;

            var toolResults = new Dictionary<string, string>();
            var complianceTools = new ChemicalComplianceTools();

            foreach (string toolName in toolsToCall)
            {
                string result = CallTool(complianceTools, toolName);
                toolResults.Add(toolName, result);
                Console.WriteLine($"✓ {toolName} → {result}");
            }
            Console.ResetColor();

            Console.WriteLine("\n   【Step 4 - Conclusion】ReAct推理 - 生成最终结论");
            Console.ForegroundColor = ConsoleColor.Blue;

            var observationSummary = string.Join("\n", toolResults.Select(kv => $"- {kv.Value}"));

            string conclusionPrompt = $@"【角色】化工园区危化品合规审核专家
【对话历史】
{context.History}
【当前问题】{input}
【工具调用结果】
{observationSummary}
【要求】
1. 严格基于工具返回的真实数据，禁止编造任何信息
2. 判断是否合规，引用具体法规条款
3. 指出违规点和对应的整改措施
4. 输出格式清晰易读，分条目列出";

            var answer = await _llmService.InvokeStreamWithRetryAsync(conclusionPrompt, ConsoleColor.Blue, "ReAct最终结论");
            Console.ResetColor();

            _memoryService.ExtractAndStoreKeyFacts(input, answer);
            
            return answer;
        }

        private string[] ParseToolCalls(string modelOutput)
        {
            int startIndex = modelOutput.IndexOf("【工具调用】:");
            if (startIndex == -1)
                startIndex = modelOutput.IndexOf("[工具调用]:");

            if (startIndex == -1)
                return new string[0];

            string toolPart = modelOutput.Substring(startIndex + 6).Trim();
            return toolPart.Split(',')
                          .Select(t => t.Trim())
                          .Where(t => !string.IsNullOrEmpty(t) && !t.Contains("无") && !t.Contains("空") && !t.Equals("-"))
                          .ToArray();
        }

        private string CallTool(ChemicalComplianceTools tools, string toolName)
        {
            return toolName.Trim()
                .Replace("(", "").Replace(")", "")
                .Replace("：", "").Replace(":", "") switch
            {
                "CheckHazardCategory" => tools.CheckHazardCategory(""),
                "CheckStorageCompatibility" => tools.CheckStorageCompatibility("", ""),
                "GetSafetyDistance" => tools.GetSafetyDistance(""),
                "GetCurrentTime" => tools.GetCurrentTime(),
                "Calculate" => tools.Calculate("1+1"),
                _ => $"未知工具: {toolName}"
            };
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

