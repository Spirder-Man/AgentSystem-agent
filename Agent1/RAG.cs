
using Agent1.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agent1
{
    /// <summary>
    /// RAG推理器
    /// </summary>
    public class RAG
    {
        /// <summary>
        /// LLM服务
        /// </summary>
        private readonly ILlmService _llmService;
        /// <summary>
        /// 会话服务
        /// </summary>
        private readonly ISessionService _sessionService;
        /// <summary>
        /// 工具服务
        /// </summary>
        private readonly IndustrialTools _industrialTools;
        /// &lt;summary&gt;
        /// 知识库服务（工业级BM25检索）
        /// &lt;/summary&gt;
        private readonly IKnowledgeBaseService _knowledgeBase;
        /// &lt;summary&gt;
        /// 会话上下文
        /// &lt;/summary&gt;
        private readonly SessionContext _session;
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="llmService">LLM服务</param>
        /// <param name="sessionService">会话服务</param>
        public RAG(ILlmService llmService, ISessionService sessionService)
        {
            _llmService = llmService;
            _sessionService = sessionService;
            _industrialTools = new IndustrialTools();
            _knowledgeBase = new KnowledgeBaseService();  // 初始化知识库服务
            _session = _sessionService.CreateSession(SessionType.IndustrialDiagnostic);
        }
        /// <summary>
        /// 加载工业知识库
        /// </summary>
        /// <returns>工业知识库</returns>
        /// 读取整个本地文件知识库的规则，将所有内容添加到知识知识库中，也就是list集合当中
        private List<string> LoadIndustrialKnowledgeBase()
        {
            /// <summary>
            /// 工业知识库
            /// </summary>
            /// <returns>工业知识库</returns>
            List<string> knowledgeBase = new List<string>();
            /// <summary>
            /// 主轴故障案例文件路径
            /// </summary>
            string casePath = @"D:\桌面\agent\本地文件\主轴故障案例.txt";
            /// <summary>
            /// 读取主轴故障案例文件内容
            /// </summary>
            /// <returns>主轴故障案例内容</returns>
            if (File.Exists(casePath))
            {
                string caseContent = File.ReadAllText(casePath, Encoding.UTF8);//读取主轴故障案例文件内容
                knowledgeBase.AddRange(caseContent.Split(new string[] { "案例" }, StringSplitOptions.RemoveEmptyEntries)
                                              .Select(s => "案例" + s.Replace("\n", " ").Replace("\r", " ").Trim()));
                //将主轴故障案例内容按"案例"分隔，添加到知识知识库中
            }
            /// <summary>
            /// 温度阈值表文件路径
            /// </summary>
            /// <returns>温度阈值表内容</returns>
            string thresholdPath = @"D:\桌面\agent\本地文件\温度阈值表.csv";
            /// <summary>
            /// 读取温度阈值表文件内容
            /// </summary>
            /// <returns>温度阈值表内容</returns>
            if (File.Exists(thresholdPath))
            {

                var lines = File.ReadAllLines(thresholdPath, Encoding.UTF8).Skip(1);//跳过第一行（标题）
                foreach (var line in lines)
                {
                    var parts = line.Split(',');
                    if (parts.Length >= 3)
                    {
                        string thresholdInfo = $"型号{parts[0]}：安全阈值{parts[1]}，异常阈值{parts[2]}";
                        knowledgeBase.Add(thresholdInfo);
                    }
                }
            }

            return knowledgeBase;
        }
        /// <summary>
        /// 运行RAG推理器（多轮对话）
        /// </summary>
        /// <returns>异步任务</returns>
        public async Task RunRAGReflectionStreamTools()
        {
            Console.WriteLine("\n====Agent + RAG（工业级检索增强·多轮对话）====");
            Console.WriteLine($"✅ 会话已创建，Session ID: {_session.SessionId}");
            Console.WriteLine("💡 输入 'exit' 或 'quit' 退出对话");
            Console.WriteLine("-----------------------------------");
            /// <summary>
            /// 加载工业知识库
            /// </summary>
            /// <returns>工业知识库</returns>
            /// 读取整个本地文件知识库的规则，将所有内容添加到知识知识库中，也就是list集合当中
            List<string> knowledgeBase = LoadIndustrialKnowledgeBase();
            await _knowledgeBase.AddDocumentsAsync(knowledgeBase);  // 新：添加到知识库服务
            Console.WriteLine($"\n📚 工业知识库已加载，共 {_knowledgeBase.GetDocumentCount()} 条记录（BM25检索引擎就绪）");
            //又是判断退出当前对话的逻辑
            while (true)
            {
                Console.Write("\n👤 请输入: ");
                var userInput = Console.ReadLine();

                if (userInput == null) continue;
                if (userInput.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                    userInput.Equals("quit", StringComparison.OrdinalIgnoreCase) ||
                    userInput.Equals("退出", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("🚪 退出对话");
                    break;
                }

                _sessionService.AddDialogTurn(_session.SessionId, "User", userInput);

                Console.WriteLine($"【用户提问】{userInput}");

                Console.WriteLine("\n【Step 0 - LLM自主判断】是否需要检索知识库？");
                string needRetrievePrompt = $@"用户问题：{userInput}
你需要调用知识库吗？只回答：是/否";

                string needRetrieve = await _llmService.InvokeStreamWithRetryAsync(needRetrievePrompt, ConsoleColor.Gray, "判断");

                List<string> relevantKnowledge = new List<string>();
                if (needRetrieve.Contains("是"))
                {
                    Console.WriteLine("✅ LLM 需要检索 → 执行BM25检索");
                    Console.ForegroundColor = ConsoleColor.Cyan;

                    var chunks = await _knowledgeBase.RetrieveAsync(userInput, topK: 3);
                    relevantKnowledge = chunks.Select(c => c.Content).ToList();

                    Console.WriteLine("✅ BM25检索到的相关内容：");
                    foreach (var item in relevantKnowledge)
                    {
                        Console.WriteLine($"  - {item}");
                    }
                    Console.ResetColor();
                }
                else
                {
                    Console.WriteLine("✅ LLM 不需要检索 → 跳过检索");
                    relevantKnowledge.Add("无检索内容");
                }
                //将检索到的内容做处理用\n分隔，形成一个字符串并添加历史对话的最多10论对话的 内容，这个机制是刚刚几个范式统一的做法，都是这个机制
                string knowledgeSummary = string.Join("\n", relevantKnowledge);
                var history = _sessionService.GetFormattedHistory(_session.SessionId, 10);

                Console.WriteLine("\n【Step 1 - Thought】模型分析需要调用的工具");
                Console.ForegroundColor = ConsoleColor.DarkGray;

                string thoughtPrompt = $@"【角色】工业设备诊断专家
【对话历史】
{history}
【当前问题】{userInput}
【知识库】
{knowledgeSummary}
【可用工具】
1. GetSpindleTemperature - 获取机床主轴实时温度
2. GetTemperatureThreshold - 获取温度安全阈值

请分析：
1. 是否需要调用工具？
2. 如果需要，调用哪些工具？

【输出格式（严格遵守，二选一）】
A. 如果需要调用工具 → 只写：需要调用的工具：工具名1,工具名2
B. 如果不需要调用工具 → 只写：需要调用的工具：无
";

                Console.WriteLine($"   💭 Thought Prompt调试: {thoughtPrompt}");
                
                string thoughtResult = await _llmService.InvokeStreamWithRetryAsync(thoughtPrompt, ConsoleColor.DarkGray, "分析思考");
                Console.WriteLine($"\n   💭 LLM完整思考结果: {thoughtResult}");
                Console.ResetColor();

                Console.WriteLine("\n【Step 2 - Action】解析工具调用指令");
                string[] toolsToCall = ParseToolCalls(thoughtResult);
                //将模型的思考结果中的工具调用指令解析出来，将工具调用指令中的工具名称提取出来，形成一个字符串数组
                Console.WriteLine($"解析到的工具调用指令：{string.Join(", ", toolsToCall)}");
                Console.ResetColor();
                if (toolsToCall.Length == 0)
                {
                    Console.WriteLine("✅ LLM 未选择工具，不调用任何工具");
                    toolsToCall = Array.Empty<string>();
                }

                Console.WriteLine("\n【Step 3 - Observation】调用真实工业工具获取数据");
                Console.ForegroundColor = ConsoleColor.Green;

                Dictionary<string, string> toolResults = new Dictionary<string, string>();
                //将模型的思考结果中的工具调用指令解析出来，将工具调用指令中的工具名称提取出来，形成一个字符串数组
                foreach (string toolName in toolsToCall)
                {
                    string result = CallTool(_industrialTools, toolName);
                    toolResults.Add(toolName, result);
                    Console.WriteLine($"✓ {toolName} → {result}");
                }
                Console.ResetColor();
                //然后我发现，这些1.加载对话历史2.当前问题3.知识库4.工具调用返回的结果集数据5.初步结论这是第一轮次的字符串拼接，频繁的用到字符串拼接
                Console.WriteLine("\n【Step 4 - Initial Conclusion】生成初步诊断结论（RAG增强）");
                //这里RAG的检索机制很简单可以说没有任何检索机制，只是简单的读取固定我给的文件内容对吧？
                Console.ForegroundColor = ConsoleColor.Yellow;
                string observationSummary = string.Join("\n", toolResults.Select(kv => $"- {kv.Value}"));
                //这里又进行多个字段存储的拼接，形成一个字符串，用于后续的模型调用
                //问题很明显：所有的Prompt只有当中的字段是真实的方法调用，但是其中的
//                 【要求】
//                  1. 优先参考知识库中的故障案例和技术标准
//                  2. 所有数据必须真实，禁止编造
//                  3. 分析温度异常原因，给出与案例匹配的具体解决方案"      
//类似这些的输出也是固态输出，大模型全程没有任何参与，这其实就在一定程度上造成了，不可逆转的代码性质的幻觉；严重的编码技术逻辑的bug
                string initialPrompt = $@"【角色】工业设备诊断专家
【规则】
1. 只根据提供的资料回答
2. 无资料时直接说明
3. 不编造数据
4. 不强行分析任何设备参数

【对话历史】
{history}

【当前问题】
{userInput}

【资料】
{knowledgeSummary}

【工具结果】
{observationSummary}";
                //这里依旧调用的是llm会话服务管理的异步方法InvokeStreamWithRetryAsync，将模型的思考结果打印出来
                string initialConclusion = await _llmService.InvokeStreamWithRetryAsync(initialPrompt, ConsoleColor.Yellow, "初步结论");
                Console.ResetColor();

                Console.WriteLine("\n【Step 5 - 真实数据校验】检查是否编造");

                string validatePrompt = $@"重要：请严格区分三类信息：
1. 用户的假设问题（用户说的）
2. 真实知识库资料（系统提供的）
3. 真实工具数据（系统实际调用工具返回的）

【用户原始问题】
{userInput}

【系统真实知识库资料】
{knowledgeSummary}

【系统真实工具数据】
{observationSummary}

【需要校验的回答】
{initialConclusion}

请严格检查：
1. 回答中是否有【真实知识库】和【真实工具数据】里都不存在的数字/参数/事实？
2. 是否把用户的「假设」当成了「真实数据」来论述？
3. 是否有与真实资料矛盾的内容？

如果有任何编造或假设当成真实的情况，直接说「不通过」并指出具体问题。
如果回答完全基于真实资料，说「通过」。

格式：通过/不通过 + 具体原因。
";

                string validateResult = await _llmService.InvokeStreamWithRetryAsync(validatePrompt, ConsoleColor.Magenta, "校验");

                Console.WriteLine($"【校验结果】{validateResult}");
                Console.ResetColor();

                Console.WriteLine("\n【Step 6 - Final Conclusion】纠错后的最终诊断结论");
                Console.ForegroundColor = ConsoleColor.Blue;
                string finalPrompt = $@"【角色】工业设备诊断专家
【规则】
1. 只根据提供的资料回答
2. 无资料时直接说明
3. 不编造数据

【对话历史】
{history}

【当前问题】
{userInput}

【资料】
{knowledgeSummary}

【工具结果】
{observationSummary}

【初步回答】
{initialConclusion}

【校验结果】
{validateResult}

请根据校验结果修正回答。";

                await _llmService.InvokeStreamAsync(finalPrompt, ConsoleColor.Blue);

                _sessionService.AddDialogTurn(_session.SessionId, "Assistant", "已生成最终诊断结论");

                Console.ResetColor();
                Console.WriteLine("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Console.WriteLine("✅ Agent+RAG工业级流程执行完成！");
                Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            }
        }
        /// <summary>
        /// 检索与用户问题相关的知识库内容
        /// </summary>
        /// <param name="userQuestion">用户问题</param>
        /// <param name="knowledgeBase">知识库内容</param>
        /// <returns>与用户问题相关的知识库内容</returns>
        private List<string> RetrieveRelevantKnowledge(string userQuestion, List<string> knowledgeBase)
        {
            if (knowledgeBase == null || knowledgeBase.Count == 0)
            {
                return new List<string> { "未加载到工业知识库，将按通用工业标准分析" };
            }

            Console.WriteLine($"   🔍 调试：用户问题 = '{userQuestion}'");
            Console.WriteLine($"   🔍 调试：知识库记录数 = {knowledgeBase.Count}");

            // 更简单的关键词提取：直接找重要词汇
            var keywords = new List<string>();
            
            // 检测用户问题中的重要词汇
            if (userQuestion.Contains("主轴", StringComparison.OrdinalIgnoreCase)) keywords.Add("主轴");
            if (userQuestion.Contains("温度", StringComparison.OrdinalIgnoreCase)) keywords.Add("温度");
            if (userQuestion.Contains("故障", StringComparison.OrdinalIgnoreCase)) keywords.Add("故障");
            if (userQuestion.Contains("异常", StringComparison.OrdinalIgnoreCase)) keywords.Add("异常");
            if (userQuestion.Contains("阈值", StringComparison.OrdinalIgnoreCase)) keywords.Add("阈值");
            if (userQuestion.Contains("告警", StringComparison.OrdinalIgnoreCase)) keywords.Add("告警");

            Console.WriteLine($"   🔍 调试：提取到的关键词 = [{string.Join(", ", keywords)}]");

            // 如果没有关键词，就用默认的
            if (keywords.Count == 0)
            {
                keywords.AddRange(new string[] { "主轴", "温度" });
            }

            // 根据关键词对知识库内容进行打分
            var scoredKnowledge = knowledgeBase.Select(content => new
            {
                Content = content,
                Score = keywords.Count(keyword => content.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .Take(3)
            .Select(item => item.Content)
            .ToList();

            Console.WriteLine($"   🔍 调试：匹配到的记录数 = {scoredKnowledge.Count}");

            // 如果没有检索到，就兜底返回全部知识库内容的前3条
            if (scoredKnowledge.Count == 0)
            {
                Console.WriteLine($"   ⚠️  未精确匹配，返回前3条知识库记录");
                scoredKnowledge = knowledgeBase.Take(3).ToList();
            }

            return scoredKnowledge;
        }
        /// <summary>
        /// 解析模型输出中的工具调用
        /// </summary>
        /// <param name="modelOutput">模型输出</param>
        /// <returns>工具调用列表</returns>
        /// ParseToolCalls方法是干什么用起到什么作用？？是怎么用在模型输出当中列出工具调用的名称？？
        /// 刚刚不是已经硬编码写好了吗？这里再去写不是为了什么？？？？不是写和没写没什么区别，这段我觉得没什么用
        /// 直接调用刚才的垃圾硬编码不就好了吗？
        private string[] ParseToolCalls(string? modelOutput)
        {
            if (string.IsNullOrEmpty(modelOutput))
                return new string[0];

            Console.WriteLine($"   🔧 ParseToolCalls调试: 原始输出={modelOutput}");

            // 【关键】先判断是否显式说"无"或"不需要"！
            if (modelOutput.Contains("无", StringComparison.OrdinalIgnoreCase) || 
                modelOutput.Contains("不需要", StringComparison.OrdinalIgnoreCase) ||
                modelOutput.Contains("不调用", StringComparison.OrdinalIgnoreCase) ||
                modelOutput.Contains("调用工具：无", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"   🔧 ParseToolCalls: 检测到显式不调用工具！");
                return new string[0];
            }

            // 查找工具名，但更严格
            var tools = new List<string>();
            var toolNames = new[] { "GetSpindleTemperature", "GetTemperatureThreshold", "GetCurrentTime", "Calculate", "WebSearch" };
            
            foreach (var toolName in toolNames)
            {
                // 【更严格】工具名必须完整出现，且上下文相关
                if (modelOutput.Contains(toolName, StringComparison.OrdinalIgnoreCase))
                {
                    tools.Add(toolName);
                }
            }

            Console.WriteLine($"   🔧 ParseToolCalls: 最终解析到={string.Join(",", tools)}");
            return tools.Distinct().ToArray();
        }
        /// <summary>
        /// 调用工具
        /// </summary>
        /// <param name="tools">工业工具</param>
        /// <param name="toolName">工具名称</param>
        /// <returns>工具调用结果</returns>
        private string CallTool(IndustrialTools tools, string toolName)
        {
            return toolName switch
            {
                "GetSpindleTemperature" => tools.GetSpindleTemperature(),
                "GetTemperatureThreshold" => tools.GetTemperatureThreshold(),
                _ => $"未知工具: {toolName}"
            };
        }

    }
}
