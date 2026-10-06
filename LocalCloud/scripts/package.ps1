param([string]$Runtime='win-x64',[string]$NsisPath='makensis.exe')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
& "$PSScriptRoot/build.ps1"
if($Runtime -ne 'win-x64'){throw 'The public release targets Windows x64.'}
Set-Location $root
$output=Join-Path $root "artifacts/$Runtime"
if(Test-Path $output){
 if(Test-Path (Join-Path $output '.localcloud')){throw 'Build output contains user storage; refusing to clean.'}
 Remove-Item -LiteralPath $output -Recurse -Force
}
foreach($item in @(@('LocalCloud.Server','server'),@('LocalCloud.Desktop',''),@('LocalCloud.Updater','updater'))){
 $dest=Join-Path $output $item[1]
 dotnet publish "src/$($item[0])" -c Release -r $Runtime --self-contained true -m:1 -p:UseSharedCompilation=false -p:RuntimeFrameworkVersion=10.0.12 -o $dest
 if($LASTEXITCODE -ne 0){throw "Publish failed: $($item[0])"}
}
python "$PSScriptRoot/collect-licenses.py"
if($LASTEXITCODE -ne 0){throw 'Dependency notices failed.'}
python "$PSScriptRoot/release-artifacts.py" --nsis $NsisPath
if($LASTEXITCODE -ne 0){throw 'Release packaging failed.'}
Write-Host 'Ready: artifacts/releases (Setup, Core, Update, Source and SHA256SUMS).'
