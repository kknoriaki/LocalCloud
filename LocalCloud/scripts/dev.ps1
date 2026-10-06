$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location "$root/src/LocalCloud.Web"
npm ci
if ($LASTEXITCODE -ne 0) { throw 'npm ci failed' }
npm run build
if ($LASTEXITCODE -ne 0) { throw 'Web build failed' }
Set-Location $root
dotnet run --project src/LocalCloud.Server
