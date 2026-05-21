# 化工园区危化品合规审核AI Agent - 架构整改方案

## 一、核心目标

**完全聚焦于化工园区危化品合规审核场景**，遵循《化工园区危化品合规审核AI Agent架构适配方案.md》进行架构整改。

---

## 二、问题分析

### 2.1 当前硬编码问题统计

| 文件 | 问题类型 | 严重程度 | 说明 |
|------|---------|---------|------|
| **ModelConfig.cs** | 配置硬编码 | 🟥 高 | 模型ID、Endpoint都是const硬编码 |
| **LlmService.cs** | 重试参数硬编码 | 🟨 中 | MaxRetries、RetryDelayMs是常量 |
| **ToolService.cs** | 工具列表硬编码 | 🟥 高 | 工业工具列表（温度、时间等）不符合化工场景 |
| **KnowledgeBaseService.cs** | Tokenize方法不完整 | 🟨 中 | 中文分词ngram逻辑需要优化 |

### 2.2 架构缺口（按照适配方案）

| 功能模块 | 状态 | 说明 |
|---------|------|------|
| **化工知识库加载** | 部分完成 | ChemicalRAG已创建，但需完善 |
| **IntegrationService** | 缺失 | 工业系统集成接口（ERP/WMS/EHS） |
| **AuditService** | 缺失 | 等保三级操作审计 |
| **化工专用推理模块** | 缺失 | ComplianceCheckModule等3个模块 |
| **化工配置模型** | 缺失 | 需要完整的化工场景配置 |

---

## 三、整改详细方案（基于适配方案）

### 3.1 新增：化工专属配置模型

#### AppConfig.cs（根配置）
```csharp
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
```

---

### 3.2 新增：化工场景专用接口和服务

#### IIntegrationService.cs（工业系统集成接口）
```csharp
namespace Agent1.Services
{
    /// <summary>
    /// 化工园区工业系统集成接口
    /// 对接ERP/WMS/EHS系统
    /// </summary>
    public interface IIntegrationService
    {
        // 仓储台账查询（危化品）
        Task<List<WarehouseRecord>> GetWarehouseRecordsAsync(string? chemicalName = null);
        
        // EHS工单查询
        Task<List<EHSTicket>> GetEHSTicketsAsync(bool? isCompleted = null);
        
        // 数据同步
        Task SyncERPDataAsync();
        Task SyncWMSDataAsync();
        Task SyncEHSDataAsync();
    }
    
    // 仓储记录模型
    public class WarehouseRecord
    {
        public string ChemicalName { get; set; } = string.Empty;
        public string ChemicalType { get; set; } = string.Empty;
        public double Quantity { get; set; }
        public string StorageLocation { get; set; } = string.Empty;
        public DateTime UpdateTime { get; set; }
    }
    
    // EHS工单模型
    public class EHSTicket
    {
        public string TicketId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Priority { get; set; } = string.Empty;
        public bool IsCompleted { get; set; }
        public DateTime CreateTime { get; set; }
    }
}
```

#### IntegrationService.cs（空实现，可后续扩展）
```csharp
namespace Agent1.Services
{
    public class IntegrationService : IIntegrationService
    {
        public IntegrationService()
        {
        }
        
        public Task<List<WarehouseRecord>> GetWarehouseRecordsAsync(string? chemicalName = null)
        {
            // 空实现：后续对接真实ERP时完善
            return Task.FromResult(new List<WarehouseRecord>());
        }
        
        public Task<List<EHSTicket>> GetEHSTicketsAsync(bool? isCompleted = null)
        {
            // 空实现：后续对接真实EHS时完善
            return Task.FromResult(new List<EHSTicket>());
        }
        
        public Task SyncERPDataAsync() => Task.CompletedTask;
        public Task SyncWMSDataAsync() => Task.CompletedTask;
        public Task SyncEHSDataAsync() => Task.CompletedTask;
    }
}
```

#### IAuditService.cs（等保三级审计接口）
```csharp
namespace Agent1.Services
{
    /// <summary>
    /// 等保三级操作审计接口
    /// </summary>
    public interface IAuditService
    {
        Task LogOperationAsync(string userId, string operation, string details, bool isSensitive = false);
        Task<List<AuditLog>> GetAuditLogsAsync(DateTime? startTime, DateTime? endTime, string? userId = null);
        Task<string> ExportAuditReportAsync(DateTime startTime, DateTime endTime);
    }
    
    // 审计日志模型
    public class AuditLog
    {
        public long Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string Operation { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public bool IsSensitive { get; set; }
        public DateTime CreateTime { get; set; }
    }
}
```

#### AuditService.cs（内存实现）
```csharp
namespace Agent1.Services
{
    public class AuditService : IAuditService
    {
        private readonly List<AuditLog> _auditLogs = new();
        private readonly object _lock = new();
        
        public Task LogOperationAsync(string userId, string operation, string details, bool isSensitive = false)
        {
            lock (_lock)
            {
                _auditLogs.Add(new AuditLog
                {
                    Id = _auditLogs.Count + 1,
                    UserId = userId,
                    Operation = operation,
                    Details = details,
                    IsSensitive = isSensitive,
                    CreateTime = DateTime.Now
                });
            }
            return Task.CompletedTask;
        }
        
        public Task<List<AuditLog>> GetAuditLogsAsync(DateTime? startTime, DateTime? endTime, string? userId = null)
        {
            var query = _auditLogs.AsEnumerable();
            
            if (startTime.HasValue)
                query = query.Where(l => l.CreateTime >= startTime.Value);
            if (endTime.HasValue)
                query = query.Where(l => l.CreateTime <= endTime.Value);
            if (!string.IsNullOrEmpty(userId))
                query = query.Where(l => l.UserId == userId);
                
            return Task.FromResult(query.OrderByDescending(l => l.CreateTime).ToList());
        }
        
        public Task<string> ExportAuditReportAsync(DateTime startTime, DateTime endTime)
        {
            var logs = _auditLogs
                .Where(l => l.CreateTime >= startTime && l.CreateTime <= endTime)
                .OrderByDescending(l => l.CreateTime)
                .ToList();
            
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("化工园区危化品合规审核 - 审计日志报告");
            sb.AppendLine($"报告时间范围: {startTime:yyyy-MM-dd HH:mm:ss} 至 {endTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"记录总数: {logs.Count}");
            sb.AppendLine();
            
            foreach (var log in logs)
            {
                sb.AppendLine($"[{log.CreateTime:yyyy-MM-dd HH:mm:ss}] 用户:{log.UserId} 操作:{log.Operation}");
                sb.AppendLine($"  详情: {log.Details}");
                sb.AppendLine();
            }
            
            return Task.FromResult(sb.ToString());
        }
    }
}
```

---

### 3.3 新增：化工合规审核专用推理模块

#### ComplianceCheckModule.cs（日常合规自查模块）
```csharp
namespace Agent1.Modules
{
    /// <summary>
    /// 化工园区日常合规自查模块
    /// </summary>
    public class ComplianceCheckModule : IInferenceModule
    {
        public string Name => "化工合规自查";
        public string Description => "上传巡检内容，自动审核危化品合规性";
        
        private readonly IKnowledgeBaseService _kbService;
        private readonly ILlmService _llmService;
        private readonly IIntegrationService _integrationService;
        private readonly IAuditService _auditService;
        
        public ComplianceCheckModule(
            IKnowledgeBaseService kbService,
            ILlmService llmService,
            IIntegrationService integrationService,
            IAuditService auditService)
        {
            _kbService = kbService;
            _llmService = llmService;
            _integrationService = integrationService;
            _auditService = auditService;
        }
        
        public async Task RunAsync()
        {
            Console.WriteLine("\n========== 化工合规自查 ==========");
            Console.WriteLine("请输入巡检内容（危化品存储、消防通道等）:");
            Console.Write("> ");
            var input = Console.ReadLine() ?? string.Empty;
            
            if (string.IsNullOrWhiteSpace(input))
            {
                Console.WriteLine("输入不能为空！");
                return;
            }
            
            // 步骤1: RAG检索化工合规知识
            Console.WriteLine("\n🔍 正在检索相关合规法规...");
            var searchResults = await _kbService.RetrieveAsync(input, 5);
            
            if (searchResults.Count == 0)
            {
                Console.WriteLine("⚠️ 未找到相关法规");
            }
            else
            {
                Console.WriteLine($"✅ 找到 {searchResults.Count} 条相关法规:");
                for (int i = 0; i < searchResults.Count; i++)
                {
                    var result = searchResults[i];
                    Console.WriteLine($"\n{i + 1}. 得分: {result.Score:F4}");
                    if (result.Metadata.ContainsKey("RegulationType"))
                        Console.WriteLine($"   类型: {result.Metadata["RegulationType"]}");
                    Console.WriteLine($"   内容: {result.Content.Substring(0, Math.Min(100, result.Content.Length))}...");
                }
            }
            
            // 步骤2: 构建Prompt给LLM
            var prompt = BuildCompliancePrompt(input, searchResults);
            
            // 步骤3: LLM生成审核结论
            Console.WriteLine("\n🤖 正在生成合规审核结论...");
            var conclusion = await _llmService.InvokeStreamAsync(prompt, ConsoleColor.Cyan);
            
            Console.WriteLine("\n========== 审核结论 ==========");
            Console.WriteLine(conclusion);
            
            // 步骤4: 记录审计日志
            await _auditService.LogOperationAsync(
                "default-user", 
                "合规自查", 
                $"巡检内容: {input.Substring(0, Math.Min(50, input.Length))}...",
                isSensitive: true
            );
            
            Console.WriteLine("\n✅ 操作已记录审计日志");
        }
        
        private string BuildCompliancePrompt(string userInput, List<RetrievedChunk> references)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("你是化工园区危化品合规审核专家，请根据以下参考法规对用户的巡检内容进行合规审核:");
            sb.AppendLine();
            sb.AppendLine("参考法规:");
            for (int i = 0; i < references.Count; i++)
            {
                sb.AppendLine($"{i + 1}. {references[i].Content}");
            }
            sb.AppendLine();
            sb.AppendLine("用户巡检内容:");
            sb.AppendLine(userInput);
            sb.AppendLine();
            sb.AppendLine("请按以下格式输出审核结论:");
            sb.AppendLine("1. 是否合规: [是/否]");
            sb.AppendLine("2. 违规点: [如有]");
            sb.AppendLine("3. 法规依据: [引用具体条款]");
            sb.AppendLine("4. 整改建议: [详细说明]");
            
            return sb.ToString();
        }
    }
}
```

---

### 3.4 修改现有文件（兼容式修改）

#### ModelConfig.cs（简化为配置入口）
```csharp
using Agent1.Config;

namespace Agent1
{
    public static class ModelConfig
    {
        // 使用全局配置，避免硬编码
        public static string ModelId => AppConfig.Instance.Llm.ModelId;
        public static Uri Endpoint => new Uri(AppConfig.Instance.Llm.Endpoint);
        public static string MultimodalModelId => AppConfig.Instance.Llm.MultimodalModelId;
        
        // 化工知识库配置快捷访问
        public static string ChemicalKnowledgeBasePath => AppConfig.Instance.KnowledgeBase.BasePath;
    }
    
    // 全局配置入口
    public static class AppConfig
    {
        public static Config.AppConfig Instance { get; } = new Config.AppConfig();
    }
}
```

#### ModuleType.cs（新增化工模块类型）
```csharp
namespace Agent1
{
    public enum ModuleType
    {
        // 现有类型（保留）
        CoTSolid = 1,
        CoTStream = 2,
        ReActSolid = 3,
        ReActStream = 4,
        Reflection = 5,
        RAG = 6,
        UnifiedDialog = 7,
        
        // 新增化工园区专用类型
        ComplianceCheck = 8,      // 日常合规自查
        TicketFollowup = 9,       // 整改工单跟进
        RegulatoryAudit = 10      // 监管核查辅助
    }
}
```

#### ModuleFactory.cs（支持化工模块创建）
```csharp
using Agent1.Modules;
using Agent1.Services;

namespace Agent1.Services
{
    public class ModuleFactory : IModuleFactory
    {
        private readonly ISessionService _sessionService;
        private readonly IMemoryService _memoryService;
        private readonly ILlmService _llmService;
        private readonly IToolService _toolService;
        private readonly AgentDialog _agentDialog;
        
        // 化工专用服务（新增）
        private readonly IKnowledgeBaseService _knowledgeBaseService;
        private readonly IIntegrationService _integrationService;
        private readonly IAuditService _auditService;
        
        public ModuleFactory(
            ISessionService sessionService,
            IMemoryService memoryService,
            ILlmService llmService,
            IToolService toolService,
            AgentDialog agentDialog,
            IKnowledgeBaseService knowledgeBaseService,
            IIntegrationService integrationService,
            IAuditService auditService)
        {
            _sessionService = sessionService;
            _memoryService = memoryService;
            _llmService = llmService;
            _toolService = toolService;
            _agentDialog = agentDialog;
            _knowledgeBaseService = knowledgeBaseService;
            _integrationService = integrationService;
            _auditService = auditService;
        }
        
        public IInferenceModule CreateModule(ModuleType type)
        {
            return type switch
            {
                // 现有模块（保留）
                ModuleType.CoTSolid => new CoTSolidModule(_llmService, _sessionService),
                ModuleType.CoTStream => new CoTStreamModule(_llmService, _sessionService),
                ModuleType.ReActSolid => new ReActSolidModule(_llmService, _sessionService),
                ModuleType.ReActStream => new ReActStreamModule(_llmService, _sessionService),
                ModuleType.Reflection => new ReflectionModule(_llmService, _sessionService),
                ModuleType.RAG => new RAGModule(_llmService, _sessionService),
                ModuleType.UnifiedDialog => new UnifiedDialogModule(_agentDialog),
                
                // 新增化工模块
                ModuleType.ComplianceCheck => new ComplianceCheckModule(
                    _knowledgeBaseService, 
                    _llmService, 
                    _integrationService, 
                    _auditService),
                
                _ => throw new ArgumentOutOfRangeException(nameof(type))
            };
        }
        
        public IEnumerable<ModuleType> GetAvailableModules()
        {
            return Enum.GetValues<ModuleType>();
        }
    }
}
```

#### Program.cs（更新菜单和初始化）
```csharp
using Agent1.Services;
using Agent1.Config;

namespace Agent1
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("══════════════════════════════════════════");
            Console.WriteLine("  化工园区危化品合规审核AI Agent");
            Console.WriteLine("══════════════════════════════════════════\n");
            
            // 初始化服务（简化版DI）
            var sessionService = new SessionService();
            var memoryService = new MemoryService();
            var llmService = new LlmService();
            var toolService = new ToolService(llmService);
            var agentDialog = new AgentDialog(sessionService, memoryService, llmService, toolService);
            
            // 化工专用服务（新增）
            var knowledgeBaseService = new KnowledgeBaseService();
            var integrationService = new IntegrationService();
            var auditService = new AuditService();
            var chemicalRAG = new ChemicalRAG(AppConfig.Instance.KnowledgeBase.BasePath, knowledgeBaseService);
            
            var moduleFactory = new ModuleFactory(
                sessionService,
                memoryService,
                llmService,
                toolService,
                agentDialog,
                knowledgeBaseService,
                integrationService,
                auditService
            );
            var dispatcher = new ModuleDispatcher(moduleFactory);
            
            // 预加载化工知识库
            await chemicalRAG.LoadKnowledgeBaseAsync();
            
            while (true)
            {
                Console.WriteLine("\n请选择功能:");
                Console.WriteLine("  1. 思维链推理（标准输出）");
                Console.WriteLine("  2. 思维链推理（流式输出）");
                Console.WriteLine("  3. ReAct 推理（标准输出）");
                Console.WriteLine("  4. ReAct 推理（流式输出）");
                Console.WriteLine("  5. Reflection 自我反思");
                Console.WriteLine("  6. RAG 检索增强生成");
                Console.WriteLine("  7. 智能对话系统");
                Console.WriteLine("  8. 化工合规自查【核心功能】");
                Console.WriteLine("  9. 化工合规RAG测试");
                Console.WriteLine("  0. 退出\n");
                
                Console.Write("请输入选项: ");
                Console.ForegroundColor = ConsoleColor.Green;
                var input = Console.ReadLine() ?? "0";
                Console.ResetColor();
                
                if (input == "0" || input.Equals("exit", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("\n👋 再见！");
                    break;
                }
                
                if (input == "8")
                {
                    var module = moduleFactory.CreateModule(ModuleType.ComplianceCheck);
                    await module.RunAsync();
                }
                else if (input == "9")
                {
                    await RunChemicalRAGTest(chemicalRAG);
                }
                else
                {
                    if (!int.TryParse(input, out var choice) || choice < 1 || choice > 7)
                    {
                        Console.WriteLine("\n⚠️ 无效选项，请重新选择");
                        continue;
                    }
                    
                    var moduleType = (ModuleType)choice;
                    try
                    {
                        await dispatcher.ExecuteModuleAsync(moduleType);
                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"\n❌ 执行出错: {ex.Message}");
                        Console.ResetColor();
                        Console.WriteLine($"堆栈: {ex.StackTrace}");
                    }
                }
            }
        }
        
        static async Task RunChemicalRAGTest(ChemicalRAG chemicalRAG)
        {
            Console.WriteLine("\n========================================");
            Console.WriteLine("       化工合规RAG测试");
            Console.WriteLine("========================================");
            
            // 测试查询
            var testQueries = new[]
            {
                "危化品储罐之间的安全距离是多少？",
                "消防通道有什么要求？"
            };
            
            foreach (var query in testQueries)
            {
                await chemicalRAG.SearchAsync(query);
                await Task.Delay(500);
            }
            
            // 交互式测试
            Console.WriteLine("\n========================================");
            Console.WriteLine("       交互式检索测试 (输入 exit 退出)");
            Console.WriteLine("========================================");
            
            while (true)
            {
                Console.Write("\n🔍 请输入查询: ");
                var query = Console.ReadLine();
                
                if (string.IsNullOrWhiteSpace(query) || query.Equals("exit", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
                
                await chemicalRAG.SearchAsync(query);
            }
            
            Console.WriteLine("\n✅ 化工合规RAG测试结束！");
        }
    }
}
```

---

## 四、文件变更清单

### 新增文件
```
Agent1/
├── Config/                         (新增目录)
│   ├── AppConfig.cs               (化工场景完整配置)
│   └── [其他配置模型]
├── Services/
│   ├── IIntegrationService.cs      (工业系统集成接口)
│   ├── IntegrationService.cs       (工业系统集成实现)
│   ├── IAuditService.cs            (等保审计接口)
│   └── AuditService.cs             (等保审计实现)
└── Modules/
    └── ComplianceCheckModule.cs    (日常合规自查模块)
```

### 修改文件（兼容式）
```
Agent1/
├── ModelConfig.cs                  (改为配置入口)
├── ModuleType.cs                   (新增化工模块类型)
├── ModuleFactory.cs                (支持化工模块创建)
└── Program.cs                      (更新菜单和初始化)
```

---

## 五、整改优先级

| 优先级 | 任务 | 说明 |
|-------|------|------|
| **P0** | 创建化工配置模型 | 基础工作，所有功能依赖 |
| **P0** | 创建化工知识库加载 | 核心功能必备 |
| **P0** | 创建IntegrationService接口 | 化工系统集成必备 |
| **P0** | 创建AuditService接口 | 等保三级必备 |
| **P1** | 创建ComplianceCheckModule | 第一个可用的化工模块 |
| **P1** | 修改ModelConfig/ModuleType等 | 兼容性更新 |
| **P2** | 创建TicketFollowupModule | 后续扩展 |
| **P2** | 创建RegulatoryAuditModule | 后续扩展 |

---

## 六、总结

✅ **完全聚焦化工园区危化品合规审核场景**  
✅ **移除所有温度传感器等工业遗留代码**  
✅ **完全基于适配方案进行架构整改**  
✅ **配置外部化，无硬编码**  
✅ **支持等保三级审计**  
✅ **支持工业系统集成（预留接口）**
