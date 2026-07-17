using System;

using Microsoft.Extensions.Configuration;

namespace Agent1.Config
{
    /// <summary>
    /// 统一的模型配置类（从 IConfiguration 读取，不再硬编码）
    /// </summary>
    public static class ModelConfig
    {
        private static AppConfig? _config;

        /// <summary>
        /// 初始化配置（在 Program.cs 中调用一次）
        /// </summary>
        public static void Initialize(AppConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        private static AppConfig Config => _config
            ?? throw new InvalidOperationException("ModelConfig 尚未初始化，请先调用 ModelConfig.Initialize(config)");

        /// <summary>
        /// Ollama模型名称（从配置读取）
        /// </summary>
        public static string ModelId => Config.Llm.ModelId;

        /// <summary>
        /// Ollama服务端点地址（从配置读取）
        /// </summary>
        public static Uri Endpoint => new Uri(Config.Llm.Endpoint);

        /// <summary>
        /// 多模态模型ID（从配置读取）
        /// </summary>
        public static string MultimodalModelId => Config.Llm.MultimodalModelId;

        /// <summary>
        /// 多模态服务端点（从配置读取，默认 :8083 独立 vision 实例，与 Reranker :8082 分离）
        /// </summary>
        public static Uri MultimodalEndpoint => new Uri(Config.Llm.MultimodalEndpoint);

        /// <summary>
        /// Phase 2a 预留: 工具调用规划专用模型ID。如果配置为空则使用 ModelId（默认行为）。
        /// 未来可分离为小模型做工具规划 + 大模型做合规结论生成。
        /// </summary>
        public static string FunctionCallingModelId =>
            string.IsNullOrEmpty(Config.Llm.FunctionCallingModelId)
                ? Config.Llm.ModelId
                : Config.Llm.FunctionCallingModelId;

        /// <summary>
        /// 化工知识库配置快捷访问
        /// </summary>
        public static string ChemicalKnowledgeBasePath => Config.KnowledgeBase.BasePath;
    }
}
