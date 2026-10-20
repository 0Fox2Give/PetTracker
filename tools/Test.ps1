param([string]$Dotnet='dotnet',[string]$Results)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(!$Results){$Results=Join-Path $root ('test-results/'+[guid]::NewGuid().ToString('N'))}
$Results=[IO.Path]::GetFullPath($Results)
if(Test-Path -LiteralPath $Results){throw 'Use a new test-results directory'}
New-Item -ItemType Directory -Path $Results -Force | Out-Null
foreach($suite in @(@('core','tests/Tests.csproj'),@('ui','tests/UiTests/UiTests.csproj'),@('updates','tests/UpdateTests/UpdateTests.csproj'))){
 & $Dotnet run --project (Join-Path $root $suite[1]) -c Release -p:UseSharedCompilation=false -- (Join-Path $Results $suite[0])
 if($LASTEXITCODE -ne 0){throw "Failed suite: $($suite[0])"}
}
Write-Output "Checks passed. Evidence: $Results"
