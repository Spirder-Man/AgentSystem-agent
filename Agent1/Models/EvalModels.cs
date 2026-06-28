using System.Text.Json.Serialization;

namespace Agent1.Models;

/// <summary>
/// 评测数据模型 — 从 Program.cs 内联类迁移为独立公共模型。
/// Task 5: 支持 JSON 序列化/反序列化，同时保持底层 JSON 键名不变以保证兼容性。
/// Sprint 1: 新增 Answer Relevance, Citation Accuracy, 工程指标(latency/token) 维度。
/// </summary>

public class EvalSet
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("created")]
    public string Created { get; set; } = "";

    [JsonPropertyName("test_cases")]
    public List<EvalCase> TestCases { get; set; } = new();

    [JsonIgnore] public string name { get => Name; set => Name = value; }
    [JsonIgnore] public string version { get => Version; set => Version = value; }
    [JsonIgnore] public string created { get => Created; set => Created = value; }
    [JsonIgnore] public List<EvalCase> test_cases { get => TestCases; set => TestCases = value; }
}

public class EvalCase
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("intent")]
    public string Intent { get; set; } = "";

    [JsonPropertyName("query")]
    public string Query { get; set; } = "";

    [JsonPropertyName("expected_tool")]
    public string ExpectedTool { get; set; } = "";

    [JsonPropertyName("expected_params")]
    public Dictionary<string, string>? ExpectedParams { get; set; }

    [JsonPropertyName("expected_conclusion")]
    public EvalConclusion? ExpectedConclusion { get; set; }

    // ── RAG 检索质量评估 ──
    [JsonPropertyName("expected_relevant_docs")]
    public List<string>? ExpectedRelevantDocs { get; set; }

    [JsonPropertyName("expected_faithfulness_checks")]
    public List<FaithfulnessCheck>? ExpectedFaithfulnessChecks { get; set; }

    // ── Phase 6: 盲测集增强字段 ──
    [JsonPropertyName("difficulty")]
    public string? Difficulty { get; set; }

    [JsonPropertyName("expected_behavior")]
    public string? ExpectedBehavior { get; set; }

    [JsonIgnore] public string id { get => Id; set => Id = value; }
    [JsonIgnore] public string category { get => Category; set => Category = value; }
    [JsonIgnore] public string intent { get => Intent; set => Intent = value; }
    [JsonIgnore] public string query { get => Query; set => Query = value; }
    [JsonIgnore] public string expected_tool { get => ExpectedTool; set => ExpectedTool = value; }
    [JsonIgnore] public Dictionary<string, string>? expected_params { get => ExpectedParams; set => ExpectedParams = value; }
    [JsonIgnore] public EvalConclusion? expected_conclusion { get => ExpectedConclusion; set => ExpectedConclusion = value; }
    [JsonIgnore] public string? difficulty { get => Difficulty; set => Difficulty = value; }
    [JsonIgnore] public string? expected_behavior { get => ExpectedBehavior; set => ExpectedBehavior = value; }
}

public class EvalConclusion
{
    [JsonPropertyName("is_compliant")]
    public bool? IsCompliant { get; set; }

    [JsonPropertyName("regulation")]
    public string Regulation { get; set; } = "";

    [JsonPropertyName("expected_regulation_number")]
    public string? ExpectedRegulationNumber { get; set; }

    [JsonPropertyName("expected_clause")]
    public string? ExpectedClause { get; set; }

    [JsonPropertyName("expected_reason_keyword")]
    public string? ExpectedReasonKeyword { get; set; }

    [JsonPropertyName("expected_distance")]
    public double? ExpectedDistance { get; set; }

    [JsonPropertyName("expected_distance_unit")]
    public string? ExpectedDistanceUnit { get; set; }

    [JsonIgnore] public bool? is_compliant { get => IsCompliant; set => IsCompliant = value; }
    [JsonIgnore] public string regulation { get => Regulation; set => Regulation = value; }
    [JsonIgnore] public string? expected_regulation_number { get => ExpectedRegulationNumber; set => ExpectedRegulationNumber = value; }
    [JsonIgnore] public string? expected_clause { get => ExpectedClause; set => ExpectedClause = value; }
    [JsonIgnore] public string? expected_reason_keyword { get => ExpectedReasonKeyword; set => ExpectedReasonKeyword = value; }
    [JsonIgnore] public double? expected_distance { get => ExpectedDistance; set => ExpectedDistance = value; }
    [JsonIgnore] public string? expected_distance_unit { get => ExpectedDistanceUnit; set => ExpectedDistanceUnit = value; }
}

public class EvalResult
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("query")]
    public string Query { get; set; } = "";

    [JsonPropertyName("expected_tool")]
    public string ExpectedTool { get; set; } = "";

    [JsonPropertyName("actual_tools")]
    public string? ActualTools { get; set; }

    [JsonPropertyName("actual_params")]
    public string? ActualParams { get; set; }

    [JsonPropertyName("actual_response")]
    public string? ActualResponse { get; set; }

    [JsonPropertyName("tool_match")]
    public bool ToolMatch { get; set; }

    [JsonPropertyName("param_match")]
    public bool ParamMatch { get; set; }

    [JsonPropertyName("conclusion_match")]
    public bool ConclusionMatch { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    // ── RAG 检索质量指标 ──
    [JsonPropertyName("retrieved_chunks")]
    public List<RetrievalHit>? RetrievedChunks { get; set; }

    [JsonPropertyName("precision_at_k")]
    public double? PrecisionAtK { get; set; }

    [JsonPropertyName("recall_at_k")]
    public double? RecallAtK { get; set; }

    [JsonPropertyName("mrr")]
    public double? MRR { get; set; }

    // ── Top-10 检索指标 (Task 11: 集成测试补全) ──
    [JsonPropertyName("precision_at_10")]
    public double? PrecisionAt10 { get; set; }

    [JsonPropertyName("recall_at_10")]
    public double? RecallAt10 { get; set; }

    [JsonPropertyName("retrieval_evaluated")]
    public bool RetrievalEvaluated { get; set; }

    // ── 生成忠实度指标 ──
    [JsonPropertyName("total_claims")]
    public int TotalClaims { get; set; }

    [JsonPropertyName("verified_claims")]
    public int VerifiedClaims { get; set; }

    [JsonPropertyName("hallucinated_claims")]
    public int HallucinatedClaims { get; set; }

    [JsonPropertyName("faithfulness_score")]
    public double? FaithfulnessScore { get; set; }

    // ── Sprint 1 新增评估维度 ──
    [JsonPropertyName("answer_relevance")]
    public double? AnswerRelevance { get; set; }

    [JsonPropertyName("citation_accuracy")]
    public double? CitationAccuracy { get; set; }

    [JsonPropertyName("latency_ms")]
    public long LatencyMs { get; set; }

    [JsonPropertyName("token_count")]
    public int TokenCount { get; set; }

    // ── Phase 6: 新增评测指标 ──
    /// <summary>系统是否正确拒绝了数据库未命中的查询</summary>
    [JsonPropertyName("refusal_detected")]
    public bool? RefusalDetected { get; set; }

    /// <summary>系统标注的置信度是否与预期一致</summary>
    [JsonPropertyName("confidence_level")]
    public string? ConfidenceLevel { get; set; }

    /// <summary>输出校验器是否检测到幻觉</summary>
    [JsonPropertyName("hallucination_detected")]
    public bool? HallucinationDetected { get; set; }

    /// <summary>预期行为与实际行为匹配度 (DATABASE_HIT/DB_MISS_REFUSAL等)</summary>
    [JsonPropertyName("expected_behavior")]
    public string? ExpectedBehavior { get; set; }

    /// <summary>行为匹配 (refusal正确/行为符合预期)</summary>
    [JsonPropertyName("behavior_match")]
    public bool? BehaviorMatch { get; set; }

    // 兼容旧代码
    [JsonIgnore] public string id { get => Id; set => Id = value; }
    [JsonIgnore] public string category { get => Category; set => Category = value; }
    [JsonIgnore] public string query { get => Query; set => Query = value; }
    [JsonIgnore] public string expected_tool { get => ExpectedTool; set => ExpectedTool = value; }
    [JsonIgnore] public string? actual_tools { get => ActualTools; set => ActualTools = value; }
    [JsonIgnore] public string? actual_params { get => ActualParams; set => ActualParams = value; }
    [JsonIgnore] public string? actual_response { get => ActualResponse; set => ActualResponse = value; }
    [JsonIgnore] public bool tool_match { get => ToolMatch; set => ToolMatch = value; }
    [JsonIgnore] public bool param_match { get => ParamMatch; set => ParamMatch = value; }
    [JsonIgnore] public bool conclusion_match { get => ConclusionMatch; set => ConclusionMatch = value; }
    [JsonIgnore] public string? error { get => Error; set => Error = value; }
}

public class EvalReport
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = "";

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("tool_call_rate")]
    public double ToolCallRate { get; set; }

    [JsonPropertyName("parameter_accuracy")]
    public double ParameterAccuracy { get; set; }

    [JsonPropertyName("conclusion_accuracy")]
    public double ConclusionAccuracy { get; set; }

    // ── RAG 检索质量汇总 ──
    [JsonPropertyName("mean_precision_at_k")]
    public double? MeanPrecisionAtK { get; set; }

    [JsonPropertyName("mean_recall_at_k")]
    public double? MeanRecallAtK { get; set; }

    [JsonPropertyName("mean_mrr")]
    public double? MeanMRR { get; set; }

    // ── Top-10 检索指标汇总 (Task 11) ──
    [JsonPropertyName("mean_precision_at_10")]
    public double? MeanPrecisionAt10 { get; set; }

    [JsonPropertyName("mean_recall_at_10")]
    public double? MeanRecallAt10 { get; set; }

    // ── 生成维度汇总 ──
    [JsonPropertyName("mean_faithfulness")]
    public double? MeanFaithfulness { get; set; }

    [JsonPropertyName("mean_answer_relevance")]
    public double? MeanAnswerRelevance { get; set; }

    [JsonPropertyName("mean_citation_accuracy")]
    public double? MeanCitationAccuracy { get; set; }

    [JsonPropertyName("total_verified_claims")]
    public int TotalVerifiedClaims { get; set; }

    [JsonPropertyName("total_hallucinated_claims")]
    public int TotalHallucinatedClaims { get; set; }

    // ── Phase 6: 新增评测汇总指标 ──
    /// <summary>拒绝率: 数据库未命中时正确拒绝的比例</summary>
    [JsonPropertyName("refusal_rate")]
    public double? RefusalRate { get; set; }

    /// <summary>幻觉检测率: 输出校验器捕获的幻觉声明比例</summary>
    [JsonPropertyName("hallucination_detection_rate")]
    public double? HallucinationDetectionRate { get; set; }

    /// <summary>行为匹配率: 实际行为与预期行为一致的用例比例</summary>
    [JsonPropertyName("behavior_match_rate")]
    public double? BehaviorMatchRate { get; set; }

    // ── 工程维度 ──
    [JsonPropertyName("engineering_metrics")]
    public EngineeringMetrics? EngineeringMetrics { get; set; }

    [JsonPropertyName("fc_readiness")]
    public FcReadinessStatus? FcReadiness { get; set; }

    [JsonPropertyName("category_breakdown")]
    public Dictionary<string, CategoryMetric> CategoryBreakdown { get; set; } = new();

    [JsonPropertyName("cases")]
    public List<EvalResult> Cases { get; set; } = new();

    // 兼容旧代码
    [JsonIgnore] public string model { get => Model; set => Model = value; }
    [JsonIgnore] public string timestamp { get => Timestamp; set => Timestamp = value; }
    [JsonIgnore] public int total { get => Total; set => Total = value; }
    [JsonIgnore] public double tool_call_rate { get => ToolCallRate; set => ToolCallRate = value; }
    [JsonIgnore] public double parameter_accuracy { get => ParameterAccuracy; set => ParameterAccuracy = value; }
    [JsonIgnore] public double conclusion_accuracy { get => ConclusionAccuracy; set => ConclusionAccuracy = value; }
    [JsonIgnore] public FcReadinessStatus? fc_readiness { get => FcReadiness; set => FcReadiness = value; }
    [JsonIgnore] public Dictionary<string, CategoryMetric> category_breakdown { get => CategoryBreakdown; set => CategoryBreakdown = value; }
    [JsonIgnore] public List<EvalResult> cases { get => Cases; set => Cases = value; }
}

/// <summary>Sprint 1+5: 工程指标汇总（含 GPU 监控）</summary>
public class EngineeringMetrics
{
    [JsonPropertyName("avg_latency_ms")]
    public double AvgLatencyMs { get; set; }

    [JsonPropertyName("p50_latency_ms")]
    public double P50LatencyMs { get; set; }

    [JsonPropertyName("p95_latency_ms")]
    public double P95LatencyMs { get; set; }

    [JsonPropertyName("avg_tokens_per_query")]
    public double AvgTokensPerQuery { get; set; }

    [JsonPropertyName("estimated_cost_per_1k_queries_usd")]
    public double EstimatedCostPer1kQueriesUsd { get; set; }

    // Sprint 5: GPU 监控指标
    [JsonPropertyName("gpu_embedding_latency_ms")]
    public double? GpuEmbeddingLatencyMs { get; set; }

    [JsonPropertyName("gpu_search_latency_ms")]
    public double? GpuSearchLatencyMs { get; set; }

    [JsonPropertyName("reranker_latency_ms")]
    public double? RerankerLatencyMs { get; set; }

    [JsonPropertyName("vram_usage_mb")]
    public double? VramUsageMb { get; set; }

    [JsonPropertyName("query_cache_hit_rate")]
    public double? QueryCacheHitRate { get; set; }
}

public class FcReadinessStatus
{
    [JsonPropertyName("passed")]
    public bool Passed { get; set; }

    [JsonPropertyName("trigger_count")]
    public int TriggerCount { get; set; }

    [JsonPropertyName("total_count")]
    public int TotalCount { get; set; }

    [JsonPropertyName("detail")]
    public string Detail { get; set; } = "";

    [JsonIgnore] public bool passed { get => Passed; set => Passed = value; }
    [JsonIgnore] public int trigger_count { get => TriggerCount; set => TriggerCount = value; }
    [JsonIgnore] public int total_count { get => TotalCount; set => TotalCount = value; }
    [JsonIgnore] public string detail { get => Detail; set => Detail = value; }
}

public class CategoryMetric
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("tool_ok")]
    public int ToolOk { get; set; }

    [JsonPropertyName("param_ok")]
    public int ParamOk { get; set; }

    [JsonPropertyName("conclusion_ok")]
    public int ConclusionOk { get; set; }

    // ── RAG 检索质量 ──
    [JsonPropertyName("precision_at_k")]
    public double? PrecisionAtK { get; set; }

    [JsonPropertyName("recall_at_k")]
    public double? RecallAtK { get; set; }

    [JsonPropertyName("mrr")]
    public double? MRR { get; set; }

    // ── 忠实度 ──
    [JsonPropertyName("faithfulness")]
    public double? Faithfulness { get; set; }

    // ── Sprint 1 新增 ──
    [JsonPropertyName("answer_relevance")]
    public double? AnswerRelevance { get; set; }

    [JsonPropertyName("citation_accuracy")]
    public double? CitationAccuracy { get; set; }

    // ── Phase 6 新增 ──
    [JsonPropertyName("refusal_count")]
    public int RefusalCount { get; set; }

    [JsonPropertyName("hallucination_count")]
    public int HallucinationCount { get; set; }

    [JsonPropertyName("refusal_rate")]
    public double? RefusalRate { get; set; }

    [JsonIgnore] public int total { get => Total; set => Total = value; }
    [JsonIgnore] public int tool_ok { get => ToolOk; set => ToolOk = value; }
    [JsonIgnore] public int param_ok { get => ParamOk; set => ParamOk = value; }
    [JsonIgnore] public int conclusion_ok { get => ConclusionOk; set => ConclusionOk = value; }
}

/// <summary>检索命中的文档片段记录</summary>
public class RetrievalHit
{
    [JsonPropertyName("chunk_id")]
    public string ChunkId { get; set; } = "";

    [JsonPropertyName("content_preview")]
    public string ContentPreview { get; set; } = "";

    [JsonPropertyName("score")]
    public double Score { get; set; }

    [JsonPropertyName("rank")]
    public int Rank { get; set; }

    [JsonPropertyName("is_relevant")]
    public bool? IsRelevant { get; set; }
}

/// <summary>忠实度检查项：答案中应出现的可验证声明</summary>
public class FaithfulnessCheck
{
    [JsonPropertyName("claim_text")]
    public string ClaimText { get; set; } = "";

    [JsonPropertyName("claim_type")]
    public string ClaimType { get; set; } = "";

    [JsonPropertyName("expected_source")]
    public string? ExpectedSource { get; set; }
}
