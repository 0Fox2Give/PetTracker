param([Parameter(Mandatory)][string]$Repository,[string]$Publisher='publisher.json')
$ErrorActionPreference='Stop'
if(-not $env:PET_TRACKER_SIGNING_KEY){throw 'Missing publisher signing secret'}
$config=Get-Content -LiteralPath $Publisher -Raw | ConvertFrom-Json
if($config.FeedRoot -cne "https://github.com/$Repository/releases/download/updates/"){throw 'Repository differs from pinned publisher'}
$folder=Join-Path ([IO.Path]::GetTempPath()) ('pet-tracker-renew-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $folder | Out-Null
$key=Join-Path $folder 'signing.pem'
try{
 [IO.File]::WriteAllText($key,$env:PET_TRACKER_SIGNING_KEY)
 $env:PET_TRACKER_SIGNING_KEY=$null
 $tool=Join-Path $PSScriptRoot 'ReleaseTools/bin/Release/net10.0/ReleaseTools.dll'
 $release=gh release view updates --repo $Repository --json assets,isDraft | ConvertFrom-Json
 if($LASTEXITCODE -ne 0 -or $release.isDraft){throw 'Public updates release unavailable'}
 $feeds=@($release.assets|Where-Object{$_.name -cin @('releases.win-stable.signed.json','releases.win-beta.signed.json')})
 if(-not $feeds.Count){throw 'No published signed feeds to renew'}
 $status=@()
 foreach($feed in $feeds){
  $channel=if($feed.name -eq 'releases.win-stable.signed.json'){'win-stable'}else{'win-beta'}
  gh release download updates --repo $Repository --pattern $feed.name --dir $folder
  if($LASTEXITCODE -ne 0){throw 'Could not retrieve current feed'}
  $path=Join-Path $folder $feed.name
  dotnet $tool renew $Publisher $channel $path $key $path
  if($LASTEXITCODE -ne 0){throw 'Could not authenticate/renew current feed'}
  dotnet $tool verify $Publisher $channel $path
  if($LASTEXITCODE -ne 0){throw 'Renewed signature did not verify'}
  gh release upload updates $path --repo $Repository --clobber
  if($LASTEXITCODE -ne 0){throw 'Renewed feed upload failed'}
  $envelope=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json
  $payload=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($envelope.Payload))|ConvertFrom-Json
  $status+=@{Channel=$channel;PublishedUtc=$payload.PublishedUtc;ExpiresUtc=$payload.ExpiresUtc;SHA256=(Get-FileHash -LiteralPath $path).Hash}
 }
 # Keep a public audit of the latest successful renewal in the source repository.
 $status|ConvertTo-Json -Depth 4|Set-Content -LiteralPath 'feed-status.json' -Encoding utf8
}finally{if(Test-Path -LiteralPath $key){Remove-Item -LiteralPath $key -Force};$env:PET_TRACKER_SIGNING_KEY=$null}
