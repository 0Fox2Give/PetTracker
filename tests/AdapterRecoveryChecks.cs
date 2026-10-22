using System.Reflection;
using PetTracker;
using SharpPcap;

static class AdapterRecoveryChecks
{
    public static void Run(Action<bool, string> check, string root)
    {
        var device = DispatchProxy.Create<ICaptureDevice, AdapterProxy>();
        var fake = (AdapterProxy)device;
        using var capture = new OrderedCapture(() => new[] { device }) { LogRoot = Path.Combine(root, "adapter-log") };
        capture.Start();
        check(capture.IsRunning && fake.Packet != null && fake.Stopped != null, "Capture subscribes to both packet and adapter-stop events");
        check(fake.Configuration?.BufferSize >= 16 * 1024 * 1024 && fake.Configuration.Mode == DeviceModes.MaxResponsiveness,
            "Native capture reserves burst capacity while retaining immediate delivery");
        var oldCallback = fake.Stopped!;
        fake.Stopped!(device, CaptureStoppedEventStatus.ErrorWhileCapturing);
        check(!capture.IsRunning, "Adapter failure cannot keep reporting healthy capture");
        long clock = 0;
        int restarts = 0;
        var recovery = new CaptureRecovery(() => capture.IsRunning, () => { }, () =>
        {
            restarts++;
            capture.Stop();
            capture.Start();
        }, () => clock);
        recovery.Tick();
        clock = 5000;
        recovery.Tick();
        check(capture.IsRunning && restarts == 1 && fake.Starts == 2 && fake.Closes == 1, "Adapter stop notification triggers the real capture close/open recovery path");
        oldCallback(device, CaptureStoppedEventStatus.ErrorWhileCapturing);
        check(capture.IsRunning, "A delayed stop callback from the previous run cannot stop the replacement");
        check(fake.Packet!.GetInvocationList().Length == 1 && fake.Stopped!.GetInvocationList().Length == 1, "Automatic reopen leaves exactly one subscription for each event");
        fake.Stopped!(device, CaptureStoppedEventStatus.CompletedWithoutError);
        check(!capture.IsRunning, "An unsolicited clean adapter stop also requires recovery");
        fake.ThrowOnStop = true;
        capture.Stop();
        check(fake.Packet == null && fake.Stopped == null && fake.Closes == 2 && !capture.IsRunning, "A throwing StopCapture still detaches handlers and closes its handle");
        fake.ThrowOnStop = false;
        capture.Start();
        capture.Stop();
        check(fake.Packet == null && fake.Stopped == null && fake.Closes == 3, "Ordinary shutdown detaches both handlers and closes the final handle");
        fake.FailStart = true;
        bool rejected = false;
        try
        {
            capture.Start();
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }
        check(rejected && !capture.IsRunning && fake.Packet == null && fake.Stopped == null && fake.Closes == 4, "A failed adapter start also removes both subscriptions and closes the partial handle");
        CheckPacketAdmission(check, root);
    }

    static void CheckPacketAdmission(Action<bool, string> check, string root)
    {
        int scans = 0, delivered = 0;
        var peers = new[] { new GameConnectionPeer("192.0.2.1", 13328, "192.0.2.2", 50001) };
        var connections = new GameConnections(_ => { scans++; return new(new[] { 101 }, peers); }, () => 0);
        var device = DispatchProxy.Create<ICaptureDevice, AdapterProxy>();
        var fake = (AdapterProxy)device;
        using var capture = new OrderedCapture(() => new[] { device }, connections) { LogRoot = Path.Combine(root, "packet-admission-log") };
        using var received = new ManualResetEventSlim();
        capture.ScopedPayloadCaptured += (_, p) =>
        {
            Interlocked.Increment(ref delivered);
            if (p.Data.SequenceEqual(new byte[] { 1, 2, 3 })) received.Set();
        };
        capture.Start();
        // Ethernet / IPv4 / TCP, using documentation-only addresses. Checksums
        // are immaterial to the capture parser; no packet is sent to an adapter.
        byte[] Packet(byte flags, byte[] payload, bool owned = true)
        {
            var bytes = new byte[54 + payload.Length];
            bytes[12] = 8;
            bytes[14] = 0x45;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(16), (ushort)(40 + payload.Length));
            bytes[22] = 64; bytes[23] = 6;
            new byte[] { 192, 0, 2, (byte)(owned ? 1 : 3), 192, 0, 2, 2 }.CopyTo(bytes, 26);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(34), 13328);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(36), 50001);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(38), flags == 0x12 ? 100u : 101u);
            bytes[46] = 0x50; bytes[47] = flags;
            payload.CopyTo(bytes, 54);
            return bytes;
        }
        void Receive(byte[] bytes) => fake.Packet!(device, new PacketCapture(device, new FixtureCaptureHeader(), bytes));
        for (int i = 0; i < 1000; i++)
            Receive(Packet(0x10, Array.Empty<byte>(), false));
        check(scans == 0, "ACK-only traffic cannot trigger or wait on process/socket discovery");
        Receive(Packet(0x12, Array.Empty<byte>()));
        check(scans == 1, "A SYN still discovers ownership before establishing the incoming stream");
        Receive(Packet(0x18, new byte[] { 9, 9, 9 }, false));
        Receive(Packet(0x18, new byte[] { 1, 2, 3 }));
        check(received.Wait(TimeSpan.FromSeconds(2)) && Volatile.Read(ref delivered) == 1,
            "The game payload reaches the consumer once while unrelated payloads remain excluded");
    }

    sealed class FixtureCaptureHeader : ICaptureHeader
    {
        public PosixTimeval Timeval { get; } = new();
    }
}

// Real SharpPcap interface boundary, with no adapter enumeration or game capture.
public class AdapterProxy : DispatchProxy
{
    public PacketArrivalEventHandler? Packet;
    public CaptureStoppedEventHandler? Stopped;
    public int Starts, Closes;
    public bool ThrowOnStop, FailStart;
    public DeviceConfiguration? Configuration;
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        switch (method!.Name)
        {
            case "get_Name":
                return "fixture-adapter";
            case "get_Description":
                return "Fixture adapter";
            case "get_LinkType":
                return PacketDotNet.LinkLayers.Ethernet;
            case "Open":
                Configuration = (DeviceConfiguration)args![0]!;
                return null;
            case "set_Filter":
                return null;
            case "add_OnPacketArrival":
                Packet += (PacketArrivalEventHandler)args![0]!;
                return null;
            case "remove_OnPacketArrival":
                Packet -= (PacketArrivalEventHandler)args![0]!;
                return null;
            case "add_OnCaptureStopped":
                Stopped += (CaptureStoppedEventHandler)args![0]!;
                return null;
            case "remove_OnCaptureStopped":
                Stopped -= (CaptureStoppedEventHandler)args![0]!;
                return null;
            case "StartCapture":
                if (FailStart)
                    throw new IOException("Adapter lost during open");
                Starts++;
                return null;
            case "StopCapture":
                if (ThrowOnStop)
                    throw new IOException("Adapter stop failed");
                return null;
            case "Close":
                Closes++;
                return null;
            default:
                throw new NotSupportedException(method.Name);
        }
    }
}
