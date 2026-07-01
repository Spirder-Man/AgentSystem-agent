using Renci.SshNet;
using Renci.SshNet.Common;
using System.Text;

if (args.Length < 4)
{
    Console.Error.WriteLine("Usage: SshRunner <host> <port> <user> <password> <command...>");
    Console.Error.WriteLine("       SshRunner --stream <host> <port> <user> <password> <command...>");
    return 1;
}

// ── --stream 模式：创建伪终端，实时逐行回传输出 ──
bool streamMode = args[0] == "--stream";
int argOffset = streamMode ? 1 : 0;

if (streamMode && args.Length < 5)
{
    Console.Error.WriteLine("Usage: SshRunner --stream <host> <port> <user> <password> <command...>");
    return 1;
}

var host = args[argOffset];
var port = int.Parse(args[argOffset + 1]);
var user = args[argOffset + 2];
var password = args[argOffset + 3];
var command = string.Join(" ", args.Skip(argOffset + 4));

try
{
    var connInfo = new ConnectionInfo(host, port, user,
        new PasswordAuthenticationMethod(user, password))
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    using var client = new SshClient(connInfo);
    client.Connect();

    if (streamMode)
    {
        // ═══════════════════════════════════════════════════════════
        // 流式模式：创建 xterm 伪终端，前台执行命令，实时回传输出
        // 适用于 auto_test_v2.sh 等长时间运行的交互式脚本
        // ═══════════════════════════════════════════════════════════
        using var shell = client.CreateShellStream("xterm-256color", 200, 60, 800, 600, 4096);

        // 注入哨兵命令 — 脚本执行完后回显退出码
        var sentinel = $"__SSHRUNNER_EXIT_$(date +%s)__";
        shell.WriteLine($"({command}); echo '{sentinel}:'$?':'");
        shell.Flush();

        var sb = new StringBuilder();
        var lastActivity = DateTime.UtcNow;
        int exitCode = 0;

        while (true)
        {
            // 非阻塞读取，500ms 超时
            var line = shell.ReadLine(TimeSpan.FromMilliseconds(500));

            if (line != null)
            {
                lastActivity = DateTime.UtcNow;

                // 检测哨兵行
                if (line.StartsWith(sentinel))
                {
                    // 格式: __SSHRUNNER_EXIT_1719000000__:0:
                    var parts = line.Split(':');
                    if (parts.Length >= 2 && int.TryParse(parts[1], out var ec))
                        exitCode = ec;
                    break;
                }

                // 过滤掉 shell 自身的 echo 回显（哨兵命令本身的回显）
                // 正常输出原样打印
                if (!string.IsNullOrEmpty(line) && !line.Contains(sentinel.Split('_').Last().Split('_')[0]))
                {
                    Console.WriteLine(line);
                }
            }

            // 无活动超时保护：20 分钟无输出则退出
            if ((DateTime.UtcNow - lastActivity).TotalMinutes > 20)
            {
                Console.Error.WriteLine($"\n[SSHRUNNER] 流式超时 (20min 无活动)，强制断开");
                break;
            }
        }

        shell.WriteLine("exit");
        client.Disconnect();
        return exitCode;
    }
    else
    {
        // ═══════════════════════════════════════════════════════════
        // 普通模式：阻塞执行，等命令结束后一次性返回全部输出
        // ═══════════════════════════════════════════════════════════
        using var cmd = client.CreateCommand(command);
        var result = cmd.Execute();

        Console.Write(result);
        Console.Error.Write(cmd.Error);

        client.Disconnect();
        return cmd.ExitStatus ?? 0;
    }
}
catch (SshAuthenticationException)
{
    Console.Error.WriteLine("SSH_AUTH_FAILED");
    return 2;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"SSH_ERROR: {ex.Message}");
    return 3;
}
