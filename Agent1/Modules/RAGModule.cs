
using Agent1.Services;

namespace Agent1.Modules
{
    /// <summary>
    /// RAG模块
    /// </summary>
    public class RAGModule : PipelineModuleBase
    {
        public override string Name => "RAG (Retrieval-Augmented)";
        public override string Description => "检索增强生成，结合本地知识库";

        private readonly RAG _rag;

        public RAGModule(ILlmService llmService, ISessionService sessionService, AgentDialog? agentDialog = null)
            : base(agentDialog!, sessionService)
        {
            _rag = new RAG(llmService, sessionService);
        }

        public override async Task RunAsync()
        {
            await _rag.RunRAGReflectionStreamTools();
        }
    }
}
