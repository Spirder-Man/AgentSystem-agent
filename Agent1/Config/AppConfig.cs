namespace Agent1.Config
{
    public class AppConfig
    {
        // LLM配置（已适配化工场景）
        public ChemicalLlmConfig Llm { get; set; } = new();

        // 化工知识库配置
        public ChemicalKnowledgeBaseConfig KnowledgeBase { get; set; } = new();

        // 向量检索配置
        public VectorSearchConfig VectorSearch { get; set; } = new();

        // 数据库配置
        public DatabaseConfig Database { get; set; } = new();

        // 工业系统集成配置
        public IntegrationConfig Integration { get; set; } = new();

        // 化工合规工具配置
        public ChemicalToolConfig ChemicalTool { get; set; } = new();

        // 等保三级审计配置
        public AuditConfig Audit { get; set; } = new();

        // 业务评测配置
        public EvaluationConfig Evaluation { get; set; } = new();

        // Prompt 模板配置
        public PromptTemplateConfig PromptTemplates { get; set; } = new();

        /// <summary>
        /// 启动时校验关键配置项，防止运行时才发现配错。
        /// 返回错误列表，空列表表示全部通过。
        /// </summary>
        public List<string> Validate()
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(Llm.ModelId))
                errors.Add("Llm.ModelId 未配置（模型名称不能为空）");
            if (string.IsNullOrWhiteSpace(Llm.Endpoint))
                errors.Add("Llm.Endpoint 未配置（Ollama 服务地址不能为空）");
            if (!Llm.Endpoint.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                errors.Add($"Llm.Endpoint 格式异常: {Llm.Endpoint}（应以 http:// 或 https:// 开头）");

            if (string.IsNullOrWhiteSpace(Database.Host))
                errors.Add("Database.Host 未配置");
            if (Database.Port <= 0 || Database.Port > 65535)
                errors.Add($"Database.Port 无效: {Database.Port}");
            if (string.IsNullOrWhiteSpace(Database.DatabaseName))
                errors.Add("Database.DatabaseName 未配置");

            if (string.IsNullOrWhiteSpace(VectorSearch.EmbeddingModelId))
                errors.Add("VectorSearch.EmbeddingModelId 未配置（嵌入模型不能为空）");

            if (string.IsNullOrWhiteSpace(PromptTemplates.SystemRole))
                errors.Add("PromptTemplates.SystemRole 未配置");
            if (string.IsNullOrWhiteSpace(PromptTemplates.EvalFastPrompt))
                errors.Add("PromptTemplates.EvalFastPrompt 未配置（评测 Prompt 不能为空）");

            return errors;
        }
    }

    // 化工场景专用LLM配置
    public class ChemicalLlmConfig
    {
        public string ModelId { get; set; } = "deepseek-r1:local7b";
        public string Endpoint { get; set; } = "http://localhost:11434";
        public string MultimodalModelId { get; set; } = "qwen-vl:latest";

        // Phase 2a 预留: 工具调用规划专用模型（可与 ModelId 相同）
        // 未来可分离为小模型做工具规划 + 大模型做合规结论生成
        public string FunctionCallingModelId { get; set; } = "";  // 空字符串表示使用 ModelId

        public int MaxRetries { get; set; } = 3;
        public int RetryDelayMs { get; set; } = 1000;
        public int BufferFlushThreshold { get; set; } = 50;
    }

    // 化工知识库配置
    public class ChemicalKnowledgeBaseConfig
    {
        public string BasePath { get; set; } = @"d:\桌面\agent\项目\Agent1\knowledgebase";
        public List<KnowledgeSourceConfig> Sources { get; set; } = new()
        {
            new() { Name = "国标", Path = "国标", Priority = 100 },
            new() { Name = "园区规则", Path = "园区规则", Priority = 80 },
            new() { Name = "历史案例", Path = "历史案例", Priority = 60 }
        };
        public int ChunkSize { get; set; } = 500;
        
        // 检索模式：BM25 / Vector / Hybrid
        public string SearchMode { get; set; } = "Hybrid";
    }

    // 知识源配置
    public class KnowledgeSourceConfig
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public int Priority { get; set; } = 50;
    }

    // 工业系统集成配置
    public class IntegrationConfig
    {
        public bool EnableERPSync { get; set; } = false;
        public bool EnableWMSSync { get; set; } = false;
        public bool EnableEHSSync { get; set; } = false;
        public string ERPApiBaseUrl { get; set; } = string.Empty;
        public string WMSApiBaseUrl { get; set; } = string.Empty;
        public string EHSApiBaseUrl { get; set; } = string.Empty;
    }

    // 等保三级审计配置
    public class AuditConfig
    {
        public bool EnableOperationLog { get; set; } = true;
        public int AuditLogRetentionDays { get; set; } = 180; // 等保三级要求6个月
        public bool EnableDataEncryption { get; set; } = true;
    }

    // 数据库配置
    public class DatabaseConfig
    {
        public string Provider { get; set; } = "PostgreSQL";
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 5432;
        public string DatabaseName { get; set; } = "chemical_park_ai_agent";
        public string Username { get; set; } = "postgres";
        // 密码通过 appsettings.json 或环境变量 DB_PASSWORD 注入，禁止硬编码
        public string Password { get; set; } = "";
        public int ConnectionTimeout { get; set; } = 30;
        public int MaxPoolSize { get; set; } = 20;
    }

    // 化工合规工具配置
    public class ChemicalToolConfig
    {
        public List<ToolDefinition> Tools { get; set; } = new()
        {
            new() { Name = "CheckHazardCategory",   Description = "查询危化品危险类别及适用国标",   KeywordTriggers = new() { "类别", "分类", "属于", "国标", "GB" } },
            new() { Name = "CheckStorageCompatibility", Description = "检查两种危化品是否可同库储存", KeywordTriggers = new() { "同库", "共存", "混合", "禁忌", "配伍", "储存冲突" } },
            new() { Name = "GetSafetyDistance",      Description = "查询设施间安全间距要求",        KeywordTriggers = new() { "安全距离", "间距", "消防通道", "储罐间距", "防火间距" } },
            new() { Name = "GetCurrentTime",         Description = "获取当前时间",                   KeywordTriggers = new() { "时间", "几点", "日期" } },
            new() { Name = "Calculate",              Description = "数学计算",                       KeywordTriggers = new() { "计算", "等于" } },
        };
    }
    // class（当前）	✅ 引用类型，可以在运行时修改；支持 JSON 反序列化；可以作为依赖注入的参数
    // struct	❌ 值类型，每次传递都会复制整个列表（5条规则）；修改不会反映到原对象
    // record	⚠️ 可以用，但 record 侧重于值相等比较，这里不需要；且 record 的 with 表达式会产生新副本，不符合"全局单例配置"的语义
    // 纯静态字段	❌ 无法从 JSON 配置文件反序列化；无法依赖注入；难以单元测试时替换
    // 设计依据：.NET Options 模式。微软官方推荐的配置方式就是 POCO class + 属性，配合 Microsoft.Extensions.Configuration 可以将 appsettings.json 自动绑定到这些类上。虽然当前项目还没接入 IConfiguration，但这是为未来扩展预留的标准接口。
    public class ToolDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> KeywordTriggers { get; set; } = new();
    }

    // 向量检索配置
    public class VectorSearchConfig
    {
        // 是否启用向量检索
        public bool EnableVectorSearch { get; set; } = true;

        // 向量嵌入模型
        public string EmbeddingModelId { get; set; } = "nomic-embed-text:latest";

        // 向量维度
        public int EmbeddingDimension { get; set; } = 768;

        // 混合检索权重（BM25权重 + 向量权重 = 1.0）
        public double Bm25Weight { get; set; } = 0.4;
        public double VectorWeight { get; set; } = 0.6;

        // 索引类型：hnsw（推荐）或 ivfflat
        public string IndexType { get; set; } = "hnsw";

        // HNSW索引参数
        public int HnswM { get; set; } = 16;  // 每层最大连接数
        public int HnswEfConstruction { get; set; } = 200;  // 构建时考察邻居数
        public int HnswEfSearch { get; set; } = 64;  // 查询时考察邻居数
    }

    // 业务评测配置
    public class EvaluationConfig
    {
        public string EvalSetPath { get; set; } = "Data/ComplianceEvalSet.json";
        public int CaseIntervalMs { get; set; } = 2000;
        public string OutputReportPath { get; set; } = "Data/eval_report.json";
    }

    // Prompt 模板配置 — 统一管理所有 LLM Prompt，调优无需改源码
    public class PromptTemplateConfig
    {
        // 角色定义
        public string SystemRole { get; set; } = "你是化工园区危化品合规审核专家。";
        public string SimpleChatRole { get; set; } = "你是友好的AI助手，名字叫{AssistantName}。";

        // 输出模板
        public string OutputTemplate { get; set; } =
            "请严格按以下模板输出（每项一行，不要多余内容）：\n" +
            "【合规判断】是/否\n" +
            "【法规依据】引用具体标准编号+条款\n" +
            "【违规点】若无违规写「无」\n" +
            "【整改建议】若无违规则写「无需整改」";

        // 对话上下文模板
        public string HistoryTemplate { get; set; } = "对话历史：\n{History}";
        public string CurrentQuestionTemplate { get; set; } = "【当前问题】{UserInput}";
        public string SimpleChatQuestionTemplate { get; set; } = "用户说：{UserInput}\n\n用户的名字可能是{UserName}。\n\n请直接回答用户，不要思考标记。";

        // 评测快速通道（精简版，无对话历史）
        public string EvalFastPrompt { get; set; } =
            "{SystemRole}\n\n" +
            "【当前问题】{UserInput}\n\n" +
            "请严格按以下模板输出（每项一行，不要多余内容）：\n" +
            "【合规判断】是/否\n" +
            "【法规依据】引用具体标准编号+条款\n" +
            "【违规点】若无违规写「无」\n" +
            "【整改建议】若无违规则写「无需整改」";
    }
}
