
using Agent1.Modules;

namespace Agent1.Services
{
    public class ModuleFactory : IModuleFactory
    {
        private readonly ISessionService _sessionService;
        private readonly IMemoryService _memoryService;
        private readonly ILlmService _llmService;
        private readonly IToolService _toolService;
        private readonly AgentDialog _agentDialog;
        
        // 化工专用服务
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
                ModuleType.CoTSolid => new CoTSolidModule(_llmService, _sessionService, _agentDialog, _knowledgeBaseService),
                ModuleType.CoTStream => new CoTStreamModule(_llmService, _sessionService, _agentDialog, _knowledgeBaseService),
                ModuleType.ReActSolid => new ReActSolidModule(_llmService, _sessionService, _agentDialog),
                ModuleType.ReActStream => new ReActStreamModule(_llmService, _sessionService, _agentDialog),
                ModuleType.Reflection => new ReflectionModule(_llmService, _sessionService, _agentDialog),
                ModuleType.RAG => new RAGModule(_llmService, _sessionService),
                ModuleType.UnifiedDialog => new UnifiedDialogModule(_agentDialog),
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

