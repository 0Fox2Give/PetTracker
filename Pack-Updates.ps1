param(
    [Parameter(Mandatory)][string]$Vpk,
    [Parameter(Mandatory)][string]$DotnetRoot,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$ReleaseNotes,
    [ValidateSet('win-beta', 'win-stable')][string]$Channel = 'win-beta'
)
# Creates a local candidate only. Publishing/signing remain separate release steps.
$ErrorActionPreference = 'Stop'
$Vpk = (Resolve-Path -LiteralPath $Vpk).Path
$DotnetRoot = (Resolve-Path -LiteralPath $DotnetRoot).Path
$ReleaseNotes = (Resolve-Path -LiteralPath $ReleaseNotes).Path
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Use a new output directory so earlier candidates remain intact' }
$build = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'package-verification.json') -Raw | ConvertFrom-Json
$runtime = (Resolve-Path -LiteralPath $build.Runtime).Path
$version = $build.Version -replace '-beta$', ''
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid built version' }
if ($OutputDirectory.StartsWith($runtime.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Output must be outside the runtime' }
$manifest = Get-Content -LiteralPath (Join-Path $runtime 'package.json') -Raw | ConvertFrom-Json -AsHashtable
$files = @(Get-ChildItem -LiteralPath $runtime -File -Recurse)
if ($files.Count -ne $manifest.Count + 1) { throw 'Runtime has missing or unlisted files' }
foreach ($file in $files) {
    $name = [IO.Path]::GetRelativePath($runtime, $file.FullName).Replace('\', '/')
    if ($name -eq 'package.json') { continue }
    if (!$manifest.ContainsKey($name) -or (Get-FileHash -LiteralPath $file.FullName).Hash -ne $manifest[$name]) { throw "Runtime changed since build: $name" }
}
foreach ($assembly in @('PetTracker.dll', 'PetTracker.Capture.dll')) {
    if ([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $runtime $assembly)).FileVersion -ne "$version.0") { throw "Version mismatch: $assembly" }
}
$savedDotnet = $env:DOTNET_ROOT; $savedTemp = $env:VELOPACK_TEMP
try {
    $env:DOTNET_ROOT = $DotnetRoot
    $env:VELOPACK_TEMP = Join-Path $PSScriptRoot ('build/vpk-temp-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $env:VELOPACK_TEMP -Force | Out-Null
    # Velopack always excludes createdump.exe. Stage a matching manifest instead of
    # shipping package.json with an entry that will be missing after installation.
    $payload = Join-Path $env:VELOPACK_TEMP 'payload'
    New-Item -ItemType Directory -Path $payload | Out-Null
    foreach ($file in $files) {
        $name = [IO.Path]::GetRelativePath($runtime, $file.FullName).Replace('\', '/')
        if ($name -in @('package.json', 'createdump.exe')) { continue }
        $target = Join-Path $payload $name
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
    $manifest.Remove('createdump.exe')
    $manifest | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $payload 'package.json') -Encoding utf8
    $toolVersion = & $Vpk --help
    if ($LASTEXITCODE -ne 0 -or ($toolVersion -join '') -notmatch 'Velopack CLI 1\.2\.161,') { throw 'Use the pinned vpk 1.2.161 tool' }
    & $Vpk pack --skip-updates --packId PetTracker.Desktop --packVersion $version --packDir $payload --mainExe PetTracker.exe --packTitle 'Pet Tracker' --packAuthors 'Pet Tracker' --channel $Channel --runtime win-x64 --delta None --releaseNotes $ReleaseNotes --icon (Join-Path $PSScriptRoot 'assets/PetTracker.ico') --outputDir $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Velopack packaging failed; candidate is not verified' }
    $verified = & (Join-Path $PSScriptRoot 'tools/Verify-UpdatePackage.ps1') -Runtime $payload -PackageDirectory $OutputDirectory -Version $version -Channel $Channel
    $verified | Add-Member -NotePropertyName VerifiedPayloadDirectory -NotePropertyValue $payload
    $verified | Add-Member -NotePropertyName PackagerExcludedFiles -NotePropertyValue @('createdump.exe')
    $verified | Add-Member -NotePropertyName PublisherConfigured -NotePropertyValue ([bool]$build.PublisherConfigured)
    $verified | Add-Member -NotePropertyName WindowsSigningVerified -NotePropertyValue $false
    $verified | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'candidate-verification.json') -Encoding utf8
    if ($build.PublisherConfigured -and $Channel -eq 'win-stable') {
        $setup = Join-Path $OutputDirectory "PetTracker.Desktop-$Channel-Setup.exe"
        Copy-Item -LiteralPath $setup -Destination (Join-Path $PSScriptRoot 'release/PetTrackerSetup.exe') -Force
        $build.SetupSHA256 = (Get-FileHash -LiteralPath $setup).Hash
        $build | Add-Member -Force -NotePropertyName InstallerFormat -NotePropertyValue 'Velopack'
        $build | Add-Member -Force -NotePropertyName UpdateCandidate -NotePropertyValue $OutputDirectory
        $build | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'package-verification.json') -Encoding utf8
    }
    $verified
}
finally { $env:DOTNET_ROOT = $savedDotnet; $env:VELOPACK_TEMP = $savedTemp }
