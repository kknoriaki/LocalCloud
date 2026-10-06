param([Parameter(Mandatory=$true)][string]$Destination)
$ErrorActionPreference='Stop'
try{
    $dest=[IO.Path]::GetFullPath($Destination).TrimEnd('\')
    $marker=Join-Path $dest 'version.json'
    if(!(Test-Path -LiteralPath $marker)){throw 'Installation marker missing; automatic uninstall is refused.'}
    $files=(Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json).applicationFiles
    foreach($relative in @($files)+@('server/tools/ffmpeg.exe','server/tools/ffprobe.exe','Uninstall.exe')){
        $target=[IO.Path]::GetFullPath((Join-Path $dest $relative))
        if(!$target.StartsWith($dest+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Unexpected installation marker path.'}
        for($parent=$target; $parent; $parent=Split-Path -Parent $parent){
            if((Test-Path -LiteralPath $parent) -and ((Get-Item -LiteralPath $parent -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Symlink/junction detected; automatic uninstall is refused.'}
        }
    }
    foreach($p in Get-Process -Name LocalCloud,LocalCloud.Server,ffmpeg,ffprobe -ErrorAction SilentlyContinue){
        if($p.Path -and $p.Path.StartsWith($dest+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'LocalCloud is still running. Exit through its tray menu before uninstalling.'}
    }
    Write-Output 'UNINSTALL OK';exit 0
}catch{Write-Output $_.Exception.Message;exit 1}
