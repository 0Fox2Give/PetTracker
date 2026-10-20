using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;

namespace PetTracker;

public sealed record GameConnectionPeer(string Source, int SourcePort, string Destination, int DestinationPort);
public sealed record GameConnectionScan(IReadOnlyCollection<int> ProcessIds, IReadOnlyCollection<GameConnectionPeer> Peers);
public sealed class GameConnections
{
    [DllImport("iphlpapi.dll")]
    static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);
    readonly object gate = new();
    readonly Func<string, GameConnectionScan> scan;
    readonly Func<long> clock;
    long next;
    sealed record State(HashSet<string> Peers, HashSet<int> Owners, long Generation);
    State state = new(new(), new(), 0);
    public GameConnections() : this(Scan, () => Environment.TickCount64)
    {
    }

    public GameConnections(Func<string, GameConnectionScan> scan, Func<long> clock)
    {
        this.scan = scan;
        this.clock = clock;
    }

    public long Generation => Volatile.Read(ref state).Generation;
    public string ProcessNames { get; set; } = "AION2,AION2-Win64-Shipping";
    public bool GameRunning => Volatile.Read(ref state).Owners.Count > 0;

    static string Normal(string address) => IPAddress.TryParse(address, out var ip) && ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4().ToString() : address;
    static string Key(string remote, int rp, string local, int lp) => $"{Normal(remote)}|{rp}|{Normal(local)}|{lp}";
    public bool Allows(string source, int sp, string destination, int dp)
    {
        string key = Key(source, sp, destination, dp);
        var current = Volatile.Read(ref state);
        if (!current.Peers.Contains(key))
        {
            // A new connection must wait for an in-flight scan: dropping its first
            // packet could lose the character identity or collection baseline.
            lock (gate)
            {
                if (clock() >= next)
                    RefreshLocked();
                return Volatile.Read(ref state).Peers.Contains(key);
            }
        }

        if (clock() >= Volatile.Read(ref next) && Monitor.TryEnter(gate))
        {
            try
            {
                if (clock() >= next)
                    RefreshLocked();
            }
            // The packet that triggers an OS read failure must survive too. Explicit
            // maintenance Refresh still reports the error; new peers remain unadmitted.
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidDataException or UnauthorizedAccessException)
            {
            }
            finally
            {
                Monitor.Exit(gate);
            }
        }

        // Established traffic does not queue behind another adapter's OS scan.
        return Volatile.Read(ref state).Peers.Contains(key);
    }

    public void Refresh()
    {
        lock (gate)
            RefreshLocked();
    }

    void RefreshLocked()
    {
        try
        {
            var found = scan(ProcessNames);
            var owners = found.ProcessIds.ToHashSet();
            var peers = found.Peers.Select(p => Key(p.Source, p.SourcePort, p.Destination, p.DestinationPort)).ToHashSet();
            var old = Volatile.Read(ref state);
            Volatile.Write(ref state, new(peers, owners, old.Generation + (old.Owners.SetEquals(owners) ? 0 : 1)));
        }
        finally
        {
            Volatile.Write(ref next, clock() + 100);
        }
    }

    static GameConnectionScan Scan(string processNames)
    {
        if (OperatingSystem.IsLinux())
            return LinuxGameConnections.Scan(processNames);
        var names = processNames.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var pids = names.SelectMany(n => Process.GetProcessesByName(n)).Select(p =>
        {
            using (p)
                return p.Id;
        }).ToHashSet();
        var peers = TcpConnectionTables.Read(pids, (IntPtr buffer, ref int size, int family) => GetExtendedTcpTable(buffer, ref size, false, family, 5, 0));
        return new(pids, peers);
    }
}
