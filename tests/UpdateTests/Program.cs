using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PetTracker.Updates;
using Velopack;
using Velopack.Logging;
using Velopack.Locators;
int checks = 0;
void Check(bool pass, string message)
{
    checks++;
    if (!pass)
        throw new Exception(message);
}
bool Refused(Action call)
{
    try
    {
        call();
        return false;
    }
    catch (InvalidDataException)
    {
        return true;
    }
}
async Task<bool> RefusedDownload(Func<Task> call)
{
    try
    {
        await call();
        return false;
    }
    catch (InvalidDataException)
    {
        return true;
    }
}
var now = DateTimeOffset.UtcNow;
using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
using var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
using var packageStream = new MemoryStream();
using (var zip = new ZipArchive(packageStream, ZipArchiveMode.Create, true))
{
    using (var writer = new StreamWriter(zip.CreateEntry("PetTracker.nuspec").Open()))
        writer.Write("<package><metadata><id>PetTracker.Desktop</id><version>0.2.0</version><authors>Pet Tracker</authors><description>Update verification fixture</description></metadata></package>");
    using var assembly = zip.CreateEntry("lib/net10.0/PetTracker.dll").Open();
    assembly.Write(new byte[] { 1, 2, 3 });
}
var package = packageStream.ToArray();
string file = "PetTracker-0.2.0-full.nupkg";
string Feed(string name = "", string identity = "PetTracker.Desktop") => JsonSerializer.Serialize(new { Assets = new[] { new { PackageId = identity, Version = "0.2.0", Type = "Full", FileName = name.Length == 0 ? file : name, SHA1 = Convert.ToHexString(SHA1.HashData(package)), SHA256 = Convert.ToHexString(SHA256.HashData(package)), Size = package.Length, NotesMarkdown = "Improve tracking" } } });
var payload = new ReleasePayload("PetTracker.Desktop", "win-beta", now.AddMinutes(-1), now.AddDays(14), Feed());
string envelope = SignedReleaseSource.Sign(payload, key);
var transport = new FakeTransport();
transport.Files[file] = package;
transport.Files["releases.win-beta.signed.json"] = Encoding.UTF8.GetBytes(envelope);
var source = new SignedReleaseSource("PetTracker.Desktop", "win-beta", key.ExportSubjectPublicKeyInfoPem(), transport, () => now);
var asset = source.Verify(Encoding.UTF8.GetBytes(envelope)).Assets.Single();
Check(asset.Version.ToString() == "0.2.0", "Signed feed is compatible with Velopack metadata");
Check(Refused(() => source.Verify(Encoding.UTF8.GetBytes(SignedReleaseSource.Sign(payload, stranger)))), "A different publisher cannot sign an accepted release");
var tampered = JsonSerializer.Deserialize<SignedRelease>(envelope)!;
var changed = Convert.FromBase64String(tampered.Payload);
changed[^3] ^= 1;
Check(Refused(() => source.Verify(JsonSerializer.SerializeToUtf8Bytes(tampered with { Payload = Convert.ToBase64String(changed) }))), "Changed release data fails the pinned signature");
foreach (var bad in new[] { payload with { Channel = "win-stable" }, payload with { AppId = "OtherProduct" }, payload with { ExpiresUtc = now.AddSeconds(-1) }, payload with { PublishedUtc = now.AddHours(1) }, payload with { Feed = Feed("../outside.nupkg") }, payload with { Feed = Feed(identity: "OtherProduct") } }) Check(Refused(() => source.Verify(Encoding.UTF8.GetBytes(SignedReleaseSource.Sign(bad, key)))), "Signed invalid identity/time/path must be refused");
string root = Path.Combine(Path.GetFullPath(args[0]), "signed-update-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
string destination = Path.Combine(root, "verified.nupkg");
var logger = new NullVelopackLogger();
source.Verify(Encoding.UTF8.GetBytes(envelope));
await source.DownloadReleaseEntry(logger, asset, destination, _ => { }, CancellationToken.None);
Check(File.ReadAllBytes(destination).SequenceEqual(package), "Authenticated download verifies package bytes before promotion");
transport.Files[file] = package.Select((b, i) => i == 0 ? (byte)(b ^ 1) : b).ToArray();
Check(await RefusedDownload(() => source.DownloadReleaseEntry(logger, asset, destination, _ => { }, CancellationToken.None)), "Tampered package fails SHA256 despite unchanged size");
Check(File.ReadAllBytes(destination).SequenceEqual(package) && Directory.GetFiles(root, "*.verified-*").Length == 0, "Failed download keeps a prior valid package and removes its stage");
var mutated = source.Verify(Encoding.UTF8.GetBytes(envelope)).Assets.Single();
mutated.Size++;
Check(await RefusedDownload(() => source.DownloadReleaseEntry(logger, mutated, destination, _ => { }, CancellationToken.None)), "Mutable caller metadata cannot change authenticated approval");
Check(Refused(() => source.Verify(Encoding.UTF8.GetBytes(SignedReleaseSource.Sign(payload with { Feed = Feed("../outside.nupkg") }, key)))) && await RefusedDownload(() => source.DownloadReleaseEntry(logger, asset, destination, _ => { }, CancellationToken.None)), "Failed feed validation clears previous download authorization");
transport.Files[file] = package;
using var cancelled = new CancellationTokenSource();
cancelled.Cancel();
bool stopped = false;
source.Verify(Encoding.UTF8.GetBytes(envelope));
try
{
    await source.DownloadReleaseEntry(logger, asset, destination, _ => { }, cancelled.Token);
}
catch (OperationCanceledException)
{
    stopped = true;
}
Check(stopped && Directory.GetFiles(root, "*.verified-*").Length == 0, "Cancelled download cleans partial data");
// ResponseHeadersRead ends HttpClient.Timeout at the headers. A server that
// stalls its package body still needs a bounded download, without app shutdown.
using (var caller = new CancellationTokenSource())
{
    var stalledTransport = new StalledTransport();
    var bounded = new SignedReleaseSource("PetTracker.Desktop", "win-beta", key.ExportSubjectPublicKeyInfoPem(), stalledTransport, () => now, TimeSpan.FromMilliseconds(50));
    bounded.Verify(Encoding.UTF8.GetBytes(envelope));
    var download = bounded.DownloadReleaseEntry(logger, asset, destination, _ => { }, caller.Token);
    bool timedOut = false, needsCallerCancellation = false;
    try
    {
        await download.WaitAsync(TimeSpan.FromSeconds(2));
    }
    catch (OperationCanceledException)
    {
        timedOut = true;
    }
    catch (TimeoutException)
    {
        needsCallerCancellation = true;
    }
    finally
    {
        caller.Cancel();
        try
        {
            await download;
        }
        catch (OperationCanceledException) { }
    }
    Check(timedOut && !needsCallerCancellation && stalledTransport.BodyRead, "Stalled package body times out independently of application shutdown");
    Check(stalledTransport.Disposed && File.ReadAllBytes(destination).SequenceEqual(package) && Directory.GetFiles(root, "*.verified-*").Length == 0, "Timed-out package disposes its response, removes partial bytes, and preserves the prior package");
}
var locator = new TestVelopackLocator("PetTracker.Desktop", "0.1.3", Path.Combine(root, "packages"), logger);
var manager = new UpdateManager(source, new UpdateOptions { ExplicitChannel = "win-beta", MaximumDeltasBeforeFallback = -1 }, locator);
var update = await manager.CheckForUpdatesAsync();
Check(update?.TargetFullRelease.Version.ToString() == "0.2.0", "Real Velopack manager discovers an authenticated newer release");
await manager.DownloadUpdatesAsync(update!);
Check(File.Exists(Path.Combine(root, "packages", file)), "Real Velopack manager downloads the full package through the authenticated source");
int applied = 0;
Check(!await UpdateShutdown.Apply(() => Task.FromResult(false), () => true, () => applied++, CancellationToken.None) && applied == 0, "Failed capture save/exit never applies an update");
Check(await UpdateShutdown.Apply(() => Task.FromResult(true), () => true, () => applied++, CancellationToken.None) && applied == 1, "Update applies only after successful save and worker exit");
bool insecure = false;
try
{
    using var http = new HttpsReleaseTransport(new Uri("http://example.com/"));
}
catch (ArgumentException)
{
    insecure = true;
}
Check(insecure, "Production update transport requires HTTPS");
var githubRoot = new Uri("https://github.com/JukeFoxZero/pet-tracker/releases/download/updates/");
var redirectHandler = new RedirectHandler("https://release-assets.githubusercontent.com/asset?token=public-download");
using (var githubTransport = new HttpsReleaseTransport(githubRoot, redirectHandler))
{
    using var downloaded = await githubTransport.Open("release.nupkg", CancellationToken.None);
    Check(downloaded.ReadByte() == 42 && redirectHandler.Requests.Count == 2, "GitHub release download follows its HTTPS asset CDN redirect");
    Check(redirectHandler.Requests.All(r => !r.Auth), "GitHub public downloads never send credentials");
}
foreach (string redirect in new[] { "http://release-assets.githubusercontent.com/asset", "https://evil.example/asset", "https://release-assets.githubusercontent.com.evil.example/asset", "https://user@release-assets.githubusercontent.com/asset", "https://release-assets.githubusercontent.com:8443/asset", "https://github.com/other/repo/releases/download/updates/file" })
{
    var handler = new RedirectHandler(redirect);
    using var githubTransport = new HttpsReleaseTransport(githubRoot, handler);
    Check(await RefusedDownload(() => githubTransport.Open("release.nupkg", CancellationToken.None)) && handler.Requests.Count == 1, "Unsafe GitHub redirects are refused before contacting the destination");
}
using (var looping = new HttpsReleaseTransport(githubRoot, new RedirectHandler("https://release-assets.githubusercontent.com/loop", true))) Check(await RefusedDownload(() => looping.Open("release.nupkg", CancellationToken.None)), "Redirect loops are bounded");
using (var ordinary = new HttpsReleaseTransport(new Uri("https://updates.example.com/"), new RedirectHandler("https://release-assets.githubusercontent.com/asset"))) Check(await RefusedDownload(() => ordinary.Open("release.nupkg", CancellationToken.None)), "Non-GitHub feeds cannot acquire GitHub redirect privileges");
VerifiedUpdateClient.VerifyPackage(destination, asset);
File.WriteAllBytes(destination, new byte[package.Length]);
Check(Refused(() => VerifiedUpdateClient.VerifyPackage(destination, asset)), "The desktop backend rejects a changed cached package before applying it");
File.WriteAllBytes(destination, package);
var validPublisher = new PublisherConfiguration("https://updates.example.com/pet-tracker/", key.ExportSubjectPublicKeyInfoPem());
validPublisher.Validate();
Check(Refused(() => (validPublisher with { PublicKey = key.ExportPkcs8PrivateKeyPem() }).Validate()), "Private publisher keys cannot be embedded as update configuration");
Check(Refused(() => PublisherConfiguration.ValidateDataLocation(Path.Combine(root, "app", "data"), Path.Combine(root, "app"))), "An update cannot remove collections stored inside its installation folder");
PublisherConfiguration.ValidateDataLocation(Path.Combine(root, "PetTracker"), Path.Combine(root, "PetTracker.Desktop"));
checks++;
using (var disabled = new UpdateSession(null))
{
    await disabled.Check("win-stable", true);
    Check(disabled.State.Phase == UpdatePhase.Unavailable, "Unconfigured test builds stay offline and do not claim to be up to date");
}
var fake = new FakeClient(update!);
using (var session = new UpdateSession(_ => fake))
{
    int notices = 0, availableNotices = 0, resumed = 0, saveCalls = 0;
    session.Ready += _ => notices++;
    session.Available += _ => availableNotices++;
    await session.Check("win-beta");
    Check(session.State.Phase == UpdatePhase.Available && fake.Downloads == 0 && fake.Applies == 0, "Checking without automatic downloads never installs or downloads a release");
    Check(availableNotices == 1, "Players are notified when an update exists with automatic downloads disabled");
    await session.Check("win-beta");
    Check(availableNotices == 1, "Repeated checks do not repeat an available-version notification");
    fake.BadDownload = true;
    await session.Download();
    Check(session.State.Phase == UpdatePhase.Available && fake.Applies == 0 && notices == 0, "Rejected download remains retryable and cannot trigger install or ready notice");
    fake.BadDownload = false;
    await session.Download();
    Check(session.State.Phase == UpdatePhase.Ready && notices == 1 && fake.Applies == 0, "Verified download offers restart without silently applying");
    Check(!await session.Install(() =>
    {
        saveCalls++;
        return Task.FromResult(false);
    }, () => true, () => resumed++, new[] { "--data", root }) && resumed == 1 && fake.Applies == 0, "Failed worker save returns to tracking without installing");
    fake.BadCache = true;
    await session.Install(() =>
    {
        saveCalls++;
        return Task.FromResult(true);
    }, () => true, () => resumed++, new[] { "--data", root });
    Check(saveCalls == 1 && fake.Applies == 0, "Changed cached bytes block installation before stopping capture");
    fake.BadCache = false;
    Check(session.State.Phase == UpdatePhase.Available, "Modified cached update offers a fresh download");
    await session.Download();
    Check(await session.Install(() => Task.FromResult(true), () => true, () => resumed++, new[] { "--data", root }) && fake.Arguments.SequenceEqual(new[] { "--data", root }), "Player-approved install preserves the exact custom data argument");
    await session.Check("win-beta", true);
    Check(notices == 1, "Repeated checks of a ready version do not repeat its notification");
    fake.FailCheck = true;
    await session.Check("win-stable");
    Check(session.State.Phase == UpdatePhase.Error && !await session.Install(() => Task.FromResult(true), () => true, () => resumed++, Array.Empty<string>()), "A failed channel switch cannot install the previous channel's pending release");
    fake.FailCheck = false;
    fake.Next = null;
    await session.Check("win-stable");
    Check(session.State.Phase == UpdatePhase.Idle && session.State.Version == null, "A successful empty feed clears the previous candidate");
}
using (var stopUpdate = new CancellationTokenSource())
{
    stopUpdate.Cancel();
    bool cancelledBeforeSave = false;
    int savedCalls = 0;
    try
    {
        await UpdateShutdown.Apply(() =>
        {
            savedCalls++;
            return Task.FromResult(true);
        }, () => true, () => { }, stopUpdate.Token);
    }
    catch (OperationCanceledException)
    {
        cancelledBeforeSave = true;
    }
    Check(cancelledBeforeSave && savedCalls == 0, "Cancellation before install does not stop the running worker");
}
// A ready package can disappear after download (for example, disk cleanup).
// Exercise the real cache verifier through the same session used by the UI.
var missingCache = new FakeClient(update!) { Verification = () => VerifiedUpdateClient.VerifyPackage(destination, asset) };
using (var session = new UpdateSession(_ => missingCache))
{
    await session.Check("win-beta", true);
    Check(session.State.Phase == UpdatePhase.Ready, "Existing cached package is ready before the cleanup fixture");
    File.Delete(destination);
    int saveCalls = 0;
    Check(!await session.Install(() =>
    {
        saveCalls++;
        return Task.FromResult(true);
    }, () => true, () => { }, Array.Empty<string>()) && saveCalls == 0 && missingCache.Applies == 0, "Missing cached package cannot stop capture or start installation");
    Check(session.State.Phase == UpdatePhase.Available, "Missing cached package offers a new download instead of repeating Restart");
    File.WriteAllBytes(destination, package);
    await session.Download();
    Check(session.State.Phase == UpdatePhase.Ready && missingCache.Downloads == 2, "A replacement verified download recovers without restarting the tracker");
}
Check(Refused(() => VerifiedUpdateClient.VerifyPackage(Path.Combine(root, "removed-cache-folder", file), asset)), "A removed cache directory also requests a replacement download");
// Hold a real session at its save/exit boundary. Neither another install nor a
// channel check may replace the candidate while shutdown is in flight.
var interruptedClient = new FakeClient(update!);
using (var session = new UpdateSession(_ => interruptedClient))
{
    await session.Check("win-beta", true);
    var saving = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    int resumes = 0, saves = 0;
    bool exited = false;
    var installing = session.Install(() =>
    {
        saves++;
        return saving.Task;
    }, () => exited, () => resumes++, new[] { "--data", root });
    Check(session.State.Phase == UpdatePhase.Installing && saves == 1 && interruptedClient.Applies == 0, "Installation waits for the outstanding collection save before applying");
    bool second = await session.Install(() =>
    {
        saves++;
        return Task.FromResult(true);
    }, () => true, () => resumes++, Array.Empty<string>());
    await session.Check("win-stable");
    Check(!second && saves == 1 && session.Channel == "win-beta" && interruptedClient.Checks == 1, "Concurrent restart and channel-check requests cannot replace an in-flight install");
    saving.SetResult(true);
    await Task.Delay(150);
    Check(!installing.IsCompleted && interruptedClient.Applies == 0, "An acknowledged save is insufficient while capture has not exited");
    session.Dispose();
    Check(!await installing.WaitAsync(TimeSpan.FromSeconds(2)) && resumes == 1 && interruptedClient.Applies == 0, "Cancelling during worker exit resumes exactly once without applying the update");
}
var failedApply = new FakeClient(update!) { BeforeApply = () => throw new IOException("Installer process could not start") };
using (var session = new UpdateSession(_ => failedApply))
{
    await session.Check("win-beta", true);
    int resumes = 0;
    Check(!await session.Install(() => Task.FromResult(true), () => true, () => resumes++, Array.Empty<string>()) && resumes == 1 && session.State.Phase == UpdatePhase.Ready, "Failure to launch the installer after capture exits resumes tracking and preserves a retryable update");
    failedApply.BeforeApply = null;
    Check(await session.Install(() => Task.FromResult(true), () => true, () => resumes++, new[] { "--data", root }) && resumes == 1 && failedApply.Arguments.SequenceEqual(new[] { "--data", root }), "Retry after installer launch failure succeeds without resuming the old worker");
}
if (args.Length > 1)
{
    string artifacts = Path.GetFullPath(args[1]);
    using var proof = JsonDocument.Parse(File.ReadAllText(Path.Combine(artifacts, "candidate-verification.json")));
    string channel = proof.RootElement.GetProperty("Channel").GetString()!, version = proof.RootElement.GetProperty("Version").GetString()!;
    Check(channel is "win-stable" or "win-beta" && Version.TryParse(version, out _), "Actual candidate has a supported channel and version");
    string realFeed = File.ReadAllText(Path.Combine(artifacts, "releases." + channel + ".json"));
    string realSigned = SignedReleaseSource.Sign(payload with
    {
        Channel = channel,
        Feed = realFeed
    }, key);
    var realSource = new SignedReleaseSource(SignedReleaseSource.PackageId, channel, key.ExportSubjectPublicKeyInfoPem(), new ArtifactTransport(artifacts, channel, Encoding.UTF8.GetBytes(realSigned)), () => now);
    var realLocator = new TestVelopackLocator(SignedReleaseSource.PackageId, "0.1.5", Path.Combine(root, "real-packages"), logger);
    var realManager = new UpdateManager(realSource, new UpdateOptions { ExplicitChannel = channel, MaximumDeltasBeforeFallback = -1 }, realLocator);
    var release = await realManager.CheckForUpdatesAsync();
    Check(release?.TargetFullRelease.Version.ToString() == version, "Actual candidate release feed authenticates and discovers the expected version");
    await realManager.DownloadUpdatesAsync(release!);
    string realPackage = Path.Combine(root, "real-packages", release!.TargetFullRelease.FileName);
    VerifiedUpdateClient.VerifyPackage(realPackage, release.TargetFullRelease);
    Check(new FileInfo(realPackage).Length == release.TargetFullRelease.Size, "Actual update package downloads through the signed source and passes the desktop cache check");
    using var archive = ZipFile.OpenRead(realPackage);
    bool octoberCatalog = Version.Parse(version) >= new Version(0, 1, 43);
    Check(archive.Entries.Count(x => x.FullName.Contains("/portraits/") && x.FullName.EndsWith(".png")) == (octoberCatalog ? 294 : 292), "Actual update package retains the complete portrait set for its catalog version");
    if (octoberCatalog)
        Check(new[] { 1343, 1363 }.All(id => archive.Entries.Any(x => x.FullName.EndsWith("/portraits/" + id + ".png"))), "October catalog package includes both new shop-pet icons");
}

Console.WriteLine($"PASS: {checks} authenticated update checks");
sealed class FakeTransport : IReleaseTransport
{
    public Dictionary<string, byte[]> Files = new();
    public Task<Stream> Open(string file, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult<Stream>(new MemoryStream(Files[file], false));
    }
}
sealed class StalledTransport : IReleaseTransport
{
    public bool BodyRead, Disposed;
    public Task<Stream> Open(string file, CancellationToken token) => Task.FromResult<Stream>(new StalledStream(this));
    sealed class StalledStream(StalledTransport owner) : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            owner.BodyRead = true;
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return 0;
        }
        protected override void Dispose(bool disposing)
        {
            owner.Disposed = true;
            base.Dispose(disposing);
        }
    }
}
sealed class RedirectHandler(string location, bool loop = false) : HttpMessageHandler
{
    public List<(Uri Uri, bool Auth)> Requests = new();
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Requests.Add((request.RequestUri!, request.Headers.Authorization != null));
        var response = new HttpResponseMessage(Requests.Count == 1 || loop ? System.Net.HttpStatusCode.Found : System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 42 }) };
        if (Requests.Count == 1 || loop)
            response.Headers.Location = new Uri(location);
        return Task.FromResult(response);
    }
}

sealed class FakeClient(UpdateInfo update) : IUpdateClient
{
    public Action? Verification, BeforeApply;
    public UpdateInfo? Next = update;
    public bool FailCheck, BadDownload, BadCache;
    public int Checks, Downloads, Applies;
    public string[] Arguments = Array.Empty<string>();
    public Task<UpdateInfo?> Check()
    {
        Checks++;
        if (FailCheck)
            throw new IOException("Offline");
        return Task.FromResult(Next);
    }
    public Task Download(UpdateInfo info, Action<int> progress, CancellationToken token)
    {
        Downloads++;
        if (BadDownload)
            throw new InvalidDataException("Bad download");
        return Task.CompletedTask;
    }
    public void VerifyReady(UpdateInfo info)
    {
        if (BadCache)
            throw new InvalidDataException("Changed cache");
        Verification?.Invoke();
    }
    public void Apply(UpdateInfo info, string[] arguments)
    {
        BeforeApply?.Invoke();
        Applies++;
        Arguments = arguments;
    }
    public void Dispose()
    {
    }
}
sealed class ArtifactTransport(string root, string channel, byte[] feed) : IReleaseTransport
{
    public Task<Stream> Open(string file, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        return Task.FromResult<Stream>(file == "releases." + channel + ".signed.json" ? new MemoryStream(feed, false) : File.OpenRead(Path.Combine(root, file)));
    }
}
