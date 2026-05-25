
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
        private readonly ChemicalComplianceTools _complianceTools; // P1: 替代 IndustrialTools 的化工合规工具集
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
            _complianceTools = new ChemicalComplianceTools(); // P1: 化工合规工具集（危险类别/储存禁忌/安全距离）
            _knowledgeBase = new KnowledgeBaseService();  // 初始化知识库服务（PostgreSQL + pgvector + BM25）
            _session = _sessionService.CreateSession(SessionType.ChemicalCompliance);
            // 注意：不再调用 LoadIndustrialKnowledgeBase()，知识库由 KnowledgeBaseService 统一管理，
            // 化工合规知识（国标/园区规则/历史案例）已通过数据库预加载，避免工业数据污染
        }
        /// <summary>
        /// 加载工业知识库，终端6路径
        /// </summary>
        /// <returns>工业知识库</returns>
        // P1: LoadIndustrialKnowledgeBase() 已删除 —— 该方法加载的工业主轴/温度文件属于遗留数据，
        // KnowledgeBaseService（PostgreSQL + pgvector + BM25）已接管所有知识库检索，无需硬编码文件加载
        /// <summary>
        /// 运行RAG推理器（多轮对话）
        /// </summary>
        /// <returns>异步任务</returns>
        public async Task RunRAGReflectionStreamTools()
        {
            Console.WriteLine("\n====Agent + RAG（化工合规检索增强·多轮对话）===="); // P1: 从工业诊断切换为化工合规领域
            Console.WriteLine($"✅ 会话已创建，Session ID: {_session.SessionId}");
            Console.WriteLine("💡 输入 'exit' 或 'quit' 退出对话");
            Console.WriteLine("-----------------------------------");

            // P1: 移除 LoadIndustrialKnowledgeBase() 调用 —— 知识库由 KnowledgeBaseService（PostgreSQL + pgvector）统一管理
            Console.WriteLine($"\n📚 化工合规知识库已就绪，共 {_knowledgeBase.GetDocumentCount()} 条记录（BM25 + pgvector 检索引擎就绪）");
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

                string thoughtPrompt = $@"【角色】化工园区危化品合规审核专家
【对话历史】
{history}
【当前问题】{userInput}
【知识库】
{knowledgeSummary}
【可用化工合规工具】
1. CheckHazardCategory - 查询危化品危险类别及适用国标（GB 30000 系列）
2. CheckStorageCompatibility - 检查两种危化品是否可同库储存（GB15603）
3. GetSafetyDistance - 查询设施间安全间距要求（GB50160/GB50016）
4. GetCurrentTime - 获取当前时间
5. Calculate - 数学计算

请分析是否需要调用工具。注意：你的思考可以自由发挥，但最后一行必须是以下格式（二选一）：
需要调用的工具：工具名1,工具名2
需要调用的工具：无
（格式说明：工具名用英文逗号分隔，不要带括号或参数，不要输出其他内容）"; // 修复2: 强化输出格式约束，确保LLM在最后一行输出标记

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

                Console.WriteLine("\n【Step 3 - Observation】调用化工合规工具获取数据"); // P1: 工业工具 → 化工合规工具
                Console.ForegroundColor = ConsoleColor.Green;

                Dictionary<string, string> toolResults = new Dictionary<string, string>();
                //将模型的思考结果中的工具调用指令解析出来，将工具调用指令中的工具名称提取出来，形成一个字符串数组
                foreach (string toolName in toolsToCall)
                {
                    string result = CallTool(_complianceTools, toolName, userInput); // P1: 传入用户输入作为工具参数
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
                string initialPrompt = $@"【角色】化工园区危化品合规审核专家
【规则】
1. 只根据提供的知识库资料回答
2. 无资料时直接说明
3. 不编造法规条款
4. 合规判断必须引用具体法规编号（如 GB 30000、GB15603、GB50160）

【对话历史】
{history}

【当前问题】
{userInput}

【知识库资料】
{knowledgeSummary}

【化工合规工具调用结果】
{observationSummary}"; // P1: Prompt从工业设备诊断切换为化工合规审核
                //这里依旧调用的是llm会话服务管理的异步方法InvokeStreamWithRetryAsync，将模型的思考结果打印出来
                string initialConclusion = await _llmService.InvokeStreamWithRetryAsync(initialPrompt, ConsoleColor.Yellow, "初步结论");
                Console.ResetColor();

                Console.WriteLine("\n【Step 5 - 真实数据校验】检查是否编造");

                string validatePrompt = $@"【任务】校验下面【需要校验的回答】是否基于真实资料，禁止复述或重新分析！

重要：请严格区分三类信息：
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

检查要点：
1. 回答中是否有【真实知识库】和【真实工具数据】里都不存在的数字/参数/事实？
2. 是否把用户的假设当成了真实数据来论述？
3. 是否有与真实资料矛盾的内容？

输出要求（严格遵守）：
- 不要复述初步回答的内容
- 不要重新分析问题
- 只输出一行：通过/不通过 + 一句简短原因
- 示例：通过，所有数据均来自工具调用结果
- 示例：不通过，回答中引用了知识库中不存在的温度数值"; // 修复3: 强化格式约束，禁止LLM自我复制

                string validateResult = await _llmService.InvokeStreamWithRetryAsync(validatePrompt, ConsoleColor.Magenta, "校验");

                Console.WriteLine($"【校验结果】{validateResult}");
                Console.ResetColor();

                Console.WriteLine("\n【Step 6 - Final Conclusion】纠错后的最终诊断结论");
                Console.ForegroundColor = ConsoleColor.Blue;
                string finalPrompt = $@"【角色】化工园区危化品合规审核专家
【规则】
1. 只根据提供的知识库资料回答
2. 无资料时直接说明
3. 不编造法规条款

【对话历史】
{history}

【当前问题】
{userInput}

【知识库资料】
{knowledgeSummary}

【化工合规工具结果】
{observationSummary}

【初步回答】
{initialConclusion}

【校验结果】
{validateResult}

请根据校验结果修正回答。"; // P1: 工业诊断 → 化工合规审核

                await _llmService.InvokeStreamAsync(finalPrompt, ConsoleColor.Blue);

                _sessionService.AddDialogTurn(_session.SessionId, "Assistant", "已生成最终诊断结论");

                Console.ResetColor();
                Console.WriteLine("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Console.WriteLine("✅ Agent+RAG化工合规流程执行完成！"); // P1: 工业级 → 化工合规
                Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            }
        }
        // P1: RetrieveRelevantKnowledge() 已删除 —— 基于关键词(主轴/温度/故障)的简陋匹配器，
        // KnowledgeBaseService 的 BM25 全文检索 + pgvector 向量检索已完全替代此功能
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

            Console.WriteLine($"   🔧 ParseToolCalls调试: 原始输出长度={modelOutput.Length}");

            // 【修复1】先查找显式标记「需要调用的工具：」，只在标记行中匹配，避免全文本误扫描
            // 旧逻辑用 modelOutput.Contains(toolName) 全文扫描，LLM在思考过程中列举所有工具名会导致全被误匹配
            string marker = "需要调用的工具：";
            int markerIndex = modelOutput.IndexOf(marker, StringComparison.OrdinalIgnoreCase);

            if (markerIndex >= 0)
            {
                // 提取标记之后到行尾的内容（只取第一行）
                string afterMarker = modelOutput.Substring(markerIndex + marker.Length);
                int newlineIndex = afterMarker.IndexOf('\n');
                string toolLine = newlineIndex >= 0 ? afterMarker.Substring(0, newlineIndex) : afterMarker;
                toolLine = toolLine.Trim();

                Console.WriteLine($"   🔧 ParseToolCalls: 标记行内容='{toolLine}'");

                // 检查是否明确说"无"（标记行内容为纯"无"或"无。"等）
                string cleanLine = toolLine.Trim().Replace("。", "").Replace(".", "").Trim();
                if (cleanLine == "无" || string.IsNullOrEmpty(cleanLine))
                {
                    Console.WriteLine($"   🔧 ParseToolCalls: 标记行明确为「无」，不调用工具");
                    return new string[0];
                }

                // 在标记行中匹配工具名
                var tools = new List<string>();
                var toolNames = new[] { "CheckHazardCategory", "CheckStorageCompatibility", "GetSafetyDistance", "GetCurrentTime", "Calculate" };

                foreach (var toolName in toolNames)
                {
                    if (toolLine.Contains(toolName, StringComparison.OrdinalIgnoreCase))
                    {
                        tools.Add(toolName);
                    }
                }

                Console.WriteLine($"   🔧 ParseToolCalls: 标记行匹配到={string.Join(",", tools)}");
                if (tools.Count > 0)
                    return tools.Distinct().ToArray();
            }
            else
            {
                Console.WriteLine($"   🔧 ParseToolCalls: 未找到「需要调用的工具：」标记，无法确定工具调用意图");
            }

            // 既没有标记行，也没有匹配到工具 → 保守返回空（不让无关工具被误调用）
            return new string[0];
        }
        // ========== 修复2: 智能参数提取辅助方法 ==========
        // 旧的 CallTool 直接把整个问句当参数传给工具（如 "氧化剂和易燃液体能一起存吗"），
        // 导致 CheckHazardCategory 和 GetSafetyDistance 等无法匹配到正确的实体名。
        // 以下方法从用户自然语言输入中提取物质名称/设施类型，显著提升工具命中率。

        /// <summary>从用户输入中提取单个物质名称</summary>
        private string ExtractSubstance(string userInput)
        {
            return userInput
                .Replace("属于什么危险类别", "").Replace("是什么类别", "")
                .Replace("的危化品", "").Replace("是什么", "")
                .Replace("属于什么", "").Replace("什么", "")
                .Replace("是多少", "").Replace("能不能", "")
                .Replace("吗", "").Replace("？", "").Replace("?", "").Trim();
        }

        /// <summary>从用户输入中提取两个物质名称（按 和/与/、 分割）</summary>
        private (string a, string b) ExtractTwoSubstances(string userInput)
        {
            var separators = new[] { "和", "与", "跟", "、" };
            foreach (var sep in separators)
            {
                if (userInput.Contains(sep))
                {
                    var parts = userInput.Split(new[] { sep }, 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        string a = CleanSubstance(parts[0]);
                        string b = CleanSubstance(parts[1]);
                        if (!string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b))
                            return (a, b);
                    }
                }
            }
            // 兜底：把整个输入作为第一个物质
            return (ExtractSubstance(userInput), "");
        }

        /// <summary>清理物质名称中的问句残留词</summary>
        private string CleanSubstance(string text)
        {
            return text.Replace("能一起存", "").Replace("可以一起", "").Replace("能不能", "")
                       .Replace("可以", "").Replace("吗", "").Replace("？", "").Replace("?", "")
                       .Replace("怎么", "").Replace("如何", "").Trim();
        }

        /// <summary>从用户输入推断设施类型，映射到 SafetyDistances 字典的 key</summary>
        private string ExtractFacilityType(string userInput)
        {
            if (userInput.Contains("液化烃")) return "液化烃储罐-储罐";
            if (userInput.Contains("仓库") && userInput.Contains("明火")) return "甲类仓库-明火点";
            if (userInput.Contains("仓库") && userInput.Contains("建筑")) return "甲类仓库-建筑";
            if (userInput.Contains("储罐") && userInput.Contains("建筑")) return "储罐-建筑";
            if (userInput.Contains("储罐") && userInput.Contains("消防")) return "储罐-消防通道";
            if (userInput.Contains("储罐") && userInput.Contains("边界")) return "储罐-厂区边界";
            if (userInput.Contains("储罐")) return "储罐-储罐";  // 默认：储罐间间距
            return userInput;  // 兜底
        }
        // ========== 修复2: 辅助方法结束 ==========

        /// <summary>
        /// 调用化工合规工具
        /// </summary>
        /// <param name="tools">化工合规工具集</param>
        /// <param name="toolName">工具名称</param>
        /// <param name="userInput">用户原始输入（用于智能参数提取）</param>
        /// <returns>工具调用结果</returns>
        private string CallTool(ChemicalComplianceTools tools, string toolName, string userInput)
        {
            // 修复2: 不再直接传 userInput，而是通过 Extract* 方法提取具体实体名
            return toolName switch
            {
                "CheckHazardCategory" => tools.CheckHazardCategory(ExtractSubstance(userInput)),
                "CheckStorageCompatibility" => CallStorageCheck(tools, userInput),
                "GetSafetyDistance" => tools.GetSafetyDistance(ExtractFacilityType(userInput)),
                "GetCurrentTime" => tools.GetCurrentTime(),
                "Calculate" => tools.Calculate(userInput),  // Calculate 直接处理表达式
                _ => $"未知工具: {toolName}"
            };
        }

        /// <summary>CheckStorageCompatibility 的参数提取 + 调用包装（switch表达式不支持内联变量声明）</summary>
        private string CallStorageCheck(ChemicalComplianceTools tools, string userInput)
        {
            var (a, b) = ExtractTwoSubstances(userInput);
            return tools.CheckStorageCompatibility(a, b);
        }

    }
}
