
using Agent1.Services;

namespace Agent1.Modules
{
    /// <summary>
    /// 思维链推理，一次性完整输出
    /// </summary>
    /// 
    public class CoTSolidModule : IInferenceModule
    //该类是动态类因为没有static方法，需要在运行时实例化；
    //继承IInferenceModule接口，实现了RunAsync方法，用于运行推理模块；
    //在RunAsync方法中，调用了CoT类的RunCoT方法，Run方法中实现了思维链推理的逻辑；
    {
        /// <summary>
        /// 模块名称
        /// </summary>
        public string Name => "CoT (Solid Output)";
        /// <summary>
        /// 模块描述这里采用了硬编码
        /// </summary>
        public string Description => "思维链推理，一次性完整输出";
        /// <summary>
        /// 思维链推理服务，推理了什么，就输出什么
        /// </summary>
        private readonly CoT _cot;
        /// <summary>
        /// 构造函数，初始化思维链推理服务
        /// </summary>
        /// <param name="llmService">LLM服务</param>
        /// <param name="sessionService">会话服务</param>
        public CoTSolidModule(ILlmService llmService, ISessionService sessionService)
        {
            _cot = new CoT(llmService, sessionService);
        }

        public async Task RunAsync()
        {
            await _cot.RunCoT();
        }
    }
}
