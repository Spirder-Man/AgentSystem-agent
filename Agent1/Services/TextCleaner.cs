using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Agent1.Services
{
    /// <summary>
    /// 文本清洗器 — 将 PDF 提取的原始文本转换为可用于检索的干净文本
    /// 处理国标 PDF 特有的噪声：封面信息、目录、页眉页脚、重复标题行
    /// </summary>
    public class TextCleaner
    {
        /// <summary>
        /// 清洗结果
        /// </summary>
        public class CleanResult
        {
            /// <summary>清洗后的文本</summary>
            public string CleanText { get; set; } = string.Empty;
            
            /// <summary>原始字符数</summary>
            public int OriginalLength { get; set; }
            
            /// <summary>清洗后字符数</summary>
            public int CleanLength { get; set; }
            
            /// <summary>移除的行数</summary>
            public int RemovedLines { get; set; }
            
            /// <summary>是否检测到乱码（中文占比过低）</summary>
            public bool IsGarbled { get; set; }
            
            /// <summary>中文字符占比</summary>
            public double ChineseRatio { get; set; }
        }

        // ── 封面噪声特征行（国标 PDF 通常在第 1-2 页重复出现）──
        private static readonly string[] CoverNoisePatterns = new[]
        {
            "ICS",                          // 国际标准分类号
            "中华人民共和国国家标准",          // 封面标题（重复）
            "中华人民共和国国家质量监督检验检疫总局",
            "中国国家标准化管理委员会",
            "发布", "实施",
            "代替", "GB",                   // 代替 GB xxxx-xxxx（版本历史）
        };

        // ── 目录页检测关键词 ──
        private static readonly string[] TocKeywords = new[]
        {
            "目  次", "目 次", "目次",
            "前言", "引言",
        };

        /// <summary>
        /// 清洗 PDF 提取的原始文本
        /// </summary>
        /// <param name="rawText">PDF 原始文本</param>
        /// <param name="regulationType">法规类型（用于定制清洗规则）</param>
        public CleanResult Clean(string rawText, string regulationType = "国标")
        {
            var result = new CleanResult
            {
                OriginalLength = rawText.Length
            };

            if (string.IsNullOrWhiteSpace(rawText))
            {
                result.CleanText = string.Empty;
                result.IsGarbled = true;
                return result;
            }

            var lines = rawText.Split('\n')
                .Select(l => l.Trim())
                .ToList();

            int removedCount = 0;
            var cleaned = new List<string>();
            bool inToc = false;   // 是否在目录区域内
            int tocLineCount = 0; // 目录区域内行数（目录通常不超过 30 行）

            foreach (var line in lines)
            {
                // ── 过滤 1：空行和纯空白行 ──
                if (string.IsNullOrWhiteSpace(line))
                {
                    removedCount++;
                    continue;
                }

                // ── 过滤 2：封面噪声行 ──
                if (IsCoverNoise(line))
                {
                    removedCount++;
                    continue;
                }

                // ── 过滤 3：目录页检测 ──
                if (!inToc && IsTocStart(line))
                {
                    inToc = true;
                    tocLineCount = 0;
                    removedCount++;
                    continue;
                }

                if (inToc)
                {
                    tocLineCount++;
                    // 目录结束条件：超过 40 行 或 遇到章节标题行
                    if (tocLineCount > 40 || IsChapterHeader(line))
                    {
                        inToc = false;
                        // 章节标题行本身不跳过
                    }
                    else
                    {
                        removedCount++;
                        continue;
                    }
                }

                // ── 过滤 4：纯数字/纯符号行 ──
                if (IsNoiseLine(line))
                {
                    removedCount++;
                    continue;
                }

                // ── 过滤 5：重复法规编号行（页眉页脚）──
                if (IsHeaderFooter(line))
                {
                    removedCount++;
                    continue;
                }

                // ── 规范化：全角转半角 ──
                var cleanedLine = NormalizeText(line);
                cleaned.Add(cleanedLine);
            }

            result.CleanText = string.Join("\n", cleaned);
            result.CleanLength = result.CleanText.Length;
            result.RemovedLines = removedCount;

            // 质量检测
            result.ChineseRatio = CalculateChineseRatio(result.CleanText);
            result.IsGarbled = result.ChineseRatio < 0.2;

            return result;
        }

        /// <summary>
        /// 判断是否为封面噪声行（发布单位、日期、ICS 编号等）
        /// </summary>
        private bool IsCoverNoise(string line)
        {
            foreach (var pattern in CoverNoisePatterns)
            {
                if (line.Contains(pattern))
                {
                    // 放行规则：不是所有含 "GB" 的都是封面 — 正文中的 GB 编号引用应该保留
                    if (pattern == "GB" && line.Length > 20)
                        continue; // 长的 GB 引用行保留

                    return true;
                }
            }

            // 日期格式行（如 "2013-10-10 发布"）
            if (Regex.IsMatch(line, @"\d{4}-\d{2}-\d{2}\s*(发布|实施)"))
                return true;

            return false;
        }

        /// <summary>
        /// 判断是否为目录起始行
        /// </summary>
        private bool IsTocStart(string line)
        {
            var clean = line.Replace(" ", "");
            return TocKeywords.Any(k => clean.StartsWith(k));
        }

        /// <summary>
        /// 判断是否为章节标题（目录结束标志）
        /// </summary>
        private bool IsChapterHeader(string line)
        {
            // "1 范围"、"第3章"、"附录A" 等
            return Regex.IsMatch(line, @"^(\d+[\.\s]|第[一二三四五六七八九十百\d]+[章节条])");
        }

        /// <summary>
        /// 判断是否为噪声行（纯数字/符号/单字符）
        /// </summary>
        private bool IsNoiseLine(string line)
        {
            // 纯数字（页码）
            if (Regex.IsMatch(line, @"^\d+$") && line.Length <= 4)
                return true;

            // 纯标点符号
            if (line.Length <= 3 && Regex.IsMatch(line, @"^[\p{P}\p{S}]+$"))
                return true;

            // 单个英文字母
            if (line.Length == 1 && char.IsLetter(line[0]))
                return true;

            return false;
        }

        /// <summary>
        /// 判断是否为页眉页脚（重复的法规编号行）
        /// 如 "GB 30000.7-2013" 单独成行
        /// </summary>
        private bool IsHeaderFooter(string line)
        {
            // 纯法规编号行（如 "GB 30000.7-2013"）
            if (Regex.IsMatch(line, @"^GB[\s+]*\d{4,5}(\.\d+)?-\d{4}$"))
                return true;

            return false;
        }

        /// <summary>
        /// 文本规范化：全角转半角、去除多余空格
        /// </summary>
        private string NormalizeText(string line)
        {
            var sb = new StringBuilder(line.Length);

            foreach (char c in line)
            {
                // 全角字母转半角（ＧＢ → GB）
                if (c >= 0xFF01 && c <= 0xFF5E)
                {
                    sb.Append((char)(c - 0xFEE0));
                }
                // 全角空格（U+3000）→ 半角空格
                else if (c == '\u3000')
                {
                    sb.Append(' ');
                }
                // 不可见控制字符 → 跳过
                else if (char.IsControl(c) && c != '\n' && c != '\r' && c != '\t')
                {
                    // 跳过
                }
                else
                {
                    sb.Append(c);
                }
            }

            // 合并多个空格
            var result = Regex.Replace(sb.ToString(), @"\s{2,}", " ").Trim();
            return result;
        }

        /// <summary>
        /// 计算中文字符占比
        /// </summary>
        private double CalculateChineseRatio(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int chineseCount = text.Count(c => c >= 0x4E00 && c <= 0x9FFF);
            return text.Length > 0 ? (double)chineseCount / text.Length : 0;
        }
    }
}
