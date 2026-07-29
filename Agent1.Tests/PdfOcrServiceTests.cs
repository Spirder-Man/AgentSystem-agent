using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Agent1.Services;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// PdfOcrService 单元测试 — 扫描件 PDF 视觉 OCR 回退管线。
/// 渲染与 OCR 均通过构造函数注入 stub，不依赖 PDFium 原生库与 GPU 视觉服务。
/// </summary>
public class PdfOcrServiceTests : IDisposable
{
    private readonly string _tempPdf;

    public PdfOcrServiceTests()
    {
        // ExtractTextAsync 只做 File.Exists 检查，占位文件即可
        _tempPdf = Path.Combine(Path.GetTempPath(), $"ocr-test-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(_tempPdf, "%PDF-1.4 stub");
    }

    public void Dispose()
    {
        try { File.Delete(_tempPdf); } catch { /* 忽略 */ }
    }

    private static List<string> FakeRender(int pages)
    {
        // 返回虚构 PNG 路径（stub OCR 不读文件）
        return Enumerable.Range(1, pages).Select(i => $"/tmp/fake-page-{i}.png").ToList();
    }

    [Fact]
    public async Task ExtractText_全页成功_拼接全文并标记页码()
    {
        var svc = new PdfOcrService(
            ocrPage: img => Task.FromResult(MultimodalResult.Ok($"第{img[^5]}页的国标条文内容，危险化学品储存要求与安全距离规定说明。")),
            renderPages: (_, _, _) => FakeRender(3));

        var outcome = await svc.ExtractTextAsync(_tempPdf);

        Assert.True(outcome.Success);
        Assert.Equal(3, outcome.PagesTotal);
        Assert.Equal(3, outcome.PagesOcred);
        Assert.Equal(0, outcome.PagesFailed);
        Assert.Contains("【第1页】", outcome.FullText);
        Assert.Contains("【第3页】", outcome.FullText);
    }

    [Fact]
    public async Task ExtractText_单页失败不中断_其余页正常拼接()
    {
        int call = 0;
        var svc = new PdfOcrService(
            ocrPage: _ => Task.FromResult(++call == 2
                ? MultimodalResult.Fail("Timeout", "视觉分析超时")
                : MultimodalResult.Ok("危险化学品重大危险源辨识标准条文，包含分级判定与临界量数值表格内容。")),
            renderPages: (_, _, _) => FakeRender(3));

        var outcome = await svc.ExtractTextAsync(_tempPdf);

        Assert.True(outcome.Success);
        Assert.Equal(2, outcome.PagesOcred);
        Assert.Equal(1, outcome.PagesFailed);
    }

    [Fact]
    public async Task ExtractText_视觉服务不可达_快速止损不逐页撞墙()
    {
        int calls = 0;
        var svc = new PdfOcrService(
            ocrPage: _ =>
            {
                calls++;
                return Task.FromResult(MultimodalResult.Fail("ServiceUnavailable", "视觉服务不可达 (8083)"));
            },
            renderPages: (_, _, _) => FakeRender(10));

        var outcome = await svc.ExtractTextAsync(_tempPdf);

        Assert.False(outcome.Success);
        Assert.Equal(1, calls); // 首页失败即熔断，不再调用剩余 9 页
        Assert.Equal(10, outcome.PagesFailed);
        Assert.Contains("不可达", outcome.ErrorMessage);
    }

    [Fact]
    public async Task ExtractText_空白页标记_不计入成功页()
    {
        var svc = new PdfOcrService(
            ocrPage: _ => Task.FromResult(MultimodalResult.Ok("【无文字】")),
            renderPages: (_, _, _) => FakeRender(2));

        var outcome = await svc.ExtractTextAsync(_tempPdf);

        Assert.False(outcome.Success);
        Assert.Equal(0, outcome.PagesOcred);
        Assert.Equal(2, outcome.PagesFailed);
    }

    [Fact]
    public async Task ExtractText_文件不存在_返回失败()
    {
        var svc = new PdfOcrService(
            ocrPage: _ => Task.FromResult(MultimodalResult.Ok("不应被调用")),
            renderPages: (_, _, _) => FakeRender(1));

        var outcome = await svc.ExtractTextAsync("/nonexistent/ghost.pdf");

        Assert.False(outcome.Success);
        Assert.Contains("不存在", outcome.ErrorMessage);
    }

    [Fact]
    public async Task ExtractText_渲染异常_返回失败并带原因()
    {
        var svc = new PdfOcrService(
            ocrPage: _ => Task.FromResult(MultimodalResult.Ok("不应被调用")),
            renderPages: (_, _, _) => throw new InvalidOperationException("PDFium 原生库加载失败"));

        var outcome = await svc.ExtractTextAsync(_tempPdf);

        Assert.False(outcome.Success);
        Assert.Contains("渲染失败", outcome.ErrorMessage);
    }

    [Theory]
    [InlineData("", 0, "failed")]                       // 空文本
    [InlineData("危险化学品安全管理条例规定内容", 0, "failed")]   // 无成功页
    [InlineData("abc def ghi", 2, "partial")]           // 无中文
    public void EvaluateOcrQuality_边界场景(string text, int pages, string expected)
    {
        Assert.Equal(expected, PdfOcrService.EvaluateOcrQuality(text, pages));
    }

    [Fact]
    public void EvaluateOcrQuality_每页中文充足_判定good()
    {
        var text = string.Concat(Enumerable.Repeat("危险化学品储存安全管理规范条文内容详细说明每页文字充足", 10)); // 260 中文字符
        Assert.Equal("good", PdfOcrService.EvaluateOcrQuality(text, 2));   // 130/页 ≥ 50
        Assert.Equal("partial", PdfOcrService.EvaluateOcrQuality(text, 10)); // 26/页 < 50
    }

    // ═══ NormalizeOcrText：源头消除会被 GarbledTextDetector 规则①误杀的合法重复串 ═══

    [Fact]
    public void NormalizeOcrText_目录点线归一_不再触发乱码拒收()
    {
        var toc = "前言…………………………………………Ⅰ\n1 范围……………………………………1\n4.1.2 动火作业安全要求········12";
        var normalized = PdfOcrService.NormalizeOcrText(toc);

        Assert.DoesNotContain("……", normalized);
        Assert.DoesNotContain("··", normalized);
        Assert.False(GarbledTextDetector.IsGarbled(normalized, out var reason), $"不应被判乱码: {reason}");
        Assert.Contains("4.1.2 动火作业安全要求", normalized); // 正文内容不受影响
    }

    [Fact]
    public void NormalizeOcrText_Markdown表格分隔线_不再触发乱码拒收()
    {
        var table = "| 化学品名称 | 闪点温度限制要求 |\n|--------|--------|\n| 甲苯介质 | 四点六摄氏度 |\n危险化学品储存安全管理规范条文要求严格执行分类存放制度并定期检查";
        var normalized = PdfOcrService.NormalizeOcrText(table);

        Assert.False(GarbledTextDetector.IsGarbled(normalized, out var reason), $"不应被判乱码: {reason}");
        Assert.Contains("甲苯介质", normalized); // 表格内容保留
    }

    [Fact]
    public void NormalizeOcrText_真乱码仍被检测拦截()
    {
        // 归一化只处理点线/横线，不应放过“书书书书”类自定义字体真乱码
        var garbled = PdfOcrService.NormalizeOcrText("书书书书书!!!!!\"#$%&'()书书书书");
        Assert.True(GarbledTextDetector.IsGarbled(garbled, out _));
    }
}
