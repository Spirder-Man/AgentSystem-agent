
using Agent1.Services;

namespace Agent1.Modules
{
    /// <summary>
    /// 思维链推理，一次性完整输出
    /// </summary>
    public class CoTSolidModule : PipelineModuleBase
    {
        public override string Name => "CoT (Solid Output)";
        public override string Description => "思维链推理，一次性完整输出";

        private readonly CoT _cot;

        public CoTSolidModule(ILlmService llmService, ISessionService sessionService, AgentDialog? agentDialog = null, IKnowledgeBaseService? kbService = null)
            : base(agentDialog!, sessionService)
        {
            _cot = new CoT(llmService, sessionService, agentDialog, kbService);
        }

        public override async Task RunAsync()
        {
            await _cot.RunCoT();
        }
    }
}
