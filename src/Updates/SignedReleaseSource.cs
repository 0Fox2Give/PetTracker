using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Velopack;
using Velopack.Logging;
using Velopack.Sources;
namespace PetTracker.Updates;

public sealed record SignedRelease(string Payload, string Signature);
public sealed record ReleasePayload(string AppId, string Channel, DateTimeOffset PublishedUtc, DateTimeOffset ExpiresUtc, string Feed, string? InstallerSHA256 = null);
public interface IReleaseTransport
{
    Task<Stream> Open(string file, CancellationToken cancellation);
}

// Pin the publisher's public key in the application build. A hash hosted next
// to a package alone cannot establish who published it.
public sealed class SignedReleaseSource : IUpdateSource
{
    public const string PackageId = "PetTracker.Desktop";
    readonly string appId, channel, publicKey;
    readonly IReleaseTransport transport;
    readonly Func<DateTimeOffset> clock;
    readonly TimeSpan downloadTimeout;
    readonly Dictionary<string, VelopackAsset> approved = new(StringComparer.Ordinal);
    public SignedReleaseSource(string appId, string channel, string publicKey, IReleaseTransport transport, Func<DateTimeOffset>? clock = null, TimeSpan? downloadTimeout = null)
    {
        if (appId != PackageId || channel is not ("win-stable" or "win-beta"))
            throw new ArgumentException("Unsupported update identity or channel.");
        this.appId = appId;
        this.channel = channel;
        this.publicKey = publicKey;
        this.transport = transport;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        this.downloadTimeout = downloadTimeout ?? TimeSpan.FromMinutes(10);
        if (this.downloadTimeout <= TimeSpan.Zero || this.downloadTimeout > TimeSpan.FromMinutes(10))
            throw new ArgumentOutOfRangeException(nameof(downloadTimeout));
    }
    public static string Sign(ReleasePayload payload, ECDsa privateKey)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        return JsonSerializer.Serialize(new SignedRelease(Convert.ToBase64String(bytes), Convert.ToBase64String(privateKey.SignData(bytes, HashAlgorithmName.SHA256))));
    }
    public VelopackAssetFeed Verify(ReadOnlySpan<byte> envelope)
    {
        approved.Clear();
        if (envelope.Length > 2097152)
            throw new InvalidDataException("Release feed is too large.");
        var signed = JsonSerializer.Deserialize<SignedRelease>(envelope) ?? throw new InvalidDataException("Empty release feed.");
        byte[] bytes = Convert.FromBase64String(signed.Payload), signature = Convert.FromBase64String(signed.Signature);
        if (bytes.Length > 1048576 || signature.Length > 256)
            throw new InvalidDataException("Release signature data is too large.");
        using var key = ECDsa.Create();
        key.ImportFromPem(publicKey);
        if (key.KeySize != 256 || !key.VerifyData(bytes, signature, HashAlgorithmName.SHA256))
            throw new InvalidDataException("Release publisher signature did not verify.");
        var payload = JsonSerializer.Deserialize<ReleasePayload>(bytes) ?? throw new InvalidDataException("Empty signed release.");
        var now = clock();
        if (payload.AppId != appId || payload.Channel != channel || payload.PublishedUtc > now.AddMinutes(5) || payload.ExpiresUtc <= now || payload.ExpiresUtc <= payload.PublishedUtc || payload.ExpiresUtc - payload.PublishedUtc > TimeSpan.FromDays(90))
            throw new InvalidDataException("Release identity, channel or validity period differs.");
        var feed = VelopackAssetFeed.FromJson(payload.Feed);
        if (feed.Assets == null || feed.Assets.Length > 100)
            throw new InvalidDataException("Invalid release list.");
        var candidate = new Dictionary<string, VelopackAsset>(StringComparer.Ordinal);
        foreach (var asset in feed.Assets)
        {
            if (asset.PackageId != appId || asset.Version == null || !Regex.IsMatch(asset.FileName ?? "", "^[A-Za-z0-9][A-Za-z0-9._-]{0,180}\\.nupkg$") || !Regex.IsMatch(asset.SHA256 ?? "", "^[0-9A-Fa-f]{64}$") || asset.Size is <= 0 or > 536870912 || asset.Type is not (VelopackAssetType.Full or VelopackAssetType.Delta) || !candidate.TryAdd(asset.FileName!, asset with
            {
            }))
                throw new InvalidDataException("Invalid release package metadata.");
        }
        foreach (var pair in candidate)
            approved.Add(pair.Key, pair.Value);
        return feed;
    }
    public async Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? requestedApp, string requestedChannel, Guid? stagingId, VelopackAsset? latestLocalRelease)
    {
        approved.Clear();
        if (requestedApp != appId || requestedChannel != channel)
            throw new InvalidDataException("Update request differs from pinned release identity.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var stream = await transport.Open("releases." + channel + ".signed.json", timeout.Token);
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, timeout.Token)) > 0)
        {
            if (bytes.Length + count > 2097152)
                throw new InvalidDataException("Release feed is too large.");
            bytes.Write(buffer, 0, count);
        }
        return Verify(bytes.ToArray());
    }
    public async Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset release, string localFile, Action<int> progress, CancellationToken cancellation)
    {
        if (!approved.TryGetValue(release.FileName, out var trusted) || release != trusted)
            throw new InvalidDataException("The package is not in the verified release feed.");
        // HttpClient uses ResponseHeadersRead, so its timeout alone does not bound
        // reading the package body. Keep one deadline through open, copy and promotion.
        using var deadline = new CancellationTokenSource(downloadTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, deadline.Token);
        cancellation = linked.Token;
        string stage = localFile + ".verified-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var source = await transport.Open(release.FileName, cancellation))
            {
                await using var target = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var bytes = new byte[81920];
                long total = 0;
                int count;
                while ((count = await source.ReadAsync(bytes, cancellation)) > 0)
                {
                    total += count;
                    if (total > trusted.Size)
                        throw new InvalidDataException("Update package size differs.");
                    digest.AppendData(bytes, 0, count);
                    await target.WriteAsync(bytes.AsMemory(0, count), cancellation);
                    progress?.Invoke((int)(total * 100 / trusted.Size));
                }
                if (total != trusted.Size || !CryptographicOperations.FixedTimeEquals(digest.GetHashAndReset(), Convert.FromHexString(trusted.SHA256)))
                    throw new InvalidDataException("Update package failed verification.");
            }
            cancellation.ThrowIfCancellationRequested();
            File.Move(stage, localFile, true);
        }
        finally
        {
            if (File.Exists(stage))
                File.Delete(stage);
        }
    }
}

public sealed class HttpsReleaseTransport : IReleaseTransport, IDisposable
{
    readonly Uri root;
    readonly HttpClient client;
    readonly bool github;
    public HttpsReleaseTransport(Uri root) : this(root, new HttpClientHandler { AllowAutoRedirect = false }) { }
    public HttpsReleaseTransport(Uri root, HttpMessageHandler handler)
    {
        if (!root.IsAbsoluteUri || root.Scheme != "https" || !root.IsDefaultPort || root.UserInfo.Length > 0 || root.Query.Length > 0 || root.Fragment.Length > 0 || !root.AbsolutePath.EndsWith('/'))
            throw new ArgumentException("Updates require a fixed HTTPS folder.");
        this.root = root;
        github = root.Host == "github.com" && Regex.IsMatch(root.AbsolutePath, "^/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/releases/download/[A-Za-z0-9_.-]+/$");
        client = new(handler)
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PetTracker-Updater/1.0");
    }
    public async Task<Stream> Open(string file, CancellationToken cancellation)
    {
        if (!Regex.IsMatch(file, "^[A-Za-z0-9][A-Za-z0-9._-]{0,200}$"))
            throw new InvalidDataException("Invalid release file name.");
        var original = new Uri(root, file);
        var address = original;
        for (int redirects = 0;
;
redirects++)
        {
            var response = await client.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, cancellation);
            try
            {
                if (response.StatusCode is System.Net.HttpStatusCode.MovedPermanently or System.Net.HttpStatusCode.Found or System.Net.HttpStatusCode.SeeOther or System.Net.HttpStatusCode.TemporaryRedirect or System.Net.HttpStatusCode.PermanentRedirect)
                {
                    var location = response.Headers.Location;
                    if (redirects >= 4 || location == null)
                        throw new InvalidDataException("Invalid update download redirect.");
                    var next = new Uri(address, location);
                    // GitHub serves public release assets through this CDN. Never send account
                    // credentials, follow a downgrade, or accept redirects from arbitrary feeds.
                    if (!github || next.Scheme != "https" || !next.IsDefaultPort || next.UserInfo.Length != 0 || next.Fragment.Length != 0 ||
                       !(next.Host == "release-assets.githubusercontent.com" || next == original))
                        throw new InvalidDataException("Update download redirected outside GitHub release storage.");
                    address = next;
                    response.Dispose();
                    continue;
                }
                response.EnsureSuccessStatusCode();
                return new ResponseStream(await response.Content.ReadAsStreamAsync(cancellation), response);
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }
    }
    sealed class ResponseStream(Stream inner, HttpResponseMessage response) : Stream
    {
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false; public override long Length => throw new NotSupportedException(); public override long Position
        {
            get => throw new NotSupportedException(); set => throw new NotSupportedException();
        }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count); public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => inner.ReadAsync(buffer, token);
        public override void Flush() => throw new NotSupportedException(); public override long Seek(long o, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long v) => throw new NotSupportedException(); public override void Write(byte[] b, int o, int n) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                response.Dispose();
            }
            base.Dispose(disposing);
        }
    }
    public void Dispose() => client.Dispose();
}
