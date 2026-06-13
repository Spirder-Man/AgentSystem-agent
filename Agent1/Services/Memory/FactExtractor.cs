using Agent1.Models;
using System.Text;
using System.Text.Json;

namespace Agent1.Services
{
    /// <summary>
    /// Phase 2.2: LLM 驱动的领域事实提取器。
    /// 从化工合规对话中提取可沉淀为长期记忆的结构化事实。
    /// </summary>
    public class FactExtractor
    {
        private readonly ILlmService _llmService;

        public FactExtractor(ILlmService llmService)
        {
            _llmService = llmService;
        }

        /// <summary>
        /// 从一轮对话中提取长期记忆候选事实。
        /// 返回 JSON 解析的 ExtractedFact 列表，失败时返回空列表。
        /// </summary>
        public async Task<List<ExtractedFact>> ExtractFactsAsync(
            string userInput, string assistantResponse,
            IReadOnlyDictionary<string, string>? toolResults = null)
        {
            var toolInfo = "";
            if (toolResults != null && toolResults.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine();
                sb.AppendLine("工具执行结果：");
                foreach (var kv in toolResults)
                    sb.AppendLine($"- {kv.Key}: {Truncate(kv.Value, 300)}");
                toolInfo = sb.ToString();
            }

            var prompt = BuildExtractionPrompt(userInput, assistantResponse, toolInfo);

            try
            {
                var rawResponse = await _llmService.GenerateSimpleResponseAsync(prompt, 1024);
                if (string.IsNullOrWhiteSpace(rawResponse)) return new List<ExtractedFact>();

                var cleaned = CleanJsonResponse(rawResponse);
                var facts = JsonSerializer.Deserialize<List<ExtractedFact>>(cleaned,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return facts ?? new List<ExtractedFact>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ 事实提取失败: {ex.Message}");
                return new List<ExtractedFact>();
            }
        }

        private static string BuildExtractionPrompt(string userInput, string assistantResponse, string toolInfo)
        {
            var sb = new StringBuilder();
            sb.AppendLine("你是化工合规领域的知识提取专家。请从以下对话中提取可沉淀为长期记忆的事实。");
            sb.AppendLine("提取规则：");
            sb.AppendLine("1. 用户偏好（type: user_preference）：如'我主要关注甲类仓库'");
            sb.AppendLine("2. 化工物质关键属性（type: chemical_fact）：如'苯的闪点为-11°C，CAS:71-43-2'");
            sb.AppendLine("3. 合规判断经验（type: compliance_experience）：如'氧化剂与易燃液体严禁同库储存'");
            sb.AppendLine("4. 法规引用（type: regulation_ref）：如'GB15603-2022 第5.3.2条规定氧化剂隔离存放'");
            sb.AppendLine();
            sb.AppendLine("重要性评分规则（0-1）：");
            sb.AppendLine("- 法规引用: 0.8-1.0");
            sb.AppendLine("- 物质属性: 0.6-0.8");
            sb.AppendLine("- 合规经验: 0.5-0.7");
            sb.AppendLine("- 用户偏好: 0.3-0.5");
            sb.AppendLine();
            sb.AppendLine("只提取有实质内容的事实，忽略闲聊和问候语。输出纯JSON数组，不要Markdown代码块。");
            sb.AppendLine();
            sb.AppendLine("对话内容：");
            sb.AppendLine($"用户: {userInput}");
            sb.AppendLine($"助手: {Truncate(assistantResponse, 2000)}");
            if (!string.IsNullOrWhiteSpace(toolInfo))
                sb.Append(toolInfo);
            sb.AppendLine();
            sb.AppendLine("输出格式示例：");
            sb.AppendLine("[{\"type\":\"regulation_ref\",\"content\":\"GB15603-2022 第5.3.2条\",\"importance\":0.9}]");

            return sb.ToString();
        }

        private static string CleanJsonResponse(string raw)
        {
            var cleaned = raw.Trim();
            // 移除 Markdown 代码块标记
            if (cleaned.StartsWith("```"))
            {
                var end = cleaned.IndexOf('\n');
                if (end > 0) cleaned = cleaned.Substring(end + 1);
                if (cleaned.EndsWith("```"))
                    cleaned = cleaned.Substring(0, cleaned.Length - 3);
                cleaned = cleaned.Trim();
            }
            return cleaned;
        }

        private static string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
        }
    }
}
