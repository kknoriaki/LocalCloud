param([Parameter(Mandatory=$true)][string]$Destination)
$ErrorActionPreference='Stop'
try {
    $dest=[IO.Path]::GetFullPath($Destination).TrimEnd('\')
    if ($dest -eq [IO.Path]::GetPathRoot($dest).TrimEnd('\')) { throw 'Choose a separate application directory, not the drive root.' }
    for ($parent=$dest; $parent; $parent=Split-Path -Parent $parent) {
        if ((Test-Path -LiteralPath $parent) -and ((Get-Item -LiteralPath $parent -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Symlinks/junctions are not supported in the installation path.' }
        if (Test-Path -LiteralPath (Join-Path $parent '.localcloud')) { throw 'The installation directory is inside a media library. Choose a separate directory.' }
    }
    $config=if($env:LOCALCLOUD_CONFIG){$env:LOCALCLOUD_CONFIG}else{Join-Path $env:LOCALAPPDATA 'LocalCloud/settings.json'}
    if(Test-Path -LiteralPath $config){
        $settings=Get-Content -LiteralPath $config -Raw | ConvertFrom-Json
        $storage=[IO.Path]::GetFullPath($settings.StorageRoot).TrimEnd('\')
        if($dest.Equals($storage,[StringComparison]::OrdinalIgnoreCase) -or $dest.StartsWith($storage+'\',[StringComparison]::OrdinalIgnoreCase) -or $storage.StartsWith($dest+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Application and library paths overlap. Keep them separate.'}
    }
    $existing=(Test-Path -LiteralPath (Join-Path $dest 'LocalCloud.exe')) -and (Test-Path -LiteralPath (Join-Path $dest 'server/LocalCloud.Server.dll'))
    if (!$existing -and (Test-Path -LiteralPath $dest) -and @(Get-ChildItem -LiteralPath $dest -Force).Count -gt 0) { throw 'For a new installation choose an empty directory. Existing unrelated files are not overwritten.' }
    Write-Output 'INSTALL PATH OK';exit 0
} catch { Write-Output $_.Exception.Message; exit 1 }
