using System.Text.Json;
using PetTracker.Updates;
using Velopack;
using Velopack.Locators;
using Velopack.Logging;

if (args.Length < 3) throw new ArgumentException("Usage: LiveUpdateCheck publisher.json installed-version download-directory");
var config = JsonSerializer.Deserialize<PublisherConfiguration>(File.ReadAllText(args[0]))!;
config.Validate();
using var transport = new HttpsReleaseTransport(new Uri(config.FeedRoot));
var source = new SignedReleaseSource(SignedReleaseSource.PackageId, "win-stable", config.PublicKey, transport);
var locator = new TestVelopackLocator(SignedReleaseSource.PackageId, args[1], Path.GetFullPath(args[2]), new NullVelopackLogger());
var manager = new UpdateManager(source, new UpdateOptions { ExplicitChannel = "win-stable", MaximumDeltasBeforeFallback = -1, AllowVersionDowngrade = false }, locator);
var update = await manager.CheckForUpdatesAsync();
if (update == null) { Console.WriteLine("Verified public feed: no newer release."); return; }
Console.WriteLine("Authenticated public release: " + update.TargetFullRelease.Version);
await manager.DownloadUpdatesAsync(update);
var file = Path.Combine(Path.GetFullPath(args[2]), update.TargetFullRelease.FileName);
VerifiedUpdateClient.VerifyPackage(file, update.TargetFullRelease);
Console.WriteLine("Public package downloaded and verified: " + file);
