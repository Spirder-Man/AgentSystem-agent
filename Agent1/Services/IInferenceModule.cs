namespace Agent1.Services
{
    /// <summary>
    /// 推理模块接口
    /// </summary>
    public interface IInferenceModule
    {
        /// <summary>
        /// 模块名称
        /// </summary>
        string Name { get; }
        /// <summary>
        /// 模块描述
        /// </summary>
        string Description { get; }
        /// <summary>
        /// 运行推理模块
        /// </summary>
        /// <returns>异步任务</returns>
        Task RunAsync();//异步运行推理模块
        //这里的异步任务调用的是CoT类的RunCoT方法，Run方法中实现了思维链推理的逻辑；
        //在RunCoT方法中，调用了LLM服务的GenerateAsync方法，生成了推理结果，就输出什么
        //调用了ModuleDispatcher.cs中的ExecuteModuleAsync方法吗？是的
        //在ExecuteModuleAsync方法中，调用了IInferenceModule接口的RunAsync方法，实现了模块的运行
    }
}
