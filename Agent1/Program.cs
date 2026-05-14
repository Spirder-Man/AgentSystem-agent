
using System;
using System.Threading.Tasks;
using Agent1.Services;
using Agent1.Config;

namespace Agent1
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("══════════════════════════════════════════");
            Console.WriteLine("        化工园区危化品合规审核AI Agent");
            Console.WriteLine("══════════════════════════════════════════\n");

            var sessionService = new SessionService();
            var memoryService = new MemoryService();
            var llmService = new LlmService();
            var toolService = new ToolService(llmService);
            var agentDialog = new AgentDialog(sessionService, memoryService, llmService, toolService);
            
            // 化工专用服务
            var knowledgeBaseService = new KnowledgeBaseService();
            var integrationService = new IntegrationService();
            var auditService = new AuditService();
            var chemicalRAG = new ChemicalRAG(AppConfig.Instance.KnowledgeBase.BasePath, knowledgeBaseService);

            var moduleFactory = new ModuleFactory(
                sessionService,
                memoryService,
                llmService,
                toolService,
                agentDialog,
                knowledgeBaseService,
                integrationService,
                auditService);
            
            var dispatcher = new ModuleDispatcher(moduleFactory);

            // 预加载化工知识库
            await chemicalRAG.LoadKnowledgeBaseAsync();

            while (true)
            {
                Console.WriteLine("\n请选择功能:");
                Console.WriteLine("  1. 思维链推理（标准输出）");
                Console.WriteLine("  2. 思维链推理（流式输出）");
                Console.WriteLine("  3. ReAct 推理（标准输出）");
                Console.WriteLine("  4. ReAct 推理（流式输出）");
                Console.WriteLine("  5. Reflection 自我反思");
                Console.WriteLine("  6. RAG 检索增强生成");
                Console.WriteLine("  7. 智能对话系统");
                Console.WriteLine("  8. 化工合规自查【核心功能】");
                Console.WriteLine("  9. 化工合规RAG测试");
                Console.WriteLine("  0. 退出\n");

                Console.Write("请输入选项: ");
                Console.ForegroundColor = ConsoleColor.Green;
                var input = Console.ReadLine() ?? "0";
                Console.ResetColor();

                if (input == "0" || input.Equals("exit", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("\n👋 再见！");
                    break;
                }

                if (input == "8")
                {
                    var module = moduleFactory.CreateModule(ModuleType.ComplianceCheck);
                    await module.RunAsync();
                }
                else if (input == "9")
                {
                    await RunChemicalRAGTest(chemicalRAG);
                }
                else
                {
                    if (!int.TryParse(input, out var choice) || choice < 1 || choice > 7)
                    {
                        Console.WriteLine("\n⚠️ 无效选项，请重新选择");
                        continue;
                    }

                    var moduleType = (ModuleType)choice;
                    try
                    {
                        await dispatcher.ExecuteModuleAsync(moduleType);
                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"\n❌ 执行出错: {ex.Message}");
                        Console.ResetColor();
                        Console.WriteLine($"堆栈: {ex.StackTrace}");
                    }
                }
            }
        }

        static async Task RunChemicalRAGTest(ChemicalRAG chemicalRAG)
        {
            Console.WriteLine("\n========================================");
            Console.WriteLine("       化工合规RAG测试");
            Console.WriteLine("========================================");

            // 测试查询
            var testQueries = new[]
            {
                "危化品储罐之间的安全距离是多少？",
                "消防通道有什么要求？"
            };

            foreach (var query in testQueries)
            {
                await chemicalRAG.SearchAsync(query);
                await Task.Delay(500);
            }

            // 交互式测试
            Console.WriteLine("\n========================================");
            Console.WriteLine("       交互式检索测试 (输入 exit 退出)");
            Console.WriteLine("========================================");

            while (true)
            {
                Console.Write("\n🔍 请输入查询: ");
                var query = Console.ReadLine();
                
                if (string.IsNullOrWhiteSpace(query) || query.Equals("exit", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                await chemicalRAG.SearchAsync(query);
            }

            Console.WriteLine("\n✅ 化工合规RAG测试结束！");
        }
    }
}

