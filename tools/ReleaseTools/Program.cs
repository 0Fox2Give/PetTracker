using System.Security.Cryptography;
using System.Text;
using PetTracker.Updates;
using System.Text.Json;

// Publisher-side only. Signing keys never belong in an installed application,
// a release archive, or the public update feed.
if(args.Length==3&&args[0]=="verify-runtime-config"){
 try{
  var expected=JsonSerializer.Deserialize<PublisherConfiguration>(File.ReadAllText(args[2]))!;expected.Validate();
  var embedded=PublisherConfiguration.Embedded(System.Reflection.Assembly.LoadFile(Path.GetFullPath(args[1])));
  if(embedded!=expected)throw new InvalidDataException("The built app has a different publisher configuration");
  Console.WriteLine("Built app matches the publishing address and public key.");return 0;
 }catch(Exception error){Console.Error.WriteLine("Runtime publisher check failed: "+error.Message);return 1;}
}
if(args.Length==4&&args[0]=="init-publisher"){
 try{
  string configPath=Path.GetFullPath(args[2]),keyPath=Path.GetFullPath(args[3]);
  if(configPath==keyPath||File.Exists(configPath)||File.Exists(keyPath))throw new InvalidDataException("Publisher files already exist or overlap. Reuse the existing signing identity.");
  using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
  var config=new PublisherConfiguration(args[1],key.ExportSubjectPublicKeyInfoPem());config.Validate();
  Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
  using(var file=new FileStream(keyPath,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(var writer=new StreamWriter(file))writer.Write(key.ExportPkcs8PrivateKeyPem());
  using(var file=new FileStream(configPath,FileMode.CreateNew,FileAccess.Write,FileShare.None))JsonSerializer.Serialize(file,config);
  Console.WriteLine("Publisher public configuration and separate private signing key created. Protect the private key; never distribute it.");return 0;
 }catch(Exception error){Console.Error.WriteLine("Publisher initialization failed: "+error.Message);return 1;}
}
if(args.Length==4&&args[0]=="verify"){
 try{
  var config=JsonSerializer.Deserialize<PublisherConfiguration>(File.ReadAllText(args[1]))!;config.Validate();
  var source=new SignedReleaseSource(SignedReleaseSource.PackageId,args[2],config.PublicKey,new UnusedTransport());
  var feed=source.Verify(File.ReadAllBytes(args[3]));Console.WriteLine($"Verified {feed.Assets.Length} published release entries.");return 0;
 }catch(Exception error){Console.Error.WriteLine("Feed verification failed: "+error.Message);return 1;}
}
if(args.Length==6&&args[0]=="renew"){
 try{
  var config=JsonSerializer.Deserialize<PublisherConfiguration>(File.ReadAllText(args[1]))!;config.Validate();
  byte[] old=File.ReadAllBytes(args[3]);if(old.Length>2097152)throw new InvalidDataException("Feed too large");
  var envelope=JsonSerializer.Deserialize<SignedRelease>(old)!;
  var payload=JsonSerializer.Deserialize<ReleasePayload>(Convert.FromBase64String(envelope.Payload))!;
  // Renewal may recover an expired feed, but must authenticate the original
  // publisher, channel, metadata and original validity period before re-signing.
  if(payload.PublishedUtc>DateTimeOffset.UtcNow.AddMinutes(5))throw new InvalidDataException("Future release timestamp");
  var source=new SignedReleaseSource(SignedReleaseSource.PackageId,args[2],config.PublicKey,new UnusedTransport(),()=>payload.PublishedUtc);
  source.Verify(old);
  using var key=ECDsa.Create();key.ImportFromPem(File.ReadAllText(args[4]));
  using var publicKey=ECDsa.Create();publicKey.ImportFromPem(config.PublicKey);
  if(!key.ExportSubjectPublicKeyInfo().SequenceEqual(publicKey.ExportSubjectPublicKeyInfo()))throw new InvalidDataException("Signing key does not match the installed publisher");
  string destination=Path.GetFullPath(args[5]);if(destination==Path.GetFullPath(args[4])||destination==Path.GetFullPath(args[1]))throw new InvalidDataException("Unsafe output path");
  var now=DateTimeOffset.UtcNow;string signed=SignedReleaseSource.Sign(payload with{PublishedUtc=now,ExpiresUtc=now.AddDays(30)},key);
  File.WriteAllText(destination,signed);Console.WriteLine("Authenticated release feed renewed for 30 days.");return 0;
 }catch(Exception error){Console.Error.WriteLine("Feed renewal failed: "+error.Message);return 1;}
}
if(args.Length==2&&args[0]=="validate-config"){
 try{var config=System.Text.Json.JsonSerializer.Deserialize<PublisherConfiguration>(File.ReadAllText(args[1]))??throw new InvalidDataException("Missing publisher configuration");config.Validate();Console.WriteLine("Publisher HTTPS root and public key validated.");return 0;}
 catch(Exception){Console.Error.WriteLine("Invalid publisher configuration. Use an HTTPS folder and a P-256 PUBLIC KEY, never a private key.");return 1;}
}
if(args.Length is not (6 or 7)||args[0]!="sign"||args[1] is not ("win-beta" or "win-stable")){
 Console.Error.WriteLine("Usage: ReleaseTools sign win-beta|win-stable feed.json private-key.pem output.signed.json validity-days");return 2;
}
if(!int.TryParse(args[5],out int days)||days is <1 or >90){Console.Error.WriteLine("Validity must be 1–90 days.");return 2;}
try{
 string feed=File.ReadAllText(args[2]);using var key=ECDsa.Create();key.ImportFromPem(File.ReadAllText(args[3]));if(key.KeySize!=256)throw new InvalidDataException("Use a P-256 release signing key.");
 string? installerHash=null;if(args.Length==7){using var installer=File.OpenRead(args[6]);installerHash=Convert.ToHexString(SHA256.HashData(installer));}
 var now=DateTimeOffset.UtcNow;string signed=SignedReleaseSource.Sign(new(SignedReleaseSource.PackageId,args[1],now,now.AddDays(days),feed,installerHash),key);
 var verify=new SignedReleaseSource(SignedReleaseSource.PackageId,args[1],key.ExportSubjectPublicKeyInfoPem(),new UnusedTransport());var release=verify.Verify(Encoding.UTF8.GetBytes(signed));
 string destination=Path.GetFullPath(args[4]);if(destination.Equals(Path.GetFullPath(args[3]),StringComparison.OrdinalIgnoreCase)||destination.Equals(Path.GetFullPath(args[2]),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Use a separate signed-feed output file.");
 Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.WriteAllText(destination,signed);Console.WriteLine($"Verified and signed {release.Assets.Length} release entries for {args[1]}.");return 0;
}catch(Exception error)when(error is IOException or ArgumentException or CryptographicException or InvalidDataException or System.Text.Json.JsonException){Console.Error.WriteLine("Release signing failed: "+error.Message);return 1;}
sealed class UnusedTransport:IReleaseTransport {public Task<Stream> Open(string file,CancellationToken cancellation)=>throw new NotSupportedException();}
