using Agent1.Models;

namespace Agent1.Services
{
    public interface IModuleFactory
    {
        IInferenceModule CreateModule(ModuleType type);
        IEnumerable<ModuleType> GetAvailableModules();
    }
}
