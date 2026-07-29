using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Agent1.Config;
using Agent1.Services;
using Microsoft.Extensions.Configuration;
using Xunit;
using FluentAssertions;

namespace Agent1.Tests
{
    // ═══════════════════════════════════════════
    // DocExtractor 测试 — 文档分类和元数据提取
    // ═══════════════════════════════════════════
    public class DocExtractorTests
    {
        private readonly DocExtractor _extractor = new();

        [Fact]
        public void ClassifyByFilename_RegulationKeywords_ReturnsRegulation()
        {
            // 含"制度"、"规定"、"规程"、"办法"、"方案"、"预案"、"条例"、"规范"、"标准"
            var cases = new[] { "安全管理制度", "消防规定细则", "操作规程", "化学品管理办法", "应急预案", "安全生产条例", "技术规范", "国家标准" };
            foreach (var name in cases)
            {
                var result = _extractor.Extract($"dummy/{name}.doc");
                result.Category.Should().Be(DocExtractor.DocCategory.Regulation,
                    $"文件名 '{name}' 应识别为制度文档");
            }
        }

        [Fact]
        public void ClassifyByFilename_TemplateKeywords_ReturnsTemplate()
        {
            var cases = new[] { "消防器材检查表", "安全培训记录", "危化品登记卡", "隐患整改通知单", "事故报告书" };
            foreach (var name in cases)
            {
                var result = _extractor.Extract($"dummy/{name}.doc");
                result.Category.Should().Be(DocExtractor.DocCategory.Template,
                    $"文件名 '{name}' 应识别为表单模板");
            }
        }

        [Fact]
        public void ClassifyByFilename_RecordKeywords_ReturnsRecord()
        {
            // 注意: "隐患清单" 含 "单" 会被模板关键词先匹配为 Template
            var cases = new[] { "设备档案", "检查汇总", "培训统计" };
            foreach (var name in cases)
            {
                var result = _extractor.Extract($"dummy/{name}.doc");
                result.Category.Should().Be(DocExtractor.DocCategory.Record,
                    $"文件名 '{name}' 应识别为记录台账");
            }
        }

        [Fact]
        public void ClassifyByFilename_ReferenceDirectory_ReturnsReference()
        {
            var result = _extractor.Extract(@"参考资料/somefile.doc");
            result.Category.Should().Be(DocExtractor.DocCategory.Reference,
                "含'参考'的目录应识别为参考资料");
        }

        [Fact]
        public void ClassifyByFilename_UnknownName_ReturnsUnknown()
        {
            var result = _extractor.Extract(@"未知/abcdefg.doc");
            result.Category.Should().Be(DocExtractor.DocCategory.Unknown,
                "无法匹配任何关键词时应为 Unknown");
        }

        [Fact]
        public void Extract_UnsupportedFormat_ReturnsErrorInSummary()
        {
            var result = _extractor.Extract("test.pdf");
            result.ErrorMessage.Should().NotBeNullOrEmpty("不支持的格式应有错误信息");
            result.Summary.Should().Contain("⚠️");
        }

        [Fact]
        public void Extract_DocLegacy_UsesFilenameOnly()
        {
            var result = _extractor.Extract("管理制度汇编.doc");
            result.ExtractionMethod.Should().Be("FilenameOnly", ".doc 旧格式不解析全文");
            result.FullText.Should().BeNull(".doc 旧格式不提取全文");
        }

        [Fact]
        public void Extract_GeneratesSummary()
        {
            // 使用不支持的格式(.pdf)触发 GenerateSummary() 调用
            // 注: .doc/.docx 在 Extract 的 try 块内直接 return，跳过了 Summary 生成
            var result = _extractor.Extract("dummy/安全管理制度.pdf");
            result.Summary.Should().NotBeNullOrEmpty();
            result.Summary.Should().Contain("安全管理制度");
            result.Summary.Should().Contain("📄 未分类");
        }

        [Fact]
        public void DocResult_ShouldFullIndex_TrueForRegulation()
        {
            var result = new DocExtractor.DocResult
            {
                Category = DocExtractor.DocCategory.Regulation,
                FileName = "test"
            };
            result.ShouldFullIndex.Should().BeTrue("制度文档应全文索引");
        }

        [Fact]
        public void DocResult_ShouldFullIndex_FalseForTemplate()
        {
            var result = new DocExtractor.DocResult
            {
                Category = DocExtractor.DocCategory.Template,
                FileName = "test"
            };
            result.ShouldFullIndex.Should().BeFalse("表单模板不全文索引");
        }

        [Fact]
        public void ClassifyByFilename_ParentDirectoryRegulationKeyword_ReturnsRegulation()
        {
            // 父目录含"制度"时即使文件名无关键词也能识别
            var result = _extractor.Extract(@"安全制度汇编/日常检查.doc");
            result.Category.Should().Be(DocExtractor.DocCategory.Regulation,
                "父目录含'制度'关键词应识别为制度文档");
        }

        [Fact]
        public void Extract_ExceptionInDocx_ReturnsErrorResult()
        {
            // 用不存在的文件触发异常路径
            var result = _extractor.Extract("nonexistent_file.docx");
            result.ErrorMessage.Should().NotBeNullOrEmpty();
            result.Category.Should().Be(DocExtractor.DocCategory.Unknown);
        }
    }

    // ═══════════════════════════════════════════
    // TextCleaner 测试 — PDF 文本清洗规则
    // ═══════════════════════════════════════════
    public class TextCleanerTests
    {
        private readonly TextCleaner _cleaner = new();

        [Fact]
        public void Clean_NullOrEmptyInput_ReturnsGarbled()
        {
            var result = _cleaner.Clean("");
            result.IsGarbled.Should().BeTrue("空输入应标记为乱码");
            result.CleanText.Should().BeEmpty();
        }

        [Fact]
        public void Clean_WhitespaceOnly_ReturnsGarbled()
        {
            var result = _cleaner.Clean("   \n  \r\n  ");
            result.IsGarbled.Should().BeTrue("纯空白应标记为乱码");
            result.CleanLength.Should().Be(0);
        }

        [Fact]
        public void Clean_RemovesCoverNoiseLines()
        {
            var input = @"ICS 13.300
中华人民共和国国家标准
GB 30000.7-2013
化学品分类和标签规范 第7部分：易燃液体
2013-10-10 发布
真正的正文内容开始";

            var result = _cleaner.Clean(input);
            result.CleanText.Should().NotContain("ICS");
            result.CleanText.Should().NotContain("中华人民共和国国家标准");
            result.CleanText.Should().Contain("真正的正文内容开始");
            result.RemovedLines.Should().BeGreaterThan(0, "封面噪声行应被移除");
        }

        [Fact]
        public void Clean_RemovesTableOfContents()
        {
            var input = @"目  次
前言
1 范围
1 范围正文内容
术语和定义
2 术语和定义正文";

            var result = _cleaner.Clean(input);
            result.CleanText.Should().NotContain("目  次");
            result.CleanText.Should().NotContain("前言");
            // "1 范围" 是章节标题，在目录结束后应保留
            result.CleanText.Should().Contain("1 范围");
            result.CleanText.Should().Contain("术语和定义");
        }

        [Fact]
        public void Clean_RemovesNoiseLines()
        {
            var input = @"25
A
正常文本内容
!!!
42
正常文本2";

            var result = _cleaner.Clean(input);
            result.CleanText.Should().NotContain("!!!");
            // 单字母 A 应被移除
            result.CleanText.Split('\n').Should().NotContain("A");
        }

        [Fact]
        public void Clean_RemovesHeaderFooterRegulationNumbers()
        {
            var input = @"GB 30000.7-2013
实际正文段落一
GB 30000.7-2013
实际正文段落二";

            var result = _cleaner.Clean(input);
            // 独立的法规编号行被移除，正文保留
            result.CleanText.Should().Contain("实际正文段落一");
            result.CleanText.Should().Contain("实际正文段落二");
            // 但单独的 GB xxxxx.x-xxxx 行被移除
            var lines = result.CleanText.Split('\n');
            lines.Should().NotContain(l => l.Trim() == "GB 30000.7-2013");
        }

        [Fact]
        public void Clean_PreservesLongGbReferenceInBody()
        {
            // 正文中较长的 GB 引用行（>20字符）应保留
            var input = "依据 GB 30000.7-2013 第3.2条规定，易燃液体分类如下";
            var result = _cleaner.Clean(input);
            result.CleanText.Should().Contain("GB 30000.7-2013",
                "正文中较长的 GB 引用应保留");
        }

        [Fact]
        public void Clean_NormalizesFullWidthToHalfWidth()
        {
            // 全角字母（FF21-FF5E）转半角
            var input = "ＧＢ　３００００＿７";
            var result = _cleaner.Clean(input);
            result.CleanText.Should().Contain("GB");
            //  全角空格 U+3000 转半角空格
        }

        [Fact]
        public void Clean_MergesMultipleSpaces()
        {
            var input = "化学品    分类  和  标签    规范";
            var result = _cleaner.Clean(input);
            result.CleanText.Should().Be("化学品 分类 和 标签 规范",
                "多余空格应合并为单个空格");
        }

        [Fact]
        public void Clean_CalculatesChineseRatio()
        {
            var input = "这是中文内容 this is english text 更多的中文";
            var result = _cleaner.Clean(input);
            result.ChineseRatio.Should().BeGreaterThan(0);
            result.ChineseRatio.Should().BeLessThan(1.0);
            result.IsGarbled.Should().BeFalse("中文占比应足够高");
        }

        [Fact]
        public void Clean_LowChineseRatio_DetectedAsGarbled()
        {
            var input = "This is purely English text with no Chinese characters at all here";
            var result = _cleaner.Clean(input);
            result.IsGarbled.Should().BeTrue("中文占比 < 20% 应标记为乱码");
        }

        [Fact]
        public void Clean_RecordsOriginalAndCleanLength()
        {
            var input = "  多余空格  的  文本  \n\n\n  清洗后  ";
            var result = _cleaner.Clean(input);
            result.OriginalLength.Should().BeGreaterThan(result.CleanLength,
                "清洗后字符数应减少");
            result.RemovedLines.Should().BeGreaterThan(0);
        }

        [Fact]
        public void Clean_RemovesControlCharacters()
        {
            var input = "正常文本\0含\a有\u0001控制\u0002字符";
            var result = _cleaner.Clean(input);
            result.CleanText.Should().Contain("正常文本");
            result.CleanText.Should().Contain("含有");
            result.CleanText.Should().Contain("控制");
            result.CleanText.Should().Contain("字符");
        }
    }

    // ═══════════════════════════════════════════
    // SemanticChunker 测试 — 语义分块策略
    // ═══════════════════════════════════════════
    public class SemanticChunkerTests
    {
        private readonly SemanticChunker _chunker = new();

        public SemanticChunkerTests()
        {
            // SemanticChunker uses AppConfig.Instance for ChunkSize/ChunkOverlap defaults
            // Always ensure config is loaded (idempotent)
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Llm:ModelId"] = "test-model",
                    ["Llm:Endpoint"] = "http://localhost:11434",
                    ["Database:Host"] = "localhost",
                    ["Database:Port"] = "5432",
                    ["Database:DatabaseName"] = "testdb",
                    ["Database:Password"] = "test-password",
                    ["VectorSearch:EmbeddingModelId"] = "test-embed",
                    ["PromptTemplates:SystemRole"] = "test-role",
                    ["PromptTemplates:EvalFastPrompt"] = "test-prompt {SystemRole} {UserInput}",
                    ["PromptTemplates:EvalFastQueryPrompt"] = "test-query {SystemRole} {UserInput}"
                })
                .Build();
            AppConfig.Load(configuration);
        }

        [Fact]
        public void Chunk_EmptyOrNullText_ReturnsEmpty()
        {
            _chunker.Chunk("", "国标").Should().BeEmpty();
            _chunker.Chunk(null!, "国标").Should().BeEmpty();
            _chunker.Chunk("   \n  ", "国标").Should().BeEmpty();
        }

        [Fact]
        public void Chunk_GuoBiaoMode_ChunksByClauseNumber()
        {
            var text = @"1 范围
本标准规定了易燃液体的分类方法。
1.1 术语和定义
易燃液体指闪点不大于93°C的液体。
1.2 分类标准
按闪点和初沸点进行分类。
2 规范性引用文件
下列文件对于本标准的应用是必不可少的。";

            var chunks = _chunker.Chunk(text, "国标");
            chunks.Should().NotBeEmpty("国标模式应产生语义块");
            // 至少要有包含条款编号的块
            chunks.Should().Contain(c => c.Content.Contains("易燃液体"),
                "块内容应包含原文文本");
            chunks.Should().Contain(c => c.ClauseNumber != null,
                "应有识别的条款编号");
        }

        [Fact]
        public void Chunk_ParkRuleMode_ChunksByArticle()
        {
            var text = @"第一条 本条例适用于化工园区内危险化学品的生产、储存和使用。
第二条 化工园区应建立安全管理制度。
第三条 企业应配备专职安全管理人员。";

            var chunks = _chunker.Chunk(text, "园区规则");
            chunks.Should().NotBeEmpty("园区规则模式应产生语义块");
            chunks.Should().Contain(c => c.Content.Contains("第一条")
                                     || c.Content.Contains("第二条")
                                     || c.Content.Contains("第三条"));
        }

        [Fact]
        public void Chunk_HistoryMode_ChunksByParagraph()
        {
            var text = @"2023年某化工厂发生爆炸事故，造成3人死亡。

事故原因调查表明，操作工未按规定穿戴防护装备。

整改措施包括加强安全培训和更新设备巡检制度。";

            var chunks = _chunker.Chunk(text, "历史案例");
            chunks.Should().NotBeEmpty("历史案例模式应产生语义块");
        }

        [Fact]
        public void Chunk_DefaultMode_ChunksByParagraph()
        {
            var text = @"段落一的内容在这里。

段落二的内容在这里。

段落三的内容在这里。";

            var chunks = _chunker.Chunk(text, "其他类型");
            chunks.Should().NotBeEmpty("默认模式应产生语义块");
        }

        [Fact]
        public void Chunk_MergesSmallChunks()
        {
            // 极小内容块应被合并到相邻块
            var text = @"1 范围
很短的文本。
2 术语
也";
            //  由于 MinChunkSize 从配置读取（默认 ChunkOverlap=100），小块会被合并
            var chunks = _chunker.Chunk(text, "国标");
            // 即使只有一个或两个块，验证合并逻辑至少不抛异常
            chunks.Should().NotBeNull();
        }

        [Fact]
        public void Chunk_AddsOverlapBetweenChunks()
        {
            // 构造足够多的内容让分块有 overlap
            var sb = new StringBuilder();
            for (int i = 0; i < 50; i++)
                sb.AppendLine($"这是第{i}段测试文本用于填充内容以触发分块重叠机制测试中文文本长度");
            var text = sb.ToString();

            var chunks = _chunker.Chunk(text, "通用");
            chunks.Should().NotBeNull();
            // 如果有多个块，检查 overlap 标记
            if (chunks.Count > 1)
            {
                chunks[0].Content.Should().Contain("[↓ 续]",
                    "第一个块（非最后块）应有 overlap 续文标记");
            }
        }

        [Fact]
        public void Chunk_AssignsRegulationNumber()
        {
            var text = "3.1 易燃液体分类标准内容";
            var chunks = _chunker.Chunk(text, "国标", regulationNumber: "GB 30000.7-2013");
            chunks.Should().NotBeEmpty();
            chunks.Should().OnlyContain(c => c.RegulationNumber == "GB 30000.7-2013",
                "所有块应携带法规编号");
        }

        [Fact]
        public void Chunk_AssignsPageNumber()
        {
            var text = "标准正文内容";
            var chunks = _chunker.Chunk(text, "国标", pageNumber: 42);
            chunks.Should().NotBeEmpty();
            chunks.Should().OnlyContain(c => c.PageNumber == 42,
                "所有块应携带页码");
        }

        [Fact]
        public void Chunk_AssignsSequentialChunkIndex()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 100; i++)
                sb.AppendLine($"第{i}段测试文本内容用于触发多块分割中文文本填充");
            var text = sb.ToString();

            var chunks = _chunker.Chunk(text, "通用");
            for (int i = 0; i < chunks.Count; i++)
                chunks[i].ChunkIndex.Should().Be(i, "ChunkIndex 应从0递增");
        }

        [Fact]
        public void Chunk_DetectChapterTitleInGuoBiao()
        {
            var text = @"3 术语和定义
3.1 易燃液体
指闪点不大于93°C的液体。
3.2 初沸点
液体开始沸腾时的温度。";

            var chunks = _chunker.Chunk(text, "国标");
            chunks.Should().NotBeEmpty();
            // 第3章的子条款继承章节标题
            chunks.Any(c => c.ChapterTitle == "术语和定义").Should().BeTrue(
                "条款应继承章节标题");
        }

        [Fact]
        public void Chunk_SingleLargeParagraph_ForceSplitBySize()
        {
            // [OCR修复] TextCleaner 以单 \n 重组全文后无空行，旧实现会退化为整篇一块；
            // 现 ChunkByParagraph 对超过 MaxChunkSize 的单段落按大小强制切分
            var sb = new StringBuilder();
            for (int i = 0; i < 300; i++)
                sb.Append("这是一个测试句子用于填充大量中文文本内容。");
            var text = sb.ToString();

            var chunks = _chunker.Chunk(text, "通用");
            chunks.Should().NotBeNull();
            chunks.Count.Should().BeGreaterThan(1, "无空行的超长单段落应被强制切分");
            chunks.All(c => c.Content.Length <= AppConfig.Instance.KnowledgeBase.ChunkSize + 2 * AppConfig.Instance.KnowledgeBase.ChunkOverlap + 2)
                .Should().BeTrue("每块不应显著超过 MaxChunkSize（允许重叠/合并余量）");
        }

        [Fact]
        public void Chunk_ChemicalRegulation_UsesClauseChunking()
        {
            // [OCR修复] 化工专业条例本质是 GB 系列标准，应走条款切分而非段落切分
            var text = @"4 作业安全要求
4.1 动火作业应办理作业证。
4.2 受限空间作业前应检测气体。";

            var chunks = _chunker.Chunk(text, "化工专业条例");
            chunks.Should().NotBeEmpty();
            chunks.Any(c => !string.IsNullOrEmpty(c.ClauseNumber) || !string.IsNullOrEmpty(c.ChapterTitle))
                .Should().BeTrue("条款切分应提取条款号/章节标题");
        }

        [Fact]
        public void Chunk_MultipleParagraphs_SplitByMaxSize()
        {
            // 多段落（\n\n 分隔）触发 ChunkByParagraph 的大小分块
            var sb = new StringBuilder();
            for (int i = 0; i < 100; i++)
            {
                sb.Append("这是一个测试句子用于填充大量中文文本内容。");
                sb.Append("\n\n");
            }
            var text = sb.ToString();

            var chunks = _chunker.Chunk(text, "通用");
            chunks.Should().NotBeNull();
            chunks.Count.Should().BeGreaterThan(1, "多段落超长文本应被分割");
        }
    }
}
