using System;
using System.Linq;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace PetTracker;

static class DirectionPollingChecks
{
    public static void Run(Action<bool, string> check, string folder, WorkerView view, Action<int> pump, Action<System.Windows.Window> show)
    {
        string root = Path.Combine(folder, "direction-polling");
        using var host = new Host(root, false);
        var settings = new AppSettings { Visible = true, TrackerMode = "Nearby", ExperimentalDirections = true, ExpireInactive = false };
        var initial = view with
        {
            Protocol = 3,
            Ready = true,
            Settings = settings,
            Nearby = new[] { new NearbyPet(1050, "Gravi", DateTimeOffset.UtcNow, new(null, DateTimeOffset.UtcNow, 0, DateTimeOffset.UtcNow, NorthUp: true)) }
        };
        host.Preview(initial);
        show(host.Overlay!);
        pump(30);
        Program.Save(host.Overlay!, Path.Combine(folder, "direction-poll-before.png"), 340, 180);
        // Exercise the real dispatcher + pipe loop without launching capture or showing
        // a sample overlay over the user's game. Preview-only hosts don't start a timer.
        var timer = (DispatcherTimer)typeof(Host).GetField("timer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        int received = 0;
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var times = new System.Collections.Concurrent.ConcurrentQueue<DateTimeOffset>();
        var server = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                using var pipe = new NamedPipeServerStream(Storage.Pipe(root), PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stop.Token);
                using var reader = new StreamReader(pipe);
                using var writer = new StreamWriter(pipe) { AutoFlush = true };
                var command = JsonSerializer.Deserialize<Command>((await reader.ReadLineAsync(stop.Token))!);
                if (command?.Action != "status")
                    throw new Exception("Unexpected polling command");
                int n = Interlocked.Increment(ref received);
                var at = DateTimeOffset.UtcNow;
                times.Enqueue(at);
                await writer.WriteLineAsync(JsonSerializer.Serialize(initial with
                {
                    Nearby = new[] { new NearbyPet(1050, "Gravi", at, new(null, at, n * 45, at, NorthUp: true)) }
                }));
            }
        });
        try
        {
            timer.Start();
            pump(850);
            timer.Stop();
            pump(40);
            check(received >= 2, "Active arrows receive multiple worker updates within one second");
            check(host.Tracker.NearbyPets.Single().Direction!.BearingDegrees == received * 45, "Newest observed direction reaches the tracker through real polling");
            Program.Save(host.Overlay!, Path.Combine(folder, "direction-poll-after.png"), 340, 180);
            File.WriteAllText(Path.Combine(folder, "direction-poll-times.json"), JsonSerializer.Serialize(times));
            foreach (var passive in new[] { initial with { Settings = new AppSettings { Visible = true, TrackerMode = "Nearby", ExperimentalDirections = false } }, initial with { Settings = new AppSettings { Visible = true, TrackerMode = "Recent", ExperimentalDirections = true } }, initial with { Settings = new AppSettings { Visible = false, TrackerMode = "Nearby", ExperimentalDirections = true } }, initial with { Ready = false } })
            {
                host.Preview(passive);
                check(timer.Interval == TimeSpan.FromSeconds(1), "Inactive navigation returns to ordinary polling");
            }
        }
        finally
        {
            timer.Stop();
            stop.Cancel();
            try
            {
                server.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { }
        }
    }
}
