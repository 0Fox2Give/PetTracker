using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Velopack;
using Velopack.Locators;

namespace PetTracker.Updates;

public sealed record PublisherConfiguration(string FeedRoot,string PublicKey) {
 public static PublisherConfiguration? Embedded(Assembly assembly){
  using var stream=assembly.GetManifestResourceStream("PetTracker.UpdatePublisher.json");
  if(stream==null)return null;
  var config=JsonSerializer.Deserialize<PublisherConfiguration>(stream)??throw new InvalidDataException("Missing update publisher.");config.Validate();return config;
 }
 public void Validate(){
  using var transport=new HttpsReleaseTransport(new Uri(FeedRoot,UriKind.Absolute));
  if(!PublicKey.StartsWith("-----BEGIN PUBLIC KEY-----",StringComparison.Ordinal)||PublicKey.Contains("PRIVATE KEY",StringComparison.Ordinal))throw new InvalidDataException("Only a public publisher key can be embedded.");
  using var key=ECDsa.Create();key.ImportFromPem(PublicKey);if(key.KeySize!=256)throw new InvalidDataException("Publisher key must use P-256.");
 }
 public static void ValidateDataLocation(string data,string installation){
  string folder=Path.TrimEndingDirectorySeparator(Path.GetFullPath(data)),app=Path.TrimEndingDirectorySeparator(Path.GetFullPath(installation));
  if(folder.Equals(app,StringComparison.OrdinalIgnoreCase)||folder.StartsWith(app+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Move saved collections outside the installation folder before updating.");
  for(var current=new DirectoryInfo(folder);current!=null;current=current.Parent)if(current.Exists&&(current.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("The data folder uses a link. Choose a direct folder outside the installation before updating.");
 }
}

public sealed class VerifiedUpdateClient:IUpdateClient {
 readonly HttpsReleaseTransport transport;readonly UpdateManager manager;readonly string packages;
 public VerifiedUpdateClient(PublisherConfiguration config,string channel,string data,IVelopackLocator? locator=null){
  config.Validate();locator??=VelopackLocator.Current;
  if(locator.AppId!=SignedReleaseSource.PackageId||locator.RootAppDir==null)throw new InvalidOperationException("Install the update-enabled edition to receive updates.");
  PublisherConfiguration.ValidateDataLocation(data,locator.RootAppDir);packages=locator.PackagesDir??throw new InvalidOperationException("Update package storage is unavailable.");
  transport=new(new Uri(config.FeedRoot));manager=new(new SignedReleaseSource(SignedReleaseSource.PackageId,channel,config.PublicKey,transport),new UpdateOptions{ExplicitChannel=channel,AllowVersionDowngrade=false,MaximumDeltasBeforeFallback=-1},locator);
 }
 public Task<UpdateInfo?> Check()=>manager.CheckForUpdatesAsync();
 public Task Download(UpdateInfo update,Action<int> progress,CancellationToken cancellation)=>manager.DownloadUpdatesAsync(update,progress,cancellation);
 public void VerifyReady(UpdateInfo update){
  var asset=update.TargetFullRelease;string path=Path.Combine(packages,asset.FileName);
  try{VerifyPackage(path,asset);}catch(InvalidDataException){File.Delete(path);throw;}
 }
 public static void VerifyPackage(string path,VelopackAsset asset){
  using var file=File.OpenRead(path);
  if(file.Length!=asset.Size||!CryptographicOperations.FixedTimeEquals(SHA256.HashData(file),Convert.FromHexString(asset.SHA256)))throw new InvalidDataException("The downloaded update changed. Download it again.");
 }
 public void Apply(UpdateInfo update,string[] arguments){VerifyReady(update);manager.ApplyUpdatesAndRestart(update.TargetFullRelease,arguments);}
 public void Dispose()=>transport.Dispose();
}
