using System.IO.Pipes;
using System.Text.Json;

namespace PetTracker;
static class Program
{
    static async Task Main(string[] args)
    {
        string root = Storage.Root(args);
        string mutexName = (OperatingSystem.IsWindows() ? "Local\\" : "") + Storage.Pipe(root) + "-worker";
        using var mutex = new Mutex(true, mutexName, out bool first);
        if (!first)
            return;
        try
        {
            using var engine = new Engine(root);
            using var capture = new OrderedCapture { LogRoot = root };
            capture.Connections.ProcessNames = engine.Settings.GameProcesses;
            capture.Adapter = engine.Settings.Adapter;
            capture.ScopedPayloadCaptured += (s, p) =>
            {
                engine.GameGeneration(capture.Connections.Generation);
                engine.Feed(p.DeviceName!, p.Data, p.SourcePort, p.DestinationPort);
            };
            capture.StreamGap += (s, g) => engine.Gap(g.Device, g.Reason == "TCP connection restarted", g.Reason);
            capture.FlowRetired += engine.RetireFlow;
            engine.Protect = (flow, source, destination) => capture.ProtectConnection(source, destination, flow);
            engine.Release = capture.ReleaseConnection;
            void Start()
            {
                try
                {
                    capture.Start();
                    engine.CaptureError = capture.OpenDeviceCount == 0 ? CaptureRequirements.Help : "";
                }
                catch (Exception)
                {
                    engine.CaptureError = CaptureRequirements.Help;
                }
            }

            void Restart()
            {
                capture.Stop();
                capture.Connections.ProcessNames = engine.Settings.GameProcesses;
                capture.Adapter = engine.Settings.Adapter;
                Start();
            }

            engine.CaptureAction = action =>
            {
                engine.StopCapture();
                Restart();
            };
            if (args.Contains("--activity-check"))
                engine.Execute(new("activity-check"));
            Start();
            using var lifetime = new CancellationTokenSource();
            long nextHealth = 0;
            var recovery = new CaptureRecovery(() => capture.IsRunning, () => engine.StopCapture("Capture stopped unexpectedly"), Restart);
            recovery.Tick();
            var health = MaintenanceLoop.Run(() =>
            {
                capture.Connections.Refresh();
                engine.GameGeneration(capture.Connections.Generation);
                // Serialize retries with settings/manual restarts and shutdown. Never open
                // a new capture session after the update/quit command has requested exit.
                lock (engine.Gate)
                {
                    if (!engine.ExitRequested)
                        recovery.Tick();
                }

                engine.Flush();
                if (Environment.TickCount64 >= nextHealth)
                {
                    nextHealth = Environment.TickCount64 + 5000;
                    var stats = capture.GetCaptureStatistics();
                    engine.WriteHealth(capture.IsRunning, capture.OpenDeviceCount, stats.Received, stats.Dropped, capture.TcpGaps, capture.Connections.GameRunning);
                }
            }, engine.MaintenanceResult, lifetime.Token);
            while (!engine.ExitRequested)
            {
                using var pipe = new NamedPipeServerStream(Storage.Pipe(root), PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync();
                using var reader = new StreamReader(pipe);
                using var writer = new StreamWriter(pipe) { AutoFlush = true };
                try
                {
                    string? line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
                    if (line == null || line.Length > 8388608)
                        throw new InvalidDataException("Invalid request size.");
                    var cmd = JsonSerializer.Deserialize<Command>(line) ?? throw new InvalidDataException("Invalid request.");
                    engine.Execute(cmd);
                    capture.Connections.Refresh();
                    var stats = capture.GetCaptureStatistics();
                    var response = engine.View(capture.IsRunning, capture.OpenDeviceCount, stats.Received, stats.Dropped, capture.TcpGaps, capture.Connections.GameRunning);
                    if (cmd.Action == "adapters")
                    {
                        try
                        {
                            response = response with
                            {
                                Adapters = SharpPcap.LibPcap.LibPcapLiveDeviceList.Instance.Select(d => new CaptureAdapterOption(d.Name, string.IsNullOrWhiteSpace(d.Description) ? d.Name : d.Description)).ToArray()
                            };
                        }
                        catch
                        {
                            response = response with { Adapters = Array.Empty<CaptureAdapterOption>() };
                        }
                    }

                    await writer.WriteLineAsync(JsonSerializer.Serialize(response));
                }
                catch (Exception ex)
                {
                    try
                    {
                        await writer.WriteLineAsync(JsonSerializer.Serialize(new { Error = ex is InvalidDataException or InvalidOperationException ? ex.Message : "The request could not be completed." }));
                    }
                    catch (IOException)
                    {
                    }
                }
            }

            lifetime.Cancel();
            await health;
            capture.Stop();
        }
        catch (Exception ex)
        {
            Environment.ExitCode = 1;
            Directory.CreateDirectory(root);
            Storage.Save(Path.Combine(root, "startup-error.json"), new { Error = ex.Message });
        }
    }
}
