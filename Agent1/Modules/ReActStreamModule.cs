
using Agent1.Services;

namespace Agent1.Modules
{
    public class ReActStreamModule : IInferenceModule
    {
        public string Name => "ReAct (Stream Output)";
        public string Description => "推理+行动范式，流式输出+工具调用";

        private readonly CoT _cot;

        public ReActStreamModule(ILlmService llmService, ISessionService sessionService, AgentDialog? agentDialog = null)
        {
            _cot = new CoT(llmService, sessionService, agentDialog);
        }

        public async Task RunAsync()
        {
            await _cot.RunReActStreamTools();
        }
    }
}
