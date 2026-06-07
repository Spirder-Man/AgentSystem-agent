using Agent1;
using Agent1.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agent1.Api.Controllers;

/// <summary>
/// 化工合规审核 API
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "Auditor")]
public class ComplianceController : ControllerBase
{
    private readonly AgentDialog _agentDialog;
    private readonly ILlmService _llmService;
    private readonly IKnowledgeBaseService _knowledgeBaseService;
    private readonly IAuditService _auditService;
    private readonly ILogger<ComplianceController> _logger;

    public ComplianceController(
        AgentDialog agentDialog,
        ILlmService llmService,
        IKnowledgeBaseService knowledgeBaseService,
        IAuditService auditService,
        ILogger<ComplianceController> logger)
    {
        _agentDialog = agentDialog;
        _llmService = llmService;
        _knowledgeBaseService = knowledgeBaseService;
        _auditService = auditService;
        _logger = logger;
    }

    /// <summary>
    /// 化工合规审核 — 提交查询并返回合规判断
    /// </summary>
    [HttpPost("check")]
    public async Task<IActionResult> CheckCompliance([FromBody] ComplianceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return BadRequest(new { error = "查询内容不能为空" });

        _logger.LogInformation("合规审核请求: {Query}", SensitiveDataMasker.MaskChemicalQuery(request.Query));

        try
        {
            var session = _agentDialog.CreateSession(SessionType.ChemicalCompliance);
            var response = await _agentDialog.ExecuteEvalFastAsync(request.Query);

            // Post-hoc 验证
            var llmSvc = _llmService as LlmService;
            var toolCalls = llmSvc?.LastFunctionCalls ?? new List<FunctionCallRecord>();
            var verification = await ConclusionVerifier.VerifyAsync(
                response ?? "", toolCalls, _knowledgeBaseService, request.Query);

            // 审计日志 (Task 3: isSensitive=true 触发脱敏)
            await _auditService.LogOperationAsync(
                GetCurrentUsername(), "合规审核",
                $"查询: {request.Query} | 工具: [{string.Join(",", toolCalls.Select(t => t.FunctionName))}] | " +
                $"验证法规: {verification.VerifiedRegulations.Count}条 | 幻觉法规: {verification.HallucinatedRegulations.Count}条",
                isSensitive: true);

            return Ok(new ComplianceResponse
            {
                Query = request.Query,
                Response = response,
                ToolsUsed = toolCalls.Select(t => t.FunctionName).ToList(),
                VerifiedRegulations = verification.VerifiedRegulations,
                HallucinatedRegulations = verification.HallucinatedRegulations,
                Warnings = verification.Warnings
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "合规审核失败: {Query}", SensitiveDataMasker.MaskChemicalQuery(request.Query));
            return StatusCode(500, new { error = "审核处理失败，请稍后重试" });
        }
    }

    /// <summary>
    /// 危化品信息查询 — 查询指定化学品的危险类别/标准
    /// </summary>
    [HttpPost("hazard/query")]
    public async Task<IActionResult> QueryHazard([FromBody] HazardQueryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SubstanceName))
            return BadRequest(new { error = "化学品名称不能为空" });

        _logger.LogInformation("危化品查询: {Substance}", SensitiveDataMasker.MaskChemicalQuery(request.SubstanceName));

        try
        {
            var response = await _agentDialog.ExecuteEvalFastQueryAsync(
                $"{request.SubstanceName} 属于什么危险类别 适用国标");

            var llmSvc = _llmService as LlmService;
            var toolCalls = llmSvc?.LastFunctionCalls ?? new List<FunctionCallRecord>();

            await _auditService.LogOperationAsync(
                GetCurrentUsername(), "危化品查询",
                $"化学品: {request.SubstanceName} | 工具: [{string.Join(",", toolCalls.Select(t => t.FunctionName))}]",
                isSensitive: true);

            return Ok(new HazardQueryResponse
            {
                SubstanceName = request.SubstanceName,
                Response = response,
                ToolsUsed = toolCalls.Select(t => t.FunctionName).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "危化品查询失败: {Substance}", SensitiveDataMasker.MaskChemicalQuery(request.SubstanceName));
            return StatusCode(500, new { error = "查询处理失败，请稍后重试" });
        }
    }

    /// <summary>
    /// 储存兼容性检查
    /// </summary>
    [HttpPost("storage/compatibility")]
    public async Task<IActionResult> CheckStorageCompatibility([FromBody] StorageCompatibilityRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SubstanceA) || string.IsNullOrWhiteSpace(request.SubstanceB))
            return BadRequest(new { error = "两种化学品名称均不能为空" });

        _logger.LogInformation("储存兼容性检查: {A} vs {B}",
            SensitiveDataMasker.MaskChemicalQuery(request.SubstanceA),
            SensitiveDataMasker.MaskChemicalQuery(request.SubstanceB));

        try
        {
            var query = $"{request.SubstanceA} 和 {request.SubstanceB} 能同库储存吗";
            var response = await _agentDialog.ExecuteEvalFastAsync(query);

            var llmSvc = _llmService as LlmService;
            var toolCalls = llmSvc?.LastFunctionCalls ?? new List<FunctionCallRecord>();

            await _auditService.LogOperationAsync(
                GetCurrentUsername(), "储存兼容性",
                $"{request.SubstanceA} vs {request.SubstanceB} | 工具: [{string.Join(",", toolCalls.Select(t => t.FunctionName))}]",
                isSensitive: true);

            return Ok(new StorageCompatibilityResponse
            {
                SubstanceA = request.SubstanceA,
                SubstanceB = request.SubstanceB,
                Response = response,
                ToolsUsed = toolCalls.Select(t => t.FunctionName).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "储存兼容性检查失败: {A} vs {B}",
                SensitiveDataMasker.MaskChemicalQuery(request.SubstanceA),
                SensitiveDataMasker.MaskChemicalQuery(request.SubstanceB));
            return StatusCode(500, new { error = "兼容性检查失败，请稍后重试" });
        }
    }

    /// <summary>
    /// 从 JWT Claims 提取当前用户名
    /// </summary>
    private string GetCurrentUsername()
    {
        return User.Identity?.Name ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "anonymous";
    }
}

// ── 请求/响应模型 ──

public record ComplianceRequest(string Query);

public record ComplianceResponse
{
    public string Query { get; init; } = "";
    public string? Response { get; init; }
    public List<string> ToolsUsed { get; init; } = new();
    public List<string> VerifiedRegulations { get; init; } = new();
    public List<string> HallucinatedRegulations { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
}

public record HazardQueryRequest(string SubstanceName);
public record HazardQueryResponse
{
    public string SubstanceName { get; init; } = "";
    public string? Response { get; init; }
    public List<string> ToolsUsed { get; init; } = new();
}

public record StorageCompatibilityRequest(string SubstanceA, string SubstanceB);
public record StorageCompatibilityResponse
{
    public string SubstanceA { get; init; } = "";
    public string SubstanceB { get; init; } = "";
    public string? Response { get; init; }
    public List<string> ToolsUsed { get; init; } = new();
}
