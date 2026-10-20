param(
    [Parameter(Mandatory)][string]$Candidate,
    [Parameter(Mandatory)][string]$Publisher,
    [Parameter(Mandatory)][string]$PrivateKey,
    [Parameter(Mandatory)][string]$Dotnet,
    [string]$Gh = 'gh',
    [switch]$Publish
)
# Default: prepare and verify locally. -Publish uploads only the reviewed assets.
$ErrorActionPreference = 'Stop'
$candidateRoot = (Resolve-Path -LiteralPath $Candidate).Path
$config = Get-Content -LiteralPath $Publisher -Raw | ConvertFrom-Json
$uri = [Uri]$config.FeedRoot
if ($uri.Scheme -ne 'https' -or $uri.Host -ne 'github.com' -or $uri.AbsolutePath -notmatch '^/([A-Za-z0-9_.-]+)/([A-Za-z0-9_.-]+)/releases/download/updates/$') { throw 'Use a fixed GitHub updates release URL' }
$repository = $Matches[1] + '/' + $Matches[2]
$proof = Get-Content -LiteralPath (Join-Path $candidateRoot 'candidate-verification.json') -Raw | ConvertFrom-Json
if (-not $proof.PublisherConfigured) { throw 'Rebuild with the publisher configuration before publishing' }
$verified = & (Join-Path $PSScriptRoot 'tools/Verify-UpdatePackage.ps1') -Runtime $proof.VerifiedPayloadDirectory -PackageDirectory $candidateRoot -Version $proof.Version -Channel $proof.Channel
$channel = $verified.Channel
$tool = Join-Path $PSScriptRoot 'tools/ReleaseTools/bin/Release/net10.0/ReleaseTools.dll'
if (-not (Test-Path -LiteralPath $tool)) { throw 'Build ReleaseTools first' }
& $Dotnet $tool verify-runtime-config (Join-Path $proof.VerifiedPayloadDirectory 'PetTracker.dll') $Publisher
if ($LASTEXITCODE -ne 0) { throw 'Candidate was built for a different publisher' }
$setup = Join-Path $candidateRoot "PetTracker.Desktop-$($proof.Channel)-Setup.exe"
$signed = Join-Path $candidateRoot "releases.$channel.signed.json"
& $Dotnet $tool sign $channel (Join-Path $candidateRoot "releases.$channel.json") $PrivateKey $signed 30 $setup
if ($LASTEXITCODE -ne 0) { throw 'Signing failed' }
& $Dotnet $tool verify $Publisher $channel $signed
if ($LASTEXITCODE -ne 0) { throw 'Feed is not signed by the configured publisher' }
$package = Join-Path $candidateRoot "PetTracker.Desktop-$($proof.Version)-$channel-full.nupkg"
$setup = Join-Path $candidateRoot "PetTracker.Desktop-$channel-Setup.exe"
$assets = @($package, $setup, $signed)
$plan = [ordered]@{Repository = $repository; Tag = 'updates'; Version = $proof.Version; Channel = $channel; Assets = @($assets | ForEach-Object { [ordered]@{Name = [IO.Path]::GetFileName($_); SHA256 = (Get-FileHash -LiteralPath $_).Hash; Bytes = (Get-Item -LiteralPath $_).Length } }) }
$plan | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $candidateRoot 'publish-plan.json') -Encoding utf8
if (-not $Publish) { $plan | ConvertTo-Json -Depth 5; return }
$repo = & $Gh repo view $repository --json nameWithOwner, visibility | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $repo.nameWithOwner -ine $repository -or $repo.visibility -ne 'PUBLIC') { throw 'Updates require the configured public release repository' }
$release = & $Gh api "repos/$repository/releases/tags/updates" | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $release.draft) { throw 'Create the public updates release first' }
# Stage immutable assets. The Actions job is serialized with feed renewal so an
# old renewal cannot overwrite a new release, even across publisher computers.
$digest = (Get-FileHash -LiteralPath $signed).Hash.ToLowerInvariant()
$stagedFeed = Join-Path $candidateRoot "candidate-$($proof.Version)-$channel-$digest.signed.json"
$stagedSetup = Join-Path $candidateRoot "PetTracker.Desktop-$($proof.Version)-$channel-Setup.exe"
Copy-Item -LiteralPath $signed -Destination $stagedFeed -Force
Copy-Item -LiteralPath $setup -Destination $stagedSetup -Force
foreach ($asset in @($package, $stagedSetup, $stagedFeed)) {
    $existing = @($release.assets | Where-Object { $_.name -ceq [IO.Path]::GetFileName($asset) })
    if ($existing.Count) {
        if ($existing[0].digest -ine ('sha256:' + (Get-FileHash -LiteralPath $asset).Hash)) { throw 'Versioned asset differs or has no digest. Increment the version.' }
    }
    else {
        & $Gh release upload updates $asset --repo $repository
        if ($LASTEXITCODE -ne 0) { throw 'Candidate upload failed; current feed retained' }
    }
}
& $Gh workflow run publish-updates.yml --repo $repository -f "version=$($proof.Version)" -f "channel=$channel" -f "feed_sha256=$digest"
if ($LASTEXITCODE -ne 0) { throw 'Publication workflow did not start; current feed retained' }
"Publication requested for $($proof.Version) on $channel. Verify the workflow completes successfully before sharing."