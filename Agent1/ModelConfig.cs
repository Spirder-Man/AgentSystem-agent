using System;
using Agent1.Config;

namespace Agent1
{
    /// <summary>
    /// 统一的模型配置类
    /// </summary>
    public static class ModelConfig
    {
        /// <summary>
        /// Ollama模型名称（从配置读取）
        /// </summary>
        public static string ModelId => AppConfig.Instance.Llm.ModelId;

        /// <summary>
        /// Ollama服务端点地址（从配置读取）
        /// </summary>
        public static Uri Endpoint => new Uri(AppConfig.Instance.Llm.Endpoint);

        /// <summary>
        /// 多模态模型ID（从配置读取）
        /// </summary>
        public static string MultimodalModelId => AppConfig.Instance.Llm.MultimodalModelId;

        /// <summary>
        /// 化工知识库配置快捷访问
        /// </summary>
        public static string ChemicalKnowledgeBasePath => AppConfig.Instance.KnowledgeBase.BasePath;
    }

    /// <summary>
    /// 全局配置入口
    /// </summary>
    public static class AppConfig
    {
        private static Config.AppConfig? _instance;
        public static Config.AppConfig Instance => _instance ??= new Config.AppConfig();
    }
}
