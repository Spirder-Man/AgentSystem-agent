using System.Text.Json.Serialization;

namespace Agent1.Models;

/// <summary>
/// 评测数据模型 — 从 Program.cs 内联类迁移为独立公共模型。
/// Task 5: 支持 JSON 序列化/反序列化，同时保持底层 JSON 键名不变以保证兼容性。
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

    // 兼容旧代码 snake_case 属性
    [JsonIgnore]
    public string name { get => Name; set => Name = value; }
    [JsonIgnore]
    public string version { get => Version; set => Version = value; }
    [JsonIgnore]
    public string created { get => Created; set => Created = value; }
    [JsonIgnore]
    public List<EvalCase> test_cases { get => TestCases; set => TestCases = value; }
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
    public List<string>? ExpectedRelevantDocs { get; set; }  // 预期应检索到的文档/片段ID或关键词

    [JsonPropertyName("expected_faithfulness_checks")]
    public List<FaithfulnessCheck>? ExpectedFaithfulnessChecks { get; set; }  // 预期答案中应出现的可验证声明

    // 兼容旧代码 snake_case 属性
    [JsonIgnore] public string id { get => Id; set => Id = value; }
    [JsonIgnore] public string category { get => Category; set => Category = value; }
    [JsonIgnore] public string intent { get => Intent; set => Intent = value; }
    [JsonIgnore] public string query { get => Query; set => Query = value; }
    [JsonIgnore] public string expected_tool { get => ExpectedTool; set => ExpectedTool = value; }
    [JsonIgnore] public Dictionary<string, string>? expected_params { get => ExpectedParams; set => ExpectedParams = value; }
    [JsonIgnore] public EvalConclusion? expected_conclusion { get => ExpectedConclusion; set => ExpectedConclusion = value; }
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

    // 兼容旧代码
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
    public List<RetrievalHit>? RetrievedChunks { get; set; }  // 实际检索到的文档片段

    [JsonPropertyName("precision_at_k")]
    public double? PrecisionAtK { get; set; }   // Precision@K (K=检索到的文档数)

    [JsonPropertyName("recall_at_k")]
    public double? RecallAtK { get; set; }      // Recall@K

    [JsonPropertyName("mrr")]
    public double? MRR { get; set; }            // Mean Reciprocal Rank

    [JsonPropertyName("retrieval_evaluated")]
    public bool RetrievalEvaluated { get; set; } // 是否进行了检索评估

    // ── 生成忠实度指标 ──
    [JsonPropertyName("total_claims")]
    public int TotalClaims { get; set; }        // 从回复中提取的声明总数

    [JsonPropertyName("verified_claims")]
    public int VerifiedClaims { get; set; }     // 可在知识库中验证的声明数

    [JsonPropertyName("hallucinated_claims")]
    public int HallucinatedClaims { get; set; }  // 无法验证的声明数（疑似幻觉）

    [JsonPropertyName("faithfulness_score")]
    public double? FaithfulnessScore { get; set; } // 忠实度 = verified / total

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

    // ── 生成忠实度汇总 ──
    [JsonPropertyName("mean_faithfulness")]
    public double? MeanFaithfulness { get; set; }

    [JsonPropertyName("total_verified_claims")]
    public int TotalVerifiedClaims { get; set; }

    [JsonPropertyName("total_hallucinated_claims")]
    public int TotalHallucinatedClaims { get; set; }

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

    // 兼容旧代码
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

    // 兼容旧代码
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
    public bool? IsRelevant { get; set; }  // 是否与基准答案相关
}

/// <summary>忠实度检查项：答案中应出现的可验证声明</summary>
public class FaithfulnessCheck
{
    [JsonPropertyName("claim_text")]
    public string ClaimText { get; set; } = "";

    [JsonPropertyName("claim_type")]
    public string ClaimType { get; set; } = "";  // "regulation_number" | "chemical_name" | "distance_value" | "compliance_conclusion"

    [JsonPropertyName("expected_source")]
    public string? ExpectedSource { get; set; }  // 预期该声明在知识库中的来源
}
