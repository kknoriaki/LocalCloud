# Only LocalCloud's rules are changed. LAN is Private/LocalSubnet; Radmin is restricted to its adapter and 26/8.
param([int]$Port=43110,[switch]$Radmin)
$ErrorActionPreference='Stop'
if ($Port -lt 1024 -or $Port -gt 65535) { throw 'Invalid port' }
$root=Split-Path $PSScriptRoot -Parent
$server=Join-Path $root 'server/LocalCloud.Server.exe'
if (!(Test-Path $server)) { throw 'Run this script from the installed Windows package.' }
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
if (!(New-Object Security.Principal.WindowsPrincipal $identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $params=@('-NoProfile','-ExecutionPolicy','Bypass','-File',"`"$PSCommandPath`"",'-Port',"$Port")
    if ($Radmin) { $params+='-Radmin' }
    Start-Process powershell -Verb RunAs -ArgumentList $params -Wait
    exit
}
function Ensure-LocalCloudRule($name,$title,$protocol,$rulePort,$profile,$remote,$interface='Any') {
    $rule=Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue
    if (!$rule) { New-NetFirewallRule -Name $name -DisplayName $title -Direction Inbound -Action Allow -Enabled True -Program $server -Protocol $protocol -LocalPort $rulePort -Profile $profile -RemoteAddress $remote -InterfaceAlias $interface | Out-Null }
    else {
        $rule | Set-NetFirewallRule -Enabled True -Direction Inbound -Action Allow -Profile $profile -InterfaceAlias $interface
        $rule | Get-NetFirewallApplicationFilter | Set-NetFirewallApplicationFilter -Program $server
        $rule | Get-NetFirewallPortFilter | Set-NetFirewallPortFilter -Protocol $protocol -LocalPort $rulePort
        $rule | Get-NetFirewallAddressFilter | Set-NetFirewallAddressFilter -RemoteAddress $remote
    }
}
if ($Radmin) {
    $adapters=@(Get-NetAdapter | Where-Object {$_.InterfaceDescription -like '*Radmin*' -or $_.Name -like '*Radmin*'})
    if ($adapters.Count -eq 0) { throw 'Radmin VPN adapter not found. Connect to Radmin first.' }
    Ensure-LocalCloudRule 'LocalCloud-Radmin' 'LocalCloud — выбранные папки через Radmin VPN' 'TCP' $Port 'Any' '26.0.0.0/8' $adapters[0].Name
} else {
    Ensure-LocalCloudRule 'LocalCloud-LAN' 'LocalCloud — домашняя сеть' 'TCP' $Port 'Private' 'LocalSubnet'
    Ensure-LocalCloudRule 'LocalCloud-Discovery' 'LocalCloud — обнаружение в домашней сети' 'UDP' 5353 'Private' 'LocalSubnet'
}
Write-Host 'Правила LocalCloud сохранены. Повторный запуск их не удаляет.'
