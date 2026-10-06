[CmdletBinding()]
param(
    [string]$InstallPath,
    [string]$ConfigPath,
    [switch]$Rollback,
    [string]$BackupPath,
    [switch]$RestoreDatabase,
    [switch]$Plan
)
$ErrorActionPreference = 'Stop'
try {
    if (!$InstallPath) {
        Add-Type -AssemblyName System.Windows.Forms
        $picker = New-Object System.Windows.Forms.FolderBrowserDialog
        $picker.Description = 'Выберите СУЩЕСТВУЮЩУЮ папку программы LocalCloud (LocalCloud.exe + server). НЕ папку с Photos/Videos.'
        $picker.ShowNewFolderButton = $false
        $startup = Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'LocalCloud' -ErrorAction SilentlyContinue
        if ($startup -and $startup.LocalCloud -match '^"([^"]+LocalCloud\.exe)"(?:\s+--background)?$') {
            $candidate = Split-Path -Parent $Matches[1]
            if (Test-Path -LiteralPath (Join-Path $candidate 'LocalCloud.exe')) { $picker.SelectedPath = $candidate }
        }
        if ($picker.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { exit 0 }
        $InstallPath = $picker.SelectedPath
    }
    $engine = Join-Path $PSScriptRoot 'updater/LocalCloud.Updater.exe'
    if (!(Test-Path -LiteralPath $engine)) { $engine = Join-Path $PSScriptRoot 'payload/updater/LocalCloud.Updater.exe' }
    if (!(Test-Path -LiteralPath $engine)) { throw 'Папка updater не найдена. Распакуйте Update-архив целиком в отдельную папку.' }
    $updateArgs = @('--install', $InstallPath)
    if ($ConfigPath) { $updateArgs += @('--config', $ConfigPath) }
    if ($Plan) { $updateArgs += '--plan' }
    if ($Rollback) {
        if (!$BackupPath) {
            $backupRoot = Join-Path $InstallPath '_update_backup'
            $items = @(Get-ChildItem -LiteralPath $backupRoot -Directory | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'update-state.json') } | Sort-Object Name -Descending)
            if ($items.Count -eq 0) { throw 'Не найден backup этой установки. Укажите -BackupPath.' }
            $BackupPath = $items[0].FullName
            Write-Host "Последний backup: $BackupPath"
        }
        $updateArgs += @('--rollback', $BackupPath)
        if ($RestoreDatabase) { $updateArgs += '--restore-database' }
    } else {
        $payload = Join-Path $PSScriptRoot 'payload'
        if (!(Test-Path -LiteralPath (Join-Path $payload 'release.json'))) { throw 'Рядом нет payload/release.json. Для обновления нужен Update-архив, а не Full/Core.' }
        $updateArgs += @('--package', $payload)
    }
    $runDirectory = Join-Path ([IO.Path]::GetTempPath()) ('LocalCloud-Updater-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $runDirectory | Out-Null
    try {
        $manifestPath = Join-Path $PSScriptRoot 'payload/release.json'
        if (Test-Path -LiteralPath $manifestPath) {
            $release = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
            foreach ($entry in $release.files) {
                if (!$entry.path.StartsWith('updater/')) { continue }
                if ($entry.path.Contains('..') -or $entry.path.Contains(':') -or $entry.path.Contains('\')) { throw 'Небезопасный путь updater.' }
                $relative = $entry.path.Substring(8)
                $source = Join-Path (Join-Path $PSScriptRoot 'payload') $entry.path
                if ($entry.existing) { $source = Join-Path $InstallPath $entry.path }
                if (!(Test-Path -LiteralPath $source) -or (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256.ToLowerInvariant()) { throw "Компонент updater отсутствует или отличается: $($entry.path). Для повреждённой установки используйте Full-пакет после резервной копии." }
                $destination = Join-Path $runDirectory $relative
                New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
                Copy-Item -LiteralPath $source -Destination $destination
            }
        } else { Copy-Item -Path (Join-Path (Split-Path -Parent $engine) '*') -Destination $runDirectory -Recurse }
        $engine = Join-Path $runDirectory 'LocalCloud.Updater.exe'
        & $engine @updateArgs
        $engineExitCode = $LASTEXITCODE
    } finally { Remove-Item -LiteralPath $runDirectory -Recurse -Force -ErrorAction SilentlyContinue }
    $global:LASTEXITCODE = $engineExitCode
    if ($LASTEXITCODE -ne 0) { throw 'Updater завершился с ошибкой. Прочитайте сообщение выше; backup не удалён.' }
} catch { Write-Host $_.Exception.Message -ForegroundColor Red; exit 1 }
