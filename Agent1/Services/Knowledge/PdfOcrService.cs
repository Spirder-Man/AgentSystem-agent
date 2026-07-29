using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Agent1.Services
{
    /// <summary>
    /// 扫描件 PDF 视觉 OCR 回退服务 — PDFium 将 PDF 页渲染为 PNG，
    /// 逐页交给视觉模型（Qwen2.5-VL，8083 端口 llama-server --mmproj 实例）转写为结构化文本。
    /// 仅在 PdfExtractor 判定文本层过薄（ExtractionMethod=OCR_NEEDED）时由 ChemicalRAG 触发。
    /// 渲染与 OCR 均可注入替换，单元测试无需原生库与 GPU。
    /// </summary>
    public class PdfOcrService
    {
        /// <summary>单个 PDF 的 OCR 汇总结果。</summary>
        public class OcrOutcome
        {
            /// <summary>是否得到可用全文（至少 1 页成功且文本非平凡）</summary>
            public bool Success { get; set; }

            /// <summary>逐页拼接的 OCR 全文（带【第N页】分隔标记）</summary>
            public string FullText { get; set; } = string.Empty;

            /// <summary>参与 OCR 的页数（受 MaxPages 截断）</summary>
            public int PagesTotal { get; set; }

            /// <summary>成功转写的页数</summary>
            public int PagesOcred { get; set; }

            /// <summary>转写失败的页数</summary>
            public int PagesFailed { get; set; }

            /// <summary>失败原因（渲染异常 / 视觉服务不可达等）</summary>
            public string? ErrorMessage { get; set; }
        }

        private readonly int _maxPages;
        private readonly int _dpi;
        private readonly Func<string, Task<MultimodalResult>>? _ocrPageOverride;
        private readonly Func<string, int, int, List<string>>? _renderPagesOverride;

        /// <param name="maxPages">单个 PDF 最多 OCR 页数（防超长文档拖垮加载）</param>
        /// <param name="dpi">页渲染 DPI（150 兼顾清晰度与视觉模型推理速度）</param>
        /// <param name="ocrPage">单页 OCR 函数注入点（默认走 MultimodalService.OcrPageAsync）</param>
        /// <param name="renderPages">页渲染函数注入点 (pdfPath, maxPages, dpi) → PNG 路径列表（默认走 PDFium）</param>
        public PdfOcrService(int maxPages = 20, int dpi = 150,
            Func<string, Task<MultimodalResult>>? ocrPage = null,
            Func<string, int, int, List<string>>? renderPages = null)
        {
            _maxPages = maxPages > 0 ? maxPages : 20;
            _dpi = dpi > 0 ? dpi : 150;
            _ocrPageOverride = ocrPage;
            _renderPagesOverride = renderPages;
        }

        /// <summary>
        /// 对扫描件 PDF 执行逐页视觉 OCR，返回拼接全文与页级统计。
        /// 任何单页失败不中断整体流程；全部失败或渲染异常时 Success=false。
        /// </summary>
        public async Task<OcrOutcome> ExtractTextAsync(string pdfPath)
        {
            var outcome = new OcrOutcome();
            if (!File.Exists(pdfPath))
            {
                outcome.ErrorMessage = $"PDF 文件不存在: {pdfPath}";
                return outcome;
            }

            List<string> pageImages;
            try
            {
                pageImages = (_renderPagesOverride ?? RenderPagesToPng)(pdfPath, _maxPages, _dpi);
            }
            catch (Exception ex)
            {
                outcome.ErrorMessage = $"PDF 页渲染失败: {ex.Message}";
                return outcome;
            }

            outcome.PagesTotal = pageImages.Count;
            if (pageImages.Count == 0)
            {
                outcome.ErrorMessage = "PDF 无可渲染页";
                return outcome;
            }

            // 默认路径：整个 PDF 共用一个 MultimodalService 实例（连接复用）
            MultimodalService? sharedService = _ocrPageOverride == null ? new MultimodalService() : null;
            var fullText = new StringBuilder();
            try
            {
                for (int i = 0; i < pageImages.Count; i++)
                {
                    var result = _ocrPageOverride != null
                        ? await _ocrPageOverride(pageImages[i])
                        : await sharedService!.OcrPageAsync(pageImages[i]);

                    if (result.Success && !IsBlankPageMarker(result.Content))
                    {
                        fullText.AppendLine($"【第{i + 1}页】");
                        fullText.AppendLine(NormalizeOcrText(result.Content.Trim()));
                        fullText.AppendLine();
                        outcome.PagesOcred++;
                    }
                    else
                    {
                        outcome.PagesFailed++;
                        // 视觉服务整体不可达时快速止损：不再逐页撞墙
                        if (result.ErrorCategory == "ServiceUnavailable")
                        {
                            outcome.ErrorMessage = result.Content;
                            outcome.PagesFailed += pageImages.Count - i - 1;
                            break;
                        }
                    }
                }
            }
            finally
            {
                sharedService?.Dispose();
                CleanupTempImages(pageImages);
            }

            outcome.FullText = fullText.ToString();
            outcome.Success = outcome.PagesOcred > 0 && outcome.FullText.Length > 50;
            if (!outcome.Success && outcome.ErrorMessage == null)
                outcome.ErrorMessage = $"全部 {outcome.PagesTotal} 页 OCR 均未产出有效文本";
            return outcome;
        }

        /// <summary>
        /// OCR 产出质量评估（与 PdfExtractor.EvaluateQuality 同口径）：
        /// 平均每成功页中文字符 ≥ 50 → good，否则 partial。
        /// </summary>
        public static string EvaluateOcrQuality(string fullText, int pagesOcred)
        {
            if (string.IsNullOrWhiteSpace(fullText) || pagesOcred <= 0) return "failed";
            int chineseChars = fullText.Count(c => c >= 0x4E00 && c <= 0x9FFF);
            return (double)chineseChars / pagesOcred >= 50 ? "good" : "partial";
        }

        /// <summary>模型对空白页的约定输出（见 MultimodalService.PageOcrPrompt）。</summary>
        private static bool IsBlankPageMarker(string content)
            => content.Trim().Length < 5 || content.Contains("【无文字】");

        // 目录点线（“第一章…………1”）与 Markdown 表格分隔线（|----|）的重复字符串，
        // 会被 GarbledTextDetector 规则①（同字符连续重复≥4）误判为乱码拒收
        private static readonly Regex DotLeaderRun = new(@"[…·•]{2,}|\.{4,}", RegexOptions.Compiled);
        private static readonly Regex DashRun = new(@"(?<![|\-])[-—_]{4,}(?![|\-])", RegexOptions.Compiled);
        private static readonly Regex TableRuleRun = new(@"(?<=\|)\s*:?-{3,}:?\s*(?=\|)", RegexOptions.Compiled);

        /// <summary>
        /// OCR 转写文本归一化：在源头消除会被乱码检测误杀的合法重复字符串，
        /// 不放松 GarbledTextDetector 对原生提取文本（“书书书书”类真乱码）的防线。
        /// </summary>
        internal static string NormalizeOcrText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            text = DotLeaderRun.Replace(text, "…");   // 目录点线→单省略号
            text = TableRuleRun.Replace(text, " --- "); // Markdown 表格分隔线→合法短线
            text = DashRun.Replace(text, "—");        // 长横线/下划线串→单破折号
            return text;
        }

        /// <summary>
        /// 默认渲染实现：PDFium（PDFtoImage 包）将前 maxPages 页渲染为 PNG 临时文件。
        /// 输出目录：%TEMP%/agent1-pdfocr/{guid}/，OCR 完成后由调用方清理。
        /// </summary>
        private static List<string> RenderPagesToPng(string pdfPath, int maxPages, int dpi)
        {
            // 运行环境仅 Windows(开发)/Linux(生产)，均在 PDFium 支持范围内
#pragma warning disable CA1416
            var pdfBytes = File.ReadAllBytes(pdfPath);
            int pageCount = PDFtoImage.Conversion.GetPageCount(pdfBytes);
            int pages = Math.Min(pageCount, maxPages);

            var tempDir = Path.Combine(Path.GetTempPath(), "agent1-pdfocr", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            var outputs = new List<string>();
            for (int i = 0; i < pages; i++)
            {
                var pngPath = Path.Combine(tempDir, $"page-{i + 1:D3}.png");
                PDFtoImage.Conversion.SavePng(pngPath, pdfBytes, page: (Index)i,
                    options: new PDFtoImage.RenderOptions(Dpi: dpi));
                outputs.Add(pngPath);
            }
            return outputs;
#pragma warning restore CA1416
        }

        private static void CleanupTempImages(List<string> pageImages)
        {
            foreach (var img in pageImages)
            {
                try { File.Delete(img); } catch { /* 忽略清理失败 */ }
            }
            // 尝试删除空的临时子目录
            var dir = pageImages.Count > 0 ? Path.GetDirectoryName(pageImages[0]) : null;
            if (dir != null && dir.Contains("agent1-pdfocr"))
            {
                try { Directory.Delete(dir); } catch { /* 非空或被占用则忽略 */ }
            }
        }
    }
}
