using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using PacketDotNet;
using SharpPcap;
using SharpPcap.LibPcap;

namespace PetTracker;
public sealed record CaptureStreamGap(int Source, int Destination, string Device, long Bytes, string Reason);
public interface IScopedCapture
{
    int OpenDeviceCount { get; }

    event EventHandler<CaptureStreamGap>? StreamGap;
}

public interface IPrioritizedCapture
{
    void ProtectConnection(int source, int destination, string device);
    void ReleaseConnection();
}

public interface IScopedPayloadCapture
{
    event EventHandler<CapturedPayload>? ScopedPayloadCaptured;
    CapturedPayload? CurrentScopedPayload { get; }
}

// One serialized reader merges TCP ranges by full endpoint identity. It
// replaces the copied meter's capture service without changing game input.
public sealed class OrderedCapture : IPacketCaptureService, IScopedCapture, IPrioritizedCapture, IScopedPayloadCapture
{
    readonly object gate = new();
    readonly List<ICaptureDevice> devices = new();
    readonly Func<IEnumerable<ICaptureDevice>> discover;
    sealed class RunState
    {
        public volatile bool Started, Failed;
    }

    RunState? run;
    public OrderedCapture() : this(() => LibPcapLiveDeviceList.Instance.Cast<ICaptureDevice>())
    {
    }

    public OrderedCapture(Func<IEnumerable<ICaptureDevice>> discover)
    {
        this.discover = discover;
    }

    CancellationTokenSource? cancel;
    long received, retransmissions, gaps, gapBytes, reordered;
    int generation;
    public GameConnections Connections { get; } = new();
    public string? Adapter { get; set; }
    public string LogRoot { get; set; } = "";
    public CapturedPayload? CurrentScopedPayload => null;

    public event EventHandler<CapturedPayload>? ScopedPayloadCaptured;
    sealed record ProtectedConnection(int Source, int Destination, string Device);
    ProtectedConnection? protectedConnection;
    public void ProtectConnection(int source, int destination, string device)
    {
        if (source <= 0 || destination <= 0 || string.IsNullOrWhiteSpace(device))
            throw new ArgumentException("Invalid capture connection.");
        Volatile.Write(ref protectedConnection, new(source, destination, device));
    }

    public void ReleaseConnection() => Volatile.Write(ref protectedConnection, null);
    bool IsProtected(Endpoint key, Flow flow)
    {
        var active = Volatile.Read(ref protectedConnection);
        return active != null && active.Source == key.Source && active.Destination == key.Destination && active.Device == flow.Device;
    }

    public bool IsRunning => Volatile.Read(ref run)is { Started: true, Failed: false };

    public int OpenDeviceCount
    {
        get
        {
            lock (gate)
                return devices.Count;
        }
    }

    public long TcpRetransmissions => Interlocked.Read(ref retransmissions);
    public long TcpGaps => Interlocked.Read(ref gaps);
    public long TcpGapBytes => Interlocked.Read(ref gapBytes);
    public long ReorderedSegments => Interlocked.Read(ref reordered);

    public event EventHandler<CapturedPayload>? PayloadCaptured;
    public event EventHandler<CaptureStreamGap>? StreamGap;
    public event Action<string>? FlowRetired;
    readonly record struct Endpoint(string SourceIp, int Source, string DestinationIp, int Destination);
    sealed record Fragment(Endpoint Key, uint Sequence, byte[] Bytes, string Device, bool Syn, bool Ack);
    sealed class Flow(string device)
    {
        public readonly TcpReassembler Assembly = new();
        public readonly string Device = device;
        public TimeSpan LastSeen;
        public uint? Syn;
        public bool HasPayload;
    }

    sealed class QueueBudget
    {
        public long Bytes;
    }

    static string Label(Endpoint key, string device)
    {
        string a = "[" + key.SourceIp + "]:" + key.Source, b = "[" + key.DestinationIp + "]:" + key.Destination;
        string identity = StringComparer.Ordinal.Compare(a, b) < 0 ? a + "|" + b : b + "|" + a;
        return "Ordered TCP / " + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..12];
    }

    public void Start()
    {
        lock (gate)
        {
            if (IsRunning)
                return;
            if (devices.Count > 0)
                Stop();
            int current = ++generation;
            cancel = new();
            var token = cancel.Token;
            var state = new RunState();
            Volatile.Write(ref run, state);
            var queue = Channel.CreateBounded<Fragment>(new BoundedChannelOptions(8192) { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });
            var dropped = new ConcurrentDictionary<Endpoint, string>();
            var budget = new QueueBudget();
            foreach (var device in discover())
            {
                if (Adapter != null && device.Name != Adapter)
                    continue;
                PacketArrivalEventHandler? callback = null;
                CaptureStoppedEventHandler? stopped = null;
                try
                {
                    device.Open(DeviceModes.MaxResponsiveness, 10);
                    device.Filter = "tcp";
                    PacketArrivalEventHandler handler = (sender, capture) =>
                    {
                        if (token.IsCancellationRequested || state.Failed)
                            return;
                        try
                        {
                            var raw = capture.GetPacket();
                            var packet = Packet.ParsePacket(raw.LinkLayerType, raw.Data);
                            var tcp = packet.Extract<TcpPacket>();
                            var ip = packet.Extract<IPPacket>();
                            if (tcp == null || ip == null || !Connections.Allows(ip.SourceAddress.ToString(), tcp.SourcePort, ip.DestinationAddress.ToString(), tcp.DestinationPort))
                                return;
                            var payload = tcp.PayloadData ?? Array.Empty<byte>();
                            if (payload.Length == 0 && !tcp.Synchronize)
                                return;
                            var key = new Endpoint(ip.SourceAddress.ToString(), tcp.SourcePort, ip.DestinationAddress.ToString(), tcp.DestinationPort);
                            string label = device.Description ?? device.Name;
                            var fragment = new Fragment(key, tcp.SequenceNumber, (byte[])payload.Clone(), label, tcp.Synchronize, tcp.Acknowledgment);
                            if (Interlocked.Add(ref budget.Bytes, fragment.Bytes.Length) > 16 * 1024 * 1024 || !queue.Writer.TryWrite(fragment))
                            {
                                Interlocked.Add(ref budget.Bytes, -fragment.Bytes.Length);
                                dropped[key] = label;
                            }
                        }
                        catch (Exception e)
                        {
                            Trace("Packet read failed: " + e.GetType().Name);
                        }
                    };
                    callback = handler;
                    // This event runs on the adapter thread. Never take the capture/engine gate
                    // here: StopCapture can wait for that thread. A per-run flag also prevents
                    // a delayed old callback from invalidating a newly opened session.
                    stopped = (sender, status) =>
                    {
                        if (token.IsCancellationRequested)
                            return;
                        state.Failed = true;
                        Trace("Capture adapter stopped: " + status);
                    };
                    device.OnPacketArrival += handler;
                    device.OnCaptureStopped += stopped;
                    device.StartCapture();
                    devices.Add(device);
                    handlers[device] = (handler, stopped);
                }
                catch (Exception e)
                {
                    try
                    {
                        if (callback != null)
                            device.OnPacketArrival -= callback;
                        if (stopped != null)
                            device.OnCaptureStopped -= stopped;
                    }
                    catch
                    {
                    }

                    try
                    {
                        device.Close();
                    }
                    catch
                    {
                    }

                    Trace("Capture device unavailable: " + e.Message);
                }
            }

            if (devices.Count == 0)
            {
                cancel.Cancel();
                throw new InvalidOperationException(CaptureRequirements.Help);
            }

            state.Started = true;
            _ = Task.Run(() => Consume(queue.Reader, dropped, budget, current, state, token), token);
            Trace("Ordered TCP capture active on " + devices.Count + " devices; bounded 1-second reorder window.");
        }
    }

    readonly Dictionary<ICaptureDevice, (PacketArrivalEventHandler Packet, CaptureStoppedEventHandler Stopped)> handlers = new();
    async Task Consume(ChannelReader<Fragment> reader, ConcurrentDictionary<Endpoint, string> dropped, QueueBudget budget, int current, RunState state, CancellationToken token)
    {
        var flows = new CaptureFlowPool<Endpoint, Flow>(128, f => f.LastSeen, IsProtected);
        var outgoing = new Dictionary<Endpoint, TimeSpan>();
        var clock = Stopwatch.StartNew();
        void Loss(Endpoint key, string device, long bytes, string reason)
        {
            if (token.IsCancellationRequested || current != generation)
                return;
            Interlocked.Increment(ref gaps);
            Interlocked.Add(ref gapBytes, bytes);
            try
            {
                StreamGap?.Invoke(this, new(key.Source, key.Destination, device, bytes, reason));
            }
            catch (Exception e)
            {
                Trace("Gap subscriber failed: " + e.GetType().Name);
            }
        }

        void Emit(Endpoint key, Flow flow, TcpAssemblyResult result)
        {
            if (result.LostBytes > 0)
                Loss(key, flow.Device, result.LostBytes, "TCP reorder window/buffer limit exceeded");
            if (result.Duplicate)
                Interlocked.Increment(ref retransmissions);
            if (result.Buffered)
                Interlocked.Increment(ref reordered);
            foreach (var data in result.Chunks)
            {
                if (token.IsCancellationRequested || current != generation || state.Failed)
                    return;
                Interlocked.Increment(ref received);
                void Dispatch(EventHandler<CapturedPayload>? subscribers, CapturedPayload payload)
                {
                    if (subscribers == null)
                        return;
                    foreach (EventHandler<CapturedPayload> subscriber in subscribers.GetInvocationList())
                        try
                        {
                            subscriber(this, payload);
                        }
                        catch (Exception e)
                        {
                            Trace("Payload subscriber failed: " + e.GetType().Name);
                            Loss(key, flow.Device, 1, "Capture payload subscriber failure");
                        }
                }

                var payload = new CapturedPayload(key.Source, key.Destination, data, flow.Device);
                Dispatch(ScopedPayloadCaptured, payload);
                Dispatch(PayloadCaptured, payload);
            }
        }

        try
        {
            while (!token.IsCancellationRequested && current == generation)
            {
                if (state.Failed)
                {
                    foreach (var pair in flows.Entries)
                        Loss(pair.Key, pair.Value.Device, 1, "Capture adapter stopped");
                    return;
                }

                foreach (var lost in dropped.ToArray())
                    if (dropped.TryRemove(lost.Key, out var label))
                    {
                        Loss(lost.Key, flows.TryGetValue(lost.Key, out var known) ? known.Device : Label(lost.Key, label), 1, "Capture delivery queue overflow");
                        if (known != null)
                            known.Assembly.Reset();
                    }

                int batch = 0;
                while (batch++ < 1024 && reader.TryRead(out var fragment))
                {
                    if (token.IsCancellationRequested || current != generation || state.Failed)
                        break;
                    Interlocked.Add(ref budget.Bytes, -fragment.Bytes.Length);
                    var key = fragment.Key;
                    var now = clock.Elapsed;
                    if (fragment.Syn && !fragment.Ack)
                    {
                        if (outgoing.Count >= 2048)
                            outgoing.Remove(outgoing.MinBy(p => p.Value).Key);
                        outgoing[key] = now;
                        continue;
                    }

                    if (outgoing.ContainsKey(key))
                    {
                        outgoing[key] = now;
                        continue;
                    }

                    if (!flows.TryGetValue(key, out var flow))
                    {
                        flow = new(Label(key, fragment.Device));
                        if (!flows.TryAdd(key, flow, out var evicted))
                        {
                            Loss(key, flow.Device, 1, "Capture flow limit reached; protected connections retained");
                            continue;
                        }

                        if (evicted is { } oldest)
                            Loss(oldest.Key, oldest.Value.Device, 1, "Capture flow limit reached; unprotected connection evicted");
                    }

                    flow.LastSeen = now;
                    if (fragment.Syn)
                    {
                        if (flow.Syn == fragment.Sequence)
                            continue;
                        if (flow.Syn.HasValue || flow.HasPayload)
                            Loss(key, flow.Device, 1, "TCP connection restarted");
                        flow.Syn = fragment.Sequence;
                        flow.HasPayload = false;
                        flow.Assembly.Reset(unchecked(fragment.Sequence + 1));
                        if (fragment.Bytes.Length == 0)
                            continue;
                    }

                    Emit(key, flow, flow.Assembly.Feed(unchecked(fragment.Sequence + (fragment.Syn ? 1u : 0u)), fragment.Bytes, now));
                    if (fragment.Bytes.Length > 0)
                        flow.HasPayload = true;
                }

                bool inputPending = reader.TryPeek(out _);
                foreach (var pair in flows.Entries.ToArray())
                {
                    Emit(pair.Key, pair.Value, pair.Value.Assembly.Poll(clock.Elapsed, inputPending));
                    if (!inputPending && !IsProtected(pair.Key, pair.Value) && clock.Elapsed - pair.Value.LastSeen > TimeSpan.FromMinutes(5))
                    {
                        FlowRetired?.Invoke(pair.Value.Device);
                        flows.Remove(pair.Key);
                    }
                }

                foreach (var pair in outgoing.Where(p => clock.Elapsed - p.Value > TimeSpan.FromMinutes(5)).ToArray())
                    outgoing.Remove(pair.Key);
                if (inputPending)
                    await Task.Yield();
                else
                    await Task.Delay(10, token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            Trace("Ordered capture consumer failed: " + e);
            foreach (var pair in flows.Entries)
                try
                {
                    Loss(pair.Key, pair.Value.Device, 1, "Capture consumer failure");
                }
                catch
                {
                }

            state.Failed = true;
        }
    }

    public void Stop()
    {
        lock (gate)
        {
            ++generation;
            cancel?.Cancel();
            Volatile.Write(ref run, null);
            ReleaseConnection();
            foreach (var device in devices)
            {
                if (handlers.Remove(device, out var handler))
                {
                    device.OnPacketArrival -= handler.Packet;
                    device.OnCaptureStopped -= handler.Stopped;
                }

                try
                {
                    device.StopCapture();
                }
                catch (Exception e)
                {
                    Trace("Capture stop failed: " + e.Message);
                }

                // A failed stop must not skip unsubscription and closing the old handle.
                try
                {
                    device.Close();
                }
                catch (Exception e)
                {
                    Trace("Capture close failed: " + e.Message);
                }
            }

            devices.Clear();
            cancel?.Dispose();
            cancel = null;
        }
    }

    public (long Received, long Dropped) GetCaptureStatistics()
    {
        lock (gate)
        {
            long rx = 0, drop = 0;
            foreach (var device in devices)
                try
                {
                    var stats = device.Statistics;
                    if (stats == null)
                        continue;
                    rx += stats.ReceivedPackets;
                    drop += stats.DroppedPackets;
                }
                catch
                {
                }

            return (rx, drop);
        }
    }

    void Trace(string text)
    {
        try
        {
            string path = LogRoot;
            Directory.CreateDirectory(path);
            if (File.Exists(Path.Combine(path, "capture-health.log")) && new FileInfo(Path.Combine(path, "capture-health.log")).Length > 524288)
                File.Move(Path.Combine(path, "capture-health.log"), Path.Combine(path, "capture-health.old.log"), true);
            File.AppendAllText(Path.Combine(path, "capture-health.log"), DateTime.UtcNow.ToString("O") + " " + text + Environment.NewLine);
        }
        catch
        {
        }
    }

    public void Dispose() => Stop();
}
