param([string]$Dotnet,[string]$Feed,[string]$UpdatePublisherFile)
$ErrorActionPreference='Stop'
$productRoot=$PSScriptRoot
[xml]$properties=Get-Content -LiteralPath (Join-Path $productRoot 'Directory.Build.props')
$version=[string]$properties.Project.PropertyGroup.Version
$runtimeVersion=[string]$properties.Project.PropertyGroup.RuntimeFrameworkVersion
if($version -notmatch '^\d+\.\d+\.\d+$' -or $runtimeVersion -notmatch '^10\.0\.\d+$'){throw 'Invalid release/runtime version in Directory.Build.props'}
if(!$Dotnet){$Dotnet=(Get-Command dotnet -ErrorAction Stop).Source}
$buildRoot=Join-Path $productRoot 'build'
$runtime=Join-Path $buildRoot ('runtime-'+[guid]::NewGuid().ToString('N'))
$worker=Join-Path $buildRoot ('capture-'+[guid]::NewGuid().ToString('N'))
$release=Join-Path $productRoot 'release'
New-Item -ItemType Directory -Force $buildRoot,$release | Out-Null
$restoreArgs=@();if($Feed){$restoreArgs=@('--source',$Feed)}
$publisherArgs=@('-p:UpdatePublisherFile=')
if($UpdatePublisherFile){
 $UpdatePublisherFile=(Resolve-Path -LiteralPath $UpdatePublisherFile).Path
 & $Dotnet build (Join-Path $productRoot 'tools/ReleaseTools/ReleaseTools.csproj') -c Release @restoreArgs --nologo -v quiet -p:UseSharedCompilation=false -m:1
 if($LASTEXITCODE -ne 0){throw 'Publisher tools build failed'}
 & $Dotnet (Join-Path $productRoot 'tools/ReleaseTools/bin/Release/net10.0/ReleaseTools.dll') validate-config $UpdatePublisherFile
 if($LASTEXITCODE -ne 0){throw 'Publisher configuration must validate before embedding'}
 $publisherArgs=@("-p:UpdatePublisherFile=$UpdatePublisherFile")
}
& $Dotnet publish (Join-Path $productRoot 'src/App/App.csproj') -c Release -r win-x64 --self-contained true -o $runtime @restoreArgs @publisherArgs --nologo -v quiet -p:UseSharedCompilation=false -m:1
if($LASTEXITCODE -ne 0){throw 'Application build failed'}
& $Dotnet publish (Join-Path $productRoot 'src/Worker/Worker.csproj') -c Release -r win-x64 --self-contained true -o $worker @restoreArgs --nologo -v quiet -p:UseSharedCompilation=false -m:1
if($LASTEXITCODE -ne 0){throw 'Capture build failed'}
foreach($file in Get-ChildItem -LiteralPath $worker -File){$target=Join-Path $runtime $file.Name;if(Test-Path -LiteralPath $target){if((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $target).Hash){if($file.Name -notin @('Microsoft.VisualBasic.dll','System.Drawing.dll','WindowsBase.dll')){throw "Dependency mismatch: $($file.Name)"}
 $desktopFile=Join-Path $env:NUGET_PACKAGES ("microsoft.windowsdesktop.app.runtime.win-x64/$runtimeVersion/runtimes/win-x64/lib/net10.0/"+$file.Name)
 if(!(Test-Path -LiteralPath $desktopFile) -or (Get-FileHash -LiteralPath $desktopFile).Hash -ne (Get-FileHash -LiteralPath $target).Hash){throw "Unverified desktop runtime assembly: $($file.Name)"}
 }}else{Copy-Item -LiteralPath $file.FullName -Destination $target}}
foreach($name in @('LICENSE','NOTICE')){Copy-Item -LiteralPath (Join-Path $productRoot $name) -Destination (Join-Path $runtime $name)}
Copy-Item -LiteralPath (Join-Path $productRoot 'docs/INSTALL.txt') -Destination (Join-Path $runtime 'Player Guide.txt')
Copy-Item -LiteralPath (Join-Path $productRoot 'THIRD-PARTY-NOTICES.txt') -Destination (Join-Path $runtime 'Third Party Notices.txt')
Copy-Item -LiteralPath (Join-Path $productRoot 'licenses') -Destination (Join-Path $runtime 'licenses') -Recurse
$entries=Get-ChildItem -LiteralPath $runtime -File -Recurse
foreach($file in $entries){$name=[IO.Path]::GetRelativePath($runtime,$file.FullName).Replace('\','/');if($name -match '(?i)AionFlex|(^|/)(profiles|backups|logs|fixture)(/|$)|\.pdb$|\.jsonl$|settings\.json$|pet-frames|Codex|ChatGPT|OpenAI'){throw "Private or unrelated file in release: $name"}}
$hashes=[ordered]@{};foreach($file in $entries){$hashes[[IO.Path]::GetRelativePath($runtime,$file.FullName).Replace('\','/')]=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash}
[IO.File]::WriteAllText((Join-Path $runtime 'package.json'),(ConvertTo-Json -InputObject $hashes -Depth 3))
$payload=Join-Path $buildRoot 'payload.zip';if(Test-Path -LiteralPath $payload){Remove-Item -LiteralPath $payload}
[IO.Compression.ZipFile]::CreateFromDirectory($runtime,$payload,[IO.Compression.CompressionLevel]::Optimal,$false)
[IO.File]::WriteAllText((Join-Path $buildRoot 'payload.sha256'),(Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash)
& $Dotnet publish (Join-Path $productRoot 'src/Setup/Setup.csproj') -c Release -r win-x64 --self-contained true -o (Join-Path $buildRoot 'setup') @restoreArgs --nologo -v quiet -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:UseSharedCompilation=false -m:1
if($LASTEXITCODE -ne 0){throw 'Installer build failed'}
if(-not $UpdatePublisherFile){Copy-Item -LiteralPath (Join-Path $buildRoot 'setup/PetTrackerSetup.exe') -Destination (Join-Path $release 'PetTrackerSetup.exe') -Force}
Copy-Item -LiteralPath $payload -Destination (Join-Path $release "PetTracker-$version-win-x64.zip") -Force
Copy-Item -LiteralPath (Join-Path $productRoot 'docs/INSTALL.txt') -Destination (Join-Path $release 'Player Guide.txt') -Force
$result=[ordered]@{Version="$version-beta";RuntimeVersion=$runtimeVersion;Files=$hashes.Count;Portraits=(Get-ChildItem -LiteralPath (Join-Path $runtime 'portraits') -File).Count;AionFlexRequired=$false;SignInRequired=$false;SelfContained=$true;InstallerSigned=$false;NpcapBundled=$false;UpdateUiIntegrated=$true;PublisherConfigured=[bool]$UpdatePublisherFile;Runtime=$runtime;SetupSHA256=if($UpdatePublisherFile){$null}else{(Get-FileHash -LiteralPath (Join-Path $release 'PetTrackerSetup.exe')).Hash};PortableSHA256=(Get-FileHash -LiteralPath (Join-Path $release "PetTracker-$version-win-x64.zip")).Hash}
[IO.File]::WriteAllText((Join-Path $productRoot 'package-verification.json'),(ConvertTo-Json -InputObject $result -Depth 3))
$result
