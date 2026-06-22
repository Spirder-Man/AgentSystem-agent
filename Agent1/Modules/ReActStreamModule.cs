
using Agent1.Services;

namespace Agent1.Modules
{
    public class ReActStreamModule : PipelineModuleBase
    {
        public override string Name => "ReAct (Stream Output)";
        public override string Description => "推理+行动范式，流式输出+工具调用";

        private readonly CoT _cot;

        public ReActStreamModule(ILlmService llmService, ISessionService sessionService, AgentDialog? agentDialog = null)
            : base(agentDialog!, sessionService)
        {
            _cot = new CoT(llmService, sessionService, agentDialog);
        }

        public override async Task RunAsync()
        {
            await _cot.RunReActStreamTools();
        }
    }
}
