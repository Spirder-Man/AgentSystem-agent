using Agent1.Models;

namespace Agent1.Services
{
    /// <summary>
    /// Phase 4.1: 统一记忆协调器 — 编排三套记忆系统的协作。
    /// 
    /// PreInference:
    ///   1. ResponseCacheService.Get(query) → 命中则返回缓存结果
    ///   2. MemoryService.TryAnswerFromMemory() → 命中则返回内存回答
    ///   3. LongTermMemory.RetrieveAsync() → 注入推理上下文
    ///   4. MemoryService.GetConversationContextAsync() → 添加上下文
    /// 
    /// PostInference:
    ///   1. MemoryService.StoreDialogueTurn() → 存储对话轮次
    ///   2. MemoryService.StoreToolFacts() → 提取领域事实
    ///   3. LongTermMemory.RecordAsync() → 异步持久化长期记忆
    ///   4. ResponseCacheService.Set() → 缓存查询结果
    /// </summary>
    public class MemoryCoordinator
    {
        private readonly IMemoryService _shortMemory;
        private readonly ILongTermMemoryService? _longMemory;
        private readonly ResponseCacheService? _cache;
        private readonly IAuditService? _audit;
        private static readonly Dictionary<string, string> AliasMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["烧碱"] = "氢氧化钠", ["火碱"] = "氢氧化钠", ["苛性钠"] = "氢氧化钠",
            ["液氯"] = "氯", ["氨水"] = "氨溶液", ["双氧水"] = "过氧化氢",
            ["酒精"] = "乙醇", ["福尔马林"] = "甲醛", ["醋酸"] = "乙酸",
        };

        public MemoryCoordinator(
            IMemoryService shortMemory,
            ILongTermMemoryService? longMemory = null,
            ResponseCacheService? cache = null,
            IAuditService? audit = null)
        {
            _shortMemory = shortMemory;
            _longMemory = longMemory;
            _cache = cache;
            _audit = audit;
        }

        /// <summary>
        /// 推理前：组装上下文。如果有缓存/内存命中，通过 out 参数返回直接结果。
        /// </summary>
        public async Task<MemoryPreResult> PreInferenceAsync(
            string sessionId, string userId, string query)
        {
            // 1. 切换到当前会话
            _shortMemory.SetSession(sessionId);

            // 2. 查询缓存
            if (_cache != null)
            {
                var cached = _cache.Get(query);
                if (cached != null)
                {
                    Console.WriteLine("   ⚡ 缓存命中");
                    return MemoryPreResult.CacheHit(cached.Response ?? "");
                }
            }

            // 3. 查询短期记忆（关键词匹配）
            var memoryAnswer = _shortMemory.TryAnswerFromMemory(query);
            if (!string.IsNullOrWhiteSpace(memoryAnswer))
            {
                Console.WriteLine("   🧠 短期记忆命中");
                return MemoryPreResult.MemoryHit(memoryAnswer);
            }

            // 4. 查询长期记忆（语义检索，含别名扩展）
            var contextLines = new List<string>();
            if (_longMemory != null)
            {
                try
                {
                    var queries = ExpandQueryWithAliases(query);
                    var allResults = new HashSet<string>();

                    foreach (var q in queries)
                    {
                        var results = await _longMemory.RetrieveAsync(userId, q, topK: 3);
                        foreach (var r in results)
                        {
                            if (allResults.Add(r.Content))
                                contextLines.Add($"[{r.MemoryType}] {r.Content}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ⚠️ 长期记忆检索失败: {ex.Message}");
                }
            }

            // 5. 获取短期对话上下文
            var shortContext = "";
            try
            {
                shortContext = await _shortMemory.GetConversationContextAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ 对话上下文获取失败: {ex.Message}");
            }

            return MemoryPreResult.ContextReady(contextLines, shortContext);
        }

        /// <summary>
        /// 推理后：存储和持久化记忆。
        /// </summary>
        public async Task PostInferenceAsync(
            string sessionId, string userId, string query, string response,
            IReadOnlyDictionary<string, string>? toolResults = null)
        {
            _shortMemory.SetSession(sessionId);

            // 1. 存储对话轮次（触发上下文压缩检查）
            _shortMemory.StoreDialogueTurn(query, response);

            // 2. 从工具结果中提取领域事实
            if (toolResults != null && toolResults.Count > 0)
            {
                _shortMemory.StoreToolFacts(query, toolResults);
            }

            // 3. 异步记录长期记忆（不阻塞响应）
            if (_longMemory != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        Guid? sessionGuid = Guid.TryParse(sessionId, out var g) ? g : null;
                        await _longMemory.RecordAsync(userId, query, response, sessionGuid, 0, toolResults);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"   ⚠️ 长期记忆记录失败: {ex.Message}");
                    }
                });
            }

            // 4. 更新缓存
            if (_cache != null)
            {
                _cache.Set(query, new CachedComplianceResponse
                {
                    Query = query,
                    Response = response,
                    ToolsUsed = toolResults?.Keys.ToList() ?? new List<string>()
                });
            }

            // 5. 审计
            if (_audit != null)
            {
                try
                {
                    var toolsSummary = toolResults != null && toolResults.Count > 0
                        ? string.Join(",", toolResults.Keys)
                        : "无";
                    await _audit.LogOperationAsync(userId, "记忆更新",
                        $"会话: {sessionId[..Math.Min(8, sessionId.Length)]} | 工具: [{toolsSummary}] | 记忆token: {_shortMemory.EstimateContextTokens()}",
                        isSensitive: false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ⚠️ 记忆审计日志写入失败: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Phase 3.3: 用化工别名映射扩展查询词，提升长期记忆召回率。
        /// e.g. "烧碱" → ["烧碱", "氢氧化钠"]
        /// </summary>
        private static List<string> ExpandQueryWithAliases(string query)
        {
            var queries = new List<string> { query };
            foreach (var kv in AliasMap)
            {
                if (query.Contains(kv.Key))
                {
                    var expanded = query.Replace(kv.Key, kv.Value);
                    if (!queries.Contains(expanded))
                        queries.Add(expanded);
                }
                else if (query.Contains(kv.Value))
                {
                    var expanded = query.Replace(kv.Value, kv.Key);
                    if (!queries.Contains(expanded))
                        queries.Add(expanded);
                }
            }
            return queries;
        }
    }

    /// <summary>记忆协调器推理前结果</summary>
    public class MemoryPreResult
    {
        public bool HasDirectAnswer { get; private init; }
        public string DirectAnswer { get; private init; } = "";
        public List<string> LongTermContext { get; private init; } = new();
        public string ShortTermContext { get; private init; } = "";

        private MemoryPreResult() { }

        public static MemoryPreResult CacheHit(string answer)
            => new() { HasDirectAnswer = true, DirectAnswer = answer };

        public static MemoryPreResult MemoryHit(string answer)
            => new() { HasDirectAnswer = true, DirectAnswer = answer };

        public static MemoryPreResult ContextReady(List<string> longTermContext, string shortTermContext)
            => new()
            {
                HasDirectAnswer = false,
                LongTermContext = longTermContext,
                ShortTermContext = shortTermContext
            };
    }
}
