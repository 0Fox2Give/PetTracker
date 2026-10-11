param(
 [Parameter(Mandatory)][string]$Repository,
 [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
 [Parameter(Mandatory)][ValidateSet('win-stable','win-beta')][string]$Channel,
 [Parameter(Mandatory)][ValidatePattern('^[0-9A-Fa-f]{64}$')][string]$FeedSHA256,
 [string]$Publisher='publisher.json'
)
$ErrorActionPreference='Stop'
$config=Get-Content -LiteralPath $Publisher -Raw | ConvertFrom-Json
if($config.FeedRoot -cne "https://github.com/$Repository/releases/download/updates/"){throw 'Repository differs from pinned publisher'}
$folder=Join-Path ([IO.Path]::GetTempPath()) ('pet-tracker-publish-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $folder | Out-Null
$tool=Join-Path $PSScriptRoot 'ReleaseTools/bin/Release/net10.0/ReleaseTools.dll'
$staged="candidate-$Version-$Channel-$($FeedSHA256.ToLowerInvariant()).signed.json"
gh release download updates --repo $Repository --pattern $staged --dir $folder
if($LASTEXITCODE -ne 0){throw 'Candidate feed download failed'}
$feed=Join-Path $folder $staged
if((Get-FileHash -LiteralPath $feed).Hash -ine $FeedSHA256){throw 'Candidate feed digest differs'}
dotnet $tool verify $Publisher $Channel $feed
if($LASTEXITCODE -ne 0){throw 'Candidate signature did not verify'}
$envelope=Get-Content -LiteralPath $feed -Raw | ConvertFrom-Json
$payload=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($envelope.Payload)) | ConvertFrom-Json
$metadata=$payload.Feed | ConvertFrom-Json
$package="PetTracker.Desktop-$Version-$Channel-full.nupkg"
$entry=@($metadata.Assets | Where-Object { $_.FileName -ceq $package -and $_.Version -ceq $Version -and $_.Type -eq 'Full' })
if($entry.Count -ne 1 -or $metadata.Assets.Count -ne 1){throw 'Candidate must contain exactly the requested full release'}
$release=gh api "repos/$Repository/releases/tags/updates" | ConvertFrom-Json
if($LASTEXITCODE -ne 0 -or $release.draft){throw 'Public updates release unavailable'}
# A serialized late job must never replace a newer feed with an older version.
$fixed="releases.$Channel.signed.json"
if(@($release.assets|Where-Object{$_.name -ceq $fixed}).Count){
 gh release download updates --repo $Repository --pattern $fixed --dir $folder
 if($LASTEXITCODE -ne 0){throw 'Current feed download failed'}
 dotnet $tool verify $Publisher $Channel (Join-Path $folder $fixed)
 if($LASTEXITCODE -ne 0){throw 'Current feed needs renewal before publishing'}
 $current=Get-Content -LiteralPath (Join-Path $folder $fixed) -Raw | ConvertFrom-Json
 $currentPayload=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($current.Payload)) | ConvertFrom-Json
 foreach($asset in ($currentPayload.Feed|ConvertFrom-Json).Assets){if([version]$asset.Version -gt [version]$Version){throw 'Refusing to replace a newer release'}}
}
gh release download updates --repo $Repository --pattern $package --dir $folder
if($LASTEXITCODE -ne 0){throw 'Package download failed'}
$packagePath=Join-Path $folder $package
if((Get-FileHash -LiteralPath $packagePath).Hash -ine $entry[0].SHA256 -or (Get-Item -LiteralPath $packagePath).Length -ne $entry[0].Size){throw 'Package does not match signed feed'}
# The installer digest is covered by the same publisher signature as the package.
$setupName="PetTracker.Desktop-$Version-$Channel-Setup.exe"
$expectedSetup=$payload.InstallerSHA256
if($expectedSetup -notmatch '^[0-9a-fA-F]{64}$'){throw 'Signed installer digest missing'}
gh release download updates --repo $Repository --pattern $setupName --dir $folder
if($LASTEXITCODE -ne 0){throw 'Installer download failed'}
$setup=Join-Path $folder $setupName
if((Get-FileHash -LiteralPath $setup).Hash -ine $expectedSetup){throw 'Installer digest differs from signed metadata'}
$fixedSetup=Join-Path $folder "PetTracker.Desktop-$Channel-Setup.exe"
Copy-Item -LiteralPath $setup -Destination $fixedSetup
gh release upload updates $fixedSetup --repo $Repository --clobber
if($LASTEXITCODE -ne 0){throw 'Installer publication failed'}
Copy-Item -LiteralPath $feed -Destination (Join-Path $folder $fixed) -Force
gh release upload updates (Join-Path $folder $fixed) --repo $Repository --clobber
if($LASTEXITCODE -ne 0){throw 'Feed publication failed'}
$public=Join-Path $folder 'public-check.json'
Invoke-WebRequest -Uri ($config.FeedRoot+$fixed) -OutFile $public
if((Get-FileHash -LiteralPath $public).Hash -ine $FeedSHA256){throw 'Public feed differs from approved candidate'}
dotnet $tool verify $Publisher $Channel $public
if($LASTEXITCODE -ne 0){throw 'Public feed authentication failed'}
"Published and verified $Version on $Channel"
