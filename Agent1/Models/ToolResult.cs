using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Agent1.Tests")]

namespace Agent1.Models
{
    /// <summary>
    /// [QF-2026-001] 工具返回值质量等级 — 编译期强制下游处理质量字段。
    /// 替代裸 string 返回值，确保缓存层能区分"有效法规数据"和"兜底诚实声明"。
    /// </summary>
    public enum QualityLevel
    {
        /// <summary>RAG 检索命中，有原文支撑</summary>
        RAG_HIT = 4,
        /// <summary>结构化数据库命中</summary>
        DATABASE_HIT = 3,
        /// <summary>硬编码字典命中</summary>
        DICTIONARY_HIT = 2,
        /// <summary>兜底诚实声明（不可作为领域事实缓存）</summary>
        FALLBACK = 0,
        /// <summary>异常</summary>
        ERROR = -1
    }

    /// <summary>
    /// [QF-2026-001] 带质量标签的工具返回值。
    /// 不改变 [KernelFunction] 签名（仍返回 string），通过 AsyncLocal 上下文传递质量信息。
    /// </summary>
    public class ToolResult
    {
        public string Content { get; init; } = "";
        public QualityLevel Quality { get; init; }
        public List<string> RegulationRefs { get; init; } = new();
        public bool IsFallback => Quality == QualityLevel.FALLBACK;
    }

    /// <summary>
    /// [QF-2026-001] AsyncLocal 上下文，在工具方法内部设置，
    /// 由 FunctionCallDiagnosticsFilter 和 StoreToolFacts 读取。
    /// 不改变 [KernelFunction] 返回签名的前提下传递质量标签。
    /// </summary>
    internal static class ToolQualityContext
    {
        private static readonly System.Threading.AsyncLocal<ToolResult?> _current = new();

        public static ToolResult? Current
        {
            get => _current.Value;
            set => _current.Value = value;
        }

        /// <summary>清除当前上下文（每次工具调用前调用）</summary>
        public static void Clear() => _current.Value = null;
    }
}
