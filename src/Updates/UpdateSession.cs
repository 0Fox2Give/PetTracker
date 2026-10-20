using Velopack;

namespace PetTracker.Updates;

public enum UpdatePhase
{
    Unavailable, Idle, Checking, Available, Downloading, Ready, Installing, Error
}
public sealed record UpdateState(UpdatePhase Phase, string Message, string? Version = null, int Progress = 0)
{
    public bool Busy => Phase is UpdatePhase.Checking or UpdatePhase.Downloading or UpdatePhase.Installing;
}
public interface IUpdateClient : IDisposable
{
    Task<UpdateInfo?> Check();
    Task Download(UpdateInfo update, Action<int> progress, CancellationToken cancellation);
    void VerifyReady(UpdateInfo update);
    void Apply(UpdateInfo update, string[] arguments);
}

// Shared by the desktop UI and integration checks. No install is scheduled until
// the player invokes Restart; a downloaded package alone never triggers apply.
public sealed class UpdateSession : IDisposable
{
    readonly Func<string, IUpdateClient>? create;
    readonly SemaphoreSlim operation = new(1, 1);
    readonly CancellationTokenSource lifetime = new();
    IUpdateClient? client;
    UpdateInfo? pending;
    public UpdateState State
    {
        get; private set;
    }
    public string? Channel
    {
        get; private set;
    }
    public string ReleaseNotes => pending?.TargetFullRelease.NotesMarkdown ?? "";
    public event Action? Changed;
    public event Action<string>? Ready;
    public event Action<string>? Available;
    string? notified, notifiedAvailable;
    bool disposed;
    public UpdateSession(Func<string, IUpdateClient>? create, string unavailable = "Automatic updates are unavailable in this edition.")
    {
        this.create = create;
        State = new(create == null ? UpdatePhase.Unavailable : UpdatePhase.Idle, create == null ? unavailable : "Check for the latest Pet Tracker release.");
    }
    void Set(UpdatePhase phase, string message, int progress = 0)
    {
        State = new(phase, message, pending?.TargetFullRelease.Version.ToString(), progress);
        Changed?.Invoke();
    }
    public async Task Check(string channel, bool download = false)
    {
        if (disposed || create == null || !await operation.WaitAsync(0))
            return;
        try
        {
            if (channel is not ("win-stable" or "win-beta"))
                throw new InvalidDataException("Unknown update channel.");
            pending = null;
            client?.Dispose();
            client = create(channel);
            Channel = channel;
            Set(UpdatePhase.Checking, "Checking for updates…");
            pending = await client.Check();
            lifetime.Token.ThrowIfCancellationRequested();
            if (pending == null)
            {
                Set(UpdatePhase.Idle, "You’re up to date on this channel.");
                return;
            }
            Set(UpdatePhase.Available, "Version " + pending.TargetFullRelease.Version + " is available.");
            string identity = channel + ":" + pending.TargetFullRelease.Version;
            if (!download && notifiedAvailable != identity)
            {
                notifiedAvailable = identity;
                Available?.Invoke(pending.TargetFullRelease.Version.ToString());
            }
            if (download)
                await DownloadCore();
        }
        catch (OperationCanceledException)
        {
            Set(UpdatePhase.Idle, "Update check cancelled.");
        }
        catch (Exception)
        {
            pending = null;
            Set(UpdatePhase.Error, "Could not verify an update. Your current version is unchanged. Try again later.");
        }
        finally
        {
            if (disposed)
                client?.Dispose();
            operation.Release();
        }
    }
    async Task DownloadCore()
    {
        if (pending == null || client == null)
            return;
        Set(UpdatePhase.Downloading, "Downloading update…");
        var progress = new Progress<int>(value =>
        {
            if (State.Phase == UpdatePhase.Downloading)
                Set(UpdatePhase.Downloading, "Downloading update…", Math.Clamp(value, 0, 100));
        });
        await client.Download(pending, value => ((IProgress<int>)progress).Report(value), lifetime.Token);
        lifetime.Token.ThrowIfCancellationRequested();
        client.VerifyReady(pending);
        Set(UpdatePhase.Ready, "Version " + pending.TargetFullRelease.Version + " is ready. Restart Pet Tracker when you’re ready.", 100);
        string identity = Channel + ":" + pending.TargetFullRelease.Version;
        if (notified != identity)
        {
            notified = identity;
            Ready?.Invoke(pending.TargetFullRelease.Version.ToString());
        }
    }
    public async Task Download()
    {
        if (disposed || State.Phase != UpdatePhase.Available || !await operation.WaitAsync(0))
            return;
        try
        {
            await DownloadCore();
        }
        catch (OperationCanceledException)
        {
            Set(UpdatePhase.Available, "Download cancelled. You can try again later.");
        }
        catch (Exception)
        {
            Set(UpdatePhase.Available, "The download could not be verified. Try again later.");
        }
        finally
        {
            if (disposed)
                client?.Dispose();
            operation.Release();
        }
    }
    public async Task<bool> Install(Func<Task<bool>> saveAndStop, Func<bool> workerExited, Action resume, string[] arguments)
    {
        if (disposed || State.Phase != UpdatePhase.Ready || pending == null || client == null || !await operation.WaitAsync(0))
            return false;
        bool applied = false;
        try
        {
            client.VerifyReady(pending);
            Set(UpdatePhase.Installing, "Saving your collection and closing capture…");
            applied = await UpdateShutdown.Apply(saveAndStop, workerExited, () => client.Apply(pending, arguments), lifetime.Token);
            if (!applied)
                Set(UpdatePhase.Ready, "Could not safely close capture. Tracking will resume; try restarting later.", 100);
            return applied;
        }
        catch (InvalidDataException)
        {
            Set(UpdatePhase.Available, "The downloaded update changed. Download it again before restarting.");
            return false;
        }
        catch (Exception)
        {
            Set(UpdatePhase.Ready, "The update could not start. Tracking will resume; try again later.", 100);
            return false;
        }
        finally
        {
            if (!applied)
                resume();
            if (disposed)
                client?.Dispose();
            operation.Release();
        }
    }
    public void Dispose()
    {
        disposed = true;
        lifetime.Cancel();/* An in-flight request owns its transport until it returns. */
        if (!State.Busy)
            client?.Dispose();
    }
}
