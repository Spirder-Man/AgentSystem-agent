using Renci.SshNet;
using System.IO;

var host = "connect.nmb2.seetacloud.com";
var port = 37103;
var user = "root";
var pwd = "32X+RIXP5/Vh";
var pkPath = Path.Combine(Path.GetTempPath(), "ssh_test_key.pub");
var pubKey = File.ReadAllText(pkPath).Trim();

Console.WriteLine("[INFO] Connecting to " + host);
using var client = new SshClient(host, port, user, pwd);
client.Connect();
Console.WriteLine("[OK] Connected");

// Install key
client.RunCommand("mkdir -p ~/.ssh").Execute();
// Use base64 to safely transfer the key
var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(pubKey));
client.RunCommand("echo " + b64 + " | base64 -d >> ~/.ssh/authorized_keys").Execute();
client.RunCommand("chmod 600 ~/.ssh/authorized_keys; chmod 700 ~/.ssh").Execute();
Console.WriteLine("[OK] Key installed");

// Test
var r = client.RunCommand("hostname; whoami; uname -a; echo ===ROOT===; ls /root/");
Console.WriteLine(r.Result);
if (!string.IsNullOrEmpty(r.Error)) Console.WriteLine("ERR: " + r.Error);

client.Disconnect();
Console.WriteLine("DONE");
