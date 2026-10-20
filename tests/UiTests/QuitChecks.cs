using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PetTracker;

static class QuitChecks
{
    public static bool RunChild(string[] args)
    {
        if (args.Length != 3 || args[0] != "--exit-fixture")
            return false;
        string gate = args[1];
        File.WriteAllText(gate + ".ready", "");
        var deadline = Stopwatch.StartNew();
        while (!File.Exists(gate) && deadline.Elapsed < TimeSpan.FromSeconds(30))
            Thread.Sleep(20);
        Environment.ExitCode = File.Exists(gate) ? int.Parse(args[2]) : 99;
        return true;
    }

    static async Task Serve(string root, Func<string, Task<string>> response, CancellationToken stop)
    {
        using var pipe = new NamedPipeServerStream(Storage.Pipe(root), PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.WaitForConnectionAsync(stop);
        using var reader = new StreamReader(pipe);
        using var writer = new StreamWriter(pipe) { AutoFlush = true };
        var request = await reader.ReadLineAsync(stop) ?? throw new IOException("Missing fixture request");
        await writer.WriteLineAsync(await response(request));
    }
    public static async Task Run(Action<bool, string> check, string folder, WorkerView view)
    {
        await CheckWorkerExit(check, folder, 0);
        await CheckWorkerExit(check, folder, 7);
        string payload = JsonSerializer.Serialize(view with
        {
            Protocol = 3
        });
        string root = Path.Combine(folder, "quit-poll");
        using var host = new Host(root, false);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exitServer = Serve(root, async request =>
        {
            received.SetResult(request);
            await release.Task.WaitAsync(stop.Token);
            return payload;
        }, stop.Token);
        var quit = host.StopWorkerForQuit();
        var request = await received.Task.WaitAsync(stop.Token);
        check(JsonSerializer.Deserialize<Command>(request)!.Action == "exit", "Quit requests a saved worker exit through the real named pipe");
        // Keep a second endpoint available even with the defect present. An erroneous
        // queued status reaches this fixture, never a real capture process.
        var statusServer = Serve(root, _ => Task.FromResult(payload), stop.Token);
        var poll = host.Send(new("status"));
        release.SetResult();
        await quit;
        await exitServer;
        bool refused = false;
        try
        {
            await poll;
        }
        catch (IOException)
        {
            refused = true;
        }
        check(refused, "A status poll queued behind Quit cannot reconnect or relaunch capture");
        stop.Cancel();
        try
        {
            await statusServer;
        }
        catch (OperationCanceledException) { }

        string failureRoot = Path.Combine(folder, "quit-save-failure");
        using var failed = new Host(failureRoot, false);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        var failureServer = Serve(failureRoot, _ => Task.FromResult("{\"Error\":\"Collection could not be saved\"}"), deadline.Token);
        bool rejected = false;
        try
        {
            await failed.StopWorkerForQuit();
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }
        await failureServer;
        check(rejected, "A rejected collection save prevents a successful Quit result");
        var resumedServer = Serve(failureRoot, _ => Task.FromResult(payload), deadline.Token);
        var resumed = await failed.Send(new("status"));
        await resumedServer;
        check(resumed.Protocol == 3, "A rejected Quit re-enables communication so the player can retry");
        using var absent = new Host(Path.Combine(folder, "quit-absent"), false);
        await absent.StopWorkerForQuit();
        check(true, "Quit succeeds when no worker is listening without launching one");
    }
    static async Task CheckWorkerExit(Action<bool, string> check, string folder, int exitCode)
    {
        string gate = Path.Combine(folder, "worker-exit-" + Guid.NewGuid().ToString("N"));
        // Reuse the compiled test executable rather than paying for a separate
        // PowerShell startup, which can exceed five seconds on cold CI runners.
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("Test executable unavailable");
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
        if (string.Equals(Path.GetFileNameWithoutExtension(exe), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(UiChecks).Assembly.Location);
        start.ArgumentList.Add("--exit-fixture");
        start.ArgumentList.Add(gate);
        start.ArgumentList.Add(exitCode.ToString());
        int pid;
        using (var child = Process.Start(start)!)
        {
            pid = child.Id;
        }
        // Dispose the launch handle: only the production monitor may retain the exit code.
        try
        {
            var startupDeadline = DateTime.UtcNow.AddSeconds(30);
            while (!File.Exists(gate + ".ready"))
            {
                if (DateTime.UtcNow >= startupDeadline)
                    throw new TimeoutException("Child exit fixture did not start");
                await Task.Delay(20);
            }
            bool refused = false;
            try
            {
                using var wrong = WorkerExitMonitor.Open(pid, exe + ".wrong");
            }
            catch (InvalidOperationException)
            {
                refused = true;
            }
            check(refused, "Update shutdown refuses a worker from an unexpected executable");
            using var monitor = WorkerExitMonitor.Open(pid, exe);
            check(!monitor.ExitedCleanly, "A running worker is not accepted as safely stopped");
            File.WriteAllText(gate, "");
            // Wait for actual exit for both success and failure codes. A false
            // ExitedCleanly while the child is still running cannot prove code 7.
            var exitDeadline = DateTime.UtcNow.AddSeconds(10);
            while (true)
            {
                bool exited;
                try
                {
                    using var probe = Process.GetProcessById(pid);
                    exited = probe.HasExited;
                }
                catch (ArgumentException) { exited = true; }
                if (exited)
                    break;
                if (DateTime.UtcNow >= exitDeadline)
                    throw new TimeoutException("Child exit fixture did not stop");
                await Task.Delay(20);
            }
            check(monitor.ExitedCleanly == (exitCode == 0), "Update shutdown retains and checks real child exit code " + exitCode);
        }
        finally
        {
            File.WriteAllText(gate, "");
        }
    }
}
