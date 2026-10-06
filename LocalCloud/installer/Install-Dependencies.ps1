param([Parameter(Mandatory=$true)][string]$Destination,[ValidateSet('Media','WebView2')][string]$Component,[ValidateSet('en','ru')][string]$Language='en')
$ErrorActionPreference='Stop'
[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
$work=Join-Path ([IO.Path]::GetTempPath()) ('LocalCloud-Dependency-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
try {
    if($Component -eq 'Media') {
        $tools=Join-Path $Destination 'server/tools'
        foreach($path in @((Join-Path $Destination 'server'),$tools)){
            if((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Symlink/junction in media tools path is not allowed.'}
        }
        if((Test-Path -LiteralPath (Join-Path $tools 'ffmpeg.exe')) -and (Test-Path -LiteralPath (Join-Path $tools 'ffprobe.exe'))) { Write-Output 'Existing FFmpeg/FFprobe retained.';exit 0 }
        $archive=Join-Path $work 'ffmpeg.zip'
        $expected='60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba'
        Invoke-WebRequest -UseBasicParsing 'https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-9.0.2-essentials_build.zip' -OutFile $archive
        if((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected){throw 'FFmpeg checksum changed. Refusing to install an unverified archive; use the pinned 9.0.2 upstream archive or a compatible manual installation.'}
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zip=[IO.Compression.ZipFile]::OpenRead($archive)
        try {
            New-Item -ItemType Directory -Force -Path $tools | Out-Null
            $root='ffmpeg-9.0.2-essentials_build/'
            foreach($item in @(@('bin/ffmpeg.exe','ffmpeg.exe'),@('bin/ffprobe.exe','ffprobe.exe'),@('LICENSE','LICENSE'),@('README.txt','README.txt'))){
                $entry=$zip.GetEntry($root+$item[0]);if(!$entry){throw 'Expected FFmpeg file is missing.'}
                $target=Join-Path $tools $item[1];$temp=$target+'.localcloud-download-tmp'
                try{[IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$temp,$true);Move-Item -LiteralPath $temp -Destination $target -Force}finally{Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue}
            }
        } finally { $zip.Dispose() }
        Write-Output 'FFmpeg/FFprobe 9.0.2 installed directly from upstream with checksum and notices.'
    } else {
        $client='{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'
        $present=$false
        foreach($base in @('HKCU:\Software\Microsoft\EdgeUpdate\Clients','HKLM:\Software\WOW6432Node\Microsoft\EdgeUpdate\Clients','HKLM:\Software\Microsoft\EdgeUpdate\Clients')){
            $pv=(Get-ItemProperty -LiteralPath ($base+'\'+$client) -Name pv -ErrorAction SilentlyContinue).pv
            if($pv -and $pv -ne '0.0.0.0'){$present=$true}
        }
        if($present){Write-Output 'WebView2 runtime already present.';exit 0}
        $bootstrapper=Join-Path $work 'MicrosoftEdgeWebview2Setup.exe'
        Invoke-WebRequest -UseBasicParsing 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $bootstrapper
        $signature=Get-AuthenticodeSignature -LiteralPath $bootstrapper
        if($signature.Status -ne 'Valid' -or !$signature.SignerCertificate.Subject.Contains('O=Microsoft Corporation')){throw 'The WebView2 installer does not have a valid Microsoft signature.'}
        $p=Start-Process -FilePath $bootstrapper -ArgumentList '/silent','/install' -PassThru -Wait
        if($p.ExitCode -notin @(0,3010)){throw ('WebView2 installer returned '+$p.ExitCode)}
        Write-Output 'WebView2 installation finished.'
    }
    exit 0
} catch { Write-Output $_.Exception.Message;exit 1 }
finally { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
