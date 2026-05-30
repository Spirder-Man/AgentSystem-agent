using System;
using System.Threading.Tasks;
using Agent1.Services;
using Agent1.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Agent1
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // ═══════════════════════════════════════════════════
            // Phase 1: 配置外部化 — appsettings.json + 环境变量
            // ═══════════════════════════════════════════════════
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            // 加载全局配置（环境变量可覆盖敏感信息如 DB_PASSWORD）
            AppConfig.Load(configuration);

            // ═══════════════════════════════════════════════════
            // Phase 1: 结构化日志 — Serilog
            // ═══════════════════════════════════════════════════
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .WriteTo.File("logs/agent1-.log", rollingInterval: RollingInterval.Day)
                .CreateLogger();

            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddSerilog(dispose: true);
            });
            var logger = loggerFactory.CreateLogger<Program>();

            logger.LogInformation("══════════════════════════════════════════");
            logger.LogInformation("        化工园区危化品合规审核AI Agent");
            logger.LogInformation("══════════════════════════════════════════");
            Console.WriteLine("══════════════════════════════════════════");
            Console.WriteLine("        化工园区危化品合规审核AI Agent");
            Console.WriteLine("══════════════════════════════════════════\n");

            // ═══════════════════════════════════════════════════
            // Phase 1: 依赖注入容器 — Microsoft.Extensions.DI
            // ═══════════════════════════════════════════════════
            var services = new ServiceCollection();

            // 注册配置（单例）
            services.AddSingleton(AppConfig.Instance);

            // 注册日志
            services.AddSingleton(loggerFactory);
            services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));

            // 注册核心服务（单例，控制台应用生命周期等同于整个进程）
            services.AddSingleton<IDatabaseService, DatabaseService>();
            services.AddSingleton<ISessionService, SessionService>();
            services.AddSingleton<IMemoryService, MemoryService>();
            services.AddSingleton<ILlmService, LlmService>();
            // Phase 2a: ChemicalComplianceTools 双模注册（RAG 构造优先，KnowledgeBaseService 稍后注册但 DI 延迟解析）
            services.AddSingleton<ChemicalComplianceTools>(sp =>
            {
                var kb = sp.GetRequiredService<IKnowledgeBaseService>();
                var llm = sp.GetRequiredService<ILlmService>();
                return new ChemicalComplianceTools(kb, llm);
            });
            services.AddSingleton<IToolService>(sp =>
            {
                var llm = sp.GetRequiredService<ILlmService>();
                var kb = sp.GetRequiredService<IKnowledgeBaseService>();
                var tools = AppConfig.Instance.ChemicalTool?.Tools;
                return new ToolService(llm, kb, tools);
            });
            services.AddSingleton<AgentDialog>();
            services.AddSingleton<IKnowledgeBaseService>(sp =>
            {
                var db = sp.GetRequiredService<IDatabaseService>();
                var llm = sp.GetRequiredService<ILlmService>();
                return new HybridKnowledgeBaseService(db, llm, AppConfig.Instance);
            });
            services.AddSingleton<IIntegrationService, IntegrationService>();
            services.AddSingleton<IAuditService, AuditService>();
            services.AddSingleton<IModuleFactory, ModuleFactory>();
            services.AddSingleton<ModuleDispatcher>();

            var serviceProvider = services.BuildServiceProvider();

            // 从 DI 容器解析服务
            var databaseService = serviceProvider.GetRequiredService<IDatabaseService>();
            var sessionService = serviceProvider.GetRequiredService<ISessionService>();
            var memoryService = serviceProvider.GetRequiredService<IMemoryService>();
            var llmService = serviceProvider.GetRequiredService<ILlmService>();
            var toolService = serviceProvider.GetRequiredService<IToolService>();
            var agentDialog = serviceProvider.GetRequiredService<AgentDialog>();
            var knowledgeBaseService = serviceProvider.GetRequiredService<IKnowledgeBaseService>();
            var integrationService = serviceProvider.GetRequiredService<IIntegrationService>();
            var auditService = serviceProvider.GetRequiredService<IAuditService>();
            var moduleFactory = serviceProvider.GetRequiredService<IModuleFactory>();
            var dispatcher = serviceProvider.GetRequiredService<ModuleDispatcher>();

            // 数据库连接初始化
            logger.LogInformation("📦 正在测试数据库连接...");
            Console.WriteLine("📦 正在测试数据库连接...");
            if (await databaseService.TestConnectionAsync())
            {
                logger.LogInformation("✅ 数据库连接成功");
                Console.WriteLine("✅ 数据库连接成功！");
                await databaseService.InitializeDatabaseAsync();
                Console.WriteLine("✅ 数据库表初始化完成！");
            }
            else
            {
                logger.LogWarning("⚠️ 数据库连接失败，请检查配置");
                Console.WriteLine("⚠️ 数据库连接失败，请检查配置");
            }

            var chemicalRAG = new ChemicalRAG(AppConfig.Instance.KnowledgeBase.BasePath, knowledgeBaseService);

            // 预加载化工知识库
            await chemicalRAG.LoadKnowledgeBaseAsync();

#region 完整的调用链路
// 程序启动
//   │
//   ▼
// ChemicalRAG.LoadKnowledgeBaseAsync()
//   │  [ChemicalRAG.cs 第38-93行]
//   │
//   ├── 扫描 knowledgebase/国标/*.txt         ← 只读 .txt 文件！
//   │   ├── GB15603-1995 常用化学危险品贮存通则.txt
//   │   ├── GB30000-2013 化学品分类和标签规范.txt
//   │   └── 危险化学品安全管理条例.txt
//   │
//   ├── 扫描 knowledgebase/园区规则/*.txt
//   │   ├── 园区动火作业安全规范.txt
//   │   └── 园区危化品存储管理规定.txt
//   │
//   ├── 扫描 knowledgebase/历史案例/*.txt
//   │   ├── 2022年安全检查整改案例.txt
//   │   └── 2023年储罐泄漏处置案例.txt
//   │
//   ▼
// LoadAndSplitFile() 对每个 .txt 文件
//   │  [ChemicalRAG.cs 第101-131行]
//   │
//   ├── File.ReadAllTextAsync(filePath)      ← 读入全文
//   ├── SplitTextIntoChunks(content, 500)    ← 按 500 字符分块
//   │
//   ▼
// _knowledgeBase.AddDocumentAsync(chunk, metadata)
//   │  [HybridKnowledgeBaseService.cs 第27-57行]
//   │
//   ├── [内存路径] _bm25Service.AddDocumentAsync(chunk)
//   │      → 存入 KnowledgeBaseService._documents 列表
//   │      → 更新 _termDocFreq 倒排索引
//   │      → 纯内存，进程重启就消失
//   │
//   └── [数据库路径] _databaseService.AddChemicalDocumentAsync(...)
//          → INSERT INTO chemical_documents (content, embedding, ...)
//          → 存入 PostgreSQL，持久化存储
//          → 会调用 _llmService.GetEmbeddingAsync(content) 生成 768 维向量


#endregion

// 所以数据是在程序每次启动时，从 knowledgebase/ 下的 .txt 文件读入，同时写入内存和数据库。 
// 因此 KnowledgeBaseService._documents 列表是空的只是因为还没运行过程序，
//或者运行过但用的 KnowledgeBaseService（纯内存版）而非 HybridKnowledgeBaseService。

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
                Console.WriteLine("  10. 数据库连接验证");
                Console.WriteLine("  11. 切换检索模式 (当前: " + (AppConfig.Instance.KnowledgeBase.SearchMode ?? "hybrid") + ")");
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
                else if (input == "10")
                {
                    await RunDatabaseValidation(databaseService);
                }
                else if (input == "11")
                {
                    await SwitchSearchMode();
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

        static async Task RunDatabaseValidation(IDatabaseService databaseService)
        {
            Console.WriteLine("\n========================================");
            Console.WriteLine("       数据库连接验证");
            Console.WriteLine("========================================");

            try
            {
                // 1. 获取数据库信息
                Console.WriteLine("\n🔍 正在获取数据库信息...");
                var info = await databaseService.GetDatabaseInfoAsync();
                Console.WriteLine(info);

                // 2. 获取表列表
                Console.WriteLine("\n📋 数据库表列表:");
                var tables = await databaseService.GetTableNamesAsync();
                if (tables.Count == 0)
                {
                    Console.WriteLine("   (空)");
                }
                else
                {
                    foreach (var table in tables)
                    {
                        Console.WriteLine($"   ✅ {table}");
                    }
                }

                // 3. 验证配置
                Console.WriteLine("\n🔧 当前配置验证:");
                var config = AppConfig.Instance.Database;
                Console.WriteLine($"   服务器: {config.Host}:{config.Port}");
                Console.WriteLine($"   数据库: {config.DatabaseName}");
                Console.WriteLine($"   用户: {config.Username}");

                // 4. 测试连接
                Console.WriteLine("\n🔗 测试连接...");
                if (await databaseService.TestConnectionAsync())
                {
                    Console.WriteLine("   ✅ 数据库连接成功！");
                }
                else
                {
                    Console.WriteLine("   ❌ 数据库连接失败！");
                }

                Console.WriteLine("\n✅ 数据库验证完成！");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"❌ 验证失败: {ex.Message}");
                Console.ResetColor();
            }
        }

        static async Task SwitchSearchMode()
        {
            Console.WriteLine("\n========================================");
            Console.WriteLine("       切换检索模式");
            Console.WriteLine("========================================");
            Console.WriteLine("\n当前检索模式: " + (AppConfig.Instance.KnowledgeBase.SearchMode ?? "hybrid"));
            Console.WriteLine("\n可用选项:");
            Console.WriteLine("  1. bm25 (关键词检索)");
            Console.WriteLine("  2. vector (向量语义检索)");
            Console.WriteLine("  3. hybrid (混合检索，默认)");

            Console.Write("\n请选择: ");
            Console.ForegroundColor = ConsoleColor.Green;
            var choice = Console.ReadLine() ?? "3";
            Console.ResetColor();

            switch (choice)
            {
                case "1":
                    AppConfig.Instance.KnowledgeBase.SearchMode = "bm25";
                    Console.WriteLine("✅ 已切换到 bm25 模式");
                    break;
                case "2":
                    AppConfig.Instance.KnowledgeBase.SearchMode = "vector";
                    Console.WriteLine("✅ 已切换到 vector 模式");
                    break;
                case "3":
                default:
                    AppConfig.Instance.KnowledgeBase.SearchMode = "hybrid";
                    Console.WriteLine("✅ 已切换到 hybrid 模式");
                    break;
            }

            Console.WriteLine("\n💡 提示: 此更改仅在当前会话有效");
        }
    }
}