using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Agent1.Services
{
    /// <summary>
    /// PDF 文档提取器 — 将国标/园区规则 PDF 转换为结构化文本
    /// 基于 PdfPig 实现，纯 .NET，无外部 C++ 依赖
    /// </summary>
    public class PdfExtractor
    {
        /// <summary>
        /// PDF 提取结果
        /// </summary>
        public class PdfResult
        {
            /// <summary>提取的完整文本</summary>
            public string FullText { get; set; } = string.Empty;

            /// <summary>法规编号（如 "GB 30000.7-2013"），从文件名/内容推断</summary>
            public string? RegulationNumber { get; set; }

            /// <summary>标准名称（如 "化学品分类和标签规范 第7部分：易燃液体"）</summary>
            public string? StandardName { get; set; }

            /// <summary>PDF 总页数</summary>
            public int PageCount { get; set; }

            /// <summary>提取方式：Text / OCR / Failed</summary>
            public string ExtractionMethod { get; set; } = "Text";

            /// <summary>提取质量评估：good / partial / failed</summary>
            public string Quality { get; set; } = "good";

            /// <summary>每页的文本行数统计</summary>
            public List<PageInfo> PageDetails { get; set; } = new();

            /// <summary>错误信息（如提取失败）</summary>
            public string? ErrorMessage { get; set; }
        }

        public class PageInfo
        {
            public int PageNumber { get; set; }
            public int CharCount { get; set; }
            public int ChineseCharCount { get; set; }
        }

        /// <summary>
        /// 从 PDF 文件提取全文和元数据
        /// </summary>
        /// <param name="filePath">PDF 文件绝对路径</param>
        /// <returns>提取结果</returns>
        public PdfResult Extract(string filePath)
        {
            var result = new PdfResult();
            var fileName = Path.GetFileNameWithoutExtension(filePath);

            try
            {
                using var pdf = PdfDocument.Open(filePath);
                result.PageCount = pdf.NumberOfPages;

                // 从文件名推断法规编号
                result.RegulationNumber = ExtractRegulationNumber(fileName);

                var allText = new StringBuilder();

                for (int i = 1; i <= pdf.NumberOfPages; i++)
                {
                    var page = pdf.GetPage(i);
                    var pageText = page.Text ?? string.Empty;

                    // 统计中文字符数
                    int chineseCount = CountChineseChars(pageText);

                    result.PageDetails.Add(new PageInfo
                    {
                        PageNumber = i,
                        CharCount = pageText.Length,
                        ChineseCharCount = chineseCount
                    });

                    allText.AppendLine(pageText);
                }

                result.FullText = allText.ToString();

                // 从文本内容推断标准名称（通常出现在前几页）
                result.StandardName = ExtractStandardName(result.FullText);

                // 质量评估
                result.Quality = EvaluateQuality(result);
            }
            catch (Exception ex)
            {
                result.ExtractionMethod = "Failed";
                result.Quality = "failed";
                result.ErrorMessage = $"PDF 解析失败: {ex.Message}";
                Console.WriteLine($"   ❌ PDF 提取异常 [{fileName}]: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// 从文件名中提取法规编号
        /// 支持模式：GB 30000.7-2013、GB+30000.7-2013、GB30000.7-2013
        /// </summary>
        private string? ExtractRegulationNumber(string fileName)
        {
            // 匹配 GB xxxxx.x-xxxx 或 GB xxxxx-xxxx 模式
            var patterns = new[]
            {
                @"GB[+\s]*(\d{4,5}(?:\.\d+)?-\d{4})",        // GB 30000.7-2013
                @"(\d{5}-\d{4})",                              // 30871-2022
                @"(国务院令第\d+号)",                            // 国务院令第591号
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(fileName, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    // 规范化：去掉 + 号，统一空格
                    var number = match.Groups[1].Value
                        .Replace("+", " ")
                        .Replace("  ", " ")
                        .Trim();
                    return number;
                }
            }

            return null;
        }

        /// <summary>
        /// 从 PDF 前几页文本中提取标准名称
        /// 国标通常在封面写明："化学品分类和标签规范 第X部分：XXX"
        /// </summary>
        private string? ExtractStandardName(string fullText)
        {
            // 只分析前 2000 字符（封面区域）
            var header = fullText.Length > 2000
                ? fullText.Substring(0, 2000)
                : fullText;

            var lines = header.Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 5)
                .ToList();

            // 找 "化学品分类和标签规范" 或类似标题行
            foreach (var line in lines)
            {
                if (line.Contains("化学品分类") || line.Contains("化学品") && line.Contains("规范"))
                    return line;

                if (line.Contains("危险化学品") && (line.Contains("条例") || line.Contains("管理")))
                    return line;
            }

            // 兜底：找最长的中文行（通常是标题）
            var longestChinese = lines
                .Where(l => CountChineseChars(l) > l.Length * 0.6)
                .OrderByDescending(l => l.Length)
                .FirstOrDefault();

            return longestChinese;
        }

        /// <summary>
        /// 统计文本中中文字符数量
        /// </summary>
        private int CountChineseChars(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return text.Count(c => c >= 0x4E00 && c <= 0x9FFF);
        }

        /// <summary>
        /// 评估提取质量
        /// </summary>
        private string EvaluateQuality(PdfResult result)
        {
            // 规则 1：全文为空 → 失败
            if (string.IsNullOrWhiteSpace(result.FullText))
                return "failed";

            // 规则 2：中文占比 < 20% → 可能是扫描件（需 OCR）
            int totalChars = result.FullText.Length;
            int chineseChars = CountChineseChars(result.FullText);
            double chineseRatio = totalChars > 0 ? (double)chineseChars / totalChars : 0;

            if (chineseRatio < 0.2)
            {
                result.ExtractionMethod = "OCR_NEEDED";
                return "partial";
            }

            // 规则 3：平均每页中文字符 < 50 → 提取不充分
            double avgChinesePerPage = result.PageCount > 0
                ? (double)chineseChars / result.PageCount
                : 0;

            if (avgChinesePerPage < 50)
                return "partial";

            return "good";
        }
    }
}
