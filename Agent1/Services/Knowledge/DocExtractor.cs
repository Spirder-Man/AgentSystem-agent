using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Agent1.Services
{
    /// <summary>
    /// DOC/DOCX 文档提取器 — 处理企业安全管理制度文件
    /// 策略：
    ///   - .docx 文件 → OpenXml 全文提取
    ///   - .doc 文件（旧格式）→ 仅提取文件名摘要（大多是空白表单模板）
    ///   - 自动分类：制度文档 / 表单模板 / 记录台账
    /// </summary>
    public class DocExtractor
    {
        /// <summary>文档类型分类</summary>
        public enum DocCategory
        {
            Regulation,  // 制度文档（如"应急救援管理制度"）→ 全文索引
            Template,    // 表单模板（如"消防器材检查表"）→ 仅存摘要
            Record,      // 记录台账（如"安全培训记录"）→ 仅存摘要
            Reference,   // 参考资料 → 全文索引
            Unknown      // 未分类 → 仅存文件名
        }

        /// <summary>DOC 提取结果</summary>
        public class DocResult
        {
            /// <summary>提取的文本（表单模板可能为 null）</summary>
            public string? FullText { get; set; }

            /// <summary>文档摘要（文件名 + 分类）</summary>
            public string Summary { get; set; } = string.Empty;

            /// <summary>文档分类</summary>
            public DocCategory Category { get; set; } = DocCategory.Unknown;

            /// <summary>是否应该全文索引</summary>
            public bool ShouldFullIndex => Category == DocCategory.Regulation
                                        || Category == DocCategory.Reference;

            /// <summary>源文件名</summary>
            public string FileName { get; set; } = string.Empty;

            /// <summary>所在目录（用于推断上下文）</summary>
            public string? ParentDirectory { get; set; }

            /// <summary>提取方式</summary>
            public string ExtractionMethod { get; set; } = "FilenameOnly";

            /// <summary>错误信息</summary>
            public string? ErrorMessage { get; set; }
        }

        /// <summary>
        /// 从 DOC/DOCX 文件提取内容
        /// </summary>
        public DocResult Extract(string filePath)
        {
            var result = new DocResult
            {
                FileName = Path.GetFileNameWithoutExtension(filePath),
                ParentDirectory = Path.GetFileName(Path.GetDirectoryName(filePath))
            };

            var ext = Path.GetExtension(filePath).ToLowerInvariant();

            try
            {
                if (ext == ".docx")
                {
                    return ExtractDocx(filePath, result);
                }
                else if (ext == ".doc")
                {
                    return ExtractDocLegacy(filePath, result);
                }
                else
                {
                    result.ErrorMessage = $"不支持的格式: {ext}";
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"提取失败: {ex.Message}";
                result.Category = DocCategory.Unknown;
            }

            // 统一生成摘要
            result.Summary = GenerateSummary(result);
            return result;
        }

        /// <summary>
        /// 处理 .docx 文件（OpenXml 格式）
        /// </summary>
        private DocResult ExtractDocx(string filePath, DocResult result)
        {
            result.ExtractionMethod = "OpenXml";

            using var doc = WordprocessingDocument.Open(filePath, false);
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body == null)
            {
                result.Category = DocCategory.Template;
                result.ErrorMessage = "文档无正文内容";
                return result;
            }

            var textBuilder = new StringBuilder();
            var paragraphs = body.Elements<Paragraph>();

            foreach (var para in paragraphs)
            {
                var text = para.InnerText?.Trim();
                if (!string.IsNullOrEmpty(text))
                    textBuilder.AppendLine(text);
            }

            var fullText = textBuilder.ToString();

            // 判断是否为空白模板（文本量 < 100 字符 → 基本是空表格）
            if (fullText.Length < 100)
            {
                result.Category = DocCategory.Template;
                result.FullText = null;
                return result;
            }

            result.FullText = fullText;
            result.Category = ClassifyDocument(result);
            return result;
        }

        /// <summary>
        /// 处理旧 .doc 文件 — 不解析二进制格式，只做文件名分析
        /// H166 目录下 99% 的 .doc 是制度模板/表单，对合规查询无全文索引价值
        /// </summary>
        private DocResult ExtractDocLegacy(string filePath, DocResult result)
        {
            result.ExtractionMethod = "FilenameOnly";
            result.Category = ClassifyByFilename(result.FileName, result.ParentDirectory);
            result.FullText = null; // 旧格式 DOC 不提取全文
            return result;
        }

        /// <summary>
        /// 基于文件名和目录对文档分类
        /// </summary>
        private DocCategory ClassifyByFilename(string fileName, string? parentDir)
        {
            var name = fileName.ToLower();
            var dir = parentDir?.ToLower() ?? "";

            // 制度类关键词
            var regulationKeywords = new[] { "制度", "规定", "规程", "办法", "方案", "预案", "条例", "规范", "标准" };
            if (regulationKeywords.Any(k => name.Contains(k) || dir.Contains(k)))
                return DocCategory.Regulation;

            // 表单/检查表类关键词
            var templateKeywords = new[] { "表", "台账", "记录", "登记", "卡", "单", "通知", "报告", "书" };
            if (templateKeywords.Any(k => name.Contains(k)))
                return DocCategory.Template;

            // 记录类关键词
            var recordKeywords = new[] { "档案", "清单", "汇总", "统计" };
            if (recordKeywords.Any(k => name.Contains(k)))
                return DocCategory.Record;

            // 参考资料
            if (dir.Contains("参考") || name.Contains("参考"))
                return DocCategory.Reference;

            return DocCategory.Unknown;
        }

        /// <summary>
        /// 基于全文内容对 DOCX 做更精确的分类
        /// </summary>
        private DocCategory ClassifyDocument(DocResult result)
        {
            // 先用文件名做初步分类
            var baseCategory = ClassifyByFilename(result.FileName, result.ParentDirectory);

            // 如果全文很短（< 200 字符），降级为 Template
            if (result.FullText != null && result.FullText.Length < 200)
                return DocCategory.Template;

            return baseCategory;
        }

        /// <summary>
        /// 生成文档摘要
        /// </summary>
        private string GenerateSummary(DocResult result)
        {
            var sb = new StringBuilder();

            // 分类标签（中文）
            var categoryLabel = result.Category switch
            {
                DocCategory.Regulation => "📋 制度文档",
                DocCategory.Template => "📝 表单模板",
                DocCategory.Record => "📊 记录台账",
                DocCategory.Reference => "📚 参考资料",
                _ => "📄 未分类"
            };

            sb.AppendLine($"{categoryLabel}: {result.FileName}");

            if (!string.IsNullOrEmpty(result.ParentDirectory))
                sb.AppendLine($"  目录: {result.ParentDirectory}");

            if (result.FullText != null)
                sb.AppendLine($"  文本量: {result.FullText.Length} 字符");

            if (!string.IsNullOrEmpty(result.ErrorMessage))
                sb.AppendLine($"  ⚠️ {result.ErrorMessage}");

            return sb.ToString();
        }
    }
}
