
using Agent1.Services;

namespace Agent1.Modules
{
    public class ReflectionModule : PipelineModuleBase
    {
        public override string Name => "Reflection (Self-Correct)";
        public override string Description => "工具调用+代码级验证+自我纠错";

        private readonly RunReflectionStreamTools _reflection;

        public ReflectionModule(ILlmService llmService, ISessionService sessionService, AgentDialog? agentDialog = null, IKnowledgeBaseService? kbService = null)
            : base(agentDialog!, sessionService)
        {
            _reflection = new RunReflectionStreamTools(llmService, sessionService, agentDialog, kbService);
        }

        public override async Task RunAsync()
        {
            await _reflection.RunReflectionStreamTool();
        }
    }
}
