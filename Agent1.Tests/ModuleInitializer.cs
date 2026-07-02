// ============================================================================
// ModuleInitializer.cs — 测试进程 UTF-8 编码初始化
// ============================================================================
// 【架构维度】[ModuleInitializer] 是 .NET 5+ 提供的模块级初始化钩子，
//   在程序集加载时、任何类型初始化器之前执行。这保证了在任何测试代码、
//   Serilog 配置、或 Console.WriteLine 调用之前，控制台编码已设为 UTF-8。
//
// 【问题根因】Windows 中文版默认 Console.OutputEncoding = GB2312(CP936)。
//   当 dotnet test 运行测试时，Serilog Console Sink 通过 Console.Out
//   输出 UTF-8 中文文本，但 TextWriter.Encoding 使用 GB2312 编码器，
//   导致 "告警通知" → "锟芥警通锟斤拷" 乱码。
//
//   乱码传播链路:
//   dotnet test 进程 (GB2312 CP) 
//     → Serilog Console.WriteLine("告警通知") 
//     → Console.Out.TextWriter.Encoding=GB2312 错误编码
//     → TRX Logger 捕获已损坏文本 
//     → test-results.trx <StdOut> 乱码
//
// 【修复原理】设置 Console.OutputEncoding = UTF8 后：
//   ① Console.Out.TextWriter 使用 UTF-8 编码器 → 输出正确
//   ② 子进程继承控制台代码页 CP65001(UTF-8) → TRX Logger 捕获正确
//   ③ PowerShell 管道捕获 dotnet test 输出时也受益于 UTF-8 代码页
//
// 【约束】方法必须满足: static, 无参, void, 非泛型, 非嵌套
// ============================================================================
using System.Runtime.CompilerServices;

namespace Agent1.Tests;

internal static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        // 强制控制台编码为 UTF-8，防止中文乱码
        // 必须在任何测试代码或 Serilog 配置之前执行
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        System.Console.InputEncoding  = System.Text.Encoding.UTF8;
    }
}
