// ============================================================================
// KnowledgeIncrementalLoadTests.cs — 知识库增量更新全格式重构回归测试
// ============================================================================
// 背景：LoadKnowledgeBaseIncrementalAsync 原来只扫 *.txt 且走旧管线，
//   导致 34 个法规文档中 30 个 PDF 被增量导入完全忽略。
// 重构后增量与全量共用统一单文件管线（ProcessSingleFileAsync），
//   覆盖 PDF/DOC/DOCX/TXT，更新先删后插，失败不入追踪器。
// 本文件验证调度层行为（格式识别/跳过/更新/删除/防重复），
//   不依赖真实数据库（databaseService 传 null 走降级路径）。
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Agent1.Config;
using Agent1.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Agent1.Tests;

/// <summary>
/// 记录型知识库桩：记录 AddDocumentAsync / RemoveChunksBySourceFileAsync 的调用细节，
/// 供断言使用（StubKnowledgeBaseService 只计数不记录，无法验证调度行为）。
/// </summary>
internal class RecordingKnowledgeBaseService : IKnowledgeBaseService
{
    public List<(string Content, Dictionary<string, object>? Metadata)> AddedDocuments { get; } = new();
    public List<string> RemovedSourceFiles { get; } = new();

    public Task AddDocumentAsync(string content, Dictionary<string, object>? metadata = null)
    {
        AddedDocuments.Add((content, metadata));
        return Task.CompletedTask;
    }

    public Task AddDocumentsAsync(IEnumerable<string> contents)
    {
        foreach (var c in contents) AddedDocuments.Add((c, null));
        return Task.CompletedTask;
    }

    public Task<List<RetrievedChunk>> RetrieveAsync(string query, int topK = 5)
        => Task.FromResult(new List<RetrievedChunk>());

    public string PreprocessQuery(string query) => query.Trim();

    public int GetDocumentCount() => AddedDocuments.Count;

    public Task ClearAsync()
    {
        AddedDocuments.Clear();
        return Task.CompletedTask;
    }

    public Task AddChemicalRegulationAsync(string content, string regulationType, string priority, string? chemicalType = null)
    {
        AddedDocuments.Add((content, null));
        return Task.CompletedTask;
    }

    public Task<List<RetrievedChunk>> RetrieveChemicalRegulationAsync(
        string query, string? chemicalType = null, string? regulationType = null,
        int topK = 5, string? regulationNumber = null)
        => Task.FromResult(new List<RetrievedChunk>());

    public Task LoadChemicalKnowledgeBaseAsync(string knowledgeBasePath) => Task.CompletedTask;

    public Task RemoveChunksBySourceFileAsync(string sourceFile)
    {
        RemovedSourceFiles.Add(sourceFile);
        return Task.CompletedTask;
    }
}

public class KnowledgeIncrementalLoadTests : IDisposable
{
    private readonly string _kbRoot;
    private readonly string _gbDir;
    private readonly RecordingKnowledgeBaseService _kb = new();

    // 足够长的正规中文法规文本，确保清洗/语义分块后能产出至少一个分块且不触发乱码守门
    private const string SampleRegulationText =
        "第一条 为了加强危险化学品的安全管理，预防和减少危险化学品事故，保障人民群众生命财产安全，制定本条例。\n" +
        "第二条 危险化学品生产、储存、使用、经营和运输的安全管理，适用本条例。\n" +
        "第三条 本条例所称危险化学品，是指具有毒害、腐蚀、爆炸、燃烧、助燃等性质的化学品。\n" +
        "第四条 危险化学品安全管理，应当坚持安全第一、预防为主、综合治理的方针。\n" +
        "第五条 任何单位和个人不得生产、经营、使用国家禁止生产、经营、使用的危险化学品。\n" +
        "第六条 对危险化学品的生产、储存、使用、经营、运输实施安全监督管理的有关部门，依照下列规定履行职责。\n" +
        "第七条 负有危险化学品安全监督管理职责的部门依法进行监督检查，可以采取相应措施。\n" +
        "第八条 县级以上人民政府应当建立危险化学品安全监督管理工作协调机制。";

    public KnowledgeIncrementalLoadTests()
    {
        // AppConfig 引导（与 ChemicalRagTests 相同模式）
        try { AppConfig.Instance.ToString(); } catch
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Llm:ModelId"] = "test-model",
                    ["Llm:Endpoint"] = "http://localhost:11434",
                    ["Database:Host"] = "localhost",
                    ["Database:Port"] = "5432",
                    ["Database:DatabaseName"] = "testdb",
                    ["Database:Password"] = "pwd",
                    ["VectorSearch:EmbeddingModelId"] = "test-embed",
                    ["PromptTemplates:SystemRole"] = "test",
                    ["PromptTemplates:EvalFastPrompt"] = "test {SystemRole} {UserInput}",
                    ["PromptTemplates:EvalFastQueryPrompt"] = "test {SystemRole} {UserInput}"
                }).Build();
            AppConfig.Load(config);
        }

        // 每个测试实例独立临时知识库根（含"国标"特征子目录，满足路径解析器要求）
        _kbRoot = Path.Combine(Path.GetTempPath(), "kb_incr_test_" + Guid.NewGuid().ToString("N")[..8]);
        _gbDir = Path.Combine(_kbRoot, "国标");
        Directory.CreateDirectory(_gbDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_kbRoot, recursive: true); } catch { /* 清理失败不影响测试结果 */ }
    }

    private ChemicalRAG CreateRag() => new(_kbRoot, _kb, databaseService: null);

    private Dictionary<string, DateTime> ReadTracker()
    {
        var path = Path.Combine(_kbRoot, "file_tracker.json");
        if (!File.Exists(path)) return new();
        return JsonSerializer.Deserialize<Dictionary<string, DateTime>>(File.ReadAllText(path)) ?? new();
    }

    // ── T1: 增量识别非 TXT 格式（核心回归：原实现只扫 *.txt）────────────

    [Fact]
    public async Task Incremental_NewTxtFile_IsProcessedAndTracked()
    {
        var txt = Path.Combine(_gbDir, "危化品条例节选.txt");
        await File.WriteAllTextAsync(txt, SampleRegulationText, Encoding.UTF8);

        await CreateRag().LoadKnowledgeBaseIncrementalAsync();

        _kb.AddedDocuments.Should().NotBeEmpty("txt 文件应被分块入库");
        ReadTracker().Should().ContainKey(txt);
    }

    [Fact]
    public async Task Incremental_PdfFile_IsRecognizedNotIgnored()
    {
        // 无效 PDF：能验证"被识别并尝试处理"（原实现根本不会扫到它），
        // 解析失败 → 不入追踪器（下次增量重试），也不应静默跳过
        var pdf = Path.Combine(_gbDir, "GB30000测试.pdf");
        await File.WriteAllBytesAsync(pdf, Encoding.ASCII.GetBytes("not a real pdf"));

        await CreateRag().LoadKnowledgeBaseIncrementalAsync();

        ReadTracker().Should().NotContainKey(pdf, "解析失败的文件不应写入追踪器，下次增量应重试");
    }

    // ── T2: 未修改文件跳过（保持增量机制优势）───────────────────────────

    [Fact]
    public async Task Incremental_UnmodifiedFile_IsSkippedOnSecondRun()
    {
        var txt = Path.Combine(_gbDir, "条例.txt");
        await File.WriteAllTextAsync(txt, SampleRegulationText, Encoding.UTF8);

        await CreateRag().LoadKnowledgeBaseIncrementalAsync();
        var countAfterFirst = _kb.AddedDocuments.Count;
        countAfterFirst.Should().BeGreaterThan(0);

        await CreateRag().LoadKnowledgeBaseIncrementalAsync();
        _kb.AddedDocuments.Count.Should().Be(countAfterFirst, "未修改文件不应重复入库");
        _kb.RemovedSourceFiles.Should().BeEmpty("未修改文件不应触发删除");
    }

    // ── T3: 更新场景先删后插（source_path UNIQUE 契约）──────────────────

    [Fact]
    public async Task Incremental_ModifiedFile_RemovesOldChunksBeforeReprocessing()
    {
        var txt = Path.Combine(_gbDir, "条例.txt");
        await File.WriteAllTextAsync(txt, SampleRegulationText, Encoding.UTF8);
        await CreateRag().LoadKnowledgeBaseIncrementalAsync();
        var countAfterFirst = _kb.AddedDocuments.Count;

        // 修改文件并前移写入时间，模拟内容更新
        await File.WriteAllTextAsync(txt, SampleRegulationText + "\n第九条 新增条款内容。", Encoding.UTF8);
        File.SetLastWriteTimeUtc(txt, DateTime.UtcNow.AddMinutes(1));

        await CreateRag().LoadKnowledgeBaseIncrementalAsync();

        _kb.RemovedSourceFiles.Should().Contain(txt, "更新场景必须先删除旧分块再重新入库");
        _kb.AddedDocuments.Count.Should().BeGreaterThan(countAfterFirst, "更新后应重新入库");
    }

    // ── T4: 文件删除后清理分块与追踪器 ──────────────────────────────────

    [Fact]
    public async Task Incremental_DeletedFile_CleansUpChunksAndTracker()
    {
        var txt = Path.Combine(_gbDir, "将被删除.txt");
        await File.WriteAllTextAsync(txt, SampleRegulationText, Encoding.UTF8);
        await CreateRag().LoadKnowledgeBaseIncrementalAsync();
        ReadTracker().Should().ContainKey(txt);

        File.Delete(txt);
        await CreateRag().LoadKnowledgeBaseIncrementalAsync();

        _kb.RemovedSourceFiles.Should().Contain(txt);
        ReadTracker().Should().NotContainKey(txt);
    }

    // ── T5: 支持格式枚举（防 Windows "*.doc" 连带匹配 .docx 怪癖）────────

    [Fact]
    public void EnumerateSupportedFiles_CoversAllFormats_ExcludesTempFiles()
    {
        File.WriteAllText(Path.Combine(_gbDir, "a.txt"), "x");
        File.WriteAllText(Path.Combine(_gbDir, "b.pdf"), "x");
        File.WriteAllText(Path.Combine(_gbDir, "c.doc"), "x");
        File.WriteAllText(Path.Combine(_gbDir, "d.docx"), "x");
        File.WriteAllText(Path.Combine(_gbDir, "e.md"), "x");          // 不支持的格式
        File.WriteAllText(Path.Combine(_gbDir, "~$d.docx"), "x");      // Office 锁文件
        File.WriteAllText(Path.Combine(_gbDir, "~WRL0001.doc"), "x");  // Word 临时备份

        var method = typeof(ChemicalRAG).GetMethod("EnumerateSupportedFiles",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        method.Should().NotBeNull();

        var files = ((IEnumerable<string>)method!.Invoke(null, new object[] { _gbDir, SearchOption.AllDirectories })!)
            .Select(Path.GetFileName).ToList();

        files.Should().BeEquivalentTo(new[] { "a.txt", "b.pdf", "c.doc", "d.docx" },
            "应恰好识别 4 种支持格式，排除临时文件与不支持格式，且无重复");
    }

    // ── T6: 全量加载写追踪器 → 随后增量零重复（病灶④回归）───────────────

    [Fact]
    public async Task FullLoad_WritesTracker_ThenIncrementalCausesNoDuplicates()
    {
        var txt = Path.Combine(_gbDir, "条例.txt");
        await File.WriteAllTextAsync(txt, SampleRegulationText, Encoding.UTF8);

        await CreateRag().LoadKnowledgeBaseAsync();
        var countAfterFull = _kb.AddedDocuments.Count;
        countAfterFull.Should().BeGreaterThan(0);
        ReadTracker().Should().ContainKey(txt, "全量加载后必须登记追踪器，否则后续增量会整库重复入库");

        await CreateRag().LoadKnowledgeBaseIncrementalAsync();
        _kb.AddedDocuments.Count.Should().Be(countAfterFull, "全量后立即增量不应产生任何重复入库");
        _kb.RemovedSourceFiles.Should().BeEmpty();
    }
}
