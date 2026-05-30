using System;
using Agent1.Config;
using Microsoft.Extensions.Configuration;

namespace Agent1
{
    /// <summary>
    /// 统一的模型配置类（从 IConfiguration 读取，不再硬编码）
    /// </summary>
    public static class ModelConfig
    {
        private static Config.AppConfig? _config;

        /// <summary>
        /// 初始化配置（在 Program.cs 中调用一次）
        /// </summary>
        public static void Initialize(Config.AppConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        private static Config.AppConfig Config => _config
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
        /// 化工知识库配置快捷访问
        /// </summary>
        public static string ChemicalKnowledgeBasePath => Config.KnowledgeBase.BasePath;
    }

    /// <summary>
    /// 全局配置入口（从 appsettings.json + 环境变量加载）
    /// </summary>
    public static class AppConfig
    {
        private static Config.AppConfig? _instance;

        /// <summary>
        /// 获取全局配置实例（首次访问时自动从 IConfiguration 加载）
        /// 优先从环境变量 DB_PASSWORD 读取数据库密码
        /// </summary>
        public static Config.AppConfig Instance
        {
            get
            {
                if (_instance == null)
                {
                    throw new InvalidOperationException(
                        "AppConfig 尚未加载。请在 Program.cs 中先调用 AppConfig.Load(configuration)");
                }
                return _instance;
            }
        }

        /// <summary>
        /// 从 IConfiguration 加载配置，并从环境变量覆盖敏感信息
        /// </summary>
        public static void Load(IConfiguration configuration)
        {
            var config = new Config.AppConfig();
            configuration.Bind(config);

            // 敏感信息：数据库密码优先从环境变量读取
            var dbPassword = Environment.GetEnvironmentVariable("DB_PASSWORD");
            if (!string.IsNullOrEmpty(dbPassword))
            {
                config.Database.Password = dbPassword;
            }

            // 敏感信息：PostgreSQL 用户名也支持环境变量
            var dbUser = Environment.GetEnvironmentVariable("DB_USERNAME");
            if (!string.IsNullOrEmpty(dbUser))
            {
                config.Database.Username = dbUser;
            }

            var dbHost = Environment.GetEnvironmentVariable("DB_HOST");
            if (!string.IsNullOrEmpty(dbHost))
            {
                config.Database.Host = dbHost;
            }

            var dbName = Environment.GetEnvironmentVariable("DB_NAME");
            if (!string.IsNullOrEmpty(dbName))
            {
                config.Database.DatabaseName = dbName;
            }

            _instance = config;

            // 同步初始化 ModelConfig
            ModelConfig.Initialize(config);
        }
    }
}
