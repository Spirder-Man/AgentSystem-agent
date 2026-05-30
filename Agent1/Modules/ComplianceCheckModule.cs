using System.Text;
using Agent1.Services;

namespace Agent1.Modules
{
    /// <summary>
    /// 化工园区日常合规自查模块
    /// </summary>
    public class ComplianceCheckModule : IInferenceModule
    {
        public string Name => "化工合规自查";
        public string Description => "上传巡检内容，自动审核危化品合规性";

        private readonly IKnowledgeBaseService _kbService;
        private readonly ILlmService _llmService;
        private readonly IIntegrationService _integrationService;
        private readonly IAuditService _auditService;

        public ComplianceCheckModule(
            IKnowledgeBaseService kbService,
            ILlmService llmService,
            IIntegrationService integrationService,
            IAuditService auditService)
        {
            _kbService = kbService;
            _llmService = llmService;
            _integrationService = integrationService;
            _auditService = auditService;
        }

        public async Task RunAsync()
        {
            Console.WriteLine("\n========== 化工合规自查 ==========");
            Console.WriteLine("请输入巡检内容（危化品存储、消防通道等）:");
            Console.Write("> ");
            var input = Console.ReadLine() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(input))
            {
                Console.WriteLine("输入不能为空！");
                return;
            }

            // 步骤1: RAG检索化工合规知识
            Console.WriteLine("\n🔍 正在检索相关合规法规...");
            var searchResults = await _kbService.RetrieveAsync(input, 5);

            if (searchResults.Count == 0)
            {
                Console.WriteLine("⚠️ 未找到相关法规");
            }
            else
            {
                Console.WriteLine($"✅ 找到 {searchResults.Count} 条相关法规:");
                for (int i = 0; i < searchResults.Count; i++)
                {
                    var result = searchResults[i];
                    Console.WriteLine($"\n{i + 1}. 得分: {result.Score:F4}");
                    if (result.Metadata.ContainsKey("RegulationType"))
                        Console.WriteLine($"   类型: {result.Metadata["RegulationType"]}");
                    Console.WriteLine($"   内容: {(result.Content ?? "").Substring(0, Math.Min(100, (result.Content ?? "").Length))}...");
                }
            }

            // 步骤2: 构建Prompt给LLM
            var prompt = BuildCompliancePrompt(input, searchResults);

            // 步骤3: LLM生成审核结论（InvokeStreamAsync 已流式输出，不重复打印）
            Console.WriteLine("\n🤖 正在生成合规审核结论...");
            await _llmService.InvokeStreamAsync(prompt, ConsoleColor.Cyan);
            Console.WriteLine("\n✅ 审核完成");

            // 步骤4: 记录审计日志
            await _auditService.LogOperationAsync(
                "default-user",
                "合规自查",
                $"巡检内容: {input.Substring(0, Math.Min(50, input.Length))}...",
                isSensitive: true
            );

            Console.WriteLine("\n✅ 操作已记录审计日志");
        }

        private string BuildCompliancePrompt(string userInput, List<RetrievedChunk> references)
        {
            var sb = new StringBuilder();
            sb.AppendLine("你是化工园区危化品合规审核专家，请根据以下参考法规对用户的巡检内容进行合规审核:");
            sb.AppendLine();
            sb.AppendLine("参考法规:");

            // 只取前 3 条，每条最多 400 字符（控制 Prompt 总长）
            int maxRefs = Math.Min(references.Count, 3);
            for (int i = 0; i < maxRefs; i++)
            {
                var content = references[i].Content ?? "";
                if (content.Length > 400)
                    content = content.Substring(0, 400) + "...";
                sb.AppendLine($"{i + 1}. {content}");
            }
            sb.AppendLine();
            sb.AppendLine("用户巡检内容:");
            sb.AppendLine(userInput);
            sb.AppendLine();
            sb.AppendLine("直接输出审核结论，不要输出推理过程。请按以下格式简洁回答:");
            sb.AppendLine("1. 是否合规: [是/否]");
            sb.AppendLine("2. 法规依据: [引用条款编号]");
            sb.AppendLine("3. 整改建议: [简短说明，最多2句]");

            return sb.ToString();
        }
    }
}
