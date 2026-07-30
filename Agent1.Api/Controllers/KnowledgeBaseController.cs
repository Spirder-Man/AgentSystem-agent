using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Agent1.Config;
using Agent1.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Agent1.Api.Controllers;

/// <summary>
/// 知识库管理 API — 检索模式切换、RAG 测试、增量更新。
/// 覆盖控制台功能：#9 化工合规RAG测试、#11 切换检索模式、#16 知识库增量更新。
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "Viewer")]
public class KnowledgeBaseController : ControllerBase
{
    private readonly ChemicalRAG _chemicalRAG;
    private readonly IKnowledgeBaseService _knowledgeBase;
    private readonly ILogger<KnowledgeBaseController> _logger;

    public KnowledgeBaseController(
        ChemicalRAG chemicalRAG,
        IKnowledgeBaseService knowledgeBase,
        ILogger<KnowledgeBaseController> logger)
    {
        _chemicalRAG = chemicalRAG;
        _knowledgeBase = knowledgeBase;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════
    // #11 切换检索模式
    // ═══════════════════════════════════════════════

    /// <summary>获取当前检索模式</summary>
    [HttpGet("search-mode")]
    public IActionResult GetSearchMode()
    {
        var mode = AppConfig.Instance.KnowledgeBase.SearchMode;
        return Ok(new
        {
            mode = mode.ToString(),
            available = new[] { "Bm25", "Vector", "Hybrid" },
            description = mode switch
            {
                SearchModeType.Bm25 => "关键词匹配 — 精确但语义能力弱",
                SearchModeType.Vector => "向量语义 — 扩展性好但可能召回不相关文档",
                SearchModeType.Hybrid => "混合检索（默认）— 兼顾精确与语义",
                _ => "未知模式"
            }
        });
    }

    /// <summary>切换检索模式（仅当前会话有效）</summary>
    [HttpPut("search-mode")]
    public IActionResult SetSearchMode([FromBody] SearchModeRequest request)
    {
        if (!Enum.TryParse<SearchModeType>(request.Mode, true, out var newMode))
        {
            return BadRequest(new { error = $"无效的检索模式 '{request.Mode}'，支持: Bm25 / Vector / Hybrid" });
        }

        var oldMode = AppConfig.Instance.KnowledgeBase.SearchMode;
        AppConfig.Instance.KnowledgeBase.SearchMode = newMode;

        _logger.LogInformation("检索模式切换: {OldMode} → {NewMode}", oldMode, newMode);

        return Ok(new
        {
            previous = oldMode.ToString(),
            current = newMode.ToString(),
            warning = "此更改仅当前进程有效，重启后恢复默认 Hybrid 模式"
        });
    }

    // ═══════════════════════════════════════════════
    // #9 化工合规 RAG 测试
    // ═══════════════════════════════════════════════

    /// <summary>执行 RAG 检索测试，验证知识库检索可用性</summary>
    [HttpPost("rag-test")]
    public async Task<IActionResult> RagTest([FromBody] RagTestRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return BadRequest(new { error = "请输入查询关键词" });
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var chunks = await _chemicalRAG.SearchAsync(request.Query, topK: request.TopK > 0 ? request.TopK : 5);
            sw.Stop();

            return Ok(new
            {
                query = request.Query,
                mode = AppConfig.Instance.KnowledgeBase.SearchMode.ToString(),
                totalResults = chunks.Count,
                elapsedMs = sw.ElapsedMilliseconds,
                results = chunks.Select(c => new
                {
                    c.Id,
                    c.Content,
                    c.Score,
                    c.Rank,
                    c.RetrievalMethod,
                    // [Bug-043 FIX] 读规范键 MetadataKeys.SourceFile（miss 时回退历史小写别名）；
                    // importance 概念映射到 Priority —— 旧代码读从未被写入的 "importance" 恒落默认值
                    source = c.Metadata.TryGetValue(MetadataKeys.SourceFile, out var src) ? src
                        : c.Metadata.TryGetValue(MetadataKeys.LegacySourceLower, out var srcLegacy) ? srcLegacy
                        : "未知",
                    importance = c.Metadata.TryGetValue(MetadataKeys.Priority, out var imp) ? imp : "未标注"
                }),
                summary = chunks.Count > 0
                    ? $"检索到 {chunks.Count} 条相关文档成果，耗时 {sw.ElapsedMilliseconds}ms"
                    : "未检索到相关文档，请检查知识库是否已加载。"
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "RAG 检索测试失败: {Query}", request.Query);
            return StatusCode(500, new { error = $"检索失败: {ex.Message}", elapsedMs = sw.ElapsedMilliseconds });
        }
    }

    // ═══════════════════════════════════════════════
    // #16 知识库增量更新
    // ═══════════════════════════════════════════════

    /// <summary>
    /// 触发知识库增量更新 — 仅处理新增/修改/删除的文件。
    /// 大型知识库操作可能耗时，前端建议设置较长超时。
    /// </summary>
    [HttpPost("incremental-load")]
    [Authorize(Policy = "Auditor")]
    public async Task<IActionResult> IncrementalLoad()
    {
        var sw = Stopwatch.StartNew();
        // [Bug-039 FIX ②] 绑定停机信号：SIGTERM 后增量在文件边界安全收手，杜绝临终遗写僵尸行。
        var lifetime = HttpContext.RequestServices.GetService<IHostApplicationLifetime>();
        var stopToken = lifetime?.ApplicationStopping ?? HttpContext.RequestAborted;
        try
        {
            var docCountBefore = _knowledgeBase.GetDocumentCount();
            await _chemicalRAG.LoadKnowledgeBaseIncrementalAsync(stopToken);
            var docCountAfter = _knowledgeBase.GetDocumentCount();
            sw.Stop();

            _logger.LogInformation(
                "知识库增量更新完成: {Before} → {After} 文档, 耗时 {Elapsed}ms",
                docCountBefore, docCountAfter, sw.ElapsedMilliseconds);

            return Ok(new
            {
                documentCountBefore = docCountBefore,
                documentCountAfter = docCountAfter,
                elapsedMs = sw.ElapsedMilliseconds,
                message = $"增量更新完成，文档数 {docCountBefore} → {docCountAfter}"
            });
        }
        // [Bug-039 FIX ③] 已有增量在运行 → 409 Conflict，客户端可退避重试，避免并发撞 source_path UNIQUE。
        catch (IncrementalAlreadyRunningException ex)
        {
            sw.Stop();
            _logger.LogWarning("知识库增量更新被拒绝：已有任务运行中");
            return Conflict(new { error = ex.Message, elapsedMs = sw.ElapsedMilliseconds });
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "知识库增量更新失败");
            return StatusCode(500, new { error = $"增量更新失败: {ex.Message}", elapsedMs = sw.ElapsedMilliseconds });
        }
    }
}

// ═══════════════════════════════════════════════
// 请求/响应 DTO
// ═══════════════════════════════════════════════

public record SearchModeRequest(
    [Required] string Mode
);

public record RagTestRequest(
    [Required] string Query,
    int TopK = 5
);
