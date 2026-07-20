using Renci.SshNet;
var host = "connect.nmb2.seetacloud.com";
var port = 37103;
var user = "root";
var password = "32X+RIXP5/Vh";
var pubKey = File.ReadAllText(Path.Combine(Path.GetTempPath(), "ssh_test_key.pub")).Trim();
Console.WriteLine("[INFO] Connecting...");
using var client = new SshClient(host, port, user, password);
client.Connect();
Console.WriteLine("[OK] Connected.");
client.RunCommand("mkdir -p ~/.ssh").Execute();
using (var sw = new StreamWriter(client.CreateCommand("cat >> ~/.ssh/authorized_keys").CreateTextWriter())) { sw.Write(pubKey); }
client.RunCommand("chmod 600 ~/.ssh/authorized_keys; chmod 700 ~/.ssh; echo KEY_OK").Execute();
var result = client.RunCommand("hostname; whoami; uname -a; echo ==ROOT==; ls /root/");
Console.WriteLine(result.Result);
client.Disconnect();
Console.WriteLine("DONE");
