#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
(cd src/LocalCloud.Web && npm ci && npm run build)
dotnet build LocalCloud.sln -c Release -m:1 -p:UseSharedCompilation=false
dotnet tests/LocalCloud.Tests/bin/Release/net10.0/LocalCloud.Tests.dll
dotnet publish src/LocalCloud.Server -c Release -o artifacts/linux -m:1 -p:UseSharedCompilation=false
