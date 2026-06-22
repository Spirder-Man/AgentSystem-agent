
using Agent1.Services;

namespace Agent1.Modules
{
    public class ReActSolidModule : PipelineModuleBase
    {
        public override string Name => "ReAct (Solid Output)";
        public override string Description => "推理+行动范式，一次性完整输出";

        private readonly CoT _cot;

        public ReActSolidModule(ILlmService llmService, ISessionService sessionService, AgentDialog? agentDialog = null)
            : base(agentDialog!, sessionService)
        {
            _cot = new CoT(llmService, sessionService, agentDialog);
        }

        public override async Task RunAsync()
        {
            await _cot.RunReActStream();
        }
    }
}
