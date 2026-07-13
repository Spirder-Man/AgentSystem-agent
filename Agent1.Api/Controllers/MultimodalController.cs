using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Agent1.Config;
using Agent1.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agent1.Api.Controllers;

/// <summary>
/// 多模态视觉分析 API — GHS 标签识别、储罐/管道场景分析。
/// 覆盖控制台功能：#15 多模态视觉分析 [GHS标签/储罐照片]。
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "Auditor")]
public class MultimodalController : ControllerBase
{
    private readonly ILogger<MultimodalController> _logger;

    public MultimodalController(ILogger<MultimodalController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 上传图片进行多模态视觉分析。
    /// 支持 GHS 危险标签识别、储罐/管道场景合规检查、自定义分析。
    /// </summary>
    /// <param name="image">图片文件 (jpg/png/webp)</param>
    /// <param name="analysisType">分析类型: hazard-label | storage-scene | custom</param>
    /// <param name="customPrompt">自定义分析提示词 (仅 analysisType=custom 时使用)</param>
    [HttpPost("analyze")]
    [RequestSizeLimit(20_000_000)] // 20MB max
    public async Task<IActionResult> Analyze(
        [Required] IFormFile image,
        [FromForm] string analysisType = "hazard-label",
        [FromForm] string? customPrompt = null)
    {
        if (image == null || image.Length == 0)
            return BadRequest(new { error = "请上传图片文件" });

        // 验证文件类型
        var ext = Path.GetExtension(image.FileName).ToLower();
        if (ext is not ".jpg" and not ".jpeg" and not ".png" and not ".webp")
            return BadRequest(new { error = $"不支持的图片格式 '{ext}'，请上传 jpg/png/webp" });

        var validTypes = new[] { "hazard-label", "storage-scene", "custom" };
        if (!validTypes.Contains(analysisType))
        {
            return BadRequest(new
            {
                error = $"无效的分析类型 '{analysisType}'",
                supported = validTypes
            });
        }

        // 保存临时文件（MultimodalService 需要文件路径）
        var tempDir = Path.Combine(Path.GetTempPath(), "agent1-multimodal");
        Directory.CreateDirectory(tempDir);
        var tempPath = Path.Combine(tempDir, $"{Guid.NewGuid():N}{ext}");

        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.Create))
            {
                await image.CopyToAsync(stream);
            }

            var sw = Stopwatch.StartNew();
            using var multimodal = new MultimodalService();

            string result;
            switch (analysisType)
            {
                case "storage-scene":
                    result = await multimodal.AnalyzeStorageSceneAsync(tempPath);
                    break;
                case "custom":
                    var prompt = string.IsNullOrWhiteSpace(customPrompt)
                        ? "你是化工安全专家。请详细分析这张图片，识别其中与化工安全、危化品管理、设备合规相关的内容。如果没有化工安全相关内容，请简要描述图片内容。必须使用中文回复。"
                        : customPrompt;
                    result = await multimodal.AnalyzeImageAsync(tempPath, prompt);
                    break;
                default: // hazard-label
                    result = await multimodal.AnalyzeHazardLabelAsync(tempPath);
                    break;
            }

            sw.Stop();

            _logger.LogInformation(
                "多模态分析完成: 类型={Type}, 文件={File}, 耗时={Elapsed}ms",
                analysisType, image.FileName, sw.ElapsedMilliseconds);

            return Ok(new
            {
                fileName = image.FileName,
                fileSize = image.Length,
                analysisType,
                model = ModelConfig.MultimodalModelId,
                elapsedMs = sw.ElapsedMilliseconds,
                result
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "多模态分析失败: {File}", image.FileName);
            return StatusCode(500, new { error = $"分析失败: {ex.Message}" });
        }
        finally
        {
            // 清理临时文件
            try { System.IO.File.Delete(tempPath); } catch { /* 忽略清理失败 */ }
        }
    }
}
