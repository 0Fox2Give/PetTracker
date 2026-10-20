using System.Windows.Threading;
using PetTracker.Updates;
using Velopack.Locators;
namespace PetTracker;

public sealed class AppUpdateService : IDisposable
{
    readonly Host host;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMinutes(1) };
    DateTimeOffset nextCheck = DateTimeOffset.UtcNow.AddSeconds(30);
    public UpdateSession Session
    {
        get;
    }
    public bool SameChannel => Session.Channel == host.PetOverlayPreferences.UpdateChannel;
    public event Action<string>? Ready;
    public event Action<string>? Available;
    public AppUpdateService(Host host, UpdateSession? preview = null)
    {
        if (preview != null && !host.PreviewOnly)
            throw new InvalidOperationException("Update fixtures require a preview host.");
        this.host = host;
        Func<string, IUpdateClient>? factory = null;
        string unavailable = "Automatic updates are unavailable in this edition.";
        try
        {
            var config = host.PreviewOnly ? null : PublisherConfiguration.Embedded(typeof(AppUpdateService).Assembly);
            if (config != null)
            {
                if (VelopackLocator.Current.AppId == SignedReleaseSource.PackageId)
                    factory = channel => new VerifiedUpdateClient(config, channel, host.AssetRoot);
                else
                    unavailable = "Install the update-enabled edition to receive updates here.";
            }
        }
        catch (Exception)
        {
            unavailable = "Update configuration is invalid. Reinstall an official Pet Tracker release.";
        }
        Session = preview ?? new(factory, unavailable);
        Session.Ready += version =>
        {
            if (SameChannel)
                Ready?.Invoke(version);
        };
        Session.Available += version =>
        {
            if (SameChannel)
                Available?.Invoke(version);
        };
        timer.Tick += async (s, e) =>
        {
            if (host.PetOverlayPreferences.CheckUpdatesAutomatically && !Session.State.Busy && Session.State.Phase != UpdatePhase.Ready && DateTimeOffset.UtcNow >= nextCheck)
                await Check();
        };
        if (!host.PreviewOnly && factory != null)
            timer.Start();
    }
    public Task Check()
    {
        nextCheck = DateTimeOffset.UtcNow.AddHours(6);
        return Session.Check(host.PetOverlayPreferences.UpdateChannel, host.PetOverlayPreferences.DownloadUpdatesAutomatically);
    }
    public Task Download() => SameChannel ? Session.Download() : Task.CompletedTask;
    public async Task Install()
    {
        if (!SameChannel)
            return;
        await Session.Install(host.StopWorkerForUpdate, host.UpdateWorkerExited, host.ResumeAfterUpdateFailure, new[] { "--data", host.AssetRoot });
    }
    public void Dispose()
    {
        timer.Stop();
        Session.Dispose();
    }
}
