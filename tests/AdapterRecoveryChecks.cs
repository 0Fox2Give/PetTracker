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
    }
}

// Real SharpPcap interface boundary, with no adapter enumeration or game capture.
public class AdapterProxy : DispatchProxy
{
    public PacketArrivalEventHandler? Packet;
    public CaptureStoppedEventHandler? Stopped;
    public int Starts, Closes;
    public bool ThrowOnStop, FailStart;
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        switch (method!.Name)
        {
            case "get_Name":
                return "fixture-adapter";
            case "get_Description":
                return "Fixture adapter";
            case "Open":
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
