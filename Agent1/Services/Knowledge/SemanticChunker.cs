using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Agent1.Config;

namespace Agent1.Services
{
    /// <summary>
    /// 语义分块器 — 按文档类型采用不同切分策略，替代固定 500 字符一刀切
    /// 
    /// 策略：
    ///   国标 PDF   → 按条款编号切分（"3.1", "3.2.1" 等）
    ///   园区规则   → 按"第X条"切分
    ///   历史案例   → 按段落 + overlap 切分
    ///   通用文档   → 按段落（双换行）切分，固定 overlap
    /// </summary>
    public class SemanticChunker
    {
        /// <summary>语义块</summary>
        public class SemanticChunk
        {
            /// <summary>块文本内容</summary>
            public string Content { get; set; } = string.Empty;

            /// <summary>法规编号（如 "GB 30000.7-2013"）</summary>
            public string? RegulationNumber { get; set; }

            /// <summary>章节名（如 "第3章 术语和定义"）</summary>
            public string? ChapterTitle { get; set; }

            /// <summary>条款编号（如 "3.1"）</summary>
            public string? ClauseNumber { get; set; }

            /// <summary>原始页码</summary>
            public int? PageNumber { get; set; }

            /// <summary>同文档内块序号（从 1 开始）</summary>
            public int ChunkIndex { get; set; }
        }

        // [P3] 从配置读取分块参数
        private static int MaxChunkSize => AppConfig.Instance.KnowledgeBase.ChunkSize;
        private static int MinChunkSize => AppConfig.Instance.KnowledgeBase.ChunkOverlap;
        private static int OverlapSize => AppConfig.Instance.KnowledgeBase.ChunkOverlap;

        /// <summary>
        /// 按文档类型执行语义分块
        /// </summary>
        /// <param name="cleanText">已清洗的文本</param>
        /// <param name="regulationType">文档类型：国标 / 园区规则 / 历史案例 / 通用</param>
        /// <param name="regulationNumber">法规编号（来自 PdfExtractor）</param>
        /// <param name="pageNumber">[P2-5] 页码（来自 PdfExtractor，用于检索结果溯源）</param>
        /// <returns>语义块列表</returns>
        public List<SemanticChunk> Chunk(string cleanText, string regulationType, string? regulationNumber = null, int? pageNumber = null)
        {
            if (string.IsNullOrWhiteSpace(cleanText))
                return new List<SemanticChunk>();

            return regulationType switch
            {
                "国标" => ChunkByClause(cleanText, regulationNumber, pageNumber),
                // 化工专业条例本质是 GB 系列标准（含 "4.1.2" 式条款号），走条款切分；
                // 段落切分依赖 \n\n 空行，而 TextCleaner 以单 \n 重组全文，会退化为整篇一块
                "化工专业条例" => ChunkByClause(cleanText, regulationNumber, pageNumber),
                "园区规则" => ChunkByArticle(cleanText, regulationNumber, pageNumber),
                "历史案例" => ChunkByParagraph(cleanText, regulationNumber, pageNumber),
                _ => ChunkByParagraph(cleanText, regulationNumber, pageNumber)
            };
        }

        /// <summary>
        /// 按条款编号切分 — 适用于 GB 系列国标
        /// 匹配模式："3.1 术语", "3.2.1 分类", "A.1 附录" 等
        /// </summary>
        private List<SemanticChunk> ChunkByClause(string text, string? regulationNumber, int? pageNumber)
        {
            var chunks = new List<SemanticChunk>();

            // 匹配章节/条款标题行：
            // "1 范围"、"3.1 术语和定义"、"3.2.1 分类标准"、"附录A"、"第4章"
            var clausePattern = new Regex(
                @"^(?:(\d+(?:\.\d+)*)\s+|(第[一二三四五六七八九十百\d]+[章节条])\s*|(附录[A-Z]))(.+)?",
                RegexOptions.Multiline);

            var lines = text.Split('\n');
            var currentContent = new StringBuilder();
            string? currentChapter = null;
            string? currentClause = null;
            int chunkIndex = 0;

            foreach (var line in lines)
            {
                var match = clausePattern.Match(line);
                if (match.Success && line.Length < 100) // 标题行通常较短
                {
                    // 遇到新条款 → 保存上一个块
                    if (currentContent.Length >= MinChunkSize)
                    {
                        chunks.Add(new SemanticChunk
                        {
                            Content = currentContent.ToString().Trim(),
                            RegulationNumber = regulationNumber,
                            ChapterTitle = currentChapter,
                            ClauseNumber = currentClause,
                            PageNumber = pageNumber,
                            ChunkIndex = chunkIndex++
                        });
                        currentContent.Clear();
                    }

                    // 更新当前上下文
                    currentClause = match.Groups[1].Success ? match.Groups[1].Value
                                  : match.Groups[2].Success ? match.Groups[2].Value
                                  : match.Groups[3].Value;

                    // 如果是章节级别（如 "3 术语和定义"），更新章节标题
                    if (match.Groups[1].Success && !match.Groups[1].Value.Contains("."))
                    {
                        currentChapter = match.Groups[4].Value?.Trim();
                    }
                }

                currentContent.AppendLine(line);

                // 如果当前块已超过最大大小，强制分割
                if (currentContent.Length >= MaxChunkSize)
                {
                    chunks.Add(new SemanticChunk
                    {
                        Content = currentContent.ToString().Trim(),
                        RegulationNumber = regulationNumber,
                        ChapterTitle = currentChapter,
                        ClauseNumber = currentClause,
                        PageNumber = pageNumber,
                        ChunkIndex = chunkIndex++
                    });
                    currentContent.Clear();
                }
            }

            // 保存最后一个块
            if (currentContent.Length > 0)
            {
                chunks.Add(new SemanticChunk
                {
                    Content = currentContent.ToString().Trim(),
                    RegulationNumber = regulationNumber,
                    ChapterTitle = currentChapter,
                    ClauseNumber = currentClause,
                    PageNumber = pageNumber,
                    ChunkIndex = chunkIndex++
                });
            }

            // 合并过小的块到相邻块
            chunks = MergeSmallChunks(chunks);

            // 添加块间重叠
            chunks = AddOverlap(chunks);

            return chunks;
        }

        /// <summary>
        /// 按"第X条"切分 — 适用于园区规则
        /// </summary>
        private List<SemanticChunk> ChunkByArticle(string text, string? regulationNumber, int? pageNumber)
        {
            var chunks = new List<SemanticChunk>();
            var articlePattern = new Regex(@"^(第[一二三四五六七八九十百]+条)", RegexOptions.Multiline);

            var parts = articlePattern.Split(text);
            var currentContent = new StringBuilder();
            string? currentArticle = null;
            int chunkIndex = 0;

            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (string.IsNullOrEmpty(part)) continue;

                // 检查是否为条款标记
                if (articlePattern.IsMatch(part))
                {
                    // 保存上一个块
                    if (currentContent.Length >= MinChunkSize)
                    {
                        chunks.Add(new SemanticChunk
                        {
                            Content = currentContent.ToString().Trim(),
                            RegulationNumber = regulationNumber,
                            ClauseNumber = currentArticle,
                            PageNumber = pageNumber,
                            ChunkIndex = chunkIndex++
                        });
                        currentContent.Clear();
                    }
                    currentArticle = part;
                }

                currentContent.Append(part);

                if (currentContent.Length >= MaxChunkSize)
                {
                    chunks.Add(new SemanticChunk
                    {
                        Content = currentContent.ToString().Trim(),
                        RegulationNumber = regulationNumber,
                        ClauseNumber = currentArticle,
                        PageNumber = pageNumber,
                        ChunkIndex = chunkIndex++
                    });
                    currentContent.Clear();
                }
            }

            if (currentContent.Length > 0)
            {
                chunks.Add(new SemanticChunk
                {
                    Content = currentContent.ToString().Trim(),
                    RegulationNumber = regulationNumber,
                    ClauseNumber = currentArticle,
                    PageNumber = pageNumber,
                    ChunkIndex = chunkIndex++
                });
            }

            chunks = MergeSmallChunks(chunks);
            chunks = AddOverlap(chunks);
            return chunks;
        }

        /// <summary>
        /// 按段落切分 — 适用于历史案例和通用文档
        /// </summary>
        private List<SemanticChunk> ChunkByParagraph(string text, string? regulationNumber, int? pageNumber)
        {
            var chunks = new List<SemanticChunk>();
            var paragraphs = text.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);

            var currentContent = new StringBuilder();
            int chunkIndex = 0;

            foreach (var rawPara in paragraphs)
            {
                var para = rawPara.Trim();

                // 超长段落防御：清洗后全文可能无空行（如 OCR 全文整篇一段），
                // 按 MaxChunkSize 强制切分，优先在句末/换行处断开
                while (para.Length > MaxChunkSize)
                {
                    if (currentContent.Length > 0)
                    {
                        chunks.Add(new SemanticChunk
                        {
                            Content = currentContent.ToString().Trim(),
                            RegulationNumber = regulationNumber,
                            PageNumber = pageNumber,
                            ChunkIndex = chunkIndex++
                        });
                        currentContent.Clear();
                    }

                    int cut = MaxChunkSize;
                    int boundary = para.LastIndexOfAny(new[] { '\n', '。', '；' }, MaxChunkSize - 1);
                    if (boundary >= MaxChunkSize / 2) cut = boundary + 1;

                    chunks.Add(new SemanticChunk
                    {
                        Content = para.Substring(0, cut).Trim(),
                        RegulationNumber = regulationNumber,
                        PageNumber = pageNumber,
                        ChunkIndex = chunkIndex++
                    });
                    para = para.Substring(cut).TrimStart();
                }
                if (para.Length == 0) continue;

                if (currentContent.Length + para.Length > MaxChunkSize && currentContent.Length > 0)
                {
                    chunks.Add(new SemanticChunk
                    {
                        Content = currentContent.ToString().Trim(),
                        RegulationNumber = regulationNumber,
                        PageNumber = pageNumber,
                        ChunkIndex = chunkIndex++
                    });
                    currentContent.Clear();
                }

                if (currentContent.Length > 0)
                    currentContent.AppendLine();
                currentContent.Append(para);
            }

            if (currentContent.Length > 0)
            {
                chunks.Add(new SemanticChunk
                {
                    Content = currentContent.ToString().Trim(),
                    RegulationNumber = regulationNumber,
                    PageNumber = pageNumber,
                    ChunkIndex = chunkIndex++
                });
            }

            chunks = MergeSmallChunks(chunks);
            chunks = AddOverlap(chunks);
            return chunks;
        }

        /// <summary>
        /// 合并过小的块到前一个块中
        /// </summary>
        private List<SemanticChunk> MergeSmallChunks(List<SemanticChunk> chunks)
        {
            if (chunks.Count <= 1) return chunks;

            var merged = new List<SemanticChunk>();
            var current = chunks[0];

            for (int i = 1; i < chunks.Count; i++)
            {
                var next = chunks[i];
                if (next.Content.Length < MinChunkSize)
                {
                    // 合并到当前块
                    current.Content += "\n" + next.Content;
                }
                else
                {
                    merged.Add(current);
                    current = next;
                }
            }
            merged.Add(current);

            // 重新编号
            for (int i = 0; i < merged.Count; i++)
                merged[i].ChunkIndex = i;

            return merged;
        }

        /// <summary>
        /// 为相邻块添加重叠区域，防止边界信息丢失
        /// </summary>
        private List<SemanticChunk> AddOverlap(List<SemanticChunk> chunks)
        {
            if (chunks.Count <= 1 || OverlapSize <= 0) return chunks;

            for (int i = 0; i < chunks.Count - 1; i++)
            {
                var current = chunks[i];
                var next = chunks[i + 1];

                // 从下一个块头部取 overlap 字符，追加到当前块尾部
                var overlapText = next.Content.Length > OverlapSize
                    ? next.Content.Substring(0, OverlapSize)
                    : next.Content;

                current.Content += "\n[↓ 续] " + overlapText;
            }

            return chunks;
        }
    }
}
