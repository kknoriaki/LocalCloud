param([Parameter(Mandatory=$true)][string]$Destination,[string]$ExpectedVersion)
$ErrorActionPreference='Stop'
try {
 $base=[IO.Path]::GetFullPath($Destination).TrimEnd('\')
 $manifest=Get-Content -LiteralPath (Join-Path $base 'release.json') -Raw | ConvertFrom-Json
 if($manifest.product -ne 'LocalCloud' -or $manifest.version -ne $ExpectedVersion -or $manifest.releaseChannel -ne 'public'){throw 'Unexpected installed release manifest.'}
 foreach($f in $manifest.files){
  $path=[IO.Path]::GetFullPath((Join-Path $base $f.path))
  if(!$path.StartsWith($base+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Manifest path escapes application directory.'}
  if(!(Test-Path -LiteralPath $path) -or (Get-Item -LiteralPath $path).Length -ne $f.size -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $f.sha256){throw ('Installed file verification failed: '+$f.path)}
 }
 Write-Output 'Installed application hashes verified.';exit 0
} catch {Write-Output $_.Exception.Message;exit 1}
