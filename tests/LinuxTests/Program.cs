using System.Diagnostics;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using PetTracker;

string root = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(Path.GetTempPath(), "PetTracker-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int checks = 0;
void Check(bool success, string message)
{
    checks++;
    if (!success)
        throw new InvalidOperationException(message);
}

GameConnectionChecks.Run(Check);
TcpConnectionTableChecks.Run(Check);
CaptureRecoveryChecks.Run(Check);
AdapterRecoveryChecks.Run(Check, root);
PetPinChecks.Run(Check, root);

const string names = "AION2,AION2-Win64-Shipping";
Check(LinuxGameConnections.MatchesProcess("C:\\Games\\AION2.exe\0", names), "Wine Windows argv path matches");
Check(LinuxGameConnections.MatchesProcess("/usr/bin/wine64\0C:\\Games\\AION2-Win64-Shipping.exe\0", names), "Wine loader matches exact game executable");
Check(!LinuxGameConnections.MatchesProcess("/usr/bin/editor\0AION2.exe\0", names), "Mentioning game in an argument does not grant capture");
Check(!LinuxGameConnections.MatchesProcess("AION2-helper.exe\0", names), "Similar process name is not admitted");
Check(!LinuxGameConnections.MatchesProcess("wineserver\0", names), "Never admit all Wine applications through wineserver");
Check(!LinuxGameConnections.MatchesProcess("\0", names), "Empty kernel command line ignored");

string Row(string local, string remote, string state, ulong inode) => $"0: {local} {remote} {state} 00000000:00000000 00:00000000 00000000 1000 0 {inode} 1";
var table = new[]
{
    "sl local_address rem_address st tx_queue rx_queue tr tm->when retrnsmt uid timeout inode",
    Row("0100007F:C350", "0200007F:3410", "01", 123),
    Row("0100007F:C351", "0200007F:3410", "02", 124),
    Row("0100007F:C352", "0200007F:3410", "01", 999),
    Row("0100007F:C353", "00000000:0000", "0A", 123),
    Row("0100007F:C354", "0200007F:3410", "06", 123)
};
var peers = LinuxGameConnections.ParseTable(table, new HashSet<ulong> { 123, 124 });
Check(peers.Count == 2, "Only owned established and connecting sockets admitted");
Check(peers[0] == new GameConnectionPeer("127.0.0.2", 13328, "127.0.0.1", 50000), "IPv4 kernel word order and server-to-client direction");
var ipv6 = LinuxGameConnections.ParseTable(new[] { "header", Row("00000000000000000000000001000000:C350", "00000000000000000000000002000000:3410", "01", 123) }, new HashSet<ulong> { 123 });
Check(ipv6.Single() == new GameConnectionPeer("::2", 13328, "::1", 50000), "IPv6 per-word byte order");
bool rejected = false;
try { LinuxGameConnections.ParseTable(new[] { "header", Row("BAD:FFFF", "0200007F:3410", "01", 123) }, new HashSet<ulong> { 123 }); }
catch (InvalidDataException) { rejected = true; }
Check(rejected, "Malformed owned endpoint fails scan instead of broadening capture");
Check(PetFamily.Label(Catalog.Entries.First(p => p.Category == "Feral").Id) == "Fera", "Shared Fera label");
Check(PetFamily.Label(-1) == "", "Unknown family remains unknown");

if (OperatingSystem.IsLinux())
{
    string privateRoot = Path.Combine(root, "private-data");
    using (var engine = new Engine(privateRoot))
        Check((File.GetUnixFileMode(privateRoot) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) == 0,
            "New Linux collection directory is private to the current user");
    Check(Storage.Pipe(Path.Combine(root, "A")) != Storage.Pipe(Path.Combine(root, "a")), "Linux data roots are case-sensitive");
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    using var client = new TcpClient();
    client.Connect((IPEndPoint)listener.LocalEndpoint);
    using var server = listener.AcceptTcpClient();
    string executable = File.ReadAllText("/proc/self/cmdline").Split('\0')[0];
    var live = LinuxGameConnections.Scan(executable);
    Check(live.ProcessIds.Contains(Environment.ProcessId), "Real procfs discovers test process");
    int port = ((IPEndPoint)client.Client.LocalEndPoint!).Port;
    Check(live.Peers.Any(p => p.DestinationPort == port), "Real procfs resolves owned loopback socket inode");
    Check(LinuxGameConnections.Scan("no-such-pet-tracker-process").Peers.Count == 0, "Missing game never admits unrelated TCP traffic");
}

// Optional package probe, used on the Linux runner with a self-contained worker.
if (args.Length > 1)
{
    string data = Path.Combine(root, "worker");
    Storage.Save(Path.Combine(data, "settings.json"), new AppSettings { GameProcesses = "no-such-pet-tracker-process" });
    var start = new ProcessStartInfo(Path.GetFullPath(args[1])) { UseShellExecute = false };
    start.ArgumentList.Add("--data");
    start.ArgumentList.Add(data);
    using var worker = Process.Start(start)!;
    async Task<WorkerView> Request(string action)
    {
        using var pipe = new NamedPipeClientStream(".", Storage.Pipe(data), PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(15000);
        using var writer = new StreamWriter(pipe) { AutoFlush = true };
        using var reader = new StreamReader(pipe);
        await writer.WriteLineAsync(JsonSerializer.Serialize(new Command(action)));
        return JsonSerializer.Deserialize<WorkerView>((await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)))!)!;
    }
    try
    {
        var status = await Request("status");
        Check(!status.Ready && !status.CollectionSynced, "Fresh worker cannot invent a collection");
        Check(status.Devices > 0, "Linux package opens libpcap with assigned capture capability");
    }
    finally { await Request("exit"); }
    await worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
    Check(worker.ExitCode == 0, "Worker exits cleanly through same-user IPC");
}
Console.WriteLine($"{checks} portable and Linux checks passed.");
