
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Agent1.Services
{
    /// <summary>
    /// 评测模式全局开关：开启时压制 RAG 详细日志，减少 Console I/O 开销。
    /// </summary>
    public static class EvalMode
    {
        public static bool IsActive { get; set; } = false;
    }

    /// <summary>
    /// 知识库服务类，负责处理化工合规检索。
    /// </summary>
    public class KnowledgeBaseService : IKnowledgeBaseService
    {
        /// <summary>
        /// 知识库检索参数，用于计算文档相似度。
        /// </summary>
        private const double K1 = 1.5;
        /// <summary>
        /// 知识库检索参数，用于计算文档相似度。
        /// </summary>
        private const double B = 0.75;

        // [P2] GB编号标准化正则：匹配 GB/T? 数字.子编号-年份 等变体，统一为紧凑格式
        // 例如 "GB 30000.14" / "GB30000.14" / "GB/T 30000.14-2013" → "gb3000014" / "gb/t30000142013"
        private static readonly Regex _gbNumberRegex = new Regex(
            @"\bGB\s*/?\s*[TZ]?\s*(\d{4,5})[.\s]*(\d*)(?:[-\s]*(\d{4}))?\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// [P2] GB编号标准化：将 GB30000.14 / GB 30000.14 / GB/T 30000.14-2013 等
        /// 统一为无分隔符紧凑格式 gb3000014，避免 tokenizer 按 . 和空格拆散导致 BM25 失配。
        /// </summary>
        public static string NormalizeGbNumbers(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            return _gbNumberRegex.Replace(text, match =>
            {
                var fullMatch = match.Value;
                var prefix = fullMatch.Contains("/T", StringComparison.OrdinalIgnoreCase) ? "gb/t"
                           : fullMatch.Contains("/Z", StringComparison.OrdinalIgnoreCase) ? "gb/z"
                           : "gb";

                var mainNumber = match.Groups[1].Value;
                var subNumber = match.Groups[2].Value;
                var year = match.Groups[3].Value;

                var result = prefix + mainNumber;
                if (!string.IsNullOrEmpty(subNumber))
                    result += subNumber;
                if (!string.IsNullOrEmpty(year))
                    result += year;

                return result.ToLowerInvariant();
            });
        }

        /// <summary>
        /// 知识库中的文档列表。
        /// </summary>
        private readonly List<Document> _documents = new List<Document>();
        //内存索引：将所有文档加载到内存，避免每次检索都读文件，提升 BM25 计算速度；耦合度「中」（仅内部依赖 Document）
        /// <summary>
        /// 知识库中的文档索引，用于快速检索。
        /// </summary>
        private readonly Dictionary<string, Dictionary<int, int>> _termDocFreq = new Dictionary<string, Dictionary<int, int>>();
        //倒排索引：核心数据结构，存储「关键词 → {文档 ID: 关键词在该文档中出现次数}」的映射，
        // 解决 “遍历所有文档匹配关键词” 的性能问题（时间复杂度从 O (n) 降为 O (1) 级），耦合度「高」（算法核心依赖，不可拆分）。
        /// <summary>
        /// 知识库中的文档平均长度，用于计算文档相似度。
        /// </summary>
        private double _avgDocLength = 0;
        //BM25 算法核心参数：所有文档的平均长度，用于对「词频（TF）」做归一化
        // （避免长文档因关键词出现次数多而被过度加权），耦合度「低」（仅算法内部使用）。
        /// <summary>
        /// 知识库中的文档模型，包含文档内容、分词、词频、长度和元数据。
        /// </summary>
        private class Document
        {
            /// <summary>
            /// 文档ID，唯一标识文档。
            /// </summary>
            public int Id { get; set; }
            /// <summary>
            /// 文档内容，包含原始文本。
            /// </summary>
            public string Content { get; set; }
            /// <summary>
            /// 文档分词，包含文档中的所有单词。
            /// </summary>
            public List<string> Tokens { get; set; }
            /// <summary>
            /// 文档词频，包含每个单词在文档中的出现次数。
            /// </summary>
            public Dictionary<string, int> TermFreq { get; set; }
            /// <summary>
            /// 文档长度，即文档中单词的数量。
            /// </summary>
            public int Length { get; set; }
            /// <summary>
            /// 文档元数据，包含文档的额外信息，如文档标题、文档类型等。
            /// </summary>
            public Dictionary<string, object> Metadata { get; set; }
        }
        /// <summary>
        /// 知识库服务类的构造函数，初始化知识库。
        /// </summary>
        public KnowledgeBaseService()
        {
        }
        /// <summary>
        /// 添加文档到知识库。
        /// </summary>
        /// <param name="content">文档内容，包含原始文本。</param>
        /// <param name="metadata">文档元数据，包含文档的额外信息，如文档标题、文档类型等。</param>
        /// <returns>任务对象，用于异步操作。</returns>
        public Task AddDocumentAsync(string content, Dictionary<string, object>? metadata = null)
        {
            var doc = new Document
            {
                Id = _documents.Count,
                Content = content,
                Tokens = Tokenize(content),
                Metadata = metadata ?? new Dictionary<string, object>()
            };
            doc.TermFreq = CalculateTermFreq(doc.Tokens);
            doc.Length = doc.Tokens.Count;

            _documents.Add(doc);
            UpdateIndex(doc);
            UpdateAvgLength();

            return Task.CompletedTask;
        }
        /// <summary>
        /// 添加文档到知识库。
        /// </summary>
        /// <param name="contents">文档内容列表，包含原始文本。</param>
        /// <returns>任务对象，用于异步操作。</returns>
        // [P1-10 FIX] 改为 async/await 替代 .Wait(), 防止将来 AddDocumentAsync 变为真异步时死锁
        public async Task AddDocumentsAsync(IEnumerable<string> contents)
        {
            foreach (var content in contents)
            {
                await AddDocumentAsync(content);
            }
        }
        /// <summary>
        /// 从知识库中检索文档，根据BM25算法计算文档相似度。
        /// </summary>
        /// <param name="query">查询文本，包含用户输入的关键词。</param>
        /// <param name="topK">返回的文档数量，默认返回5条。</param>
        /// <returns>任务对象，用于异步操作。</returns>
        public Task<List<RetrievedChunk>> RetrieveAsync(string query, int topK = 5)
        {
            var queryTokens = Tokenize(PreprocessQuery(query));
            if (queryTokens.Count == 0)
            {
                return Task.FromResult(new List<RetrievedChunk>());
            }

            var docScores = new Dictionary<int, double>();

            foreach (var doc in _documents)
            {
                double score = 0;

                foreach (var qToken in queryTokens)
                {
                    if (!_termDocFreq.ContainsKey(qToken))
                        continue;

                    int df = _termDocFreq[qToken].Count;
                    int tf = doc.TermFreq.GetValueOrDefault(qToken, 0);

                    if (tf == 0)
                        continue;

                    double idf = Math.Log((_documents.Count - df + 0.5) / (df + 0.5) + 1);
                    // [P1-5 FIX] 防止 _avgDocLength==0 时除以零导致 NaN
                    var safeAvgLen = Math.Max(1.0, _avgDocLength);
                    double tfComp = (tf * (K1 + 1)) / (tf + K1 * (1 - B + B * (doc.Length / safeAvgLen)));
                    score += idf * tfComp;
                }

                if (score > 0)
                {
                    docScores[doc.Id] = score;
                }
            }

            var results = docScores
                .OrderByDescending(kvp => kvp.Value)
                .Take(topK)
                .Select((kvp, idx) => RetrievedChunk.Create(_documents[kvp.Key].Content, kvp.Value, idx, _documents[kvp.Key].Metadata))
                .ToList();

            if (!EvalMode.IsActive)
            {
                Console.WriteLine($"   🔍 BM25检索: 查询='{query}', 召回={results.Count}条");
                foreach (var r in results)
                {
                    Console.WriteLine($"      {r}");
                }
            }
            else
            {
                Console.WriteLine($"   🔍 BM25检索: 召回={results.Count}条");
            }

            return Task.FromResult(results);
        }
        /// <summary>
        /// 预处理查询文本，移除首尾空格。
        /// </summary>
        /// <param name="query">查询文本，包含用户输入的关键词。</param>
        /// <returns>预处理后的查询文本。</returns>
        public string PreprocessQuery(string query)
        {
            // [P2] GB编号标准化：查询词也做同样归一化
            return NormalizeGbNumbers(query.Trim());
        }
        /// <summary>
        /// 获取知识库中文档的数量。
        /// </summary>
        /// <returns>知识库中文档的数量。</returns>
        public int GetDocumentCount() => _documents.Count;
        /// <summary>
        /// 清空知识库中的所有文档。
        /// </summary>
        public void Clear()
        {
            _documents.Clear();
            _termDocFreq.Clear();
            _avgDocLength = 0;
        }

        /// <summary>[P3 增量更新] 按源文件删除文档，返回被删除数量</summary>
        public int RemoveBySourceFile(string sourceFile)
        {
            var fileName = Path.GetFileNameWithoutExtension(sourceFile);
            var removed = _documents.RemoveAll(d =>
                d.Metadata.TryGetValue("SourceFile", out var src) &&
                (src?.ToString() ?? "") == fileName);

            if (removed > 0)
            {
                RebuildIndex();
                Console.WriteLine($"   🗑️ 已移除 {removed} 个分块 (源文件: {fileName})");
            }
            return removed;
        }

        private void RebuildIndex()
        {
            _termDocFreq.Clear();
            if (_documents.Count == 0) { _avgDocLength = 0; return; }
            for (int i = 0; i < _documents.Count; i++)
            {
                _documents[i].Id = i;
                UpdateIndex(_documents[i]);
            }
            UpdateAvgLength();
        }

        // [P3] 接口兼容 — 纯 BM25 版只能清理内存分块，无法清 DB
        public Task RemoveChunksBySourceFileAsync(string sourceFile)
        {
            RemoveBySourceFile(sourceFile);
            return Task.CompletedTask;
        }

        /// <summary>
        /// 对文本进行分词处理，生成基础词和ngram分词。
        /// </summary>
        /// <param name="text">待分词的文本。</param>
        /// <returns>分词后的词列表。</returns>
        private List<string> Tokenize(string text)
        {
            // 空字符串直接返回空列表
            if (string.IsNullOrWhiteSpace(text))
                return new List<string>();

            // [P2] GB编号标准化：先规范化为紧凑格式，避免分词器按 . - 空格拆散
            var normalized = NormalizeGbNumbers(text);
            var cleanedText = normalized.Replace("\n", " ").Replace("\r", " ");
            // 初始化分词列表
            var tokens = new List<string>();

            // 第一步：先按分隔符把句子切分基础词
            var basicParts = Regex.Split(cleanedText, @"[\s,.\-_\(\)\[\]，。：:；""""、！？!?]+")// 中文分隔符
                .Where(t => !string.IsNullOrWhiteSpace(t))// 过滤空字符串
                .Select(t => t.ToLowerInvariant().Trim())// 转换为小写并移除首尾空格
                .Where(t => t.Length > 0)// 过滤空字符串
                .ToList();

            // 第二步：对于每个词再生成ngram分词（中文核心修复！）
            foreach (var part in basicParts)
            {
                // 先添加完整词

                tokens.Add(part);
                
                // 生成2-gram分词（中文2个字一组！
                if (part.Length >= 2)
                {
                    for (int i = 0; i <= part.Length - 2; i++)
                    {
                        tokens.Add(part.Substring(i, 2));
                    }
                }
                
                // 生成单字分词（重要！）
                for (int i = 0; i < part.Length; i++)
                {
                    tokens.Add(part.Substring(i, 1));
                }
            }

            // 调试用（仅在需排查分词问题时取消注释）
            // Console.WriteLine($"   🔍 Tokenize调试: 原文='{text.Substring(0, Math.Min(50, text.Length))}' → Tokens=[{string.Join(", ", tokens)}]");
            return tokens;
        }
        /// <summary>
        /// 计算文档中每个词的词频。
        /// </summary>
        /// <param name="tokens">文档中的词列表。</param>
        /// <returns>每个词的词频字典。</returns>
        private Dictionary<string, int> CalculateTermFreq(List<string> tokens)
        {
            var freq = new Dictionary<string, int>();
            foreach (var token in tokens)
            {
                if (!freq.ContainsKey(token))
                    freq[token] = 0;
                freq[token]++;
            }
            return freq;
        }
        /// <summary>
        /// 更新知识库索引，将文档中的词词频添加到索引中。
        /// </summary>
        /// <param name="doc">待更新的文档。</param>
        private void UpdateIndex(Document doc)
        {
            foreach (var kvp in doc.TermFreq)
            {
                var term = kvp.Key;
                if (!_termDocFreq.ContainsKey(term))
                    _termDocFreq[term] = new Dictionary<int, int>();
                _termDocFreq[term][doc.Id] = kvp.Value;
            }
        }
        /// <summary>
        /// 更新知识库中所有文档的平均长度。
        /// </summary>
        private void UpdateAvgLength()
        {
            if (_documents.Count == 0)
            {
                _avgDocLength = 0;
                return;
            }
            _avgDocLength = _documents.Average(d => d.Length);
        }
        
        // ==================== 化工场景专用方法 ====================
        
        // 化工专业术语词典（用于提高检索准确率）
        private static readonly HashSet<string> _chemicalTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "甲苯", "甲醇", "乙醇", "丙酮", "过氧化氢", "硫酸", "盐酸", "硝酸",
            "危化品", "危险化学品", "储罐", "防火堤", "消防通道", "安全距离",
            "甲类", "乙类", "丙类", "贮存", "存储", "国标", "GB15603", "GB30000",
            "GB50160", "防火间距", "气柜", "液化烃", "居住区", "长管拖车",
            "gb15603", "gb30000", "gb50160", "禁忌物料", "氧化剂", "易燃液体", "易燃固体", "泄漏", "应急"
        };
        
        // 化工规则优先级（用于重排序，key 匹配 Metadata["Priority"] 的值）
        private static readonly Dictionary<string, int> _priorityLevels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "高", 3 },
            { "中", 2 },
            { "低", 1 }
        };
        
        public Task AddChemicalRegulationAsync(string content, string regulationType, string priority, string? chemicalType = null)
        {
            var metadata = new Dictionary<string, object>
            {
                { "RegulationType", regulationType },
                { "Priority", priority },
                { "ChemicalType", chemicalType ?? "通用" }
            };
            
            Console.WriteLine($"   📚 添加化工法规: 类型={regulationType}, 优先级={priority}, 危化品={chemicalType ?? "通用"}");
            
            return AddDocumentAsync(content, metadata);
        }
        
        public async Task<List<RetrievedChunk>> RetrieveChemicalRegulationAsync(string query, string? chemicalType = null, string? regulationType = null, int topK = 5)
        {
            // 第一步：BM25检索
            var allResults = await RetrieveAsync(query, topK * 2);
            
            // 第二步：化工场景过滤
            var filteredResults = allResults.Where(r =>
            {
                var metadata = r.Metadata;
                
                // 按危化品类型过滤（如指定）
                if (!string.IsNullOrEmpty(chemicalType) && metadata.ContainsKey("ChemicalType"))
                {
                    var docChemicalType = metadata["ChemicalType"]?.ToString();
                    if (docChemicalType != "通用" && !docChemicalType.Equals(chemicalType, StringComparison.OrdinalIgnoreCase))
                        return false;
                }
                
                // 按法规类型过滤（如指定）
                if (!string.IsNullOrEmpty(regulationType) && metadata.ContainsKey("RegulationType"))
                {
                    var docRegulationType = metadata["RegulationType"]?.ToString();
                    if (!docRegulationType.Equals(regulationType, StringComparison.OrdinalIgnoreCase))
                        return false;
                }
                
                return true;
            }).ToList();
            
            // 第三步：化工场景重排序（按优先级+BM25分数）
            var rerankedResults = filteredResults
                .Select(r => new { Chunk = r, Score = CalculateChemicalRelevanceScore(r) })
                .OrderByDescending(x => x.Score)
                .Take(topK)
                .Select(x => x.Chunk)
                .ToList();
            
            Console.WriteLine($"   🔬 化工合规检索: 查询='{query}', 危化品={chemicalType ?? "全部"}, 法规类型={regulationType ?? "全部"}, 召回={rerankedResults.Count}条");
            
            return rerankedResults;
        }
        
        public async Task LoadChemicalKnowledgeBaseAsync(string knowledgeBasePath)
        {
            Console.WriteLine($"   📚 正在加载化工知识库: {knowledgeBasePath}");
            
            var baseDir = new DirectoryInfo(knowledgeBasePath);
            if (!baseDir.Exists)
            {
                Console.WriteLine($"   ⚠️ 化工知识库目录不存在: {knowledgeBasePath}");
                return;
            }
            
            int totalDocs = 0;
            
            // 加载国标文档
            var gbDir = Path.Combine(knowledgeBasePath, "国标");
            if (Directory.Exists(gbDir))
            {
                foreach (var file in Directory.GetFiles(gbDir, "*.txt"))
                {
                    var content = await File.ReadAllTextAsync(file, System.Text.Encoding.UTF8);
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    await AddChemicalRegulationAsync(content, "国标", "高", GetChemicalTypeFromFileName(fileName));
                    totalDocs++;
                }
            }
            
            // 加载园区规则文档
            var parkDir = Path.Combine(knowledgeBasePath, "园区规则");
            if (Directory.Exists(parkDir))
            {
                foreach (var file in Directory.GetFiles(parkDir, "*.txt"))
                {
                    var content = await File.ReadAllTextAsync(file, System.Text.Encoding.UTF8);
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    await AddChemicalRegulationAsync(content, "园区规则", "中", "通用");
                    totalDocs++;
                }
            }
            
            // 加载历史案例文档
            var caseDir = Path.Combine(knowledgeBasePath, "历史案例");
            if (Directory.Exists(caseDir))
            {
                foreach (var file in Directory.GetFiles(caseDir, "*.txt"))
                {
                    var content = await File.ReadAllTextAsync(file, System.Text.Encoding.UTF8);
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    await AddChemicalRegulationAsync(content, "历史案例", "低", "通用");
                    totalDocs++;
                }
            }
            
            Console.WriteLine($"   ✅ 化工知识库加载完成，共 {totalDocs} 个文档");
        }
        
        private double CalculateChemicalRelevanceScore(RetrievedChunk chunk)
        {
            double baseScore = chunk.Score;
            
            // 优先级加分
            int priorityBonus = 0;
            if (chunk.Metadata.ContainsKey("Priority"))
            {
                var priority = chunk.Metadata["Priority"]?.ToString();
                if (!string.IsNullOrEmpty(priority) && _priorityLevels.ContainsKey(priority))
                {
                    priorityBonus = _priorityLevels[priority] * 1000;
                }
            }
            
            // 化工术语匹配加分
            int termBonus = 0;
            var content = chunk.Content;
            foreach (var term in _chemicalTerms)
            {
                if (content.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    termBonus += 50;
                }
            }
            
            return baseScore + priorityBonus + termBonus;
        }
        
        private string? GetChemicalTypeFromFileName(string fileName)
        {
            var lowerFileName = fileName.ToLowerInvariant();
            if (lowerFileName.Contains("甲苯")) return "甲苯";
            if (lowerFileName.Contains("甲醇")) return "甲醇";
            if (lowerFileName.Contains("乙醇")) return "乙醇";
            if (lowerFileName.Contains("丙酮")) return "丙酮";
            if (lowerFileName.Contains("硫酸")) return "硫酸";
            if (lowerFileName.Contains("盐酸")) return "盐酸";
            return "通用";
        }

        // ═══════════════════════════════════════
        // Sprint 4.1: 智能分块器
        // ═══════════════════════════════════════

        /// <summary>
        /// Sprint 4.1: 智能文本分块，支持重叠窗口和语义边界识别。
        /// </summary>
        /// <param name="text">待分块文本</param>
        /// <param name="maxChunkSize">每个块的最大字符数</param>
        /// <param name="overlap">块间重叠字符数（0=无重叠）</param>
        /// <param name="enableSemantic">是否启用语义分块（识别标题/章节）</param>
        public static List<string> SplitTextIntoChunks(string text, int maxChunkSize = 500, int overlap = 100, bool enableSemantic = true)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<string>();

            var chunks = new List<string>();

            // Step 1: 语义边界识别（按标题/章节/段落分割）
            var paragraphs = enableSemantic
                ? SplitBySemanticBoundaries(text)
                : text.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries).ToList();

            // Step 2: 按 chunkSize 组装段落，保留 overlap
            string? pendingOverlap = null;
            var currentChunk = new System.Text.StringBuilder();

            foreach (var para in paragraphs)
            {
                var paraTrimmed = para.Trim();
                if (string.IsNullOrWhiteSpace(paraTrimmed))
                    continue;

                // 如果当前段落本身就超过 chunkSize，单独切割
                if (paraTrimmed.Length > maxChunkSize)
                {
                    // 先刷出当前积累的块
                    if (currentChunk.Length > 0)
                    {
                        chunks.Add(currentChunk.ToString().Trim());
                        currentChunk.Clear();
                    }

                    // 切割长段落（保留 overlap）
                    int pos = 0;
                    while (pos < paraTrimmed.Length)
                    {
                        var chunkLen = Math.Min(maxChunkSize, paraTrimmed.Length - pos);
                        var chunkText = paraTrimmed.Substring(pos, chunkLen);

                        // 尝试在句号/换行处断开（语义友好）
                        if (chunkLen == maxChunkSize && pos + chunkLen < paraTrimmed.Length)
                        {
                            var breakPoint = FindBestBreakPoint(chunkText);
                            if (breakPoint > maxChunkSize / 2)
                            {
                                chunkText = chunkText.Substring(0, breakPoint);
                                chunkLen = breakPoint;
                            }
                        }

                        chunks.Add(chunkText);
                        pos += chunkLen - (chunkLen < maxChunkSize ? 0 : overlap);
                        if (pos < 0) pos = 0;
                    }
                    continue;
                }

                // 当前块装不下这个段落 → 刷出当前块，开始新块
                if (currentChunk.Length + paraTrimmed.Length > maxChunkSize && currentChunk.Length > 0)
                {
                    var chunkText = currentChunk.ToString().Trim();
                    chunks.Add(chunkText);

                    // overlap: 保留当前块末尾 overlap 字符作为新块开头
                    if (overlap > 0 && chunkText.Length > overlap)
                    {
                        pendingOverlap = chunkText.Substring(chunkText.Length - overlap);
                        currentChunk.Clear();
                        currentChunk.Append(pendingOverlap);
                        currentChunk.Append(' ');
                    }
                    else
                    {
                        currentChunk.Clear();
                    }
                }

                if (currentChunk.Length > 0)
                    currentChunk.Append(' ');
                currentChunk.Append(paraTrimmed);
            }

            // 最后剩余的块
            if (currentChunk.Length > 0)
                chunks.Add(currentChunk.ToString().Trim());

            return chunks;
        }

        /// <summary>
        /// Sprint 4.1: 按语义边界分割——识别章节标题、法规条款编号等。
        /// </summary>
        private static List<string> SplitBySemanticBoundaries(string text)
        {
            var result = new List<string>();

            // 语义边界模式：
            // - "第X章" / "第X节" (中文法规章节)
            // - "X.X.X" (编号条款，如 "3.1.2")
            // - 双换行（段落分隔）
            var pattern = @"(?<=\n|^)(?=(?:第[\d零一二三四五六七八九十百]+[章节条]|\d+(?:\.\d+)+\s))|\n\s*\n";

            try
            {
                var sections = System.Text.RegularExpressions.Regex.Split(text, pattern, System.Text.RegularExpressions.RegexOptions.Compiled);
                foreach (var section in sections)
                {
                    var trimmed = section.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed))
                        result.Add(trimmed);
                }
            }
            catch
            {
                // 正则失败时降级为简单双换行分割
                result = text.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();
            }

            return result.Count > 0 ? result : new List<string> { text };
        }

        /// <summary>
        /// Sprint 4.1: 在文本中寻找最佳断点（句号、换行等自然边界）。
        /// </summary>
        private static int FindBestBreakPoint(string text)
        {
            // 优先级：句号 > 分号 > 换行 > 逗号
            var breakChars = new[] { '。', '；', '\n', '，', ',' };
            foreach (var ch in breakChars)
            {
                var lastIdx = text.LastIndexOf(ch);
                if (lastIdx > text.Length / 2)
                    return lastIdx + 1;
            }
            return text.Length;
        }
    }
}
