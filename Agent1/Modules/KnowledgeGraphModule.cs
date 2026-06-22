using Agent1.Services;
using Agent1.Models;

namespace Agent1.Modules
{
    /// <summary>
    /// [P3] 化工安全知识图谱模块 — 化学品/法规/事故案例关联网络。
    /// 支持：多跳图遍历、法规引用链查询、历史事故关联、法规冲突检测、DOT 可视化导出。
    /// </summary>
    public class KnowledgeGraphModule : IInferenceModule
    {
        public string Name => "知识图谱查询";
        public string Description => "查询化学品-法规-事故的知识关联网络，支持图表导出";

        public Task<CliExecutionResult> RunWithResultAsync(string userInput)
            => Task.FromResult(new CliExecutionResult
            {
                Success = true,
                DisplayOutput = "知识图谱模块仅支持交互式运行，请使用 RunAsync()",
                AuditRecord = "交互模式"
            });

        private readonly IKnowledgeBaseService _kbService;

        public KnowledgeGraphModule(IKnowledgeBaseService kbService)
        {
            _kbService = kbService;
        }

        public async Task RunAsync()
        {
            Console.WriteLine("\n========== 化工安全知识图谱 ==========");
            Console.WriteLine("  1. 化学品关联查询（法规+事故+禁忌）");
            Console.WriteLine("  2. 导出 DOT 可视化文件");
            Console.Write("请选择: ");
            var choice = Console.ReadLine() ?? "1";

            var graph = KnowledgeGraphFactory.GetOrBuild(_kbService);

            if (choice == "2")
            {
                var path = "knowledgebase/knowledge_graph.dot";
                File.WriteAllText(path, graph.ExportDOT());
                Console.WriteLine($"✅ 已导出到 {path} (可用 Graphviz 渲染: dot -Tpng {path} -o graph.png)");
                return;
            }

            Console.Write("请输入化学品名称 (如 苯、氯、硝酸铵): ");
            var chemical = Console.ReadLine() ?? "";

            if (string.IsNullOrWhiteSpace(chemical))
            {
                Console.WriteLine("化学品名称不能为空！");
                return;
            }

            var result = await graph.QueryAsync(chemical);
            Console.WriteLine(result);
        }
    }
}
