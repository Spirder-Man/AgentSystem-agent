
using Agent1.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agent1
{
    /// <summary>
    /// 化工RAG类，用于加载和使用化工知识库。
    /// </summary>
    public class ChemicalRAG
    {
        /// <summary>
        /// 知识库服务实例，用于加载和查询化工知识库。
        /// </summary>
        private readonly IKnowledgeBaseService _knowledgeBase;
        /// <summary>
        /// 知识库路径，包含国标、园区规则和历史案例目录。
        /// </summary>
        private readonly string _knowledgeBasePath;
        /// <summary>
        /// 构造函数，初始化化工RAG实例。
        /// </summary>
        /// <param name="knowledgeBasePath">知识库路径，包含国标、园区规则和历史案例目录。</param>
        /// <param name="knowledgeBase">知识库服务实例，用于加载和查询化工知识库。</param>
        public ChemicalRAG(string knowledgeBasePath, IKnowledgeBaseService knowledgeBase)
        {
            _knowledgeBasePath = knowledgeBasePath;
            _knowledgeBase = knowledgeBase;
        }
        /// <summary>
        /// 异步加载化工知识库，包括国标、园区规则和历史案例。
        /// </summary>
        public async Task LoadKnowledgeBaseAsync()
        {
            Console.WriteLine("\n========== 加载化工知识库 ==========");
            Console.WriteLine("知识库路径: " + _knowledgeBasePath);

            if (!Directory.Exists(_knowledgeBasePath))
            {
                Console.WriteLine("知识库目录不存在！");
                return;
            }

            int totalFiles = 0;
            int totalChunks = 0;

            var gbDir = Path.Combine(_knowledgeBasePath, "国标");
            if (Directory.Exists(gbDir))
            {
                var files = Directory.GetFiles(gbDir, "*.txt");
                totalFiles += files.Length;
                foreach (var file in files)
                {
                    var chunks = await LoadAndSplitFile(file, "国标", "高");
                    totalChunks += chunks.Count;
                }
            }

            var parkDir = Path.Combine(_knowledgeBasePath, "园区规则");
            if (Directory.Exists(parkDir))
            {
                var files = Directory.GetFiles(parkDir, "*.txt");
                totalFiles += files.Length;
                foreach (var file in files)
                {
                    var chunks = await LoadAndSplitFile(file, "园区规则", "中");
                    totalChunks += chunks.Count;
                }
            }

            var caseDir = Path.Combine(_knowledgeBasePath, "历史案例");
            if (Directory.Exists(caseDir))
            {
                var files = Directory.GetFiles(caseDir, "*.txt");
                totalFiles += files.Length;
                foreach (var file in files)
                {
                    var chunks = await LoadAndSplitFile(file, "历史案例", "低");
                    totalChunks += chunks.Count;
                }
            }

            Console.WriteLine("\n化工知识库加载完成！");
            Console.WriteLine("   - 文件数量: " + totalFiles);
            Console.WriteLine("   - 分块数量: " + totalChunks);
            Console.WriteLine("   - 知识库总文档数: " + _knowledgeBase.GetDocumentCount());
            Console.WriteLine("====================================\n");
        }
        /// <summary>
        /// 异步加载并分块处理文件内容，将其添加到知识库中。
        /// </summary>
        /// <param name="filePath">文件路径，包含文件名。</param>
        /// <param name="regulationType">文件类型，例如"国标"、"园区规则"或"历史案例"。</param>
        /// <param name="priority">文件优先级，例如"高"、"中"或"低"。</param>
        /// <returns>包含所有分块的列表。</returns>
        private async Task<List<string>> LoadAndSplitFile(string filePath, string regulationType, string priority)
        {
            var chunks = new List<string>();
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            
            try
            {
                var content = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
                Console.WriteLine("  加载文件: " + fileName + " (" + content.Length + " 字符)");

                var splitChunks = SplitTextIntoChunks(content, 500);
                
                foreach (var chunk in splitChunks)
                {
                    var metadata = new Dictionary<string, object>();
                    metadata["RegulationType"] = regulationType;
                    metadata["Priority"] = priority;
                    metadata["SourceFile"] = fileName;
                    await _knowledgeBase.AddDocumentAsync(chunk, metadata);
                    chunks.Add(chunk);
                }

                Console.WriteLine("    - 分块数: " + splitChunks.Count);
            }
            catch (Exception ex)
            {
                Console.WriteLine("    加载失败: " + ex.Message);
            }

            return chunks;
        }
        /// <summary>
        /// 将文本内容按段落分块，每个分块最大500个字符。
        /// </summary>
        /// <param name="text">要分块的文本内容。</param>
        /// <param name="maxChunkSize">每个分块的最大字符数。</param>
        /// <returns>包含所有分块的列表。</returns>
        private List<string> SplitTextIntoChunks(string text, int maxChunkSize)
        {
            var chunks = new List<string>();
            var paragraphs = text.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();

            var currentChunk = new StringBuilder();
            int currentSize = 0;

            foreach (var paragraph in paragraphs)
            {
                if (currentSize + paragraph.Length > maxChunkSize && currentSize > 0)
                {
                    chunks.Add(currentChunk.ToString());
                    currentChunk.Clear();
                    currentSize = 0;
                }
                
                if (currentChunk.Length > 0)
                    currentChunk.AppendLine();
                
                currentChunk.Append(paragraph);
                currentSize += paragraph.Length;
            }

            if (currentSize > 0)
            {
                chunks.Add(currentChunk.ToString());
            }

            return chunks;
        }
        /// <summary>
        /// 异步执行化工合规检索，返回与查询相关的前topK个结果。
        /// </summary>
        /// <param name="query">用户查询的合规问题。</param>
        /// <param name="topK">返回的结果数量，默认5个。</param>
        /// <returns>包含所有检索结果的列表。</returns>
        public async Task<List<RetrievedChunk>> SearchAsync(string query, int topK = 5)
        {
            Console.WriteLine("\n========== 化工合规检索 ==========");
            Console.WriteLine("查询: " + query);
            Console.WriteLine("----------------------------------");

            // 第一步：BM25检索，多拿一些结果用于重排序
            var bm25Results = await _knowledgeBase.RetrieveAsync(query, topK * 3);
            
            if (bm25Results.Count == 0)
            {
                Console.WriteLine("未找到相关法规！");
                Console.WriteLine("==================================\n");
                return bm25Results;
            }

            // 第二步：优先级重排序（核心修复！）
            var priorityLevels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "国标", 3000 },
                { "园区规则", 2000 },
                { "历史案例", 1000 }
            };

            var rerankedResults = bm25Results
                .Select(r => 
                {
                    var priority = 0;
                    if (r.Metadata.ContainsKey("Priority"))
                    {
                        var p = r.Metadata["Priority"]?.ToString();
                        if (!string.IsNullOrEmpty(p) && priorityLevels.ContainsKey(p))
                        {
                            priority = priorityLevels[p];
                        }
                    }
                    return new { Result = r, AdjustedScore = r.Score + priority };
                })
                .OrderByDescending(x => x.AdjustedScore)
                .Take(topK)
                .Select(x => x.Result)
                .ToList();

            Console.WriteLine("找到 " + rerankedResults.Count + " 条相关结果:\n");

            for (int i = 0; i < rerankedResults.Count; i++)
            {
                var result = rerankedResults[i];
                var metadata = result.Metadata;
                
                Console.WriteLine("【" + (i + 1) + "】 得分: " + result.Score.ToString("F4"));
                
                if (metadata.ContainsKey("RegulationType"))
                    Console.WriteLine("      类型: " + metadata["RegulationType"]);
                
                if (metadata.ContainsKey("Priority"))
                    Console.WriteLine("      优先级: " + metadata["Priority"]);
                
                if (metadata.ContainsKey("SourceFile"))
                    Console.WriteLine("      来源: " + metadata["SourceFile"]);
                
                var contentPreview = result.Content.Substring(0, Math.Min(150, result.Content.Length));
                Console.WriteLine("      内容: " + contentPreview);
                if (result.Content.Length > 150)
                    Console.WriteLine("             ...");
                
                Console.WriteLine();
            }

            Console.WriteLine("==================================\n");
            return rerankedResults;
        }
    }
}
