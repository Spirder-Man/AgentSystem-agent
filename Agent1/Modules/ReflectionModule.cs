
using Agent1.Services;

namespace Agent1.Modules
{
    public class ReflectionModule : IInferenceModule
    {
        public string Name => "Reflection (Self-Correct)";
        public string Description => "工具调用+代码级验证+自我纠错";

        private readonly RunReflectionStreamTools _reflection;

        public ReflectionModule(ILlmService llmService, ISessionService sessionService, AgentDialog? agentDialog = null, IKnowledgeBaseService? kbService = null)
        {
            _reflection = new RunReflectionStreamTools(llmService, sessionService, agentDialog, kbService);
        }

        public async Task RunAsync()
        {
            await _reflection.RunReflectionStreamTool();
        }
    }
}
