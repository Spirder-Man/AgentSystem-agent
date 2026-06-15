using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Agent1.Config;

namespace Agent1.Services
{
    /// <summary>
    /// Sprint 3: Cross-Encoder Reranker 服务。
    /// 
    /// 流程：BM25+向量粗排 TopK=20 → Reranker精排 → TopK=5
    /// 
    /// 生产环境：调用 Python sidecar 微服务 (bge-reranker-v2-m3)
    /// 降级模式：基于关键词密度 + 段落位置做简易重排序
    /// 
    /// API 约定（Python reranker_server.py）：
    ///   POST /rerank
    ///   { "query": "...", "documents": ["...", "..."], "top_k": 5 }
    ///   → { "results": [{"index": 0, "score": 0.95}, ...] }
    /// </summary>
    public class RerankerService
    {
        private readonly HttpClient _httpClient;
        private readonly VectorSearchConfig _config;
        private bool _remoteAvailable = true;

        public bool IsEnabled => _config.RerankerEnabled;

        public RerankerService(AppConfig config)
        {
            _config = config.VectorSearch;
            _httpClient = new HttpClient(new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                MaxConnectionsPerServer = 2,
                EnableMultipleHttp2Connections = true
            })
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
        }

        /// <summary>
        /// 对粗排候选列表进行精排。
        /// </summary>
        /// <param name="query">原始查询</param>
        /// <param name="candidates">粗排候选 (BM25+向量融合后的 TopK)</param>
        /// <param name="topK">精排后保留数</param>
        /// <returns>精排后的结果列表</returns>
        public async Task<List<RetrievedChunk>> RerankAsync(
            string query,
            List<RetrievedChunk> candidates,
            int topK)
        {
            if (!_config.RerankerEnabled || candidates.Count == 0)
                return candidates.Take(topK).ToList();

            // 候选数不足时跳过 Reranker
            if (candidates.Count <= topK)
                return candidates;

            try
            {
                // 尝试远程 Reranker API
                if (_remoteAvailable)
                {
                    var result = await RemoteRerankAsync(query, candidates, topK);
                    if (result != null)
                        return result;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ Reranker 远程调用失败: {ex.Message}，降级为本地重排序");
                _remoteAvailable = false;
            }

            // 降级：本地启发式重排序
            return LocalHeuristicRerank(query, candidates, topK);
        }

        /// <summary>
        /// 远程 Cross-Encoder Reranker API 调用。
        /// </summary>
        private async Task<List<RetrievedChunk>?> RemoteRerankAsync(
            string query,
            List<RetrievedChunk> candidates,
            int topK)
        {
            var url = _config.RerankerEndpoint;
            var documents = candidates.Select(c => c.Content ?? "").ToList();

            var request = new
            {
                query,
                documents,
                top_k = topK,
                model = _config.RerankerModelId
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, content);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"   ⚠️ Reranker API 返回 {response.StatusCode}");
                return null;
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            var results = doc.RootElement.GetProperty("results");

            var reranked = new List<RetrievedChunk>();
            foreach (var item in results.EnumerateArray())
            {
                var index = item.GetProperty("index").GetInt32();
                var score = item.GetProperty("score").GetDouble();

                if (index >= 0 && index < candidates.Count)
                {
                    var chunk = candidates[index];
                    reranked.Add(new RetrievedChunk
                    {
                        Id = chunk.Id,
                        Content = chunk.Content,
                        Score = score,
                        Rank = reranked.Count,
                        Metadata = chunk.Metadata,
                        RetrievalMethod = (chunk.RetrievalMethod ?? "") + "+Reranker"
                    });
                }
            }

            Console.WriteLine($"   🎯 Reranker精排: {candidates.Count} → {reranked.Count} 条");
            return reranked;
        }

        /// <summary>
        /// 本地启发式重排序（Reranker 不可用时的降级方案）。
        /// 
        /// 策略：
        /// 1. 关键词密度：查询词在文档中出现频率越高，得分越高
        /// 2. 位置加权：查询词在文档前部出现，得分更高
        /// 3. 法规编号精确匹配加分
        /// </summary>
        private List<RetrievedChunk> LocalHeuristicRerank(
            string query,
            List<RetrievedChunk> candidates,
            int topK)
        {
            // 提取查询关键词
            var queryTerms = query
                .Split(new[] { ' ', '，', '。', '？', '！', '、' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length >= 2)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (queryTerms.Count == 0)
                return candidates.Take(topK).ToList();

            var scored = candidates.Select((c, idx) =>
            {
                var content = c.Content ?? "";
                double bonus = 0;

                foreach (var term in queryTerms)
                {
                    int pos = content.IndexOf(term, StringComparison.OrdinalIgnoreCase);
                    while (pos >= 0)
                    {
                        // 位置加权：越靠前权重越高
                        double positionWeight = pos < 100 ? 1.5 : pos < 300 ? 1.2 : 1.0;
                        bonus += positionWeight;

                        pos = content.IndexOf(term, pos + 1, StringComparison.OrdinalIgnoreCase);
                    }
                }

                // 法规编号 GB XXXX 匹配加分
                if (System.Text.RegularExpressions.Regex.IsMatch(content, @"GB\s*/?T?\s*\d{4,}"))
                    bonus += 2.0;

                // 综合得分：原始向量分 * 0.3 + 启发式 * 0.7
                var heuristicScore = c.Score * 0.3 + bonus * 0.1;
                return (Chunk: c, OriginalScore: c.Score, HeuristicScore: heuristicScore, Index: idx);
            })
            .OrderByDescending(x => x.HeuristicScore)
            .Take(topK)
            .Select((x, idx) =>
            {
                x.Chunk.Score = x.HeuristicScore;
                x.Chunk.Rank = idx;
                x.Chunk.RetrievalMethod = (x.Chunk.RetrievalMethod ?? "") + "+LocalRerank";
                return x.Chunk;
            })
            .ToList();

            Console.WriteLine($"   📊 本地重排序: {candidates.Count} → {scored.Count} 条 (降级模式)");
            return scored;
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
