namespace Agent1.Services
{
    public class ModuleDispatcher
    {
        private readonly IModuleFactory _factory;
        private readonly Dictionary<ModuleType, IInferenceModule> _modules;

        public ModuleDispatcher(IModuleFactory factory)
        {
            _factory = factory;
            _modules = new Dictionary<ModuleType, IInferenceModule>();
        }
        /// <summary>
        /// 执行模块
        /// </summary>
        /// <param name="type">模块类型</param>
        /// <returns>异步任务</returns>
        public async Task ExecuteModuleAsync(ModuleType type)
        {
            /// <summary>
            /// 检查模块是否存在
            /// </summary>
            /// <param name="type">模块类型</param>
            /// <returns>异步任务</returns>
            if (!_modules.TryGetValue(type, out var module))
            {
                /// <summary>
                /// 如果模块不存在，创建新模块
                /// </summary>
                /// <param name="type">模块类型</param>
                /// <returns>异步任务</returns>
                module = _factory.CreateModule(type);
                _modules[type] = module;//将新模块添加到字典中
            }

            Console.WriteLine($"\n🚀 启动模块: {module.Name}");
            Console.WriteLine($"   {module.Description}\n");
            await module.RunAsync();
        }

        public void ListModules()
        {
            Console.WriteLine("\n📦 可用模块:");
            foreach (var type in _factory.GetAvailableModules())
            {
                var module = _factory.CreateModule(type);
                Console.WriteLine($"  [{(int)type}] {module.Name}: {module.Description}");
            }
        }
    }
}
