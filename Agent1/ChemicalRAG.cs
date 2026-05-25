
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
    /// 支持 PDF/DOC/DOCX/TXT 多格式文档的提取、清洗、语义分块和双存储。
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

        // K2-K5: 文档处理管道组件
        private readonly PdfExtractor _pdfExtractor = new();
        private readonly DocExtractor _docExtractor = new();
        private readonly TextCleaner _textCleaner = new();
        private readonly SemanticChunker _semanticChunker = new();

        // K8: 加载统计
        private int _totalFiles;
        private int _successFiles;
        private int _partialFiles;
        private int _failedFiles;
        private int _skippedFiles;
        private int _totalChunks;
        private readonly List<string> _failedFileList = new();
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
            Console.WriteLine("\n========== 加载化工知识库（多格式管道） ==========");
            Console.WriteLine("知识库路径: " + _knowledgeBasePath);
            Console.WriteLine("支持格式: PDF / DOC / DOCX / TXT");
            Console.WriteLine("管道: PdfExtractor → TextCleaner → SemanticChunker → 双存储");
            Console.WriteLine("==================================================");

            if (!Directory.Exists(_knowledgeBasePath))
            {
                Console.WriteLine("知识库目录不存在！");
                return;
            }

            _totalFiles = 0; _successFiles = 0; _partialFiles = 0;
            _failedFiles = 0; _skippedFiles = 0; _totalChunks = 0;
            _failedFileList.Clear();

            var gbDir = Path.Combine(_knowledgeBasePath, "国标");
            if (Directory.Exists(gbDir))
                await LoadDirectoryAsync(gbDir, "国标", "高");

            var specDir = Path.Combine(_knowledgeBasePath, "化工专业条例", "化工专业条例");
            if (Directory.Exists(specDir))
                await LoadDirectoryAsync(specDir, "国标", "高");

            var parkDir = Path.Combine(_knowledgeBasePath, "园区规则");
            if (Directory.Exists(parkDir))
                await LoadDirectoryAsync(parkDir, "园区规则", "中");

            var caseDir = Path.Combine(_knowledgeBasePath, "历史案例");
            if (Directory.Exists(caseDir))
                await LoadDirectoryAsync(caseDir, "历史案例", "低");

            var h166Dir = Path.Combine(_knowledgeBasePath, "H166—危险化学品化工企业安全生产三级标准化管理制度消防台账资料档案");
            if (Directory.Exists(h166Dir))
            {
                Console.WriteLine("\n   扫描 H166 制度模板目录...");
                await LoadH166DirectoryAsync(h166Dir);
            }

            PrintQualityReport();
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
        // ==================== K6: 多格式文档处理管道 ====================

        /// <summary>
        /// 统一目录加载：自动识别 PDF/DOC/DOCX/TXT
        /// </summary>
        private async Task LoadDirectoryAsync(string dirPath, string regulationType, string priority)
        {
            var pdfFiles = Directory.GetFiles(dirPath, "*.pdf", SearchOption.TopDirectoryOnly)
                .Where(f => !Path.GetFileName(f).StartsWith("~$"));
            foreach (var f in pdfFiles)
                await ProcessPdfFileAsync(f, regulationType, priority);

            var docFiles = Directory.GetFiles(dirPath, "*.doc", SearchOption.TopDirectoryOnly)
                .Concat(Directory.GetFiles(dirPath, "*.docx", SearchOption.TopDirectoryOnly))
                .Where(f => !Path.GetFileName(f).StartsWith("~$"))
                .Distinct();
            foreach (var f in docFiles)
                await ProcessDocFileAsync(f, regulationType, priority);

            var txtFiles = Directory.GetFiles(dirPath, "*.txt", SearchOption.TopDirectoryOnly);
            foreach (var f in txtFiles)
            {
                _totalFiles++;
                var chunks = await LoadAndSplitFile(f, regulationType, priority);
                _totalChunks += chunks.Count;
                _successFiles++;
            }
        }
        /// <summary>
        /// 加载并处理H166目录下的文档文件。
        /// </summary>
        /// <param name="rootDir">根目录路径。</param>
        private async Task LoadH166DirectoryAsync(string rootDir)
        {
            var docFiles = Directory.GetFiles(rootDir, "*.doc", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(rootDir, "*.docx", SearchOption.AllDirectories))
                .Where(f => !Path.GetFileName(f).StartsWith("~$"))
                .ToList();

            foreach (var file in docFiles)
            {
                _totalFiles++;
                var result = _docExtractor.Extract(file);
                if (result.ShouldFullIndex && result.FullText != null)
                {
                    var cr = _textCleaner.Clean(result.FullText, "通用");
                    var chunks = _semanticChunker.Chunk(cr.CleanText, "通用");
                    foreach (var c in chunks)
                    {
                        await _knowledgeBase.AddDocumentAsync(c.Content, new Dictionary<string, object>
                        {
                            ["RegulationType"] = "企业制度", ["Priority"] = "低",
                            ["SourceFile"] = result.FileName, ["ParentDir"] = result.ParentDirectory ?? ""
                        });
                        _totalChunks++;
                    }
                    _successFiles++;
                }
                else
                {
                    await _knowledgeBase.AddDocumentAsync(result.Summary, new Dictionary<string, object>
                    {
                        ["RegulationType"] = "企业制度", ["Priority"] = "低", ["SourceFile"] = result.FileName
                    });
                    _totalChunks++;
                    _skippedFiles++;
                }
            }
        }
        /// <summary>
        /// 处理PDF文件并添加到知识库中。
        /// </summary>
        /// <param name="filePath">文件路径，包含文件名。</param>
        /// <param name="regulationType">文件类型，例如"国标"、"园区规则"或"历史案例"。</param>
        /// <param name="priority">文件优先级，例如"高"、"中"或"低"。</param>
        private async Task ProcessPdfFileAsync(string filePath, string regulationType, string priority)
        {
            _totalFiles++;
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            try
            {
                var pdfResult = _pdfExtractor.Extract(filePath);
                if (pdfResult.Quality == "failed")
                {
                    _failedFiles++; _failedFileList.Add($"{fileName} (PDF解析失败)");
                    return;
                }
                var cleanResult = _textCleaner.Clean(pdfResult.FullText, regulationType);
                if (cleanResult.IsGarbled) _partialFiles++;

                var chunks = _semanticChunker.Chunk(cleanResult.CleanText, regulationType,
                    pdfResult.RegulationNumber ?? fileName);

                foreach (var c in chunks)
                {
                    await _knowledgeBase.AddDocumentAsync(c.Content, new Dictionary<string, object>
                    {
                        ["RegulationType"] = regulationType, ["Priority"] = priority,
                        ["SourceFile"] = fileName,
                        ["RegulationNumber"] = c.RegulationNumber ?? "",
                        ["ChapterTitle"] = c.ChapterTitle ?? "",
                        ["ClauseNumber"] = c.ClauseNumber ?? "",
                        ["ChunkIndex"] = c.ChunkIndex.ToString()
                    });
                    _totalChunks++;
                }
                if (cleanResult.IsGarbled) _partialFiles++; else _successFiles++;
                Console.WriteLine($"   ✅ [{fileName}]: {pdfResult.PageCount}页 → {chunks.Count}块 (质量:{pdfResult.Quality})");
            }
            catch (Exception ex)
            {
                _failedFiles++; _failedFileList.Add($"{fileName} ({ex.Message})");
            }
        }
        /// <summary>
        /// 处理DOC/DOCX文件并添加到知识库中。
        /// </summary>
        /// <param name="filePath">文件路径，包含文件名。</param>
        /// <param name="regulationType">文件类型，例如"国标"、"园区规则"或"历史案例"。</param>
        /// <param name="priority">文件优先级，例如"高"、"中"或"低"。</param>
        private async Task ProcessDocFileAsync(string filePath, string regulationType, string priority)
        {
            _totalFiles++;
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            try
            {
                var docResult = _docExtractor.Extract(filePath);
                if (docResult.ShouldFullIndex && docResult.FullText != null)
                {
                    var cr = _textCleaner.Clean(docResult.FullText, regulationType);
                    var chunks = _semanticChunker.Chunk(cr.CleanText, regulationType);
                    foreach (var c in chunks)
                    {
                        await _knowledgeBase.AddDocumentAsync(c.Content, new Dictionary<string, object>
                        {
                            ["RegulationType"] = regulationType, ["Priority"] = priority, ["SourceFile"] = fileName
                        });
                        _totalChunks++;
                    }
                    _successFiles++;
                }
                else
                {
                    await _knowledgeBase.AddDocumentAsync(docResult.Summary, new Dictionary<string, object>
                    {
                        ["RegulationType"] = regulationType, ["Priority"] = priority, ["SourceFile"] = fileName
                    });
                    _totalChunks++;
                    _skippedFiles++;
                }
            }
            catch (Exception ex)
            {
                _failedFiles++; _failedFileList.Add($"{fileName} ({ex.Message})");
            }
        }
        /// <summary>
        /// 打印知识库加载质量报告。
        /// </summary>          
        private void PrintQualityReport()
        {
            Console.WriteLine("\n========================================");
            Console.WriteLine("        知识库加载质量报告");
            Console.WriteLine("========================================");
            Console.WriteLine($"  文件总数: {_totalFiles}");
            Console.WriteLine($"  ✅ 成功:   {_successFiles}");
            Console.WriteLine($"  ⚠️  部分:  {_partialFiles}");
            Console.WriteLine($"  ❌ 失败:   {_failedFiles}");
            Console.WriteLine($"  ⏭️  跳过:  {_skippedFiles} (空表单/模板)");
            Console.WriteLine("----------------------------------------");
            Console.WriteLine($"  总块数:    {_totalChunks}");
            Console.WriteLine($"  知识库文档数: {_knowledgeBase.GetDocumentCount()}");
            if (_failedFileList.Count > 0)
            {
                Console.WriteLine("----------------------------------------");
                Console.WriteLine("  失败文件:");
                foreach (var f in _failedFileList.Take(10))
                    Console.WriteLine($"    - {f}");
                if (_failedFileList.Count > 10)
                    Console.WriteLine($"    ... 共 {_failedFileList.Count} 个");
            }
            Console.WriteLine("========================================\n");
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
