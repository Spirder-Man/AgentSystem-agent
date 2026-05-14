
using Agent1.Services;

namespace Agent1.Modules
{
    public class ReActSolidModule : IInferenceModule
    {
        public string Name => "ReAct (Solid Output)";
        public string Description => "推理+行动范式，一次性完整输出";

        private readonly CoT _cot;

        public ReActSolidModule(ILlmService llmService, ISessionService sessionService)
        {
            _cot = new CoT(llmService, sessionService);
        }

        public async Task RunAsync()
        {
            await _cot.RunReActStream();
        }
    }
}
