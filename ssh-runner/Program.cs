using Renci.SshNet;
using Renci.SshNet.Common;
using System.Text;

if (args.Length < 4)
{
    Console.Error.WriteLine("Usage: SshRunner <host> <port> <user> <password> <command...>");
    return 1;
}

var host = args[0];
var port = int.Parse(args[1]);
var user = args[2];
var password = args[3];
var command = string.Join(" ", args.Skip(4));

try
{
    var connInfo = new ConnectionInfo(host, port, user,
        new PasswordAuthenticationMethod(user, password))
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    using var client = new SshClient(connInfo);
    client.Connect();

    using var cmd = client.CreateCommand(command);
    var result = cmd.Execute();

    Console.Write(result);
    Console.Error.Write(cmd.Error);

    client.Disconnect();
    return cmd.ExitStatus ?? 0;
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
