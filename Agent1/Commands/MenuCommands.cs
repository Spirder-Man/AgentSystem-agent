using Agent1.Config;
using Agent1.Models;
using Agent1.Modules;
using Agent1.Services;

namespace Agent1.Commands
{
    /// <summary>
    /// [P2 命令模式] 菜单命令接口 — 将 Program.cs 中 14 个 if-else 拆解为独立命令类。
    /// 每个菜单选项对应一个 IMenuCommand 实现，新增功能只需注册新 Command，不修改主循环。
    /// </summary>
    public interface IMenuCommand
    {
        string Key { get; }
        string Label { get; }
        Task ExecuteAsync();
    }

    /// <summary>退出命令 (选项 0 / exit)</summary>
    public class ExitCommand : IMenuCommand
    {
        public string Key => "0";
        public string Label => "退出";
        public Task ExecuteAsync()
        {
            Console.WriteLine("\n👋 再见！");
            Environment.Exit(0);
            return Task.CompletedTask;
        }
    }

    /// <summary>模块调度命令 (选项 1-7): CoT/ReAct/Reflection/RAG/UnifiedDialog</summary>
    public class ModuleCommand : IMenuCommand
    {
        private readonly ModuleDispatcher _dispatcher;
        private readonly ModuleType _moduleType;

        public string Key { get; }
        public string Label { get; }

        public ModuleCommand(string key, string label, ModuleType moduleType, ModuleDispatcher dispatcher)
        {
            Key = key;
            Label = label;
            _moduleType = moduleType;
            _dispatcher = dispatcher;
        }

        public async Task ExecuteAsync()
        {
            try
            {
                await _dispatcher.ExecuteModuleAsync(_moduleType);
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

    /// <summary>化工合规自查 (选项 8)</summary>
    public class ComplianceCheckCommand : IMenuCommand
    {
        private readonly IModuleFactory _factory;
        public string Key => "8";
        public string Label => "化工合规自查【核心功能】";

        public ComplianceCheckCommand(IModuleFactory factory) => _factory = factory;

        public async Task ExecuteAsync()
        {
            var module = _factory.CreateModule(ModuleType.ComplianceCheck);
            await module.RunAsync();
        }
    }

    /// <summary>化工合规 RAG 测试 (选项 9)</summary>
    public class ChemicalRagTestCommand : IMenuCommand
    {
        private readonly ChemicalRAG _chemicalRAG;
        public string Key => "9";
        public string Label => "化工合规RAG测试";

        public ChemicalRagTestCommand(ChemicalRAG chemicalRAG) => _chemicalRAG = chemicalRAG;

        public async Task ExecuteAsync()
        {
            var testQueries = new[] { "危化品储罐之间的安全距离是多少？", "消防通道有什么要求？" };
            foreach (var q in testQueries)
            {
                await _chemicalRAG.SearchAsync(q);
                await Task.Delay(500);
            }

            Console.WriteLine("\n======== 交互式检索测试 (输入 exit 退出) ========");
            while (true)
            {
                Console.Write("\n🔍 请输入查询: ");
                var query = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(query) || query.Equals("exit", StringComparison.OrdinalIgnoreCase))
                    break;
                await _chemicalRAG.SearchAsync(query);
            }
            Console.WriteLine("\n✅ 化工合规RAG测试结束！");
        }
    }

    /// <summary>数据库连接验证 (选项 10)</summary>
    public class DatabaseValidationCommand : IMenuCommand
    {
        private readonly IDatabaseService _db;
        public string Key => "10";
        public string Label => "数据库连接验证";

        public DatabaseValidationCommand(IDatabaseService db) => _db = db;

        public async Task ExecuteAsync()
        {
            try
            {
                Console.WriteLine("\n🔍 正在获取数据库信息...");
                var info = await _db.GetDatabaseInfoAsync();
                Console.WriteLine(info);

                Console.WriteLine("\n📋 数据库表列表:");
                var tables = await _db.GetTableNamesAsync();
                foreach (var t in tables) Console.WriteLine($"   ✅ {t}");

                var config = AppConfig.Instance.Database;
                Console.WriteLine($"\n🔧 服务器: {config.Host}:{config.Port}, 数据库: {config.DatabaseName}, 用户: {config.Username}");
                Console.WriteLine($"\n🔗 测试连接... {(await _db.TestConnectionAsync() ? "✅ 成功" : "❌ 失败")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 验证失败: {ex.Message}");
            }
        }
    }

    /// <summary>切换检索模式 (选项 11)</summary>
    public class SwitchSearchModeCommand : IMenuCommand
    {
        public string Key => "11";
        public string Label => "切换检索模式";

        public Task ExecuteAsync()
        {
            var current = AppConfig.Instance.KnowledgeBase.SearchMode;
            Console.WriteLine($"\n当前检索模式: {current}");
            Console.WriteLine("  1. Bm25 (关键词)  2. Vector (向量语义)  3. Hybrid (混合, 默认)");
            Console.Write("请选择: ");
            var choice = Console.ReadLine() ?? "3";

            AppConfig.Instance.KnowledgeBase.SearchMode = choice switch
            {
                "1" => SearchModeType.Bm25,
                "2" => SearchModeType.Vector,
                _ => SearchModeType.Hybrid
            };
            Console.WriteLine($"✅ 已切换到 {AppConfig.Instance.KnowledgeBase.SearchMode}");
            Console.WriteLine("💡 此更改仅当前会话有效");
            return Task.CompletedTask;
        }
    }

    /// <summary>工具调用诊断 (选项 12)</summary>
    public class FunctionCallingDiagnosticsCommand : IMenuCommand
    {
        private readonly AgentDialog _agentDialog;
        private readonly ILlmService _llmService;
        public string Key => "12";
        public string Label => "工具调用诊断验证 [Phase 2a]";

        public FunctionCallingDiagnosticsCommand(AgentDialog agentDialog, ILlmService llmService)
        {
            _agentDialog = agentDialog;
            _llmService = llmService;
        }

        public async Task ExecuteAsync()
        {
            Console.WriteLine($"\n======== Phase 2a 工具调用诊断 ========");
            Console.WriteLine($"   模型: {ModelConfig.ModelId}");

            var tests = new (string q, string tools, string desc)[]
            {
                ("苯属于什么危险类别", "CheckHazardCategory", "危化品类别查询"),
                ("苯和丙酮能同库储存吗", "CheckStorageCompatibility", "储存兼容性检查"),
                ("甲类仓库与明火点的安全距离是多少", "GetSafetyDistance", "安全距离查询"),
                ("现在几点", "GetCurrentTime", "时间查询"),
                ("甲醇和硝酸存放在同一个仓库是否合规", "CheckHazardCategory,CheckStorageCompatibility", "多工具"),
            };

            var session = _agentDialog.CreateSession(SessionType.ChemicalCompliance);
            int pass = 0;
            for (int i = 0; i < tests.Length; i++)
            {
                var t = tests[i];
                Console.WriteLine($"\n━━━ {i + 1}/{tests.Length}: {t.desc} ━━━");
                try
                {
                    await _agentDialog.ExecuteAsync(t.q, session);
                    var llmSvc = _llmService as LlmService;
                    if (llmSvc != null && llmSvc.LastFunctionCalls.Count > 0)
                    {
                        Console.WriteLine($"   ✅ {string.Join(", ", llmSvc.LastFunctionCalls.Select(fc => fc.FunctionName))}");
                        pass++;
                    }
                    else Console.WriteLine("   ❌ 未触发工具调用");
                }
                catch (Exception ex) { Console.WriteLine($"   ❌ {ex.Message}"); }
                await Task.Delay(1000);
            }
            Console.WriteLine($"\n   诊断完成: {pass}/{tests.Length} 触发");
        }
    }

    /// <summary>合规评测 (选项 13)</summary>
    public class ComplianceEvalCommand : IMenuCommand
    {
        private readonly AgentDialog _agentDialog;
        private readonly ILlmService _llmService;
        private readonly IKnowledgeBaseService _knowledgeBaseService;
        public string Key => "13";
        public string Label => "合规评测集 [50条业务指标]";

        public ComplianceEvalCommand(AgentDialog agentDialog, ILlmService llmService, IKnowledgeBaseService kb)
        {
            _agentDialog = agentDialog;
            _llmService = llmService;
            _knowledgeBaseService = kb;
        }

        public async Task ExecuteAsync()
        {
            var verifier = new ReflectionVerifier(_knowledgeBaseService);
            var engine = new EvalEngine(_agentDialog, _llmService, _knowledgeBaseService, verifier);
            await engine.RunComplianceEvalAsync();
        }
    }

    /// <summary>整改工单跟进 (选项 14)</summary>
    public class TicketFollowupCommand : IMenuCommand
    {
        private readonly IModuleFactory _factory;
        public string Key => "14";
        public string Label => "整改工单跟进";

        public TicketFollowupCommand(IModuleFactory factory) => _factory = factory;

        public async Task ExecuteAsync()
        {
            var module = _factory.CreateModule(ModuleType.TicketFollowup);
            await module.RunAsync();
        }
    }

    /// <summary>[P2] 多模态视觉分析 (选项 15) — GHS标签识别、储罐/管道照片分析</summary>
    public class MultimodalCommand : IMenuCommand
    {
        private readonly MultimodalService _multimodal = new();
        public string Key => "15";
        public string Label => "多模态视觉分析 [GHS标签/储罐照片]";

        public async Task ExecuteAsync()
        {
            Console.WriteLine("\n======== 多模态视觉分析 ========");
            Console.WriteLine("  1. GHS 危险标签识别");
            Console.WriteLine("  2. 储罐/管道场景合规检查");
            Console.WriteLine("  3. 自定义图片分析");
            Console.Write("请选择: ");
            var choice = Console.ReadLine() ?? "1";

            Console.Write("请输入图片路径: ");
            var path = Console.ReadLine() ?? "";
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                Console.WriteLine($"❌ 图片不存在: {path}");
                return;
            }

            Console.WriteLine($"\n🔍 正在分析: {Path.GetFileName(path)} (模型: {ModelConfig.MultimodalModelId})...");
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var result = choice switch
            {
                "2" => await _multimodal.AnalyzeStorageSceneAsync(path),
                "3" => await _multimodal.AnalyzeImageAsync(path, "请详细描述这张图片的内容，重点关注安全相关信息"),
                _ => await _multimodal.AnalyzeHazardLabelAsync(path),
            };

            sw.Stop();
            Console.WriteLine($"\n📋 分析结果 (耗时 {sw.Elapsed.TotalSeconds:F1}s):");
            Console.WriteLine(new string('-', 50));
            Console.WriteLine(result);
            Console.WriteLine(new string('-', 50));
        }
    }

    /// <summary>[P3] 监管核查辅助 (选项 17)</summary>
    public class RegulatoryAuditCommand : IMenuCommand
    {
        private readonly IModuleFactory _factory;
        public string Key => "17";
        public string Label => "监管核查辅助 [安监核查清单逐条评估]";

        public RegulatoryAuditCommand(IModuleFactory factory) => _factory = factory;

        public async Task ExecuteAsync()
        {
            var module = _factory.CreateModule(ModuleType.RegulatoryAudit);
            await module.RunAsync();
        }
    }

    /// <summary>[P3] 知识图谱查询 (选项 19)</summary>
    public class KnowledgeGraphCommand : IMenuCommand
    {
        private readonly IKnowledgeBaseService _kb;
        public string Key => "19";
        public string Label => "知识图谱 [化学品-法规-事故关联网]";

        public KnowledgeGraphCommand(IKnowledgeBaseService kb) => _kb = kb;

        public async Task ExecuteAsync()
        {
            var module = new KnowledgeGraphModule(_kb);
            await module.RunAsync();
        }
    }

    /// <summary>[P3] 知识库增量更新 — 仅处理新增/修改/删除的文件</summary>
    public class IncrementalKnowledgeBaseCommand : IMenuCommand
    {
        private readonly ChemicalRAG _chemicalRAG;
        public string Key => "16";
        public string Label => "知识库增量更新 [新增/修改/删除文件]";

        public IncrementalKnowledgeBaseCommand(ChemicalRAG chemicalRAG) => _chemicalRAG = chemicalRAG;

        public async Task ExecuteAsync()
        {
            await _chemicalRAG.LoadKnowledgeBaseIncrementalAsync();
        }
    }

    /// <summary>[P3] 应急响应方案 (选项 18)</summary>
    public class EmergencyResponseCommand : IMenuCommand
    {
        private readonly ILlmService _llm;
        private readonly IKnowledgeBaseService _kb;
        private readonly IAuditService _audit;
        private readonly IIntegrationService _integration;
        public string Key => "18";
        public string Label => "应急响应方案 [泄漏/火灾/爆炸/中毒]";

        public EmergencyResponseCommand(ILlmService llm, IKnowledgeBaseService kb, IAuditService audit, IIntegrationService integration)
        {
            _llm = llm; _kb = kb; _audit = audit; _integration = integration;
        }

        public async Task ExecuteAsync()
        {
            var module = new EmergencyResponseModule(_llm, _kb, _audit, _integration);
            await module.RunAsync();
        }
    }
}
