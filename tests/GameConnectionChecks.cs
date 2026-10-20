using PetTracker;

static class GameConnectionChecks
{
    public static void Run(Action<bool, string> check)
    {
        var oldPeer = new GameConnectionPeer("192.0.2.1", 13328, "192.0.2.2", 50001);
        var newPeer = new GameConnectionPeer("192.0.2.3", 13328, "192.0.2.2", 50002);
        long now = 0;
        int scans = 0;
        bool block = false, fail = false;
        var peers = new List<GameConnectionPeer> { oldPeer };
        var owners = new List<int> { 101 };
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var connections = new GameConnections(_ =>
        {
            Interlocked.Increment(ref scans);
            if (block)
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("Test scan did not release");
            }
            if (fail)
                throw new IOException("Fixture enumeration failed");
            return new(owners, peers);
        }, () => Volatile.Read(ref now));
        bool Allows(GameConnectionPeer p) => connections.Allows(p.Source, p.SourcePort, p.Destination, p.DestinationPort);
        check(Allows(oldPeer) && scans == 1 && connections.Generation == 1 && connections.GameRunning, "First packet discovers its game connection before admission");
        check(connections.Allows("::ffff:192.0.2.1", 13328, "::ffff:192.0.2.2", 50001), "IPv4-mapped IPv6 endpoints preserve connection identity");
        check(!connections.Allows(oldPeer.Source, 13328, oldPeer.Destination, 50003) && scans == 1, "A different endpoint is not admitted from a cached peer");
        owners.Add(202);
        peers.Add(newPeer);
        check(connections.Generation == 1 && !Allows(newPeer), "Mutable discovery results cannot alter a published snapshot");
        now = 200;
        block = true;
        var refresh = Task.Factory.StartNew(connections.Refresh, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Task<bool>? firstPacket = null;
        Task<bool>[] established = Array.Empty<Task<bool>>();
        try
        {
            check(entered.Wait(TimeSpan.FromSeconds(2)), "Background connection scan entered the controlled slow OS boundary");
            established = Enumerable.Range(0, 4).Select(_ => Task.Factory.StartNew(() => Allows(oldPeer), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
            check(Task.WaitAll(established, TimeSpan.FromSeconds(1)) && established.All(t => t.Result), "Established adapter packets must not block behind a slow connection scan");
            check(connections.Generation == 1 && connections.GameRunning, "Process generation remains unchanged while discovery is incomplete");
            using var queryEntered = new ManualResetEventSlim();
            firstPacket = Task.Factory.StartNew(() =>
            {
                queryEntered.Set();
                return Allows(newPeer);
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            check(queryEntered.Wait(TimeSpan.FromSeconds(2)) && !firstPacket.Wait(100), "A new connection waits for the scan instead of dropping its first packet");
        }
        finally
        {
            release.Set();
            refresh.GetAwaiter().GetResult();
            Task.WaitAll(established);
            firstPacket?.GetAwaiter().GetResult();
        }
        check(firstPacket?.Result == true && connections.Generation == 2 && scans == 2, "Complete scan atomically publishes the new owner generation and admits the waiting packet");
        block = false;
        connections.Refresh();
        check(connections.Generation == 2, "Unchanged process owners do not reset a live tracker generation");
        fail = true;
        bool rejected = false;
        try
        {
            connections.Refresh();
        }
        catch (IOException)
        {
            rejected = true;
        }
        check(rejected && connections.Generation == 2 && Allows(oldPeer) && Allows(newPeer), "Failed scan cannot partially publish or corrupt the previous complete connection snapshot");
        now += 200;
        bool packetAccepted = false;
        try
        {
            packetAccepted = Allows(oldPeer);
        }
        catch (IOException) { }
        check(packetAccepted && connections.Generation == 2, "Established packet triggering a failed scan must still use its last complete connection snapshot");
        fail = false;
        owners.Clear();
        peers.Clear();
        connections.Refresh();
        check(!connections.GameRunning && connections.Generation == 3 && !Allows(oldPeer) && !Allows(newPeer), "Completed game-exit discovery revokes old endpoints and advances generation");
    }
}
