using System.Collections.Generic;
using System.Threading.Tasks;

namespace Agent1.Services
{
    public interface IToolService
    {
        Task<ToolPlan> AnalyzeAndPlanToolsAsync(string userInput, string history);
        Task<Dictionary<string, string>> ExecuteToolsAsync(ToolPlan plan, string userInput);
    }
}