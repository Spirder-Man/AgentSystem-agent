using System.Threading.Tasks;
using Agent1.Models;

namespace Agent1.Services
{
    /// <summary>
    /// 推理模块抽象基类 — 为所有 CLI 功能模块提供统一的执行契约。
    /// 
    /// 化工安全系统要求：
    ///   1. 每个模块必须支持 RunWithResultAsync（返回可审计的结构化结果）
    ///   2. 安全检测和审计日志应在统一管道中处理
    /// 
    /// 默认实现：
    ///   RunWithResultAsync → AgentDialog.ExecuteAsync（享受安全检测+审计日志）
    ///   子类可覆写以使用自定义执行逻辑。
    /// </summary>
    public abstract class PipelineModuleBase : IInferenceModule
    {
        protected readonly AgentDialog _agentDialog;
        protected readonly ISessionService _sessionService;

        public abstract string Name { get; }
        public abstract string Description { get; }

        protected PipelineModuleBase(AgentDialog agentDialog, ISessionService sessionService)
        {
            _agentDialog = agentDialog;
            _sessionService = sessionService;
        }

        /// <summary>交互式运行（保留向后兼容）</summary>
        public abstract Task RunAsync();

        /// <summary>
        /// 带结构化结果的运行入口。
        /// 默认委托给 AgentDialog.ExecuteAsync，自动享受安全检测+审计日志。
        /// 子类可覆写以使用自定义执行逻辑。
        /// </summary>
        public virtual async Task<CliExecutionResult> RunWithResultAsync(string userInput)
        {
            var session = _agentDialog.CreateSession(SessionType.General);
            return await _agentDialog.ExecuteAsync(userInput, session);
        }
    }
}
