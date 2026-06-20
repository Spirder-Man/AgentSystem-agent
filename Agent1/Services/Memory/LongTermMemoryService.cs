using Agent1.Models;

namespace Agent1.Services
{
    /// <summary>
    /// Phase 2-3: 长期记忆服务实现。
    /// Record: FactExtractor → Embedding → pgvector
    /// Retrieve: Embedding → pgvector 语义检索 → 结果注入
    /// </summary>
    public class LongTermMemoryService : ILongTermMemoryService
    {
        private readonly IDatabaseService _db;
        private readonly ILlmService _llm;
        private readonly FactExtractor _extractor;

        public LongTermMemoryService(IDatabaseService db, ILlmService llm)
        {
            _db = db;
            _llm = llm;
            _extractor = new FactExtractor(llm);
        }

        // ═══ Record 管道 ═══

        public async Task<List<LongTermMemoryRecord>> RecordAsync(
            string userId, string userInput, string assistantResponse,
            Guid? sourceSessionId = null, int sourceTurnIndex = 0,
            IReadOnlyDictionary<string, string>? toolResults = null)
        {
            var facts = await _extractor.ExtractFactsAsync(userInput, assistantResponse, toolResults);
            var records = new List<LongTermMemoryRecord>();

            foreach (var fact in facts)
            {
                try
                {
                    // 生成向量嵌入
                    var embedding = await GenerateEmbeddingAsync(fact.Content);

                    // 冲突解决：停用旧版本
                    await _db.DeactivateConflictingMemoriesAsync(userId, fact.Type, fact.Content);

                    var record = new LongTermMemoryRecord
                    {
                        UserId = userId,
                        MemoryType = fact.Type,
                        Content = fact.Content,
                        Embedding = embedding,
                        SourceSessionId = sourceSessionId,
                        SourceTurnIndex = sourceTurnIndex,
                        Importance = Math.Clamp(fact.Importance, 0f, 1f),
                    };

                    await _db.AddLongTermMemoryAsync(record);
                    records.Add(record);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ⚠️ 记忆记录失败 [{fact.Type}]: {ex.Message}");
                }
            }

            if (records.Count > 0)
                Console.WriteLine($"   🧠 长期记忆: 提取 {facts.Count} 条事实，持久化 {records.Count} 条");

            return records;
        }

        public async Task AddMemoryAsync(LongTermMemoryRecord record)
        {
            if (record.Embedding == null)
                record.Embedding = await GenerateEmbeddingAsync(record.Content);
            await _db.AddLongTermMemoryAsync(record);
        }

        // ═══ Retrieve 管道 ═══

        public async Task<List<LongTermMemoryRecord>> RetrieveAsync(
            string userId, string query, int topK = 5,
            string? memoryTypeFilter = null)
        {
            try
            {
                var queryEmbedding = await _llm.GetEmbeddingAsync(query);
                if (queryEmbedding == null)
                {
                    // 嵌入失败降级：关键词搜索
                    Console.WriteLine("   ⚠️ 向量化失败，降级为关键词搜索");
                    return await SearchByKeywordAsync(userId, query, topK);
                }

                var results = await _db.SearchLongTermMemoriesAsync(userId, queryEmbedding, topK, memoryTypeFilter);

                // 按化工场景优先级二次排序
                results = results
                    .OrderByDescending(r => MemoryTypePriority(r.MemoryType))
                    .ThenByDescending(r => r.Importance)
                    .ThenByDescending(r => r.HitCount)
                    .ToList();

                // 记录命中
                foreach (var r in results.Take(topK))
                {
                    try { await _db.UpdateMemoryHitAsync(r.Id); } catch { /* 非关键：命中计数更新失败不影响检索 */ }
                }

                return results;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ 长期记忆检索失败: {ex.Message}");
                return new List<LongTermMemoryRecord>();
            }
        }

        public async Task<List<LongTermMemoryRecord>> SearchByKeywordAsync(
            string userId, string keyword, int topK = 10)
        {
            try
            {
                var results = await _db.SearchLongTermMemoriesByKeywordAsync(userId, keyword, topK);
                foreach (var r in results.Take(3))
                {
                    try { await _db.UpdateMemoryHitAsync(r.Id); } catch { /* 非关键：命中计数更新失败不影响检索 */ }
                }
                return results;
            }
            catch
            {
                return new List<LongTermMemoryRecord>();
            }
        }

        // ═══ 生命周期管理 ═══

        public async Task RecordHitAsync(Guid memoryId)
        {
            await _db.UpdateMemoryHitAsync(memoryId);
        }

        public async Task DeactivateAsync(Guid memoryId)
        {
            await _db.DeactivateMemoryAsync(memoryId);
        }

        public async Task ResolveConflictsAsync(string userId, string memoryType, string newContent)
        {
            await _db.DeactivateConflictingMemoriesAsync(userId, memoryType, newContent);
        }

        public async Task<int> CleanupAsync(int retentionDays = 180)
        {
            return await _db.CleanupMemoriesAsync(retentionDays);
        }

        // ═══ 统计 ═══

        public async Task<LongTermMemoryStats> GetStatsAsync(string userId)
        {
            return await _db.GetLongTermMemoryStatsAsync(userId);
        }

        // ═══ 私有方法 ═══

        private async Task<float[]?> GenerateEmbeddingAsync(string text)
        {
            try
            {
                return await _llm.GetEmbeddingAsync(text);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ 嵌入生成失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>化工场景记忆类型优先级</summary>
        private static int MemoryTypePriority(string memoryType) => memoryType switch
        {
            "regulation_ref" => 4,
            "chemical_fact" => 3,
            "compliance_experience" => 2,
            "user_preference" => 1,
            _ => 0
        };
    }
}
