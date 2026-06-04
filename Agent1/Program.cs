using System;
using System.Threading.Tasks;
using Agent1.Services;
using Agent1.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

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

            // 启动前校验关键配置项，防止运行时才发现配错
            var configErrors = AppConfig.Instance.Validate();
            if (configErrors.Count > 0)
            {
                Console.WriteLine("❌ 配置校验失败，请检查 appsettings.json:");
                foreach (var err in configErrors)
                    Console.WriteLine($"   - {err}");
                Console.WriteLine("\n按任意键退出...");
                Console.ReadKey();
                return;
            }

            // ═══════════════════════════════════════════════════
            // Phase 1: 结构化日志 — Serilog + Console 输出双写文件
            // ═══════════════════════════════════════════════════
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .WriteTo.File("logs/agent1-.log", rollingInterval: RollingInterval.Day)
                .CreateLogger();

            // Phase 2d: 所有 Console.WriteLine 同时写入 logs/full-YYYYMMDD.log
            // 解决终端输出超出缓冲区后无法回溯查看诊断信息的问题
            var logDir = "logs";
            if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
            var fullLogPath = Path.Combine(logDir, $"full-{DateTime.Now:yyyyMMdd}.log");
            var fileWriter = new StreamWriter(fullLogPath, append: true) { AutoFlush = true };
            Console.SetOut(new ConsoleTeeWriter(Console.Out, fileWriter));
            Console.WriteLine($"📝 诊断日志双写已启用 → {fullLogPath}");

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

            // Phase 2a 修复: 打破循环依赖
            // ILlmService ← IKnowledgeBaseService ← ILlmService 形成死锁
            // 解决方案: ILlmService 先以无 kbService 方式注册, DI 完成后再调用 InitializeTools 注入
            services.AddSingleton<LlmService>(sp => new LlmService(null!));
            services.AddSingleton<ILlmService>(sp => sp.GetRequiredService<LlmService>());

            services.AddSingleton<IToolService>(sp =>
            {
                var llm = sp.GetRequiredService<ILlmService>();
                var kb = sp.GetRequiredService<IKnowledgeBaseService>();
                return new ToolService(llm, kb, AppConfig.Instance.ChemicalTool?.Tools);
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

            // Phase 2a DI 循环修复: 延迟注入 RAG 服务到 ChemicalComplianceTools
            // LlmService 构造函数传了 null, 现在用真实 kbService 替换
            var llmSvc = serviceProvider.GetRequiredService<LlmService>();
            llmSvc.SetKnowledgeBaseService(knowledgeBaseService);
            Console.WriteLine("🔗 RAG 知识库已注入 ChemicalComplianceTools (延迟绑定)");

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
                Console.WriteLine("  12. 工具调用诊断验证 [Phase 2a]");
                Console.WriteLine("  13. 合规评测集 [50条业务指标]");
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
                else if (input == "12")
                {
                    await RunFunctionCallingDiagnostics(agentDialog, llmService);
                }
                else if (input == "13")
                {
                    await RunComplianceEval(agentDialog, llmService);
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

        /// <summary>
        /// Phase 2a 验证: 工具调用诊断 — 运行预设测试用例，验证 SK Auto Function Calling 是否生效
        /// </summary>
        static async Task RunFunctionCallingDiagnostics(AgentDialog agentDialog, ILlmService llmService)
        {
            Console.WriteLine("\n========================================");
            Console.WriteLine("    Phase 2a 工具调用诊断验证");
            Console.WriteLine("========================================");
            Console.WriteLine($"   当前模型: {ModelConfig.ModelId}");
            Console.WriteLine("   预期: SK Auto Function Calling 应自动触发对应工具");
            Console.WriteLine("========================================\n");

            var testCases = new (string query, string expectedTools, string description)[]
            {
                ("苯属于什么危险类别", "CheckHazardCategory", "单一工具: 危化品类别查询"),
                ("苯和丙酮能同库储存吗", "CheckStorageCompatibility", "单一工具: 储存兼容性检查"),
                ("甲类仓库与明火点的安全距离是多少", "GetSafetyDistance", "单一工具: 安全距离查询"),
                ("现在几点", "GetCurrentTime", "通用工具: 时间查询"),
                ("甲醇和硝酸存放在同一个仓库是否合规", "CheckHazardCategory,CheckStorageCompatibility", "多工具: 类别+兼容性"),
            };

            var session = agentDialog.CreateSession(SessionType.ChemicalCompliance);
            int passCount = 0;

            for (int i = 0; i < testCases.Length; i++)
            {
                var tc = testCases[i];
                Console.WriteLine($"\n━━━ 测试 {i + 1}/{testCases.Length}: {tc.description} ━━━");
                Console.WriteLine($"   查询: \"{tc.query}\"");
                Console.WriteLine($"   预期工具: {tc.expectedTools}");

                try
                {
                    var result = await agentDialog.ExecuteAsync(tc.query, session);

                    // 读取诊断结果
                    var llmSvc = llmService as LlmService;
                    if (llmSvc != null && llmSvc.LastFunctionCalls.Count > 0)
                    {
                        var actualTools = string.Join(", ", llmSvc.LastFunctionCalls.Select(fc => fc.FunctionName));
                        Console.WriteLine($"   ✅ 实际调用: {actualTools}");
                        foreach (var fc in llmSvc.LastFunctionCalls)
                        {
                            Console.WriteLine($"      📋 {fc.FunctionName}({fc.Arguments}) → {(fc.Success ? "成功" : "失败")}");
                            var resultPreview = (fc.Result ?? "").Length > 100
                                ? (fc.Result ?? "").Substring(0, 100) + "..."
                                : fc.Result;
                            Console.WriteLine($"         结果: {resultPreview}");
                        }
                        passCount++;
                    }
                    else
                    {
                        Console.WriteLine($"   ❌ 未触发任何工具调用! 预期: {tc.expectedTools}");
                        Console.WriteLine("      → LLM 可能绕过了 Function Calling，直接凭记忆回答");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ❌ 测试异常: {ex.Message}");
                }

                await Task.Delay(1000); // 间隔1秒避免 Ollama 过载
            }

            Console.WriteLine($"\n========================================");
            Console.WriteLine($"   诊断完成: {passCount}/{testCases.Length} 用例触发了工具调用");
            Console.WriteLine($"   ⚠️ 关键发现: DeepSeek-R1:7b 在 Ollama 上不支持 Function Calling");
            Console.WriteLine($"      → 错误信息: 'does not support tools'");
            Console.WriteLine($"      → SK Auto Function Calling 无法与此模型配合使用");
            Console.WriteLine($"      → 5 个'工具调用'实际是 SK 内部函数，非 ChemicalComplianceTools");
            Console.WriteLine($"   ");
            Console.WriteLine($"   建议: 切换到支持 Function Calling 的模型 (如 Qwen3-8B)");
            Console.WriteLine($"   操作步骤:");
            Console.WriteLine($"     1. ollama pull qwen3:8b");
            Console.WriteLine($"     2. 修改 appsettings.json: \"ModelId\": \"qwen3:8b\"");
            Console.WriteLine($"     3. 重新运行 dotnet run 并选 12 验证");
            Console.WriteLine($"   详见: docs/architecture/ModelScope模型选型决策框架.md");
            Console.WriteLine("========================================\n");
        }

        // ═══════════════════════════════════════════════════════
        // 业务评测引擎 — 50条合规评测集全量跑测
        // ═══════════════════════════════════════════════════════
        static async Task RunComplianceEval(AgentDialog agentDialog, ILlmService llmService)
        {
            var evalConfig = AppConfig.Instance.Evaluation;
            var jsonPath = Path.Combine(AppContext.BaseDirectory, evalConfig.EvalSetPath);

            Console.WriteLine("\n══════════════════════════════════════════════");
            Console.WriteLine("   化工合规AI Agent — 50条业务评测");
            Console.WriteLine("══════════════════════════════════════════════");
            Console.WriteLine($"   模型: {ModelConfig.ModelId}");
            Console.WriteLine($"   评测集: {jsonPath}");
            Console.WriteLine("   评估维度: 工具触发 + 参数提取 + 合规结论");
            Console.WriteLine("══════════════════════════════════════════════\n");

            if (!File.Exists(jsonPath))
            {
                Console.WriteLine($"❌ 评测集文件不存在: {jsonPath}");
                return;
            }

            // 评测模式：压制 RAG 详细日志，减少 Console I/O 开销
            EvalMode.IsActive = true;

            var json = await File.ReadAllTextAsync(jsonPath);
            EvalSet evalSet;
            try
            {
                evalSet = JsonSerializer.Deserialize<EvalSet>(json) ?? new EvalSet();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 评测集 JSON 解析失败: {ex.Message}");
                return;
            }

            var cases = evalSet.test_cases ?? new List<EvalCase>();
            Console.WriteLine($"📋 加载评测集: {evalSet.name} (v{evalSet.version})");
            Console.WriteLine($"   共 {cases.Count} 条用例\n");

            var results = new List<EvalResult>();
            var categoryStats = new Dictionary<string, (int total, int toolOk, int paramOk, int conclusionOk)>();

            for (int i = 0; i < cases.Count; i++)
            {
                var tc = cases[i];
                Console.WriteLine($"━━━ [{tc.id}] {tc.category} ({i + 1}/{cases.Count}) ━━━");
                Console.WriteLine($"   查询: \"{tc.query}\"");
                Console.WriteLine($"   预期工具: {tc.expected_tool}");

                var result = new EvalResult
                {
                    id = tc.id,
                    category = tc.category,
                    query = tc.query,
                    expected_tool = tc.expected_tool
                };

                // 初始化分类统计
                if (!categoryStats.ContainsKey(tc.category))
                    categoryStats[tc.category] = (0, 0, 0, 0);
                var cat = categoryStats[tc.category];
                categoryStats[tc.category] = (cat.total + 1, cat.toolOk, cat.paramOk, cat.conclusionOk);

                try
                {
                    var response = await agentDialog.ExecuteEvalFastAsync(tc.query);
                    result.actual_response = response ?? "";

                    // 1) 工具触发检查
                    var llmSvc = llmService as LlmService;
                    if (llmSvc != null && llmSvc.LastFunctionCalls.Count > 0)
                    {
                        var calledTools = llmSvc.LastFunctionCalls.Select(fc => fc.FunctionName).ToList();
                        result.actual_tools = string.Join(",", calledTools);

                        if (calledTools.Contains(tc.expected_tool, StringComparer.OrdinalIgnoreCase))
                        {
                            result.tool_match = true;
                            Console.WriteLine($"   ✅ 工具触发: {result.actual_tools}");

                            // 2) 参数检查
                            var matchedCall = llmSvc.LastFunctionCalls
                                .FirstOrDefault(fc => fc.FunctionName.Equals(tc.expected_tool, StringComparison.OrdinalIgnoreCase));
                            if (matchedCall != null && tc.expected_params != null)
                            {
                                result.actual_params = matchedCall.Arguments ?? "";
                                result.param_match = CheckParams(matchedCall.Arguments, tc.expected_params);
                                Console.WriteLine($"      {(result.param_match ? "✅" : "⚠️")} 参数: {result.actual_params}");
                            }

                            // 3) 结论检查
                            if (tc.expected_conclusion != null)
                            {
                                result.conclusion_match = CheckConclusion(response, tc.expected_conclusion);
                                Console.WriteLine($"      {(result.conclusion_match ? "✅" : "⚠️")} 结论: is_compliant={tc.expected_conclusion.is_compliant}");
                            }
                        }
                        else
                        {
                            result.tool_match = false;
                            Console.WriteLine($"   ❌ 工具错误: 预期 {tc.expected_tool}, 实际 {result.actual_tools}");
                        }
                    }
                    else
                    {
                        result.tool_match = false;
                        result.actual_tools = "(无工具调用)";
                        Console.WriteLine($"   ❌ 未触发任何工具");
                    }

                    // 更新分类统计
                    var cat2 = categoryStats[tc.category];
                    categoryStats[tc.category] = (
                        cat2.total,
                        cat2.toolOk + (result.tool_match ? 1 : 0),
                        cat2.paramOk + (result.param_match ? 1 : 0),
                        cat2.conclusionOk + (result.conclusion_match ? 1 : 0)
                    );
                }
                catch (Exception ex)
                {
                    result.error = ex.Message;
                    Console.WriteLine($"   ❌ 异常: {ex.Message}");
                }

                results.Add(result);
                await Task.Delay(evalConfig.CaseIntervalMs);
            }

            // ═══ 输出评测报告 ═══
            var total = results.Count;
            var toolOk = results.Count(r => r.tool_match);
            var paramOk = results.Count(r => r.param_match);
            var conclusionOk = results.Count(r => r.conclusion_match);
            var errors = results.Count(r => !string.IsNullOrEmpty(r.error));

            Console.WriteLine("\n╔════════════════════════════════════════╗");
            Console.WriteLine("║         业 务 评 测 报 告              ║");
            Console.WriteLine("╠════════════════════════════════════════╣");
            Console.WriteLine($"║  总用例数:     {total,3}                        ║");
            Console.WriteLine($"║  成功执行:     {total - errors,3}                        ║");
            Console.WriteLine($"║  异常:         {errors,3}                        ║");
            Console.WriteLine("╠════════════════════════════════════════╣");
            Console.WriteLine("║  核心业务指标:                         ║");
            Console.WriteLine($"║  工具触发率:   {toolOk,3}/{total} = {toolOk * 100.0 / Math.Max(total, 1):F1}%               ║");
            Console.WriteLine($"║  参数准确率:   {paramOk,3}/{total} = {paramOk * 100.0 / Math.Max(total, 1):F1}%               ║");
            Console.WriteLine($"║  结论准确率:   {conclusionOk,3}/{total} = {conclusionOk * 100.0 / Math.Max(total, 1):F1}%               ║");
            Console.WriteLine("╠════════════════════════════════════════╣");
            Console.WriteLine("║  分类细项:                             ║");

            foreach (var kvp in categoryStats)
            {
                var catName = kvp.Key;
                var (catTotal, catTool, catParam, catConcl) = kvp.Value;
                Console.WriteLine($"║  {catName}:");
                Console.WriteLine($"║    工具: {catTool}/{catTotal}  参数: {catParam}/{catTotal}  结论: {catConcl}/{catTotal}");
            }
            Console.WriteLine("╚════════════════════════════════════════╝\n");

            // 写入报告文件
            var report = new EvalReport
            {
                model = ModelConfig.ModelId,
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                total = total,
                tool_call_rate = toolOk * 100.0 / Math.Max(total, 1),
                parameter_accuracy = paramOk * 100.0 / Math.Max(total, 1),
                conclusion_accuracy = conclusionOk * 100.0 / Math.Max(total, 1),
                category_breakdown = categoryStats.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new CategoryMetric
                    {
                        total = kvp.Value.total,
                        tool_ok = kvp.Value.toolOk,
                        param_ok = kvp.Value.paramOk,
                        conclusion_ok = kvp.Value.conclusionOk
                    }
                ),
                cases = results
            };

            var reportPath = Path.Combine(AppContext.BaseDirectory, evalConfig.OutputReportPath);
            var reportDir = Path.GetDirectoryName(reportPath);
            if (!string.IsNullOrEmpty(reportDir) && !Directory.Exists(reportDir))
                Directory.CreateDirectory(reportDir);
            var reportJson = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(reportPath, reportJson);
            Console.WriteLine($"📄 详细报告已保存: {reportPath}");

            // 模型评级
            var avgScore = (toolOk * 100.0 / Math.Max(total, 1)
                          + paramOk * 100.0 / Math.Max(total, 1)
                          + conclusionOk * 100.0 / Math.Max(total, 1)) / 3.0;
            Console.Write("\n📊 综合评级: ");
            if (avgScore >= 85) Console.WriteLine("★★★ 优秀 — 可投入生产使用");
            else if (avgScore >= 70) Console.WriteLine("★★☆ 良好 — 建议针对性优化后上线");
            else if (avgScore >= 55) Console.WriteLine("★☆☆ 可用 — 需进一步 Prompt/模型调优");
            else Console.WriteLine("☆☆☆ 不合格 — 建议更换模型或架构方案");
            Console.WriteLine($"   (工具触发率 × 参数准确率 × 结论准确率 综合: {avgScore:F1}%)\n");

            // 恢复正常日志模式
            EvalMode.IsActive = false;
        }

        /// <summary>宽松检查工具参数是否匹配预期</summary>
        static bool CheckParams(string? actualArgs, Dictionary<string, string>? expected)
        {
            if (string.IsNullOrEmpty(actualArgs) || expected == null || expected.Count == 0)
                return false;
            var argsLower = actualArgs.ToLowerInvariant();
            foreach (var kvp in expected)
                if (argsLower.Contains(kvp.Value.ToLowerInvariant()))
                    return true;
            return false;
        }

        /// <summary>检查响应中的合规结论是否匹配预期</summary>
        static bool CheckConclusion(string? response, EvalConclusion? expected)
        {
            if (string.IsNullOrEmpty(response) || expected == null)
                return false;
            var respLower = response.ToLowerInvariant();
            if (expected.is_compliant)
                return respLower.Contains("合规") || respLower.Contains("允许") || respLower.Contains("可以");
            else
                return respLower.Contains("不合规") || respLower.Contains("不允许") || respLower.Contains("禁止") || respLower.Contains("严禁");
        }

        // ═══════ 评测数据模型 ═══════
        class EvalSet
        {
            public string name { get; set; } = "";
            public string version { get; set; } = "";
            public string created { get; set; } = "";
            public List<EvalCase> test_cases { get; set; } = new();
        }

        class EvalCase
        {
            public string id { get; set; } = "";
            public string category { get; set; } = "";
            public string query { get; set; } = "";
            public string expected_tool { get; set; } = "";
            public Dictionary<string, string>? expected_params { get; set; }
            public EvalConclusion? expected_conclusion { get; set; }
        }

        class EvalConclusion
        {
            public bool is_compliant { get; set; }
            public string regulation { get; set; } = "";
        }

        class EvalResult
        {
            public string id { get; set; } = "";
            public string category { get; set; } = "";
            public string query { get; set; } = "";
            public string expected_tool { get; set; } = "";
            public string? actual_tools { get; set; }
            public string? actual_params { get; set; }
            public string? actual_response { get; set; }
            public bool tool_match { get; set; }
            public bool param_match { get; set; }
            public bool conclusion_match { get; set; }
            public string? error { get; set; }
        }

        class EvalReport
        {
            public string model { get; set; } = "";
            public string timestamp { get; set; } = "";
            public int total { get; set; }
            public double tool_call_rate { get; set; }
            public double parameter_accuracy { get; set; }
            public double conclusion_accuracy { get; set; }
            public Dictionary<string, CategoryMetric> category_breakdown { get; set; } = new();
            public List<EvalResult> cases { get; set; } = new();
        }

        class CategoryMetric
        {
            public int total { get; set; }
            public int tool_ok { get; set; }
            public int param_ok { get; set; }
            public int conclusion_ok { get; set; }
        }
    }

    /// <summary>
    /// 双写 TextWriter：同时写入原始 Console 输出流和文件流
    /// 解决终端缓冲区溢出后无法回溯诊断日志的问题
    /// </summary>
    public class ConsoleTeeWriter : TextWriter
    {
        private readonly TextWriter _original;
        private readonly TextWriter _file;

        public ConsoleTeeWriter(TextWriter original, TextWriter file)
        {
            _original = original;
            _file = file;
        }

        public override Encoding Encoding => _original.Encoding;

        public override void Write(char value)
        {
            _original.Write(value);
            _file.Write(value);
        }

        public override void Write(string? value)
        {
            _original.Write(value);
            _file.Write(value);
        }

        public override void WriteLine(string? value)
        {
            _original.WriteLine(value);
            _file.WriteLine(value);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _file.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}