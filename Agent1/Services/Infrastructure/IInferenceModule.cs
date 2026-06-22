using System.Threading.Tasks;
using Agent1.Models;

namespace Agent1.Services
{
    /// <summary>
    /// 推理模块接口 — 所有 CLI 功能模块的统一契约。
    /// 
    /// RunAsync:        原有入口，不返回结构化结果（向后兼容）
    /// RunWithResultAsync: 新入口，返回 CliExecutionResult（含安全警告/审计记录/工具调用）
    /// 
    /// 化工安全系统要求每个模块的输出必须可审计、可追溯、可被下游消费。
    /// </summary>
    public interface IInferenceModule
    {
        string Name { get; }
        string Description { get; }

        /// <summary>运行推理模块（向后兼容入口）</summary>
        Task RunAsync();

        /// <summary>
        /// 运行推理模块并返回结构化结果。
        /// 子类可覆写以返回包含安全警告/审计记录/工具调用的完整结果。
        /// </summary>
        Task<CliExecutionResult> RunWithResultAsync(string userInput);
    }
}
