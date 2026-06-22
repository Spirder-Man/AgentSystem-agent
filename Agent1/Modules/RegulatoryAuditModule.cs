using System.Text;
using Agent1.Services;
using Agent1.Models;

namespace Agent1.Modules
{
    /// <summary>
    /// [P3] 监管核查辅助模块 — 模拟安监局/应急管理局现场核查流程。
    /// 输入一份核查清单，AI 逐条比对 GB 国标/园区规则给出合规评估。
    /// 输出结构化报告：每项标注 合规/不合规/需补充材料 + 法规引用 + 整改建议。
    /// </summary>
    public class RegulatoryAuditModule : IInferenceModule
    {
        public string Name => "监管核查辅助";
        public string Description => "输入监管核查清单，AI 逐条比对法规给出合规评估报告";

        public Task<CliExecutionResult> RunWithResultAsync(string userInput)
            => Task.FromResult(new CliExecutionResult
            {
                Success = true,
                DisplayOutput = "监管核查辅助模块仅支持交互式运行，请使用 RunAsync() 或 API 调用 GenerateAuditReportAsync()",
                AuditRecord = "交互模式"
            });

        private readonly ILlmService _llmService;
        private readonly IKnowledgeBaseService _kbService;
        private readonly IAuditService _auditService;

        public RegulatoryAuditModule(
            ILlmService llmService,
            IKnowledgeBaseService kbService,
            IAuditService auditService)
        {
            _llmService = llmService;
            _kbService = kbService;
            _auditService = auditService;
        }

        public async Task RunAsync()
        {
            Console.WriteLine("\n========== 监管核查辅助 ==========");
            Console.WriteLine("请输入核查清单（每行一项，输入空行结束）:");
            Console.WriteLine("示例:");
            Console.WriteLine("  1. 危化品仓库消防通道宽度是否合规");
            Console.WriteLine("  2. 储罐区安全距离是否符合 GB 50160");
            Console.WriteLine("  3. 可燃气体报警器安装位置及数量");
            Console.Write("\n> ");

            var lines = new List<string>();
            while (true)
            {
                var line = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(line)) break;
                lines.Add(line.Trim());
            }

            if (lines.Count == 0)
            {
                Console.WriteLine("核查清单不能为空！");
                return;
            }

            var report = await GenerateAuditReportAsync(lines);

            Console.WriteLine(report);

            await _auditService.LogOperationAsync("system", "RegulatoryAudit",
                $"监管核查 {lines.Count} 项完成");
        }

        /// <summary>
        /// 核心方法：逐条比对法规生成合规评估报告。也可供 API 直接调用。
        /// </summary>
        public async Task<string> GenerateAuditReportAsync(List<string> checklistItems)
        {
            var sb = new StringBuilder();
            sb.AppendLine("\n══════════════ 监管核查评估报告 ══════════════");
            sb.AppendLine($"核查时间: {DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"核查项数: {checklistItems.Count}");
            sb.AppendLine("══════════════════════════════════════════════\n");

            int compliant = 0, nonCompliant = 0, needsReview = 0;

            for (int i = 0; i < checklistItems.Count; i++)
            {
                var item = checklistItems[i];
                Console.WriteLine($"\n🔍 正在核查第 {i + 1}/{checklistItems.Count} 项: {item.Truncate(60)}");

                // 1. RAG 检索相关法规
                var chunks = await _kbService.RetrieveChemicalRegulationAsync(item, topK: 3);

                // 2. 构建 Prompt
                var prompt = BuildAuditPrompt(item, chunks);

                // 3. LLM 生成评估
                Console.Write("   🤖 ");
                var llmOutput = await _llmService.InvokeStreamAsync(prompt, ConsoleColor.Cyan);

                // 4. 解析结果判定
                var (status, refs, suggestion) = ParseAuditResult(llmOutput);

                sb.AppendLine($"━━━ 第 {i + 1} 项 ━━━");
                sb.AppendLine($"核查内容: {item}");
                sb.AppendLine($"判定: {status}");
                sb.AppendLine($"法规依据: {(string.IsNullOrWhiteSpace(refs) ? "未引用具体条款" : refs)}");
                if (!string.IsNullOrWhiteSpace(suggestion))
                    sb.AppendLine($"建议: {suggestion}");
                sb.AppendLine();

                switch (status)
                {
                    case "✅ 合规": compliant++; break;
                    case "❌ 不合规": nonCompliant++; break;
                    default: needsReview++; break;
                }

                await Task.Delay(300);
            }

            sb.AppendLine("══════════════ 汇总 ══════════════");
            sb.AppendLine($"  ✅ 合规: {compliant} 项");
            sb.AppendLine($"  ❌ 不合规: {nonCompliant} 项");
            sb.AppendLine($"  ⚠️ 需补充材料/人工复核: {needsReview} 项");
            sb.AppendLine($"  合规率: {(checklistItems.Count > 0 ? (double)compliant / checklistItems.Count * 100 : 0):F1}%");
            sb.AppendLine("═══════════════════════════════════");

            return sb.ToString();
        }

        private static string BuildAuditPrompt(string item, List<RetrievedChunk> chunks)
        {
            var sb = new StringBuilder();
            sb.AppendLine("你是化工园区安全监管审核专家。请根据以下法规依据，对核查项进行合规评估。");
            sb.AppendLine();
            sb.AppendLine("【法规依据】");
            if (chunks.Count == 0)
            {
                sb.AppendLine("（未检索到直接相关法规，请基于化工安全常识判断）");
            }
            else
            {
                for (int i = 0; i < Math.Min(chunks.Count, 3); i++)
                {
                    var content = chunks[i].Content ?? "";
                    if (content.Length > 300) content = content[..300] + "...";
                    sb.AppendLine($"  {i + 1}. {content}");
                }
            }
            sb.AppendLine();
            sb.AppendLine($"【核查项】{item}");
            sb.AppendLine();
            sb.AppendLine("请按以下格式输出（每行一项，不要输出推理过程）:");
            sb.AppendLine("判定: [✅ 合规 / ❌ 不合规 / ⚠️ 需补充材料]");
            sb.AppendLine("法规: [引用具体 GB 编号和条款，如无则写\"见上述依据\"]");
            sb.AppendLine("建议: [如不合规则给出整改建议，1-2句；如合规则写\"无需整改\"]");

            return sb.ToString();
        }

        private static (string status, string refs, string suggestion) ParseAuditResult(string llmOutput)
        {
            var status = "⚠️ 需补充材料";
            var refs = "";
            var suggestion = "";

            if (string.IsNullOrWhiteSpace(llmOutput)) return (status, refs, suggestion);

            foreach (var line in llmOutput.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Contains("判定") || trimmed.Contains("合规"))
                {
                    if (trimmed.Contains("✅") || trimmed.Contains("合规") && !trimmed.Contains("不合规") && !trimmed.Contains("补充"))
                        status = "✅ 合规";
                    else if (trimmed.Contains("❌") || trimmed.Contains("不合规"))
                        status = "❌ 不合规";
                }
                else if (trimmed.Contains("法规") || trimmed.Contains("GB") || trimmed.Contains("依据"))
                {
                    var idx = trimmed.IndexOf('：');
                    if (idx < 0) idx = trimmed.IndexOf(':');
                    refs = idx >= 0 ? trimmed[(idx + 1)..].Trim() : trimmed;
                }
                else if (trimmed.Contains("建议") || trimmed.Contains("整改"))
                {
                    var idx = trimmed.IndexOf('：');
                    if (idx < 0) idx = trimmed.IndexOf(':');
                    suggestion = idx >= 0 ? trimmed[(idx + 1)..].Trim() : trimmed;
                }
            }

            return (status, refs, suggestion);
        }
    }
}
