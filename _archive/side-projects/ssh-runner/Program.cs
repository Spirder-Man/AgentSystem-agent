using Renci.SshNet;
using Renci.SshNet.Common;
using System.Text;

if (args.Length < 4)
{
    Console.Error.WriteLine("Usage: SshRunner <host> <port> <user> <password> <command...>");
    Console.Error.WriteLine("       SshRunner --stream <host> <port> <user> <password> <command...>");
    Console.Error.WriteLine("       SshRunner --upload <host> <port> <user> <password> <local_path> <remote_path>");
    return 1;
}

// ── --upload 模式：通过 SFTP 上传文件 ──
bool uploadMode = args[0] == "--upload";
// ── --stream 模式：创建伪终端，实时逐行回传输出 ──
bool streamMode = !uploadMode && args[0] == "--stream";
int argOffset = (uploadMode || streamMode) ? 1 : 0;

if (uploadMode)
{
    if (args.Length < 6 + argOffset)
    {
        Console.Error.WriteLine("Usage: SshRunner --upload <host> <port> <user> <password> <local_path> <remote_path>");
        return 1;
    }
}
else if (streamMode && args.Length < 5)
{
    Console.Error.WriteLine("Usage: SshRunner --stream <host> <port> <user> <password> <command...>");
    return 1;
}

var host = args[argOffset];
var port = int.Parse(args[argOffset + 1]);
var user = args[argOffset + 2];
var password = args[argOffset + 3];

// ═══════════════════════════════════════════════════════════
// 企业级 SSH 连接配置
// L1: KeepAlive 15s — 防止 NAT 网关/防火墙空闲断连
// L2: CommandTimeout 30s — 远程负载高时快速失败，避免无限阻塞
// L3: 连接重试 3 次 (指数退避 2s/4s/8s) — 应对 sshd CPU 竞争
// ═══════════════════════════════════════════════════════════
const int CONNECTION_TIMEOUT_SEC = 30;
const int KEEPALIVE_INTERVAL_SEC = 15;
const int COMMAND_TIMEOUT_SEC = 30;
const int MAX_CONNECTION_RETRIES = 3;

// ── 上传模式：本地路径 + 远程路径 ──
if (uploadMode)
{
    var localPath = args[argOffset + 4];
    var remotePath = args[argOffset + 5];
    if (!File.Exists(localPath))
    {
        Console.Error.WriteLine($"SSH_ERROR: 本地文件不存在: {localPath}");
        return 5;
    }

    var authMethod = new PasswordAuthenticationMethod(user, password);
    var uploadConnInfo = new ConnectionInfo(host, port, user, authMethod)
    {
        Timeout = TimeSpan.FromSeconds(CONNECTION_TIMEOUT_SEC),
        MaxSessions = 2
    };

    using var sftp = new SftpClient(uploadConnInfo);
    for (int attempt2 = 1; attempt2 <= MAX_CONNECTION_RETRIES; attempt2++)
    {
        try
        {
            sftp.Connect();
            break;
        }
        catch when (attempt2 < MAX_CONNECTION_RETRIES)
        {
            var delaySec = (int)Math.Pow(2, attempt2);
            Console.Error.WriteLine($"[SSHRUNNER] SFTP 连接失败，{delaySec}s 后重试 ({attempt2}/{MAX_CONNECTION_RETRIES})...");
            Thread.Sleep(delaySec * 1000);
        }
    }

    if (!sftp.IsConnected)
    {
        Console.Error.WriteLine("SSH_CONNECT_FAILED: SFTP 重试耗尽");
        return 4;
    }

    // 确保远程目录存在
    var remoteDir = Path.GetDirectoryName(remotePath)?.Replace('\\', '/');
    if (!string.IsNullOrEmpty(remoteDir) && !remoteDir.StartsWith("/"))
        remoteDir = "/" + remoteDir;
    if (!string.IsNullOrEmpty(remoteDir))
    {
        try { sftp.CreateDirectory(remoteDir); } catch { /* 目录已存在 */ }
    }

    using var fs = File.OpenRead(localPath);
    sftp.UploadFile(fs, remotePath, true);
    sftp.Disconnect();
    Console.WriteLine($"UPLOAD_OK: {localPath} -> {remotePath} ({fs.Length} bytes)");
    return 0;
}

var command = string.Join(" ", args.Skip(argOffset + 4));

try
{
    var connInfo = new ConnectionInfo(host, port, user,
        new PasswordAuthenticationMethod(user, password))
    {
        Timeout = TimeSpan.FromSeconds(CONNECTION_TIMEOUT_SEC),
        MaxSessions = 2  // 限制并发会话，避免触发 sshd MaxSessions 上限
    };

    using var client = new SshClient(connInfo);

    // ── L3: 连接重试 (指数退避) ──
    for (int attempt = 1; attempt <= MAX_CONNECTION_RETRIES; attempt++)
    {
        try
        {
            client.Connect();
            break;
        }
        catch (SshConnectionException) when (attempt < MAX_CONNECTION_RETRIES)
        {
            var delaySec = (int)Math.Pow(2, attempt); // 2s, 4s, 8s
            Console.Error.WriteLine($"[SSHRUNNER] 连接失败，{delaySec}s 后重试 ({attempt}/{MAX_CONNECTION_RETRIES})...");
            Thread.Sleep(delaySec * 1000);
        }
        catch (SshOperationTimeoutException) when (attempt < MAX_CONNECTION_RETRIES)
        {
            var delaySec = (int)Math.Pow(2, attempt);
            Console.Error.WriteLine($"[SSHRUNNER] 连接超时，{delaySec}s 后重试 ({attempt}/{MAX_CONNECTION_RETRIES})...");
            Thread.Sleep(delaySec * 1000);
        }
    }

    if (!client.IsConnected)
    {
        Console.Error.WriteLine("SSH_CONNECT_FAILED: 重试耗尽，无法建立连接");
        return 4;
    }

    // ── L1: KeepAlive 心跳 ──
    client.KeepAliveInterval = TimeSpan.FromSeconds(KEEPALIVE_INTERVAL_SEC);

    if (streamMode)
    {
        // ═══════════════════════════════════════════════════════════
        // 流式模式：创建 xterm 伪终端，前台执行命令，实时回传输出
        // 适用于 auto_test_v2.sh 等长时间运行的交互式脚本
        // ═══════════════════════════════════════════════════════════
        using var shell = client.CreateShellStream("xterm-256color", 200, 60, 800, 600, 4096);

        // 注入哨兵命令 — 脚本执行完后回显退出码（固定前缀，C#侧直接匹配）
        var sentinelPrefix = "__SSHRUNNER_EXIT__";
        shell.WriteLine($"({command}); echo '{sentinelPrefix}'$?__");
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

                // 检测哨兵行（格式: __SSHRUNNER_EXIT__0__）
                if (line.StartsWith(sentinelPrefix))
                {
                    var rest = line.Substring(sentinelPrefix.Length);
                    var codePart = rest.Split('_')[0];
                    if (int.TryParse(codePart, out var ec))
                        exitCode = ec;
                    break;
                }

                // 正常输出原样打印（跳过哨兵 echo 回显）
                if (!string.IsNullOrEmpty(line) && !line.StartsWith(sentinelPrefix))
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
        // L2: CommandTimeout 30s — 远程负载高时不无限等待
        // ═══════════════════════════════════════════════════════════
        using var cmd = client.CreateCommand(command);
        cmd.CommandTimeout = TimeSpan.FromSeconds(COMMAND_TIMEOUT_SEC);

        string result;
        try
        {
            result = cmd.Execute();
            Console.Write(result);
            Console.Error.Write(cmd.Error);
        }
        catch (SshOperationTimeoutException)
        {
            // 命令超时：仍然尝试获取已输出的部分内容
            Console.Error.WriteLine($"[SSHRUNNER] 命令执行超时 ({COMMAND_TIMEOUT_SEC}s)，已输出部分结果");
            if (!string.IsNullOrEmpty(cmd.Result))
                Console.Write(cmd.Result);
            if (!string.IsNullOrEmpty(cmd.Error))
                Console.Error.Write(cmd.Error);
            client.Disconnect();
            return 124; // 同 timeout 命令的退出码
        }

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
