using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using System.Windows.Threading;

namespace PetTracker;

public sealed class Host : IDisposable
{
    public string AssetRoot { get; }
    public object Gate { get; } = new();
    public Tracker Tracker { get; }
    public bool StartHidden { get; set; }
    public AppSettings PetOverlayPreferences { get; private set; } = new();
    public WorkerView? View { get; private set; }
    public MerchantDisplay MerchantDisplay { get; } = new();
    public bool PreviewOnly { get; private set; }
    public string? ConnectionError { get; private set; }
    public bool ActivityCheckOnStart { get; set; }
    public AppUpdateService Updates { get; }

    bool updateStopping;
    WorkerExitMonitor? updateWorker;
    public Func<string, bool> IsGameForeground { get; set; } = WindowBehavior.GameForeground;

    long nextUiHealth;
    PetFinderWindow? finder;
    public async Task<bool> PinPet(int id, bool pinned) => await Change(new(pinned ? "pin" : "unpin", PetId: id));
    public async Task<bool> UnlockPetOverlay(bool showError = true) => await Change(new("patch", Patch: SettingsPatch.Fields((nameof(AppSettings.Locked), false), (nameof(AppSettings.ClickThrough), false))), showError);
    public void OpenPetFinder(int? id = null)
    {
        if (finder == null)
        {
            finder = new(this, id);
            finder.Closed += (s, e) => finder = null;
        }
        else
            finder.SelectPet(id);
        finder.Show();
        finder.Activate();
    }

    readonly SemaphoreSlim rpc = new(1, 1);
    readonly DispatcherTimer timer;
    bool busy, disposed;
    public PetTrackerOverlay? Overlay { get; private set; }

    AccountsWindow? accounts;
    SettingsWindow? settings;
    bool revealEmpty;
    public string HotkeyError { get; private set; } = "";

    public void SetHotkeyError(string error)
    {
        HotkeyError = error;
        settings?.RefreshStatus();
    }

    public event Action? TestNotification;
    public void PreviewNotification()
    {
        if (!PreviewOnly)
            TestNotification?.Invoke();
    }

    public event Action? Changed;
    public event Action<string>? Notification;
    public Host(string root, bool connect = true, PetTracker.Updates.UpdateSession? updatePreview = null)
    {
        PreviewOnly = !connect;
        AssetRoot = root;
        Directory.CreateDirectory(root);
        Tracker = new("", Catalog.Create());
        PetOverlayPreferences = AutomaticRecovery.Settings(root);
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += async (s, e) =>
        {
            if (busy || disposed)
                return;
            busy = true;
            try
            {
                Accept(await Send(new("status")));
            }
            catch (Exception ex)
            {
                SetFastPolling(false);
                View = null;
                ConnectionError = ex is UnauthorizedAccessException ? "Windows blocked the local capture connection. Open Pet Tracker from your Windows shortcut." : ex.Message;
                Tracker.Pause();
                Overlay?.Refresh();
                settings?.RefreshStatus();
                Changed?.Invoke();
            }
            finally
            {
                busy = false;
            }
        };
        Updates = new(this, updatePreview);
        if (connect)
            timer.Start();
    }

    // Faster delivery preserves more of each short-lived navigation observation.
    // It never extends evidence freshness, and the existing busy/RPC guards prevent overlap.
    void SetFastPolling(bool enabled)
    {
        var interval = TimeSpan.FromMilliseconds(enabled ? 250 : 1000);
        if (timer.Interval != interval)
            timer.Interval = interval;
    }

    public async Task<WorkerView> Send(Command command)
    {
        await rpc.WaitAsync();
        try
        {
            if (updateStopping && command.Action != "exit")
                throw new IOException("Pet Tracker is preparing an update.");
            using var pipe = await ConnectWorker(command);
            using var reader = new StreamReader(pipe);
            using var writer = new StreamWriter(pipe) { AutoFlush = true };
            await writer.WriteLineAsync(JsonSerializer.Serialize(command));
            string line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)) ?? throw new IOException("Capture is unavailable.");
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("Error", out var error) && error.ValueKind == JsonValueKind.String)
                throw new InvalidOperationException(error.GetString());
            var view = JsonSerializer.Deserialize<WorkerView>(line)!;
            if (view.Protocol != 3)
                throw new InvalidDataException("Restart Pet Tracker to complete the update.");
            return view;
        }
        finally
        {
            rpc.Release();
        }
    }

    string? connectedPipe;
    IEnumerable<string> WorkerEndpoints()
    {
        if (connectedPipe != null)
            yield return connectedPipe;
        yield return Storage.Pipe(AssetRoot);
        yield return Storage.LegacyPipe(AssetRoot);
        // Older workers hashed the original path spelling. Read this optional hint
        // only after ordinary endpoints fail; never recover or modify diagnostics.
        if (RecordedLegacyEndpoint() is { } recorded)
            yield return recorded;
    }

    string? RecordedLegacyEndpoint()
    {
        try
        {
            using var file = new FileStream(Path.Combine(AssetRoot, "ui-status.json"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var bytes = new byte[16385];
            int count = file.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
            if (count > 16384)
                return null;
            using var doc = JsonDocument.Parse(bytes.AsMemory(0, count));
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("Data", out var data) || data.ValueKind != JsonValueKind.String)
                return null;
            string? root = data.GetString();
            if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root) || Storage.Pipe(root) != Storage.Pipe(AssetRoot))
                return null;
            return Storage.LegacyPipe(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    async Task<NamedPipeClientStream> ConnectWorker(Command command)
    {
        string canonical = Storage.Pipe(AssetRoot);
        // Try the last successful endpoint first to avoid a timeout on every poll
        // while an older worker is awaiting an orderly upgrade/quit.
        foreach (string name in WorkerEndpoints().Distinct())
        {
            var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await pipe.ConnectAsync(400);
                connectedPipe = name;
                return pipe;
            }
            catch (TimeoutException)
            {
                pipe.Dispose();
            }
            catch
            {
                pipe.Dispose();
                throw;
            }
        }

        if (updateStopping && command.Action == "exit")
            throw new WorkerNotRunningException();
        if (updateStopping)
            throw new IOException("Pet Tracker is preparing an update.");
        LaunchWorker();
        var started = new NamedPipeClientStream(".", canonical, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await started.ConnectAsync(8000);
            connectedPipe = canonical;
            return started;
        }
        catch
        {
            started.Dispose();
            throw;
        }
    }

    void LaunchWorker()
    {
        string file = Path.Combine(AppContext.BaseDirectory, "PetTracker.Capture.exe");
        if (!File.Exists(file))
            throw new IOException("The capture component is missing. Reinstall Pet Tracker.");
        var start = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            ArgumentList =
            {
                "--data",
                AssetRoot
            }
        };
        if (ActivityCheckOnStart)
            start.ArgumentList.Add("--activity-check");
        Process.Start(start);
    }

    bool captureSetupShown;
    public void Accept(WorkerView view)
    {
        var oldIncrease = View?.Increase;
        View = view;
        ConnectionError = null;
        PetOverlayPreferences = view.Settings;
        Tracker.ReadOnlyView(view);
        settings?.RefreshStatus();
        if (!PreviewOnly && !captureSetupShown && view.Devices == 0)
        {
            captureSetupShown = true;
            OpenConnectionHelp();
        }

        bool hasRows = (Tracker.Ready && Activity.Select(Tracker, PetOverlayPreferences, DateTimeOffset.UtcNow).Any()) || MerchantDisplay.Select(view, DateTimeOffset.UtcNow).Count > 0;
        if (hasRows)
            revealEmpty = false;
        bool show = (!PetOverlayPreferences.HideWhenEmpty || !Tracker.Ready || hasRows || revealEmpty) && PetOverlayPreferences.Visible && !StartHidden && (!PetOverlayPreferences.GameOnly || IsGameForeground(PetOverlayPreferences.GameProcesses));
        SetFastPolling(show && view.Ready && PetOverlayPreferences.ExperimentalDirections && PetOverlayPreferences.TrackerMode == "Nearby");
        if (show)
        {
            if (Overlay == null)
            {
                Overlay = new(this);
                Overlay.SourceInitialized += (s, e) => ApplyAppearance();
            }

            ApplyAppearance();
            if (!PreviewOnly && !Overlay.IsVisible)
                Overlay.Show();
            Overlay.Refresh();
        }
        else
            Overlay?.Hide();
        Changed?.Invoke();
        WriteUiHealth();
        if (view.Increase != null && view.Increase != oldIncrease && view.LastSync is { } synced && view.Increase.SeenUtc >= synced)
        {
            string name = new PetNames().Resolve(view.Profile.Pets[view.Increase.PetId]);
            Notification?.Invoke(name + (view.Increase.Rank == 3 ? " reached MAX" : " reached Rank " + view.Increase.Rank));
            if (PetOverlayPreferences.Sounds)
                System.Media.SystemSounds.Asterisk.Play();
        }
    }

    public async Task<bool> Change(Command command, bool showError = true)
    {
        try
        {
            if (PreviewOnly)
            {
                if (View == null)
                    throw new InvalidOperationException("Preview is not initialized");
                if (command.Action == "patch")
                    Accept(View with { Settings = SettingsPatch.Apply(View.Settings, command.Patch!) });
                else if (command.Action is "pin" or "unpin")
                    Accept(View with { Settings = PetPins.Change(View.Settings, command.PetId, command.Action == "pin") });
                else if (command.Action == "settings")
                    Accept(View with { Settings = command.Settings! });
                else
                    throw new InvalidOperationException("This action needs a connected app");
            }
            else
                Accept(await Send(command));
            return true;
        }
        catch (Exception ex)
        {
            if (showError)
                Ui.Error(ex, settings);
            return false;
        }
    }

    public async void SetPetOverlayVisible(bool visible)
    {
        StartHidden = false;
        revealEmpty = visible;
        await Change(new("patch", Patch: SettingsPatch.Fields((nameof(AppSettings.Visible), visible))));
    }

    public async Task<bool> InitializeLaunch(bool minimized)
    {
        StartHidden = minimized;
        if (minimized)
            return true;
        // Closing the overlay hides this session; it must not make the next normal
        // launch appear broken. Keep an empty tracker visible until activity arrives.
        revealEmpty = true;
        return await Change(new("patch", Patch: SettingsPatch.Fields((nameof(AppSettings.Visible), true))));
    }

    public async void SetPetOverlayOptions(bool hide, bool expire, int seconds)
    {
        await Change(new("patch", Patch: SettingsPatch.Fields((nameof(AppSettings.HideMaxed), hide), (nameof(AppSettings.ExpireInactive), expire), (nameof(AppSettings.InactiveSeconds), seconds))));
    }

    public async void SavePetOverlayPosition(double left, double top)
    {
        await Change(new("patch", Patch: SettingsPatch.Fields((nameof(AppSettings.Left), left), (nameof(AppSettings.Top), top))));
    }

    public void OpenConnectionHelp()
    {
        OpenPetOverlaySettings();
        settings!.ShowConnection();
    }

    public void OpenAccounts()
    {
        if (accounts == null)
        {
            accounts = new(this);
            accounts.Closed += (s, e) => accounts = null;
        }

        accounts.Show();
        accounts.Activate();
    }

    public void OpenPetOverlaySettings()
    {
        if (settings == null)
        {
            settings = new(this);
            settings.Closed += (s, e) => settings = null;
        }

        settings.Show();
        settings.Activate();
    }

    public void ApplyAppearance()
    {
        if (Overlay == null)
            return;
        var s = PetOverlayPreferences;
        Overlay.Opacity = 1;
        Overlay.SetBackgroundOpacity(s.Opacity);
        Overlay.Width = (s.Compact ? 300 : 340) * s.Scale;
        if (Overlay.Content is FrameworkElement fe)
            fe.LayoutTransform = new ScaleTransform(s.Scale, s.Scale);
        Overlay.Resources["ArcanaGold"] = NativeTheme.Accent(s.Accent);
        WindowBehavior.ClickThrough(Overlay, s.ClickThrough);
        if (!PreviewOnly)
            OverlayGeometry.RestorePosition(Overlay, s, OverlayGeometry.WorkAreas(Overlay));
    }

    void WriteUiHealth()
    {
        if (PreviewOnly || Environment.TickCount64 < nextUiHealth)
            return;
        nextUiHealth = Environment.TickCount64 + 5000;
        try
        {
            Storage.Save(Path.Combine(AssetRoot, "ui-status.json"), new { Version = typeof(Host).Assembly.GetName().Version?.ToString(), RecordedUtc = DateTimeOffset.UtcNow, ProcessId = Environment.ProcessId, Executable = Environment.ProcessPath, Data = AssetRoot, TrackerWindows = Application.Current.Windows.OfType<PetTrackerOverlay>().Count(), Visible = Overlay?.IsVisible == true, Rows = Overlay?.DisplayedPetCount ?? 0, PetOverlayPreferences.GameOnly });
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Preview(WorkerView view)
    {
        Accept(view);
    }

    sealed class WorkerNotRunningException : IOException
    {
    }

    public async Task StopWorkerForQuit()
    {
        // Block already queued polls as well as future timer ticks before sending
        // exit. A missing worker is success; a failed save must keep tracking alive.
        timer.Stop();
        updateStopping = true;
        try
        {
            await Send(new("exit"));
        }
        catch (WorkerNotRunningException)
        {
        }
        catch
        {
            ResumeAfterUpdateFailure();
            throw;
        }
    }

    public async Task<bool> StopWorkerForUpdate()
    {
        timer.Stop();
        updateStopping = true;
        updateWorker?.Dispose();
        updateWorker = null;
        try
        {
            if (View?.WorkerProcessId is not > 0)
                return false;
            string expected = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "PetTracker.Capture.exe"));
            updateWorker = WorkerExitMonitor.Open(View.WorkerProcessId, expected);
            await Send(new("exit"));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool UpdateWorkerExited() => updateWorker?.ExitedCleanly == true;
    public void ResumeAfterUpdateFailure()
    {
        updateStopping = false;
        if (!disposed && !PreviewOnly)
            timer.Start();
    }

    public void Dispose()
    {
        disposed = true;
        timer.Stop();
        Updates.Dispose();
        updateWorker?.Dispose();
        Overlay?.DisposeOverlay();
        accounts?.Close();
        settings?.Close();
        finder?.Close();
    }
}
