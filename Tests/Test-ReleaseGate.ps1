$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$rejected=$false
try{& (Join-Path $root 'Installer/Build-Installer.ps1') -RequireSignature}
catch{if($_.Exception.Message -notlike 'Public release requires*'){throw};$rejected=$true}
if(-not $rejected){throw 'Public build accepted no signing identity'}
Write-Host 'PASS: signed public release gate rejects missing identity before building or modifying runtime files.'
