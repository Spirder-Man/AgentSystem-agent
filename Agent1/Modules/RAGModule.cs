
using Agent1.Services;

namespace Agent1.Modules
{
    /// <summary>
    /// RAG模块
    /// </summary>
    public class RAGModule : IInferenceModule
    {
        /// <summary>
        /// 模块名称
        /// </summary>
        public string Name => "RAG (Retrieval-Augmented)";
        /// <summary>
        /// 模块描述
        /// </summary>
        public string Description => "检索增强生成，结合本地知识库";
        /// <summary>
        /// RAG推理器
        /// </summary>
        private readonly RAG _rag;
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="llmService">LLM服务</param>
        /// <param name="sessionService">会话服务</param>
        public RAGModule(ILlmService llmService, ISessionService sessionService)
        {
            _rag = new RAG(llmService, sessionService);
        }
        /// <summary>
        /// 运行RAG推理器（多轮对话）
        /// </summary>
        /// <returns>异步任务</returns>
        public async Task RunAsync()
        {
            await _rag.RunRAGReflectionStreamTools();
        }
    }
}
