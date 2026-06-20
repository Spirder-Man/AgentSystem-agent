
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using Agent1.Config;
using Agent1.Models;

namespace Agent1.Services
{
    /// <summary>
    /// 化工领域记忆服务（Phase 1.1: 会话作用域改造）。
    /// 内部按 sessionId 隔离数据，解决单例模式下用户数据泄漏问题。
    /// - 用户信息提取与缓存
    /// - 工具执行事实缓存
    /// - 多轮上下文压缩（对话摘要）
    /// - Token 感知上下文卸载
    /// </summary>
    public class MemoryService : IMemoryService
    {
        private readonly ILlmService? _llmService;
        private readonly ConcurrentDictionary<string, SessionMemoryData> _sessions = new();
        private string _currentSessionId = "__default__";
        private static readonly object _lock = new();

        // ── 上下文压缩阈值 ──
        // [P3] 从配置读取压缩/保留参数
        private static int CompressTriggerTurns => AppConfig.Instance.Memory.CompressTriggerTurns;
        private static int KeepRecentTurns => AppConfig.Instance.Memory.KeepRecentTurns;

        public MemoryService(ILlmService? llmService = null)
        {
            _llmService = llmService;
        }

        /// <summary>
        /// Phase 1.1: 切换到指定会话的作用域。
        /// 首次访问自动创建该会话的隔离数据空间。
        /// </summary>
        public void SetSession(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                _currentSessionId = "__default__";
            else
                _currentSessionId = sessionId;

            _sessions.GetOrAdd(_currentSessionId, _ => new SessionMemoryData());
        }

        /// <summary>获取当前会话的隔离数据（确保非 null）</summary>
        private SessionMemoryData Current =>
            _sessions.GetOrAdd(_currentSessionId, _ => new SessionMemoryData());

        public string? TryAnswerFromMemory(string userInput)
        {
            var data = Current;
            var lower = userInput.ToLower();

            // 用户信息查询
            if (lower.Contains("我叫") || lower.Contains("名字") || lower.Contains("我是谁"))
            {
                if (!string.IsNullOrEmpty(data.UserProfile.UserName))
                {
                    if (!string.IsNullOrEmpty(data.UserProfile.AssistantName))
                        return $"你叫 {data.UserProfile.UserName}，我叫 {data.UserProfile.AssistantName}！";
                    return $"你叫 {data.UserProfile.UserName}！";
                }
            }
            if (lower.Contains("你叫") || lower.Contains("你是谁"))
            {
                if (!string.IsNullOrEmpty(data.UserProfile.AssistantName))
                {
                    if (!string.IsNullOrEmpty(data.UserProfile.UserName))
                        return $"我叫 {data.UserProfile.AssistantName}，你叫 {data.UserProfile.UserName}！";
                    return $"我叫 {data.UserProfile.AssistantName}！";
                }
            }

            // Phase 2c: 化工领域事实缓存查询
            if (data.KeyFacts.Count == 0) return null;

            var matchingFacts = new List<string>();
            foreach (var kv in data.KeyFacts)
            {
                var keyLower = kv.Key.ToLower();
                if (lower.Contains(keyLower) || keyLower.Contains(lower))
                {
                    matchingFacts.Add($"- {kv.Value}");
                }
            }

            if (matchingFacts.Count > 0)
            {
                return $"🧠 从记忆中匹配到 {matchingFacts.Count} 条已知信息：\n{string.Join("\n", matchingFacts)}";
            }

            return null;
        }

        public void ExtractAndStoreKeyFacts(string userInput, string assistantResponse)
        {
            var data = Current;
            var lowerInput = userInput.ToLower();

            if (lowerInput.Contains("我叫") || lowerInput.Contains("我是"))
            {
                var idx = lowerInput.IndexOf("我叫");
                if (idx < 0) idx = lowerInput.IndexOf("我是");
                
                if (idx >= 0)
                {
                    var nameStart = idx + 2;
                    if (nameStart < userInput.Length)
                    {
                        var nameEnd = userInput.IndexOf("，", nameStart);
                        if (nameEnd < 0) nameEnd = userInput.IndexOf("。", nameStart);
                        if (nameEnd < 0) nameEnd = userInput.Length;
                        
                        var name = userInput.Substring(nameStart, nameEnd - nameStart).Trim();
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            data.UserProfile.UserName = name;
                        }
                    }
                }
            }

            if (lowerInput.Contains("你叫"))
            {
                var idx = lowerInput.IndexOf("你叫");
                if (idx >= 0)
                {
                    var nameStart = idx + 2;
                    if (nameStart < userInput.Length)
                    {
                        var nameEnd = userInput.IndexOf("，", nameStart);
                        if (nameEnd < 0) nameEnd = userInput.IndexOf("。", nameStart);
                        if (nameEnd < 0) nameEnd = userInput.Length;
                        
                        var name = userInput.Substring(nameStart, nameEnd - nameStart).Trim();
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            data.UserProfile.AssistantName = name;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Phase 2c: 从化工工具执行结果中提取领域事实，存入 _keyFacts。
        /// 键格式: "物质名_事实类型" (如 "苯_HazardCategory")，值格式: "类型+法规"
        /// </summary>
        public void StoreToolFacts(string userInput, IReadOnlyDictionary<string, string> toolResults)
        {
            var data = Current;
            foreach (var kv in toolResults)
            {
                var toolName = kv.Key;
                var result = kv.Value ?? "";

                if (toolName.Contains("HazardCategory") || toolName.Contains("CheckHazard"))
                {
                    var parts = result.Split('，', '。', ';');
                    if (parts.Length >= 1)
                    {
                        var substance = ExtractSubstance(userInput);
                        if (!string.IsNullOrEmpty(substance))
                        {
                            data.KeyFacts[substance] = $"危险类别: {result}";
                        }
                    }
                }
                else if (toolName.Contains("StorageCompatibility") || toolName.Contains("CheckStorage"))
                {
                    var substances = ExtractTwoSubstances(userInput);
                    if (!string.IsNullOrEmpty(substances.Item1) && !string.IsNullOrEmpty(substances.Item2))
                    {
                        data.KeyFacts[$"{substances.Item1}+{substances.Item2}"] = $"储存兼容性: {result}";
                    }
                }
                else if (toolName.Contains("SafetyDistance") || toolName.Contains("GetSafety"))
                {
                    var facility = ExtractFacility(userInput);
                    if (!string.IsNullOrEmpty(facility))
                    {
                        data.KeyFacts[facility] = $"安全间距: {result}";
                    }
                }
            }

            if (data.KeyFacts.Count > 0)
            {
                Console.WriteLine($"   🧠 记忆[{_currentSessionId[..Math.Min(8, _currentSessionId.Length)]}]: 已缓存 {data.KeyFacts.Count} 条领域事实");
            }
        }

        // 从用户输入中提取物质名（简单规则：常见化工品名）
        private static string ExtractSubstance(string input)
        {
            string[] candidates = { "苯", "甲苯", "丙酮", "甲醇", "乙醇", "硫酸", "盐酸", "硝酸",
                                    "过氧化氢", "氢氧化钠", "液氨", "氯气", "乙炔", "甲烷", "氢气",
                                    "汽油", "柴油", "甲醛", "苯酚", "乙酸" };
            foreach (var c in candidates)
                if (input.Contains(c)) return c;
            return "";
        }

        private static (string, string) ExtractTwoSubstances(string input)
        {
            var subs = new List<string>();
            string[] candidates = { "苯", "甲苯", "丙酮", "甲醇", "乙醇", "硫酸", "盐酸", "硝酸",
                                    "过氧化氢", "氢氧化钠", "液氨", "氯气", "乙炔", "甲烷", "氢气",
                                    "汽油", "柴油", "甲醛", "苯酚", "乙酸" };
            foreach (var c in candidates)
                if (input.Contains(c)) subs.Add(c);
            return subs.Count >= 2 ? (subs[0], subs[1]) : (subs.Count == 1 ? (subs[0], "") : ("", ""));
        }

        private static string ExtractFacility(string input)
        {
            if (input.Contains("储罐") || input.Contains("罐区")) return "储罐";
            if (input.Contains("消防通道") || input.Contains("消防")) return "消防通道";
            if (input.Contains("仓库") || input.Contains("库房")) return "仓库";
            if (input.Contains("厂房")) return "厂房";
            return "";
        }

        public void ClearMemory()
        {
            var data = Current;
            data.UserProfile.UserName = string.Empty;
            data.UserProfile.JobTitle = string.Empty;
            data.UserProfile.AssistantName = string.Empty;
            data.KeyFacts.Clear();
            data.DialogueTurns.Clear();
            data.CompressedSummary = "";
            data.CompressedUpToTurn = 0;
            data.OffloadedFiles.Clear();
        }

        public void StoreDialogueTurn(string userInput, string assistantResponse)
        {
            var data = Current;
            data.DialogueTurns.Add(new DialogueTurn
            {
                UserInput = userInput,
                AssistantResponse = TruncateForMemory(assistantResponse, 500),
                Timestamp = DateTime.Now
            });
        }

        public int GetDialogueTurnCount()
        {
            var data = Current;
            return data.DialogueTurns.Count;
        }

        public async Task<string> GetConversationContextAsync(int maxRecentTurns = 5)
        {
            var data = Current;
            // 检查是否需要压缩
            var uncompressedTurns = data.DialogueTurns.Count - data.CompressedUpToTurn;
            if (uncompressedTurns >= CompressTriggerTurns)
            {
                await CompressDialogueAsync();
            }

            var sb = new StringBuilder();

            // 1. 压缩摘要（旧对话）
            if (!string.IsNullOrWhiteSpace(data.CompressedSummary))
            {
                sb.AppendLine("【对话历史摘要】");
                sb.AppendLine(data.CompressedSummary);
                sb.AppendLine();
            }

            // 2. 缓存的事实信息
            if (data.KeyFacts.Count > 0)
            {
                sb.AppendLine("【已知领域事实】");
                foreach (var kv in data.KeyFacts.Take(10))
                    sb.AppendLine($"- {kv.Key}: {TruncateForMemory(kv.Value, 200)}");
                sb.AppendLine();
            }

            // 3. 最近N轮原始对话（未压缩部分）
            var recentTurns = data.DialogueTurns.Skip(data.CompressedUpToTurn).TakeLast(maxRecentTurns).ToList();
            if (recentTurns.Count > 0)
            {
                sb.AppendLine("【最近对话】");
                foreach (var turn in recentTurns)
                {
                    sb.AppendLine($"用户: {turn.UserInput}");
                    sb.AppendLine($"助手: {turn.AssistantResponse}");
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// 调用 LLM 将旧对话压缩为结构化摘要。
        /// 策略：保留化工关键信息（物质名、法规编号、合规结论），丢弃闲聊。
        /// </summary>
        private async Task CompressDialogueAsync()
        {
            var data = Current;
            var turnsToCompress = data.DialogueTurns
                .Skip(data.CompressedUpToTurn)
                .Take(data.DialogueTurns.Count - data.CompressedUpToTurn - KeepRecentTurns)
                .ToList();

            if (turnsToCompress.Count == 0) return;

            if (_llmService != null)
            {
                try
                {
                    var dialogueText = new StringBuilder();
                    foreach (var turn in turnsToCompress)
                    {
                        dialogueText.AppendLine($"Q: {turn.UserInput}");
                        dialogueText.AppendLine($"A: {turn.AssistantResponse}");
                    }

                    var prompt = $"""
                        你是化工合规领域的对话摘要助手。请将以下对话压缩为结构化摘要，保留关键信息：
                        - 涉及的化学品名称和查询类别
                        - 引用的法规编号（GB/T 等）
                        - 合规判断结论
                        - 安全距离等数值信息

                        忽略闲聊和问候语。用中文输出，不超过300字。

                        对话内容：
                        {dialogueText}
                        """;

                    var summary = await _llmService.GenerateSimpleResponseAsync(prompt);
                    if (!string.IsNullOrWhiteSpace(summary))
                    {
                        if (!string.IsNullOrWhiteSpace(data.CompressedSummary))
                            data.CompressedSummary += "\n---\n" + summary;
                        else
                            data.CompressedSummary = summary;

                        data.CompressedUpToTurn += turnsToCompress.Count;
                        Console.WriteLine($"   🧠 上下文压缩: {turnsToCompress.Count} 轮 → {summary.Length} 字符摘要 (累计 {data.CompressedUpToTurn}/{data.DialogueTurns.Count} 轮已压缩)");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ⚠️ 上下文压缩失败: {ex.Message}");
                }
            }
            else
            {
                // 无 LLM 时的简单降级：只保留每轮的关键词
                var keywords = new List<string>();
                foreach (var turn in turnsToCompress)
                {
                    var combined = turn.UserInput + " " + turn.AssistantResponse;
                    var regs = System.Text.RegularExpressions.Regex.Matches(combined, @"GB\s*/?T?\s*\d{4,}[.\-]?\d*");
                    foreach (System.Text.RegularExpressions.Match m in regs)
                        keywords.Add(m.Value);
                }

                if (keywords.Count > 0)
                {
                    var fallbackSummary = $"涉及法规: {string.Join(", ", keywords.Distinct().Take(10))}";
                    if (!string.IsNullOrWhiteSpace(data.CompressedSummary))
                        data.CompressedSummary += "\n---\n" + fallbackSummary;
                    else
                        data.CompressedSummary = fallbackSummary;

                    data.CompressedUpToTurn += turnsToCompress.Count;
                    Console.WriteLine($"   🧠 上下文压缩(无LLM降级): {turnsToCompress.Count} 轮 → 关键词提取 (累计 {data.CompressedUpToTurn}/{data.DialogueTurns.Count} 轮已压缩)");
                }
            }
        }

        public Dictionary<string, string> GetKeyFacts()
        {
            var data = Current;
            return data.KeyFacts;
        }

        public UserProfile GetUserProfile()
        {
            var data = Current;
            return data.UserProfile;
        }

        /// <summary>
        /// Phase 1.2: 估算当前会话上下文的 token 数量。
        /// 中文约 1.5 字符/token，英文约 4 字符/token（取保守 1.5）。
        /// </summary>
        public int EstimateContextTokens()
        {
            var data = Current;
            int chars = 0;

            // 压缩摘要
            if (!string.IsNullOrWhiteSpace(data.CompressedSummary))
                chars += data.CompressedSummary.Length;

            // 领域事实
            foreach (var kv in data.KeyFacts.Take(10))
                chars += kv.Key.Length + kv.Value.Length;

            // 最近对话轮次
            var recentTurns = data.DialogueTurns.Skip(data.CompressedUpToTurn).TakeLast(KeepRecentTurns);
            foreach (var turn in recentTurns)
                chars += turn.UserInput.Length + turn.AssistantResponse.Length;

            return (int)(chars / 1.5);
        }

        /// <summary>
        /// Phase 1.3: 卸载大型工具结果到文件，返回压缩后的引用文本。
        /// 阈值：≥1000 字符触发卸载，≤200 字符保留在内存。
        /// </summary>
        public string OffloadLargeResult(string toolName, string result)
        {
            if (string.IsNullOrWhiteSpace(result) || result.Length < 1000)
                return result;

            var data = Current;
            var offloadDir = Path.Combine("memory", "offloads", _currentSessionId);
            Directory.CreateDirectory(offloadDir);

            var safeName = SanitizeFileName(toolName);
            var fileName = $"{safeName}_{DateTime.Now:yyyyMMddHHmmss}.txt";
            var filePath = Path.Combine(offloadDir, fileName);

            File.WriteAllText(filePath, result);
            data.OffloadedFiles[filePath] = toolName;

            var preview = result.Length > 200 ? result.Substring(0, 200) : result;
            Console.WriteLine($"   📦 上下文卸载: {toolName} ({result.Length} 字符) → {filePath}");

            return $"[工具结果已卸载: ./{filePath}，{result.Length}字符，预览: {preview}...]";
        }

        public SessionMemoryStats GetSessionStats()
        {
            var data = Current;
            return new SessionMemoryStats
            {
                SessionId = _currentSessionId,
                TurnCount = data.DialogueTurns.Count,
                CompressedTurnCount = data.CompressedUpToTurn,
                EstimatedTokens = EstimateContextTokens(),
                CompressedSummaryLength = data.CompressedSummary.Length,
                KeyFactsCount = data.KeyFacts.Count,
                OffloadedFilesCount = data.OffloadedFiles.Count
            };
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = new StringBuilder(name.Length);
            foreach (var c in name)
                sanitized.Append(invalid.Contains(c) ? '_' : c);
            return sanitized.Length > 50 ? sanitized.ToString(0, 50) : sanitized.ToString();
        }

        private static string TruncateForMemory(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
        }
    }

    /// <summary>Phase 1.1: 每个会话的隔离记忆数据</summary>
    internal class SessionMemoryData
    {
        public UserProfile UserProfile { get; } = new();
        public Dictionary<string, string> KeyFacts { get; } = new();
        public List<DialogueTurn> DialogueTurns { get; } = new();
        public string CompressedSummary { get; set; } = "";
        public int CompressedUpToTurn { get; set; }
        public Dictionary<string, string> OffloadedFiles { get; } = new();
    }

    /// <summary>对话轮次记录</summary>
    public class DialogueTurn
    {
        public string UserInput { get; set; } = "";
        public string AssistantResponse { get; set; } = "";
        public DateTime Timestamp { get; set; }
    }

    /// <summary>Phase 1.4: 短期记忆统计快照</summary>
    public class SessionMemoryStats
    {
        public string SessionId { get; set; } = "";
        public int TurnCount { get; set; }
        public int CompressedTurnCount { get; set; }
        public int EstimatedTokens { get; set; }
        public int CompressedSummaryLength { get; set; }
        public int KeyFactsCount { get; set; }
        public int OffloadedFilesCount { get; set; }
    }
}

