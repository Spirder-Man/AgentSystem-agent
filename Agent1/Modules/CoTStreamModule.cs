
using Agent1.Services;

namespace Agent1.Modules
{
    /// <summary>
    /// CoT流式输出模块
    /// </summary>
    public class CoTStreamModule : PipelineModuleBase
    {
        public override string Name => "CoT (Stream Output)";
        public override string Description => "思维链推理，豆包同款流式输出";

        private readonly CoT _cot;

        public CoTStreamModule(ILlmService llmService, ISessionService sessionService, AgentDialog? agentDialog = null, IKnowledgeBaseService? kbService = null)
            : base(agentDialog!, sessionService)
        {
            _cot = new CoT(llmService, sessionService, agentDialog, kbService);
        }

        public override async Task RunAsync()
        {
            await _cot.RunCoTL();
        }
    }
}
