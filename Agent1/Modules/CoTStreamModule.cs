
using Agent1.Services;

namespace Agent1.Modules
{
    /// <summary>
    /// CoT流式输出模块
    /// </summary>
    public class CoTStreamModule : IInferenceModule
    {
        /// <summary>
        /// 模块名称
        /// </summary>
        public string Name => "CoT (Stream Output)";
        /// <summary>
        /// 模块描述
        /// </summary>
        public string Description => "思维链推理，豆包同款流式输出";
        /// <summary>
        /// CoT推理器
        /// </summary>
        private readonly CoT _cot;
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="llmService">LLM服务</param>
        /// <param name="sessionService">会话服务</param>
        /// <param name="agentDialog">Phase 2b: AgentDialog 统一 ReAct 循环</param>
        /// <param name="kbService">Phase 2d: 知识库服务（CoT RAG 增强）</param>
        public CoTStreamModule(ILlmService llmService, ISessionService sessionService, AgentDialog? agentDialog = null, IKnowledgeBaseService? kbService = null)
        {
            _cot = new CoT(llmService, sessionService, agentDialog, kbService);//创建CoT推理器
        }
        /// <summary>
        /// 运行推理模块
        /// </summary>
        /// <returns>异步任务</returns>
        public async Task RunAsync()
        {
            await _cot.RunCoTL();//异步运行CoT推理器
            //在RunCoT方法中，调用了LLM服务的GenerateAsync方法，生成了推理结果，就输出什么
        }
    }
}
