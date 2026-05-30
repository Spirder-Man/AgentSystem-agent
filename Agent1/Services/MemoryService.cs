
using System.Collections.Generic;

namespace Agent1.Services
{
    public class MemoryService : IMemoryService
    {
        private readonly UserProfile _userProfile;
        private readonly Dictionary<string, string> _keyFacts;

        public MemoryService()
        {
            _userProfile = new UserProfile();
            _keyFacts = new Dictionary<string, string>();
        }

        public string? TryAnswerFromMemory(string userInput)
        {
            var lower = userInput.ToLower();

            // 用户信息查询
            if (lower.Contains("我叫") || lower.Contains("名字") || lower.Contains("我是谁"))
            {
                if (!string.IsNullOrEmpty(_userProfile.UserName))
                {
                    if (!string.IsNullOrEmpty(_userProfile.AssistantName))
                        return $"你叫 {_userProfile.UserName}，我叫 {_userProfile.AssistantName}！";
                    return $"你叫 {_userProfile.UserName}！";
                }
            }
            if (lower.Contains("你叫") || lower.Contains("你是谁"))
            {
                if (!string.IsNullOrEmpty(_userProfile.AssistantName))
                {
                    if (!string.IsNullOrEmpty(_userProfile.UserName))
                        return $"我叫 {_userProfile.AssistantName}，你叫 {_userProfile.UserName}！";
                    return $"我叫 {_userProfile.AssistantName}！";
                }
            }

            // Phase 2c: 化工领域事实缓存查询
            if (_keyFacts.Count == 0) return null;

            var matchingFacts = new List<string>();
            foreach (var kv in _keyFacts)
            {
                var keyLower = kv.Key.ToLower();
                // 检查用户的输入是否包含某个已知物质名或设施名
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
            var lowerInput = userInput.ToLower();
            var lowerResponse = assistantResponse.ToLower();

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
                            _userProfile.UserName = name;
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
                            _userProfile.AssistantName = name;
                        }
                    }
                }
            }

            // Phase 2c: 化工合规领域事实由 StoreToolFacts 统一提取，此处不再处理
        }

        /// <summary>
        /// Phase 2c: 从化工工具执行结果中提取领域事实，存入 _keyFacts。
        /// 键格式: "物质名_事实类型" (如 "苯_HazardCategory")，值格式: "类型+法规"
        /// </summary>
        public void StoreToolFacts(string userInput, IReadOnlyDictionary<string, string> toolResults)
        {
            foreach (var kv in toolResults)
            {
                var toolName = kv.Key;
                var result = kv.Value ?? "";

                if (toolName.Contains("HazardCategory") || toolName.Contains("CheckHazard"))
                {
                    // 尝试从结果中提取: "苯属于易燃液体，危险类别3，适用标准 GB 30000.7-2013"
                    var parts = result.Split('，', '。', ';');
                    if (parts.Length >= 1)
                    {
                        var substance = ExtractSubstance(userInput);
                        if (!string.IsNullOrEmpty(substance))
                        {
                            _keyFacts[substance] = $"危险类别: {result}";
                        }
                    }
                }
                else if (toolName.Contains("StorageCompatibility") || toolName.Contains("CheckStorage"))
                {
                    var substances = ExtractTwoSubstances(userInput);
                    if (!string.IsNullOrEmpty(substances.Item1) && !string.IsNullOrEmpty(substances.Item2))
                    {
                        _keyFacts[$"{substances.Item1}+{substances.Item2}"] = $"储存兼容性: {result}";
                    }
                }
                else if (toolName.Contains("SafetyDistance") || toolName.Contains("GetSafety"))
                {
                    var facility = ExtractFacility(userInput);
                    if (!string.IsNullOrEmpty(facility))
                    {
                        _keyFacts[facility] = $"安全间距: {result}";
                    }
                }
            }

            if (_keyFacts.Count > 0)
            {
                Console.WriteLine($"   🧠 记忆: 已缓存 {_keyFacts.Count} 条领域事实");
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
            _userProfile.UserName = string.Empty;
            _userProfile.JobTitle = string.Empty;
            _userProfile.AssistantName = string.Empty;
            _keyFacts.Clear();
        }

        public Dictionary<string, string> GetKeyFacts() => _keyFacts;

        public UserProfile GetUserProfile() => _userProfile;
    }
}

