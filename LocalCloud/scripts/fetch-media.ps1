# Development/packaging only. The finished application never downloads tools.
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$cache=Join-Path $root 'artifacts/ffmpeg-9.0.2.zip'
$expected='60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba'
New-Item -ItemType Directory -Force (Split-Path $cache) | Out-Null
if (!(Test-Path $cache)) { Invoke-WebRequest 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip' -OutFile $cache }
if ((Get-FileHash $cache -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'FFmpeg distribution changed. Refusing an unverified build; obtain the pinned 9.0.2 distribution from the upstream build archive.' }
$unpacked=Join-Path $root 'artifacts/ffmpeg-unpacked'
Expand-Archive $cache $unpacked -Force
$dir=Join-Path $unpacked 'ffmpeg-9.0.2-essentials_build'
$output=Join-Path $root 'tools/win-x64'
New-Item -ItemType Directory -Force $output | Out-Null
Copy-Item "$dir/bin/ffmpeg.exe","$dir/bin/ffprobe.exe","$dir/LICENSE","$dir/README.txt" $output -Force
