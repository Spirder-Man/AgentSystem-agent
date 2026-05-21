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
                    Console.WriteLine($"   内容: {result.Content.Substring(0, Math.Min(100, result.Content.Length))}...");
                }
            }

            // 步骤2: 构建Prompt给LLM
            var prompt = BuildCompliancePrompt(input, searchResults);

            // 步骤3: LLM生成审核结论
            Console.WriteLine("\n🤖 正在生成合规审核结论...");
            var conclusion = await _llmService.InvokeStreamAsync(prompt, ConsoleColor.Cyan);

            Console.WriteLine("\n========== 审核结论 ==========");
            Console.WriteLine(conclusion);

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
            for (int i = 0; i < references.Count; i++)
            {
                sb.AppendLine($"{i + 1}. {references[i].Content}");
            }
            sb.AppendLine();
            sb.AppendLine("用户巡检内容:");
            sb.AppendLine(userInput);
            sb.AppendLine();
            sb.AppendLine("请按以下格式输出审核结论:");
            sb.AppendLine("1. 是否合规: [是/否]");
            sb.AppendLine("2. 违规点: [如有]");
            sb.AppendLine("3. 法规依据: [引用具体条款]");
            sb.AppendLine("4. 整改建议: [详细说明]");

            return sb.ToString();
        }
    }
}
