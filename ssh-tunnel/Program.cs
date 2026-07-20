using Renci.SshNet;
using System.Net;
using System.Net.Sockets;

if (args.Length < 7)
{
    Console.Error.WriteLine("Usage: SshTunnel <host> <port> <user> <password> <localPort> <remoteHost> <remotePort>");
    return 1;
}

var host = args[0];
var port = int.Parse(args[1]);
var user = args[2];
var password = args[3];
var localPort = int.Parse(args[4]);
var remoteHost = args[5];
var remotePort = int.Parse(args[6]);

var connInfo = new ConnectionInfo(host, port, user, new PasswordAuthenticationMethod(user, password))
{
    Timeout = TimeSpan.FromSeconds(15)
};

using var client = new SshClient(connInfo);
client.Connect();
client.KeepAliveInterval = TimeSpan.FromSeconds(15);
Console.Error.WriteLine($"[TUNNEL] Connected. localhost:{localPort} -> {remoteHost}:{remotePort}");

using var forwardedPort = new ForwardedPortLocal("127.0.0.1", (uint)localPort, remoteHost, (uint)remotePort);
client.AddForwardedPort(forwardedPort);
forwardedPort.Start();
Console.Error.WriteLine("[TUNNEL] Forwarding active. Press Ctrl+C to stop.");

// Block forever
var mre = new ManualResetEventSlim(false);
Console.CancelKeyPress += (s, e) => { e.Cancel = true; mre.Set(); };
mre.Wait();
return 0;
