using System.Collections.Concurrent;
using System.Text.Json;
using Agent1.Models;

namespace Agent1.Services;

/// <summary>
/// 化工合规审计日志服务 —— 零失误架构安全组件 (Phase 5.5)
/// 
/// 职责: 记录每次合规查询的完整审计轨迹
///   - 查询时间戳、用户问题、触发工具
///   - 数据来源 (DATABASE_HIT / RAG_HIT / DICTIONARY_HIT / FALLBACK)
///   - 置信度等级 (HIGH / MEDIUM / LOW / UNKNOWN)
///   - 法规编号列表
///   - 是否触发拒绝及拒绝原因
/// 
/// 原则: 所有输出可追溯到具体法规条款，所有拒绝有据可查。
/// </summary>
public class ComplianceAuditLogger
{
    /// <summary>单条审计记录</summary>
    public class AuditEntry
    {
        public string Timestamp { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        public string UserQuery { get; set; } = "";
        public string? TriggeredTool { get; set; }
        public string? DataSource { get; set; }
        public string? ConfidenceLevel { get; set; }
        public List<string> RegulationNumbers { get; set; } = new();
        public bool IsRefusal { get; set; }
        public string? RefusalReason { get; set; }
        public long LatencyMs { get; set; }
        public string? ToolOutput { get; set; }
        public string? LlmResponsePreview { get; set; }
    }

    private static readonly ConcurrentQueue<AuditEntry> _auditLog = new();
    private static readonly object _fileLock = new();
    private static string? _logFilePath;

    /// <summary>初始化审计日志文件路径</summary>
    public static void Initialize(string? logDir = null)
    {
        _logFilePath = logDir ?? Path.Combine(AppContext.BaseDirectory, "logs", "compliance_audit.jsonl");
        var dir = Path.GetDirectoryName(_logFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    /// <summary>
    /// 记录一次合规查询审计。
    /// 应在 ChemicalComplianceTools 每次返回结果后调用。
    /// </summary>
    public static void Log(
        string userQuery,
        string? triggeredTool,
        string? dataSource,
        string? confidenceLevel,
        List<string>? regulationNumbers = null,
        bool isRefusal = false,
        string? refusalReason = null,
        long latencyMs = 0,
        string? toolOutput = null,
        string? llmResponsePreview = null)
    {
        var entry = new AuditEntry
        {
            Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
            UserQuery = userQuery,
            TriggeredTool = triggeredTool,
            DataSource = dataSource ?? "UNKNOWN",
            ConfidenceLevel = confidenceLevel ?? "UNKNOWN",
            RegulationNumbers = regulationNumbers ?? new List<string>(),
            IsRefusal = isRefusal,
            RefusalReason = refusalReason,
            LatencyMs = latencyMs,
            ToolOutput = Truncate(toolOutput, 500),
            LlmResponsePreview = Truncate(llmResponsePreview, 500)
        };

        _auditLog.Enqueue(entry);

        // 每 10 条或包含拒绝时自动刷新到文件
        if (_auditLog.Count >= 10 || isRefusal)
            _ = FlushAsync();
    }

    /// <summary>
    /// 便捷方法：从 ToolQualityContext 中提取审计信息并记录。
    /// </summary>
    public static void LogFromToolContext(
        string userQuery,
        string toolName,
        string? llmResponse = null,
        long latencyMs = 0)
    {
        var qualityContext = ToolQualityContext.Current;
        var dataSource = qualityContext?.Quality.ToString() ?? "UNKNOWN";
        var confidenceLevel = MapQualityToConfidence(qualityContext?.Quality);
        var regulationNumbers = qualityContext?.RegulationRefs ?? new List<string>();
        var toolOutput = qualityContext?.Content;

        var isRefusal = toolOutput?.Contains("无法给出确定", StringComparison.OrdinalIgnoreCase) == true
            || toolOutput?.Contains("未收录于结构化", StringComparison.OrdinalIgnoreCase) == true
            || toolOutput?.Contains("建议联系安环部门人工", StringComparison.OrdinalIgnoreCase) == true;

        var refusalReason = isRefusal ? ExtractRefusalReason(toolOutput) : null;

        Log(
            userQuery: userQuery,
            triggeredTool: toolName,
            dataSource: dataSource,
            confidenceLevel: confidenceLevel,
            regulationNumbers: regulationNumbers,
            isRefusal: isRefusal,
            refusalReason: refusalReason,
            latencyMs: latencyMs,
            toolOutput: toolOutput,
            llmResponsePreview: llmResponse
        );
    }

    /// <summary>刷新所有缓冲的审计条目到文件</summary>
    public static async Task FlushAsync()
    {
        if (_auditLog.IsEmpty)
            return;

        Initialize(); // 确保路径已初始化

        var entries = new List<AuditEntry>();
        while (_auditLog.TryDequeue(out var entry))
            entries.Add(entry);

        if (entries.Count == 0)
            return;

        lock (_fileLock)
        {
            try
            {
                using var writer = new StreamWriter(_logFilePath!, true, System.Text.Encoding.UTF8);
                foreach (var entry in entries)
                {
                    var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = false });
                    writer.WriteLine(json);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ 审计日志写入失败: {ex.Message}");
            }
        }

        Console.WriteLine($"   📝 审计日志已刷新: {entries.Count} 条 → {_logFilePath}");
    }

    /// <summary>获取审计统计摘要</summary>
    public static AuditSummary GetSummary()
    {
        var allEntries = new List<AuditEntry>();
        // 从文件读取（如果有）
        if (_logFilePath != null && File.Exists(_logFilePath))
        {
            try
            {
                var lines = File.ReadAllLines(_logFilePath);
                foreach (var line in lines)
                {
                    try
                    {
                        var entry = JsonSerializer.Deserialize<AuditEntry>(line);
                        if (entry != null)
                            allEntries.Add(entry);
                    }
                    catch { }
                }
            }
            catch { }
        }

        return new AuditSummary
        {
            TotalQueries = allEntries.Count,
            RefusalCount = allEntries.Count(e => e.IsRefusal),
            DatabaseHitCount = allEntries.Count(e => e.DataSource == "DATABASE_HIT"),
            RagHitCount = allEntries.Count(e => e.DataSource == "RAG_HIT"),
            FallbackCount = allEntries.Count(e => e.DataSource == "FALLBACK"),
            HighConfidenceCount = allEntries.Count(e => e.ConfidenceLevel == "HIGH"),
            MediumConfidenceCount = allEntries.Count(e => e.ConfidenceLevel == "MEDIUM"),
            LowConfidenceCount = allEntries.Count(e => e.ConfidenceLevel == "LOW"),
            UnknownCount = allEntries.Count(e => e.ConfidenceLevel == "UNKNOWN"),
            AverageLatencyMs = allEntries.Count > 0 ? allEntries.Average(e => e.LatencyMs) : 0,
        };
    }

    // ════════════════════════════════════════
    // 辅助方法
    // ════════════════════════════════════════

    private static string MapQualityToConfidence(QualityLevel? quality)
    {
        return quality switch
        {
            QualityLevel.DATABASE_HIT => "HIGH",
            QualityLevel.RAG_HIT => "MEDIUM",
            QualityLevel.DICTIONARY_HIT => "LOW",
            QualityLevel.FALLBACK => "UNKNOWN",
            _ => "UNKNOWN"
        };
    }

    private static string? ExtractRefusalReason(string? toolOutput)
    {
        if (string.IsNullOrWhiteSpace(toolOutput))
            return null;

        if (toolOutput.Contains("未收录于结构化", StringComparison.OrdinalIgnoreCase))
            return "化学品未收录于结构化数据库";
        if (toolOutput.Contains("未找到精确", StringComparison.OrdinalIgnoreCase))
            return "未找到精确匹配数据";
        if (toolOutput.Contains("无法给出确定", StringComparison.OrdinalIgnoreCase))
            return "数据库与RAG均无法给出确定结论";
        return "未知原因";
    }

    private static string? Truncate(string? text, int maxLen)
    {
        if (string.IsNullOrEmpty(text))
            return null;
        return text.Length <= maxLen ? text : text.Substring(0, maxLen) + "...";
    }
}

/// <summary>审计统计摘要</summary>
public class AuditSummary
{
    public int TotalQueries { get; set; }
    public int RefusalCount { get; set; }
    public double RefusalRate => TotalQueries > 0 ? (double)RefusalCount / TotalQueries : 0;
    public int DatabaseHitCount { get; set; }
    public int RagHitCount { get; set; }
    public int FallbackCount { get; set; }
    public int HighConfidenceCount { get; set; }
    public int MediumConfidenceCount { get; set; }
    public int LowConfidenceCount { get; set; }
    public int UnknownCount { get; set; }
    public double AverageLatencyMs { get; set; }
}
