using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Agent1.Config;
using Agent1.Models;
using Agent1.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agent1.Api.Controllers;

/// <summary>
/// 诊断验证 API — 工具调用诊断。
/// 覆盖控制台功能：#12 工具调用诊断验证。
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "Viewer")]
public class DiagnosticsController : ControllerBase
{
    private readonly AgentDialog _agentDialog;
    private readonly ILogger<DiagnosticsController> _logger;

    // 预置诊断用例集
    private static readonly (string Query, string ExpectedTools, string Description)[] PresetTests =
    {
        ("苯属于什么危险类别", "CheckHazardCategory", "危化品类别查询"),
        ("苯和丙酮能同库储存吗", "CheckStorageCompatibility", "储存兼容性检查"),
        ("甲类仓库与明火点的安全距离是多少", "GetSafetyDistance", "安全距离查询"),
        ("现在几点", "GetCurrentTime", "时间查询"),
        ("甲醇和硝酸存放在同一个仓库是否合规", "CheckHazardCategory,CheckStorageCompatibility", "多工具联合调用"),
    };

    public DiagnosticsController(AgentDialog agentDialog, ILogger<DiagnosticsController> logger)
    {
        _agentDialog = agentDialog;
        _logger = logger;
    }

    /// <summary>
    /// 执行工具调用诊断 — 运行 5 条预置用例，验证 LLM Function Calling 是否正常触发。
    /// 每条用例间隔 1s 避免 LLM 并发拥塞。
    /// </summary>
    [HttpPost("tool-calling")]
    public async Task<IActionResult> RunToolCallingDiagnostics()
    {
        var sw = Stopwatch.StartNew();
        var results = new List<object>();
        int pass = 0;

        var session = _agentDialog.CreateSession(SessionType.ChemicalCompliance);

        for (int i = 0; i < PresetTests.Length; i++)
        {
            var test = PresetTests[i];
            var caseSw = Stopwatch.StartNew();
            try
            {
                var result = await _agentDialog.ExecuteAsync(test.Query, session);
                caseSw.Stop();

                bool triggered = result.ToolCalls.Count > 0;
                if (triggered) pass++;

                results.Add(new
                {
                    index = i + 1,
                    query = test.Query,
                    description = test.Description,
                    expectedTools = test.ExpectedTools,
                    toolCalls = result.ToolCalls.Select(tc => tc.FunctionName).ToList(),
                    triggered,
                    elapsedMs = caseSw.ElapsedMilliseconds
                });
            }
            catch (Exception ex)
            {
                caseSw.Stop();
                results.Add(new
                {
                    index = i + 1,
                    query = test.Query,
                    description = test.Description,
                    expectedTools = test.ExpectedTools,
                    triggered = false,
                    error = ex.Message,
                    elapsedMs = caseSw.ElapsedMilliseconds
                });
                _logger.LogWarning(ex, "诊断用例失败 [{Index}/{Total}]: {Description}", i + 1, PresetTests.Length, test.Description);
            }

            // 间隔 1s 避免 GPU 并发拥塞
            if (i < PresetTests.Length - 1)
                await Task.Delay(1000);
        }

        sw.Stop();

        return Ok(new
        {
            model = ModelConfig.ModelId,
            total = PresetTests.Length,
            pass,
            passRate = $"{pass}/{PresetTests.Length}",
            elapsedMs = sw.ElapsedMilliseconds,
            results
        });
    }
}
