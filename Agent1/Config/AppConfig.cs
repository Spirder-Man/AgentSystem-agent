using System;
using Microsoft.Extensions.Configuration;

namespace Agent1.Config
{
    // [P2] 检索模式枚举 — 替代字符串 "bm25"/"vector"/"hybrid"，编译期类型安全
    public enum SearchModeType
    {
        Bm25,
        Vector,
        Hybrid
    }

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

        // [P3] 记忆系统配置
        public MemoryConfig Memory { get; set; } = new();

        // [P1] 告警配置（邮件 SMTP + 收件人）
        public AlertingConfig Alerting { get; set; } = new();

        // [P3] 安全检测配置
        public SafetyConfig Safety { get; set; } = new();

        // 业务评测配置
        public EvaluationConfig Evaluation { get; set; } = new();

        // Prompt 模板配置
        public PromptTemplateConfig PromptTemplates { get; set; } = new();

        // ── 单例 ──
        private static AppConfig? _instance;

        public static AppConfig Instance
        {
            get
            {
                if (_instance == null)
                    throw new InvalidOperationException("AppConfig 尚未加载。请先调用 AppConfig.Load(configuration)");
                return _instance;
            }
        }

        public static void Load(IConfiguration configuration)
        {
            var config = new AppConfig();
            configuration.Bind(config);

            var dbPassword = Environment.GetEnvironmentVariable("DB_PASSWORD");
            if (!string.IsNullOrEmpty(dbPassword))
                config.Database.Password = dbPassword;

            var dbUser = Environment.GetEnvironmentVariable("DB_USERNAME");
            if (!string.IsNullOrEmpty(dbUser))
                config.Database.Username = dbUser;

            var dbHost = Environment.GetEnvironmentVariable("DB_HOST");
            if (!string.IsNullOrEmpty(dbHost))
                config.Database.Host = dbHost;

            var dbName = Environment.GetEnvironmentVariable("DB_NAME");
            if (!string.IsNullOrEmpty(dbName))
                config.Database.DatabaseName = dbName;

            // [P1] 告警邮件密码 — 从环境变量注入，绝不写入配置文件
            // [P0-2] LLM 端点 — 支持环境变量覆盖（Linux llama-server :8080 / Windows Ollama :11434）
            var llmEndpoint = Environment.GetEnvironmentVariable("LLM_ENDPOINT");
            if (!string.IsNullOrEmpty(llmEndpoint))
                config.Llm.Endpoint = llmEndpoint;

            var mmEndpoint = Environment.GetEnvironmentVariable("MULTIMODAL_ENDPOINT");
            if (!string.IsNullOrEmpty(mmEndpoint))
                config.Llm.MultimodalEndpoint = mmEndpoint;

            var mmModel = Environment.GetEnvironmentVariable("MULTIMODAL_MODEL_ID");
            if (!string.IsNullOrEmpty(mmModel))
                config.Llm.MultimodalModelId = mmModel;

            // [P0-1] 知识库路径 — 支持环境变量覆盖（Linux 绝对路径 / Windows 相对路径）
            var kbPath = Environment.GetEnvironmentVariable("KNOWLEDGE_BASE_PATH");
            if (!string.IsNullOrEmpty(kbPath))
                config.KnowledgeBase.BasePath = kbPath;

            // [OCR] 扫描件 PDF 视觉 OCR 回退开关 — 需 8083 视觉实例在线时才建议打开
            var visionOcr = Environment.GetEnvironmentVariable("ENABLE_VISION_OCR");
            if (!string.IsNullOrEmpty(visionOcr) && bool.TryParse(visionOcr, out var visionOcrEnabled))
                config.KnowledgeBase.EnableVisionOcr = visionOcrEnabled;

            var alertPwd = Environment.GetEnvironmentVariable("ALERT_EMAIL_PASSWORD");
            if (!string.IsNullOrEmpty(alertPwd))
                config.Alerting.Email.SenderPassword = alertPwd;

            // 收件人列表 — 环境变量逗号分隔
            var recipients = Environment.GetEnvironmentVariable("ALERT_RECIPIENT_EMAILS");
            if (!string.IsNullOrEmpty(recipients))
                config.Alerting.Email.RecipientEmails = recipients
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();

            _instance = config;
            ModelConfig.Initialize(config);
        }

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
            if (string.IsNullOrWhiteSpace(Database.Password))
            {
                // 检查环境变量是否提供了密码
                var envPwd = Environment.GetEnvironmentVariable("DB_PASSWORD");
                if (string.IsNullOrEmpty(envPwd))
                    errors.Add("Database.Password 未配置 — 请设置 DB_PASSWORD 环境变量或在 appsettings.json 中配置（禁止空密码连接）");
            }

            if (string.IsNullOrWhiteSpace(VectorSearch.EmbeddingModelId))
                errors.Add("VectorSearch.EmbeddingModelId 未配置（嵌入模型不能为空）");

            if (string.IsNullOrWhiteSpace(PromptTemplates.SystemRole))
                errors.Add("PromptTemplates.SystemRole 未配置");
            if (string.IsNullOrWhiteSpace(PromptTemplates.EvalFastPrompt))
                errors.Add("PromptTemplates.EvalFastPrompt 未配置（评测 Prompt 不能为空）");

            return errors;
        }

        /// <summary>
        /// [运行时热切换] 动态切换双通道解耦架构开关。
        /// 无需重启应用，所有后续请求立即生效。
        /// 通过 admin API 端点调用：POST /admin/config/decoupled-architecture
        /// </summary>
        public static bool SetUseDecoupledArchitecture(bool enabled)
        {
            if (_instance == null)
                throw new InvalidOperationException("AppConfig 尚未加载");
            var previous = _instance.PromptTemplates.UseDecoupledArchitecture;
            _instance.PromptTemplates.UseDecoupledArchitecture = enabled;
            Serilog.Log.Information(
                "[配置热切换] UseDecoupledArchitecture: {Previous} → {Current}",
                previous, enabled);
            return previous;
        }

        /// <summary>
        /// [运行时查询] 获取当前双通道解耦架构开关状态。
        /// </summary>
        public static bool GetUseDecoupledArchitecture()
        {
            if (_instance == null) return true; // 默认启用
            return _instance.PromptTemplates.UseDecoupledArchitecture;
        }
    }

    // 化工场景专用LLM配置
    public class ChemicalLlmConfig
    {
        public string ModelId { get; set; } = "deepseek-r1:local7b";
        public string Endpoint { get; set; } = "http://localhost:8080/v1";
        public string MultimodalModelId { get; set; } = "qwen2.5-vl-7b-instruct";
        // [端口分离] 多模态(视觉)服务独占 8083，与 Reranker(8082) 分开，避免端口冲突
        public string MultimodalEndpoint { get; set; } = "http://localhost:8083/v1";

        // Phase 2a 预留: 工具调用规划专用模型（可与 ModelId 相同）
        // 未来可分离为小模型做工具规划 + 大模型做合规结论生成
        public string FunctionCallingModelId { get; set; } = "";  // 空字符串表示使用 ModelId

        public int MaxRetries { get; set; } = 3;
        public int RetryDelayMs { get; set; } = 1000;
        public int BufferFlushThreshold { get; set; } = 50;
        // [P3 生产加固] 熔断器连续失败阈值
        public int CircuitBreakerThreshold { get; set; } = 3;
    }

    // 化工知识库配置
    public class ChemicalKnowledgeBaseConfig
    {
        public string BasePath { get; set; } = "knowledgebase";
        public List<KnowledgeSourceConfig> Sources { get; set; } = new()
        {
            new() { Name = "国标", Path = "国标", Priority = 100 },
            new() { Name = "园区规则", Path = "园区规则", Priority = 80 },
            new() { Name = "历史案例", Path = "历史案例", Priority = 60 }
        };
        public int ChunkSize { get; set; } = 500;
        // [P3] RAG 结果截断上限 (字符), 防 LLM 输出全文导致幻觉
        public int ChunkOutputMaxChars { get; set; } = 300;

        // [OCR] 扫描件 PDF 视觉 OCR 回退：文本层过薄时用视觉模型(Qwen2.5-VL)逐页识别
        // 默认关闭：需 8083 视觉实例在线，且逐页推理会显著拉长全量加载耗时
        public bool EnableVisionOcr { get; set; } = false;

        // [OCR] 单个 PDF 最多 OCR 页数（防止超长文档拖垮加载）
        public int OcrMaxPagesPerPdf { get; set; } = 20;

        // [OCR] PDF 页渲染 DPI（150 兼顾清晰度与视觉模型推理速度）
        public int OcrRenderDpi { get; set; } = 150;

        // Sprint 4: 分块重叠窗口（字符数），0 表示禁用
        public int ChunkOverlap { get; set; } = 100;

        // Sprint 4: 是否启用语义分块（按标题/章节识别）
        public bool EnableSemanticChunking { get; set; } = true;

        // Sprint 4: 是否启用查询扩展
        public bool EnableQueryExpansion { get; set; } = true;

        // Sprint 5: 检索缓存 TTL（分钟），0 表示禁用
        public int QueryCacheTtlMinutes { get; set; } = 5;

        // Sprint 5: 检索缓存最大条目数
        public int QueryCacheMaxEntries { get; set; } = 500;

        // [P3] RAG 结果缓存最大条目数
        public int RagCacheMaxEntries { get; set; } = 200;
        
        // 检索模式：Bm25 / Vector / Hybrid（默认 Hybrid）
        public SearchModeType SearchMode { get; set; } = SearchModeType.Hybrid;
    }

    // 知识源配置
    public class KnowledgeSourceConfig
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public int Priority { get; set; } = 50;
    }

    // [P3] 记忆系统配置
    public class MemoryConfig
    {
        public int CompressTriggerTurns { get; set; } = 10;
        public int KeepRecentTurns { get; set; } = 5;
    }

    // [P3] 安全检测配置
    public class SafetyConfig
    {
        public int MaxInputLength { get; set; } = 4000;
        public int MaxImagePathLength { get; set; } = 500;
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
            new() { Name = "CheckStorageCompatibility", Description = "检查两种危险化学品是否可以在同一仓库中储存，用于判断同库储存合规性。当用户询问两种化学品能否放在一起、共存、同库存放时，必须调用此工具", KeywordTriggers = new() { "同库", "共存", "混合", "禁忌", "配伍", "储存冲突", "同一仓库", "放在一起", "相邻存放", "共库", "一起存放", "同库存放", "能否同存" } },
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

        // Embedding 服务端点地址（llama.cpp OpenAI兼容接口）
        public string EmbeddingEndpoint { get; set; } = "http://localhost:8081/v1";

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

        // ═══════════════════════════════════════
        // Sprint 1-2: GPU 加速配置
        // ═══════════════════════════════════════
        
        // 是否启用 GPU 嵌入生成（llama.cpp -ngl 99）
        public bool GpuEmbeddingEnabled { get; set; } = true;

        // 是否启用 GPU 向量检索（FAISS/cuVS 内存索引）
        public bool GpuSearchEnabled { get; set; } = true;

        // 是否启用 Cross-Encoder Reranker
        public bool RerankerEnabled { get; set; } = true;

        // GPU 不可用时是否自动降级为 CPU
        public bool GpuFallbackEnabled { get; set; } = true;

        // 嵌入批处理大小（llama.cpp --batch-size）
        public int EmbeddingBatchSize { get; set; } = 32;

        // 嵌入服务超时（秒）
        public int EmbeddingTimeoutSeconds { get; set; } = 30;

        // HttpClient 最大并发连接数
        public int MaxConcurrentEmbeddings { get; set; } = 4;

        // ═══════════════════════════════════════
        // Sprint 3: Reranker 配置
        // ═══════════════════════════════════════
        
        // Reranker 服务端点
        public string RerankerEndpoint { get; set; } = "http://localhost:8082/rerank";

        // Reranker 模型名称
        public string RerankerModelId { get; set; } = "bge-reranker-v2-m3";

        // Reranker 粗排召回数 (BM25+向量融合后取 topN 送 Reranker)
        public int RerankerCandidateTopK { get; set; } = 20;

        // Reranker 精排后保留数
        public int RerankerFinalTopK { get; set; } = 5;
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
        // Layer 2: 强制工具调用 + 反幻觉 + 数据不足声明
        public string EvalFastPrompt { get; set; } =
            "{SystemRole}\n\n" +
            "【强制工具调用指令】回答前必须调用至少一个可用的函数工具获取数据。\n" +
            "严禁跳过工具直接凭记忆回答——即使你认为你知道答案，也必须先通过工具验证。\n" +
            "如果工具返回的数据不足以做出判断，必须如实声明「数据不足」而非编造结论。\n\n" +
            "【输出格式】每条查询结果用一句话概括，禁止输出法规全文。只提取关键结论，不要复制粘贴整段原文。\n\n" +
            "【当前问题】{UserInput}\n\n" +
            "获取工具数据后，严格按以下模板输出（每项一行，不要多余内容）：\n" +
            "【合规判断】是/否\n" +
            "【法规依据】引用具体标准编号+条款\n" +
            "【违规点】若无违规写「无」\n" +
            "【整改建议】若无违规则写「无需整改」";

        // 评测快速通道（信息查询版）: 仅提取事实，禁止合规判断
        public string EvalFastQueryPrompt { get; set; } =
            "{SystemRole}\n\n" +
            "【输出格式】每条查询结果用一句话概括，禁止输出法规全文。只提取关键信息，不要复制粘贴整段原文。\n\n" +
            "【强制工具调用指令】回答前必须调用至少一个可用的函数工具获取数据。\n" +
            "【当前问题（信息查询，仅提取事实，禁止做合规判断）】{UserInput}\n\n" +
            "请仅提取并输出与该问题相关的具体信息（如数值、类别、法规编号等），不要主动判断是否合规。\n" +
            "输出格式：\n" +
            "【查询结果】直接给出具体信息内容（一句话概括）\n" +
            "【法规依据】引用具体标准编号+条款（如有）";

        // 双通道解耦架构开关：true=事实通道+解释通道分离，false=传统单通道
        public bool UseDecoupledArchitecture { get; set; } = true;

        // 双通道解耦架构下的 LLM 输出模板（不含法规引用要求）
        // LLM 只负责专业解读和建议，法规引用由 FactAssembler 确定性渲染
        public string OutputTemplateDecoupled { get; set; } =
            "【专业解读】基于已知事实给出专业分析\n" +
            "【操作建议】具体的行动建议（如有）\n" +
            "【注意事项】需要人工关注的风险点";
    }

    // [P1] 告警配置
    public class AlertingConfig
    {
        public bool Enabled { get; set; } = true;
        public EmailAlertConfig Email { get; set; } = new();
    }

    // [P1] 邮件告警通道配置
    public class EmailAlertConfig
    {
        public bool Enabled { get; set; } = true;
        public string SmtpHost { get; set; } = "";
        public int SmtpPort { get; set; } = 587;
        public string SenderEmail { get; set; } = "";
        /// <summary>
        /// 密码/授权码 — 通过环境变量 ALERT_EMAIL_PASSWORD 注入，
        /// 不写入 appsettings.json（安全策略）
        /// </summary>
        public string SenderPassword { get; set; } = "";
        public List<string> RecipientEmails { get; set; } = new();
    }
}
