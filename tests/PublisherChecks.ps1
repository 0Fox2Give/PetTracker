param([Parameter(Mandatory)][string]$Dotnet,[Parameter(Mandatory)][string]$Evidence)
$ErrorActionPreference='Stop'
$tool=(Resolve-Path (Join-Path $PSScriptRoot '../tools/ReleaseTools/bin/Release/net10.0/ReleaseTools.dll')).Path
$folder=[IO.Path]::GetFullPath($Evidence)
if(Test-Path -LiteralPath $folder){throw 'Use a fresh evidence directory'}
New-Item -ItemType Directory -Path $folder | Out-Null
$results=[Collections.Generic.List[string]]::new()
function Run([string[]]$Arguments,[bool]$Success,[string]$Label){
 $output=& $Dotnet $tool @Arguments 2>&1
 if(($LASTEXITCODE -eq 0) -ne $Success){throw "Failed check: $Label ($output)"}
 $results.Add($Label)
}
$config=Join-Path $folder 'publisher.json';$key=Join-Path $folder 'test-only-private.pem'
Run @('init-publisher','https://github.com/test-owner/test-repo/releases/download/updates/',$config,$key) $true 'Creates separate public configuration and private key'
$hash=(Get-FileHash -LiteralPath $key).Hash
Run @('init-publisher','https://github.com/test-owner/test-repo/releases/download/updates/',$config,$key) $false 'Refuses accidental publisher key replacement'
if((Get-FileHash -LiteralPath $key).Hash -ne $hash){throw 'Private key changed'}
if((Get-Content -LiteralPath $config -Raw).Contains('PRIVATE KEY')){throw 'Private key in configuration'}
$feed=Join-Path $folder 'feed.json';[IO.File]::WriteAllText($feed,'{"Assets":[]}')
$signed=Join-Path $folder 'signed.json';$renewed=Join-Path $folder 'renewed.json'
Run @('sign','win-stable',$feed,$key,$signed,'30') $true 'Signs publisher feed'
Run @('verify',$config,'win-stable',$signed) $true 'Verifies pinned publisher signature'
Run @('verify',$config,'win-beta',$signed) $false 'Rejects wrong release channel'
Run @('renew',$config,'win-stable',$signed,$key,$renewed) $true 'Renews authenticated feed'
Run @('verify',$config,'win-stable',$renewed) $true 'Renewed feed remains compatible with desktop verifier'
$envelope=Get-Content -LiteralPath $signed -Raw | ConvertFrom-Json
$bytes=[Convert]::FromBase64String($envelope.Signature);$bytes[0]=$bytes[0] -bxor 1;$envelope.Signature=[Convert]::ToBase64String($bytes)
$tampered=Join-Path $folder 'tampered.json';$envelope|ConvertTo-Json|Set-Content -LiteralPath $tampered
Run @('renew',$config,'win-stable',$tampered,$key,(Join-Path $folder 'bad.json')) $false 'Never renews a tampered feed'
$otherConfig=Join-Path $folder 'other.json';$otherKey=Join-Path $folder 'other-test-only-private.pem'
Run @('init-publisher','https://github.com/test-owner/test-repo/releases/download/updates/',$otherConfig,$otherKey) $true 'Creates independent test signer'
Run @('renew',$config,'win-stable',$signed,$otherKey,(Join-Path $folder 'bad-key.json')) $false 'Rejects a renewal key that differs from the installed publisher'
$results|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $folder 'results.json')
"PASS: $($results.Count) publisher checks"
