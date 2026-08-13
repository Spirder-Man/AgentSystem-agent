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
using System;
using System.IO;
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

        // ── 根治 .env 环境变量继承问题 ──
        // 根因：shell 中 source .env 仅设置 shell-local 变量，dotnet test
        // 子进程无法继承。通过 ModuleInitializer 在测试进程内加载 .env，
        // 确保 WebApplicationFactory 子进程继承所有必需环境变量。
        LoadEnvFile();
    }

    /// <summary>
    /// 从项目根目录加载 .env 文件，将 KEY=VALUE 行设为环境变量。
    /// 跳过空行、注释行（#开头）、已存在的环境变量（不覆盖）。
    /// </summary>
    private static void LoadEnvFile()
    {
        var envPath = FindEnvFile();
        if (envPath == null || !File.Exists(envPath))
            return;

        foreach (var line in File.ReadAllLines(envPath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                continue;

            var eq = trimmed.IndexOf('=');
            if (eq <= 0)
                continue;

            var key = trimmed[..eq].Trim();
            var value = trimmed[(eq + 1)..].Trim();

            // 移除引号包裹（支持 KEY="value" 和 KEY='value'）
            if ((value.StartsWith('"') && value.EndsWith('"')) ||
                (value.StartsWith('\'') && value.EndsWith('\'')))
                value = value[1..^1];

            // 不覆盖已有环境变量（CI 注入的优先级更高）
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                Environment.SetEnvironmentVariable(key, value);
        }
    }

    /// <summary>
    /// 从测试程序集目录向上查找 .env 文件。
    /// 遍历路径: bin/{Config}/{Tfm} → bin/{Config} → bin → 项目根 → 解决方案根。
    /// </summary>
    private static string? FindEnvFile()
    {
        // 先去掉尾部分隔符：GetDirectoryName 对 "...\net8.0\" 只返回 "...\net8.0"，
        // 若不去尾，首轮迭代会浪费一层，导致 5 层上限够不到项目根 .env。
        var dir = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);

        // 向上最多 5 层查找 .env
        for (int i = 0; i < 5; i++)
        {
            var candidate = Path.Combine(dir, ".env");
            if (File.Exists(candidate))
                return candidate;

            var parent = Path.GetDirectoryName(dir);
            if (parent == null || parent == dir)
                break;
            dir = parent;
        }

        return null;
    }
}
