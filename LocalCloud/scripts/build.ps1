$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location "$root/src/LocalCloud.Web"
npm ci
if ($LASTEXITCODE -ne 0) { throw 'npm ci failed' }
npm run build
if ($LASTEXITCODE -ne 0) { throw 'Web build failed' }
Set-Location $root
dotnet build LocalCloud.sln -c Release -m:1 -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
dotnet run --project tests/LocalCloud.Tests -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
