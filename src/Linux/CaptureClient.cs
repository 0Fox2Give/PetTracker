using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;

namespace PetTracker.Linux;
/// <summary>The UI has no capture privileges; only the separate worker opens libpcap.</summary>
public interface ITrackerConnection
{
    Task<WorkerView> Send(Command command);
    Task Stop();
}

public sealed class CaptureClient : ITrackerConnection
{
    readonly string root;
    readonly SemaphoreSlim requests = new(1, 1);
    long lastLaunch = long.MinValue;
    bool stopping;
    bool connected;
    public CaptureClient(string root) => this.root = Path.GetFullPath(root);
    public async Task<WorkerView> Send(Command command)
    {
        await requests.WaitAsync();
        try
        {
            if (stopping)
                throw new IOException("Capture is shutting down.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            using var pipe = await Connect(command.Action != "exit", timeout.Token);
            using var reader = new StreamReader(pipe);
            using var writer = new StreamWriter(pipe) { AutoFlush = true };
            await writer.WriteLineAsync(JsonSerializer.Serialize(command).AsMemory(), timeout.Token);
            string? line = await reader.ReadLineAsync(timeout.Token);
            if (line == null || line.Length > 8 * 1024 * 1024)
                throw new IOException("Invalid capture response.");
            using var document = JsonDocument.Parse(line);
            if (!document.RootElement.TryGetProperty("Protocol", out _))
                throw new IOException(document.RootElement.TryGetProperty("Error", out var error) ? error.GetString() : "Capture returned an invalid response.");
            var view = JsonSerializer.Deserialize<WorkerView>(line) ?? throw new IOException("Empty capture response.");
            connected = true;
            return view;
        }
        finally
        {
            requests.Release();
        }
    }

    async Task<NamedPipeClientStream> Connect(bool launch, CancellationToken cancellation)
    {
        var pipe = new NamedPipeClientStream(".", Storage.Pipe(root), PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            try
            {
                await pipe.ConnectAsync(250, cancellation);
            }
            catch (TimeoutException)when (launch)
            {
                if (lastLaunch == long.MinValue || Environment.TickCount64 - lastLaunch > 15000)
                {
                    string path = Path.Combine(AppContext.BaseDirectory, "capture", "PetTracker.Capture");
                    if (!File.Exists(path))
                        throw new IOException("Capture component missing. Build or extract the complete Linux package.");
                    var start = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true };
                    start.ArgumentList.Add("--data");
                    start.ArgumentList.Add(root);
                    using var worker = Process.Start(start);
                    lastLaunch = Environment.TickCount64;
                }

                await pipe.ConnectAsync(8000, cancellation);
            }

            return pipe;
        }
        catch
        {
            pipe.Dispose();
            throw;
        }
    }

    public async Task Stop()
    {
        if (connected || lastLaunch != long.MinValue)
            await Send(new("exit"));
        stopping = true;
    }
}
