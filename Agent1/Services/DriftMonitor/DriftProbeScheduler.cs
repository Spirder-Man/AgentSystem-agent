using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agent1.Config;
using Microsoft.Extensions.Hosting;

namespace Agent1.Services.DriftMonitor;

/// <summary>
/// 认知漂移监测·探针调度器（Phase 3）—— 定期把黄金问题喂给被测 LLM，自动测量。
///
/// 职责（一次轮询）：
///   1. 取启用的探针模板（drift_probe_templates）
///   2. 按"上次测量时间升序"取 MaxPerRun 条——优先测最久没测的血管
///   3. 用 ILlmService.GenerateSimpleResponseAsync 问被测 LLM（纯记忆回答，
///      无工具调用——测的是 LLM 对项目的认知，不是检索能力）
///   4. 回答交给 DriftMonitor.RecordProbeAsync 强制断言测量，落库
///
/// 抗故障设计：
///   · 模板表不存在（006 未迁移）→ 静默跳过，不崩
///   · LLM 连续失败 MaxConsecutiveFailures 次 → 退避 3 倍间隔再试
///   · 单条探针失败不影响整轮（各自 try/catch）
/// </summary>
public class DriftProbeScheduler : BackgroundService
{
    private readonly DriftMonitor _monitor;
    private readonly ILlmService _llm;
    private readonly DriftMonitorConfig _config;

    public DriftProbeScheduler(DriftMonitor monitor, ILlmService llm, DriftMonitorConfig config)
    {
        _monitor = monitor;
        _llm = llm;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.Enabled)
        {
            Serilog.Log.Information("[DriftProbe] 调度器已禁用（DriftMonitor:Enabled=false），不启动探针轮询");
            return;
        }

        Serilog.Log.Information(
            "[DriftProbe] 探针调度器已启动 | 间隔={Interval}min | 每轮={MaxPerRun}条 | 首轮延迟={Delay}min",
            _config.IntervalMinutes, _config.MaxPerRun, _config.StartupDelayMinutes);

        var consecutiveFailures = 0;

        // 首轮延迟：等 LLM / DB 就绪（宿主启动高峰后再测量）
        try { await Task.Delay(TimeSpan.FromMinutes(_config.StartupDelayMinutes), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            // 连续失败退避：间隔 ×3（配置只定义正常间隔，退避是防御行为）
            var interval = _config.IntervalMinutes * (consecutiveFailures >= _config.MaxConsecutiveFailures ? 3 : 1);
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(interval), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                var ran = await RunRoundAsync(stoppingToken);
                consecutiveFailures = ran > 0 ? 0 : consecutiveFailures + 1;
                if (ran > 0)
                    Serilog.Log.Information("[DriftProbe] 本轮完成：成功测量 {Count} 条探针", ran);
            }
            catch (Exception ex)
            {
                consecutiveFailures++;
                Serilog.Log.Error(ex, "[DriftProbe] 轮询异常（连续 {N} 次失败，将退避）", consecutiveFailures);
            }
        }

        Serilog.Log.Information("[DriftProbe] 探针调度器已停止");
    }

    /// <summary>
    /// 执行一轮：取最久未测的启用模板，逐条问 LLM 并测量。返回成功测量条数。
    /// public 供 POST /api/drift/probes/run 手动触发（幂等覆盖，与定时轮询并发也安全）。
    /// </summary>
    public async Task<int> RunRoundAsync(CancellationToken ct)
    {
        // 1. 启用模板清单（006 未迁移 → 表不存在 → 返回空，静默跳过）
        List<DriftProbeTemplate> templates;
        try
        {
            templates = await _monitor.GetTemplatesAsync(enabledOnly: true);
        }
        catch
        {
            Serilog.Log.Warning("[DriftProbe] drift_probe_templates 表不可用（006 未执行？）——本轮跳过");
            return 0;
        }
        if (templates.Count == 0)
            return 0;

        // 2. 上次测量时间（drift_probes.session_id='probe'，turn_no=模板id槽位）
        var history = await _monitor.GetTrendAsync("probe");
        var lastMeasuredAt = history
            .GroupBy(p => p.TurnNo)
            .ToDictionary(g => g.Key, g => g.Max(p => p.CreatedAt));

        // 3. 优先测最久没测的模板（从未测过的排最前）
        var selected = templates
            .OrderBy(t => lastMeasuredAt.TryGetValue((int)t.Id, out var ts) ? ts : DateTime.MinValue)
            .ThenBy(t => t.Id)
            .Take(_config.MaxPerRun)
            .ToList();

        // 4. 逐条问 LLM → 测量 → 落库（单条失败不拖垮整轮）
        var success = 0;
        foreach (var tpl in selected)
        {
            if (ct.IsCancellationRequested)
                break;
            try
            {
                var prompt = "你是这个项目的开发助手。请直接、简明地回答下面的问题，" +
                             "基于你对这个项目的真实了解，不确定就如实说不知道，不要编造：\n\n" +
                             tpl.Question;
                var answer = await _llm.GenerateSimpleResponseAsync(prompt, maxTokens: _config.MaxAnswerTokens);
                if (string.IsNullOrWhiteSpace(answer))
                    throw new InvalidOperationException("LLM 返回空回答");

                var result = await _monitor.RecordProbeAsync(tpl.ProbeKey, answer);
                Serilog.Log.Information(
                    "[DriftProbe] 探针 {Key}({Vessel}) 完成：断言 {Claims} 条，匹配 {Matches} 条，漂移率 {Score:F3}",
                    tpl.ProbeKey, tpl.Vessel, result.ClaimCount, result.MatchCount, result.DriftScore);
                success++;
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "[DriftProbe] 探针 {Key} 测量失败（跳过，不影响其他探针）", tpl.ProbeKey);
            }
        }
        return success;
    }
}
