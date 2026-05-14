using System;
using Agent1.Config;

namespace Agent1
{
    /// &lt;summary&gt;
    /// 统一的模型配置类
    /// &lt;/summary&gt;
    public static class ModelConfig
    {
        /// &lt;summary&gt;
        /// Ollama模型名称（从配置读取）
        /// &lt;/summary&gt;
        public static string ModelId => AppConfig.Instance.Llm.ModelId;

        /// &lt;summary&gt;
        /// Ollama服务端点地址（从配置读取）
        /// &lt;/summary&gt;
        public static Uri Endpoint => new Uri(AppConfig.Instance.Llm.Endpoint);

        /// &lt;summary&gt;
        /// 多模态模型ID（从配置读取）
        /// &lt;/summary&gt;
        public static string MultimodalModelId => AppConfig.Instance.Llm.MultimodalModelId;

        /// &lt;summary&gt;
        /// 化工知识库配置快捷访问
        /// &lt;/summary&gt;
        public static string ChemicalKnowledgeBasePath => AppConfig.Instance.KnowledgeBase.BasePath;
    }

    /// &lt;summary&gt;
    /// 全局配置入口
    /// &lt;/summary&gt;
    public static class AppConfig
    {
        public static Config.AppConfig Instance { get; } = new Config.AppConfig();
    }
}
