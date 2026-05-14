namespace Agent1.Config
{
    public class AppConfig
    {
        // LLM配置（已适配化工场景）
        public ChemicalLlmConfig Llm { get; set; } = new();

        // 化工知识库配置
        public ChemicalKnowledgeBaseConfig KnowledgeBase { get; set; } = new();

        // 工业系统集成配置
        public IntegrationConfig Integration { get; set; } = new();

        // 等保三级审计配置
        public AuditConfig Audit { get; set; } = new();
    }

    // 化工场景专用LLM配置
    public class ChemicalLlmConfig
    {
        public string ModelId { get; set; } = "deepseek-r1:local7b";
        public string Endpoint { get; set; } = "http://localhost:11434";
        public string MultimodalModelId { get; set; } = "qwen-vl:latest";
        public int MaxRetries { get; set; } = 3;
        public int RetryDelayMs { get; set; } = 1000;
        public int BufferFlushThreshold { get; set; } = 50;
    }

    // 化工知识库配置
    public class ChemicalKnowledgeBaseConfig
    {
        public string BasePath { get; set; } = @"d:\桌面\agent\化工知识库";
        public List<KnowledgeSourceConfig> Sources { get; set; } = new()
        {
            new() { Name = "国标", Path = "国标", Priority = 100 },
            new() { Name = "园区规则", Path = "园区规则", Priority = 80 },
            new() { Name = "历史案例", Path = "历史案例", Priority = 60 }
        };
        public int ChunkSize { get; set; } = 500;
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
}
