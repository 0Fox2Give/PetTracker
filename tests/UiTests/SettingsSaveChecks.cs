using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PetTracker;

static class SettingsSaveChecks
{
    static IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        for (int i = 0;
i < VisualTreeHelper.GetChildrenCount(root);
i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Children(child))
                yield return nested;
        }
    }
    static async Task Serve(string root, Func<Command, Task<WorkerView>> response, CancellationToken stop, string? endpoint = null)
    {
        using var pipe = new NamedPipeServerStream(endpoint ?? Storage.Pipe(root), PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        while (!stop.IsCancellationRequested)
        {
            await pipe.WaitForConnectionAsync(stop).ConfigureAwait(false);
            using (var reader = new StreamReader(pipe, leaveOpen: true))
            using (var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true })
            {
                var line = await reader.ReadLineAsync(stop).ConfigureAwait(false);
                if (line != null)
                {
                    string payload;
                    try
                    {
                        payload = JsonSerializer.Serialize(await response(JsonSerializer.Deserialize<Command>(line)!).ConfigureAwait(false));
                    }
                    catch (IOException)
                    {
                        payload = "{\"Error\":\"Settings file is locked\"}";
                    }
                    await writer.WriteLineAsync(payload).ConfigureAwait(false);
                }
            }
            pipe.Disconnect();
        }
    }
    public static void PipeCompatibility(Action<bool, string> check, string folder, WorkerView view, Action<int> pump)
    {
        foreach (bool legacy in new[] { false, true })
        {
            string root = Path.Combine(folder, legacy ? "Legacy-Pipe" : "Canonical-Pipe");
            // Use the historical formula independently of Storage.LegacyPipe, so the
            // fixture represents an already-running old worker, not a new implementation.
            string endpoint = legacy ? "PetTracker-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Environment.UserName + Path.GetFullPath(root))))[..20] : Storage.Pipe(root);
            var commands = new List<string>();
            var response = view with
            {
                Protocol = 3,
                Settings = new()
                {
                    Visible = false
                }
            };
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var server = Serve(root, command =>
            {
                commands.Add(command.Action);
                return Task.FromResult(response);
            }, stop.Token, endpoint);
            using var host = new Host(legacy ? root : root.ToUpperInvariant() + Path.DirectorySeparatorChar, false);
            void Wait(Task task)
            {
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (!task.IsCompleted && DateTime.UtcNow < deadline)
                    pump(10);
                if (!task.IsCompleted)
                    throw new Exception("Pipe compatibility timed out");
                task.GetAwaiter().GetResult();
            }
            try
            {
                var first = host.Send(new("status"));
                Wait(first);
                check(first.Result.Protocol == 3, "Host reaches " + (legacy ? "an old-format worker without spawning a new one" : "the same worker through a differently spelled data folder"));
                Wait(host.Send(new("status")));
                Wait(host.StopWorkerForQuit());
                check(commands.SequenceEqual(new[] { "status", "status", "exit" }), "Repeated requests and orderly quit reuse the " + (legacy ? "legacy" : "canonical") + " endpoint");
            }
            finally
            {
                host.Dispose();
                stop.Cancel();
                try
                {
                    server.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) { }
            }
        }
    }
    public static void RecordedLegacyPipe(Action<bool, string> check, string folder, WorkerView view, Action<int> pump)
    {
        foreach (string scenario in new[] { "same-folder", "different-folder", "relative", "malformed", "oversized", "wrong-type" })
        {
            string root = Path.Combine(folder, "Recorded-Pipe-" + scenario);
            Directory.CreateDirectory(root);
            string hint = scenario switch
            {
                "different-folder" => JsonSerializer.Serialize(new { Data = Path.Combine(folder, "Other-Folder") }),
                "relative" => JsonSerializer.Serialize(new { Data = "." }),
                "malformed" => "{",
                "oversized" => JsonSerializer.Serialize(new { Data = root, Padding = new string('x', 20000) }),
                "wrong-type" => "{\"Data\":123}",
                _ => JsonSerializer.Serialize(new { Data = root })
            };
            File.WriteAllText(Path.Combine(root, "ui-status.json"), hint);
            string recordedRoot = scenario == "different-folder" ? Path.Combine(folder, "Other-Folder") : scenario == "relative" ? "." : root;
            string endpoint = "PetTracker-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Environment.UserName + Path.GetFullPath(recordedRoot))))[..20];
            var commands = new List<string>();
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var server = Serve(root, command =>
            {
                commands.Add(command.Action);
                return Task.FromResult(view with
                {
                    Protocol = 3
                });
            }, stop.Token, endpoint);
            using var host = new Host(root.ToUpperInvariant() + Path.DirectorySeparatorChar, false);
            try
            {
                // Quit never launches a missing worker, including against the defective implementation.
                var quit = host.StopWorkerForQuit();
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (!quit.IsCompleted && DateTime.UtcNow < deadline)
                    pump(10);
                if (!quit.IsCompleted)
                    throw new Exception("Recorded pipe fixture timed out");
                quit.GetAwaiter().GetResult();
                check(commands.SequenceEqual(scenario == "same-folder" ? new[] { "exit" } : Array.Empty<string>()), "Only a bounded, valid same-folder diagnostic can locate a differently spelled legacy worker: " + scenario);
                check(File.ReadAllText(Path.Combine(root, "ui-status.json")) == hint, "Worker discovery leaves diagnostics unchanged: " + scenario);
            }
            finally
            {
                host.Dispose();
                stop.Cancel();
                try
                {
                    server.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) { }
            }
        }
    }
    public static void Run(Action<bool, string> check, string folder, WorkerView view, Action<int> pump, Action<Window> show)
    {
        string root = Path.Combine(folder, "settings-save-failure");
        var current = view with
        {
            Protocol = 3,
            Settings = new AppSettings { Visible = false, Accent = "Gold", Portraits = true, Locked = true, ClickThrough = true }
        };
        var commands = new System.Collections.Concurrent.ConcurrentQueue<Command>();
        int patches = 0;
        bool reject = true;
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var server = Serve(root, async command =>
        {
            commands.Enqueue(command);
            if (command.Action is "patch" or "settings")
            {
                if (Interlocked.Increment(ref patches) == 1)
                {
                    received.SetResult();
                    await release.Task.WaitAsync(stop.Token).ConfigureAwait(false);
                }
                if (Volatile.Read(ref reject))
                    throw new IOException("Fixture save failure");
                current = current with
                {
                    Settings = command.Action == "patch" ? SettingsPatch.Apply(current.Settings, command.Patch!) : command.Settings!
                };
            }
            return current;
        }, stop.Token);
        using var host = new Host(root);
        host.Accept(current);
        var window = new SettingsWindow(host);
        show(window);
        window.ShowSection("Appearance");
        pump(30);
        bool closed = false;
        window.Closed += (s, e) => closed = true;
        void Until(Func<bool> ready)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!ready() && DateTime.UtcNow < deadline)
                pump(10);
            if (!ready())
                throw new Exception("Settings fixture timed out");
        }
        var size = Children(window).OfType<Slider>().First();
        var gameOnly = Children(window).OfType<CheckBox>().Single(c => Equals(c.Content, "Only show over Aion 2"));
        var discard = Children(window).OfType<Button>().Single(b => Equals(b.Content, "Discard changes"));
        try
        {
            size.Value = 1.1;
            gameOnly.IsChecked = true;
            Until(() => received.Task.IsCompleted);
            Children(window).OfType<Slider>().Last().Value = .6;
            check(Math.Abs(size.Value - 1.1) < .001, "Editing another setting preserves the value currently being saved");
            size.Value = 1.25;
            window.Close();
            check(!closed, "Closing settings waits for an outstanding save");
            release.SetResult();
            Until(() => discard.IsVisible || closed);
            check(!closed && Math.Abs(size.Value - 1.25) < .001 && gameOnly.IsChecked == true, "A failed close-save keeps the window open with the newest edits intact");
            window.RefreshStatus();
            pump(450);
            check(patches == 1 && Math.Abs(size.Value - 1.25) < .001, "Save failure does not loop retries or lose edits to status refresh");
            Program.Save(window, Path.Combine(folder, "settings-save-failure.png"), 780, 700);
            Volatile.Write(ref reject, false);
            window.Close();
            Until(() => closed);
            check(patches == 2 && Math.Abs(host.PetOverlayPreferences.Scale - 1.25) < .001 && host.PetOverlayPreferences.GameOnly && Math.Abs(host.PetOverlayPreferences.Opacity - .6) < .001, "Done retries the full failed batch and closes only after the latest edits save");

            window = new SettingsWindow(host);
            show(window);
            window.ShowSection("Appearance");
            pump(30);
            closed = false;
            window.Closed += (s, e) => closed = true;
            size = Children(window).OfType<Slider>().First();
            discard = Children(window).OfType<Button>().Single(b => Equals(b.Content, "Discard changes"));
            Volatile.Write(ref reject, true);
            size.Value = 1;
            window.Close();
            Until(() => discard.IsVisible || closed);
            discard.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(Math.Abs(size.Value - 1.25) < .001 && !discard.IsVisible && Math.Abs(host.PetOverlayPreferences.Scale - 1.25) < .001, "Explicit discard restores saved controls without changing stored preferences");
            window.Close();
            check(closed && patches == 3, "Discard permits closing without another failed save attempt");

            var prior = host.PetOverlayPreferences;
            var unlock = host.UnlockPetOverlay(false);
            Until(() => unlock.IsCompleted);
            // Status polling can deserialize an equivalent settings object during the
            // rejected request. Assert preserved values, not incidental object identity.
            check(!unlock.GetAwaiter().GetResult() && JsonSerializer.Serialize(prior) == JsonSerializer.Serialize(host.PetOverlayPreferences) && host.PetOverlayPreferences.Locked && host.PetOverlayPreferences.ClickThrough, "A rejected tray unlock preserves the acknowledged interaction settings");
            var request = commands.Last(c => c.Action != "status");
            check(request.Action == "patch" && request.Patch!.Keys.OrderBy(k => k).SequenceEqual(new[] { nameof(AppSettings.ClickThrough), nameof(AppSettings.Locked) }), "Tray unlock sends only interaction fields instead of a stale complete settings object");
            // A newer worker-side geometry update must survive this older UI's unlock action.
            current = current with
            {
                Settings = SettingsPatch.Apply(current.Settings, SettingsPatch.Fields((nameof(AppSettings.Left), 917d), (nameof(AppSettings.Top), 211d)))
            };
            Volatile.Write(ref reject, false);
            unlock = host.UnlockPetOverlay(false);
            Until(() => unlock.IsCompleted);
            check(unlock.GetAwaiter().GetResult() && !host.PetOverlayPreferences.Locked && !host.PetOverlayPreferences.ClickThrough && host.PetOverlayPreferences.Left == 917 && host.PetOverlayPreferences.Top == 211, "Successful tray unlock preserves newer tracker geometry and applies both interaction options");
        }
        finally
        {
            // Preserve a listening fixture until the host timer is stopped, even on failure.
            host.Dispose();
            stop.Cancel();
            release.TrySetResult();
            try
            {
                server.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { }
        }
    }
}
