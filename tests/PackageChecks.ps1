param([Parameter(Mandatory)][string]$Candidate,[Parameter(Mandatory)][string]$EvidenceDirectory)
$ErrorActionPreference='Stop'
$Candidate=(Resolve-Path -LiteralPath $Candidate).Path
$evidence=[IO.Path]::GetFullPath($EvidenceDirectory)
if(Test-Path -LiteralPath $evidence){throw 'Use a fresh evidence directory'}
New-Item -ItemType Directory -Path $evidence | Out-Null
$proof=Get-Content -LiteralPath (Join-Path $Candidate 'candidate-verification.json') -Raw | ConvertFrom-Json
$verify=Join-Path $PSScriptRoot '../tools/Verify-UpdatePackage.ps1'
$results=[Collections.Generic.List[string]]::new()
& $verify -Runtime $proof.VerifiedPayloadDirectory -PackageDirectory $Candidate -Version $proof.Version -Channel $proof.Channel | Out-Null
$results.Add('Verified all staged payload hashes in the actual package')
$feedName="releases.$($proof.Channel).json"
$feed=Get-Content -LiteralPath (Join-Path $Candidate $feedName) -Raw | ConvertFrom-Json
$packageName=$feed.Assets[0].FileName
Copy-Item -LiteralPath (Join-Path $Candidate $packageName) -Destination (Join-Path $evidence $packageName)
function SaveFeed {$feed | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $evidence $feedName) -Encoding utf8}
function Reject([string]$expected,[string]$label){
 $caught=$false
 try{& $verify -Runtime $proof.VerifiedPayloadDirectory -PackageDirectory $evidence -Version $proof.Version -Channel $proof.Channel | Out-Null}catch{if($_.Exception.Message -notlike "*$expected*"){throw};$caught=$true}
 if(!$caught){throw "Verifier accepted defect: $label"}
 $results.Add($label)
}
$feed.Assets[0].Version='999.0.0';SaveFeed
Reject 'Unexpected package identity' 'Rejects a mismatched release version'
$feed.Assets[0].Version=$proof.Version;SaveFeed
$zip=[IO.Compression.ZipFile]::Open((Join-Path $evidence $packageName),[IO.Compression.ZipArchiveMode]::Update)
try{$entry=$zip.GetEntry('lib/app/Player Guide.txt');if(!$entry){throw 'Missing mutation target'};$entry.Delete();$entry=$zip.CreateEntry('lib/app/Player Guide.txt');$writer=[IO.StreamWriter]::new($entry.Open());try{$writer.Write('Altered guide for verifier regression only.')}finally{$writer.Dispose()}}finally{$zip.Dispose()}
Reject 'Package differs from release feed' 'Rejects bytes that differ from the release feed'
$feed.Assets[0].Size=(Get-Item -LiteralPath (Join-Path $evidence $packageName)).Length
$feed.Assets[0].SHA256=(Get-FileHash -LiteralPath (Join-Path $evidence $packageName) -Algorithm SHA256).Hash
SaveFeed
Reject 'Packaged hash mismatch: Player Guide.txt' 'Rejects altered payload even after outer feed checksum is recalculated'
[pscustomobject]@{ChecksPassed=$results.Count;Checks=$results;Candidate=$Candidate;TestArtifactsOnly=$true} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'results.json') -Encoding utf8
"PASS: $($results.Count) actual package checks"
