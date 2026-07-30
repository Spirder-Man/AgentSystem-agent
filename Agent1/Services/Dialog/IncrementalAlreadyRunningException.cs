using System;

namespace Agent1.Services
{
    /// <summary>
    /// [Bug-039 FIX ③] 增量更新已在运行时抛出。
    /// _incrementalGate.WaitAsync(0) 立即失败即抛此异常，调用方据此返回 HTTP 409 Conflict，
    /// 避免并发增量导致 DELETE/INSERT 交错撞 source_path UNIQUE（见 Bug-038/039）。
    /// </summary>
    public class IncrementalAlreadyRunningException : Exception
    {
        public IncrementalAlreadyRunningException()
            : base("知识库增量更新正在进行中，请稍后重试") { }

        public IncrementalAlreadyRunningException(string message)
            : base(message) { }

        public IncrementalAlreadyRunningException(string message, Exception innerException)
            : base(message, innerException) { }
    }
}
