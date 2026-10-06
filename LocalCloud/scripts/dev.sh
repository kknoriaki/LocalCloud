#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
(cd src/LocalCloud.Web && npm ci && npm run build)
dotnet publish src/LocalCloud.Server -c Debug -o artifacts/dev -m:1 -p:UseSharedCompilation=false
dotnet artifacts/dev/LocalCloud.Server.dll
