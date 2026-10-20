param(
    [Parameter(Mandatory)][string]$Runtime,
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [ValidateSet('win-beta', 'win-stable')][string]$Channel = 'win-beta'
)
$ErrorActionPreference = 'Stop'
$Runtime = (Resolve-Path -LiteralPath $Runtime).Path
$PackageDirectory = (Resolve-Path -LiteralPath $PackageDirectory).Path
$manifest = Get-Content -LiteralPath (Join-Path $Runtime 'package.json') -Raw | ConvertFrom-Json -AsHashtable
if (!$manifest.Count) { throw 'Empty runtime manifest' }
$expected = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($name in $manifest.Keys) {
    if ($name -match '(^/|\\|:|(^|/)\.\.?(/|$))' -or $manifest[$name] -notmatch '^[A-Fa-f0-9]{64}$') { throw "Invalid manifest entry: $name" }
    $expected.Add($name, [string]$manifest[$name])
}
$expected.Add('package.json', (Get-FileHash -LiteralPath (Join-Path $Runtime 'package.json') -Algorithm SHA256).Hash)
$files = @(Get-ChildItem -LiteralPath $Runtime -File -Recurse)
if ($files.Count -ne $expected.Count) { throw 'Runtime has missing or unlisted files' }
foreach ($file in $files) {
    $name = [IO.Path]::GetRelativePath($Runtime, $file.FullName).Replace('\', '/')
    if (!$expected.ContainsKey($name) -or (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -ne $expected[$name]) { throw "Runtime hash mismatch: $name" }
}
$feed = Get-Content -LiteralPath (Join-Path $PackageDirectory "releases.$Channel.json") -Raw | ConvertFrom-Json
$assets = @($feed.Assets)
if ($assets.Count -ne 1) { throw 'Candidate feed must contain exactly one release' }
$asset = $assets[0]
$filename = "PetTracker.Desktop-$Version-$Channel-full.nupkg"
if ($asset.PackageId -ne 'PetTracker.Desktop' -or $asset.Version -ne $Version -or $asset.Type -ne 'Full' -or $asset.FileName -cne $filename) { throw 'Unexpected package identity, version, channel or type' }
$package = Join-Path $PackageDirectory $filename
if ((Get-Item -LiteralPath $package).Length -ne $asset.Size -or (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne $asset.SHA256) { throw 'Package differs from release feed' }
$archive = [IO.Compression.ZipFile]::OpenRead($package)
try {
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $archive.Entries) {
        if (!$entry.FullName.StartsWith('lib/app/', [StringComparison]::Ordinal)) { continue }
        $name = $entry.FullName.Substring(8)
        if (!$name -or $name.EndsWith('/')) { continue }
        if (!$seen.Add($name)) { throw "Duplicate payload entry: $name" }
        if ($name -eq 'sq.version') {
            $reader = [IO.StreamReader]::new($entry.Open()); try { [xml]$metadata = $reader.ReadToEnd() }finally { $reader.Dispose() }
            $m = $metadata.package.metadata
            if ($m.id -ne 'PetTracker.Desktop' -or $m.version -ne $Version -or $m.channel -ne $Channel -or $m.mainExe -ne 'PetTracker.exe' -or $m.rid -ne 'win-x64' -or $m.machineArchitecture -ne 'x64') { throw 'Incorrect Velopack installation metadata' }
            continue
        }
        if ($name -cin @('Pet Tracker_ExecutionStub.exe', 'Squirrel.exe')) { continue } # Helpers supplied by the pinned packaging tool.
        if (!$expected.ContainsKey($name)) { throw "Unexpected packaged file: $name" }
        $stream = $entry.Open(); try { $sha = [Security.Cryptography.SHA256]::Create(); try { $hash = [Convert]::ToHexString($sha.ComputeHash($stream)) }finally { $sha.Dispose() } }finally { $stream.Dispose() }
        if ($hash -ne $expected[$name]) { throw "Packaged hash mismatch: $name" }
    }
    foreach ($name in $expected.Keys) { if (!$seen.Contains($name)) { throw "Missing packaged file: $name" } }
    foreach ($generated in @('sq.version', 'Pet Tracker_ExecutionStub.exe', 'Squirrel.exe')) { if (!$seen.Contains($generated)) { throw "Missing Velopack file: $generated" } }
}
finally { $archive.Dispose() }
[pscustomobject]@{Version = $Version; Channel = $Channel; PayloadFiles = $manifest.Count; Portraits = @($manifest.Keys | Where-Object { $_ -like 'portraits/*' }).Count; PackageSHA256 = $asset.SHA256; VerifiedUtc = [DateTimeOffset]::UtcNow.ToString('O'); PublicDeliveryVerified = $false }
