
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agent1
{
    public class DialogTurn
    {
        public string Role { get; set; }
        public string Content { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public Dictionary<string, string> ToolCalls { get; set; } = new Dictionary<string, string>();
    }

    public class SessionContext
    {
        public string SessionId { get; set; } = Guid.NewGuid().ToString();
        public string UserPromptTemplate { get; set; } = string.Empty;
        public List<DialogTurn> DialogHistory { get; set; } = new List<DialogTurn>();
        public Dictionary<string, object> CustomContext { get; set; } = new Dictionary<string, object>();
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime LastUpdated { get; set; } = DateTime.Now;
        public SessionType SessionType { get; set; } = SessionType.General;
    }

    public enum SessionType
    {
        General,
        ChemicalCompliance
    }

    public class SessionManager
    {
        private static readonly Dictionary<string, SessionContext> _sessions = new Dictionary<string, SessionContext>();
        private static readonly object _lock = new object();

        public static SessionContext CreateSession(string? customPrompt = null, SessionType type = SessionType.General)
        {
            lock (_lock)
            {
                var session = new SessionContext
                {
                    SessionType = type
                };
                if (!string.IsNullOrEmpty(customPrompt))
                {
                    session.UserPromptTemplate = customPrompt;
                }
                _sessions[session.SessionId] = session;
                return session;
            }
        }

        public static SessionContext? GetSession(string sessionId)
        {
            lock (_lock)
            {
                _sessions.TryGetValue(sessionId, out var session);
                return session;
            }
        }

        public static void UpdateSession(SessionContext session)
        {
            lock (_lock)
            {
                session.LastUpdated = DateTime.Now;
                _sessions[session.SessionId] = session;
            }
        }

        public static void AddDialogTurn(string sessionId, string role, string content, Dictionary<string, string>? toolCalls = null)
        {
            lock (_lock)
            {
                if (_sessions.TryGetValue(sessionId, out var session))
                {
                    // ⭐ 在保存前清理内容
                    string cleanedContent = CleanContent(content);

                    session.DialogHistory.Add(new DialogTurn
                    {
                        Role = role,
                        Content = cleanedContent,
                        ToolCalls = toolCalls ?? new Dictionary<string, string>(),
                        Timestamp = DateTime.Now
                    });
                    session.LastUpdated = DateTime.Now;
                }
            }
        }

        // ⭐ 新增：内容清理函数
        private static string CleanContent(string content)
        {
            if (string.IsNullOrEmpty(content))
                return content;

            // 移除 <think> 标记
            content = content.Replace("<think>", "").Replace("</think>", "");

            // 逐行清理
            var lines = content.Split('\n');
            var cleanedLines = new List<string>();
            
            foreach (var line in lines)
            {
                string cleaned = line;
                
                // 移除行首编号（如 "5. "）
                cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"^\s*\d+\.\s*", "");
                
                // 移除标记（如 "【内容】"）
                cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"【.*?】\s*", "");
                
                // 移除选项标签（如 "A)", "B)"）
                cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"^\s*[A-Z]\)\s*", "");
                
                // 移除多余的空格
                cleaned = cleaned.Trim();
                
                if (!string.IsNullOrWhiteSpace(cleaned))
                {
                    cleanedLines.Add(cleaned);
                }
            }

            // 合并结果
            content = string.Join("\n", cleanedLines);

            // 移除多余空行
            content = System.Text.RegularExpressions.Regex.Replace(content, @"\n\s*\n", "\n");

            return content.Trim();
        }

        public static string GetFormattedHistory(string sessionId, int maxTurns = 10)
        {
            lock (_lock)
            {
                if (_sessions.TryGetValue(sessionId, out var session))
                {
                    var recentTurns = session.DialogHistory.TakeLast(maxTurns).ToList();
                    var historyBuilder = new StringBuilder();
                    historyBuilder.AppendLine("【对话历史记录】");
                    foreach (var turn in recentTurns)
                    {
                        string roleLabel = turn.Role == "User" ? "👤 用户" : "🤖 助手";
                        historyBuilder.AppendLine($"{roleLabel} [{turn.Timestamp:HH:mm:ss}]");
                        // ⭐ 显示时再次清理
                        historyBuilder.AppendLine(CleanContent(turn.Content));
                        historyBuilder.AppendLine("────────────────────────────────────────");
                    }
                    return historyBuilder.ToString();
                }
                return string.Empty;
            }
        }

        public static string GetContextSummary(string sessionId, int maxTurns = 5)
        {
            lock (_lock)
            {
                if (_sessions.TryGetValue(sessionId, out var session))
                {
                    var recentTurns = session.DialogHistory.TakeLast(maxTurns).ToList();
                    var summary = new StringBuilder();
                    summary.AppendLine("【对话上下文】");
                    foreach (var turn in recentTurns)
                    {
                        // ⭐ 给 LLM 的 Prompt 也清理内容
                        summary.AppendLine($"{turn.Role}: {CleanContent(turn.Content)}");
                    }
                    return summary.ToString();
                }
                return "【无历史对话】";
            }
        }

        public static void ClearHistory(string sessionId)
        {
            lock (_lock)
            {
                if (_sessions.TryGetValue(sessionId, out var session))
                {
                    session.DialogHistory.Clear();
                    session.LastUpdated = DateTime.Now;
                }
            }
        }

        public static void CleanupExpiredSessions(TimeSpan timeout)
        {
            lock (_lock)
            {
                var expiredIds = _sessions.Where(kv => DateTime.Now - kv.Value.LastUpdated > timeout)
                                         .Select(kv => kv.Key)
                                         .ToList();
                foreach (var id in expiredIds)
                {
                    _sessions.Remove(id);
                }
            }
        }

        public static int GetHistoryCount(string sessionId)
        {
            lock (_lock)
            {
                if (_sessions.TryGetValue(sessionId, out var session))
                {
                    return session.DialogHistory.Count;
                }
                return 0;
            }
        }

        public static void ClearDialogHistory(string sessionId)
        {
            lock (_lock)
            {
                if (_sessions.TryGetValue(sessionId, out var session))
                {
                    session.DialogHistory.Clear();
                }
            }
        }
    }
}

