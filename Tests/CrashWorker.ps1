param([string]$Root,[string]$Mode,[string]$Stage)
$ErrorActionPreference='Stop'
$base=Split-Path $PSScriptRoot -Parent
. (Join-Path $base 'StateEngine.ps1')
$script:DpStore=$Root
$script:DpManifest=Join-Path $Root 'pages.xml'
$script:DpState=Read-DpManifest $script:DpManifest
$script:DpSuspended=$false;$script:hostReady=$false
function Write-DpLog {param($Message)}
function Save-DpPageLayout {param($Page)}
function Set-DpFilePage {
    param($Page,[bool]$Commit=$true,[bool]$RegisterNew=$true)
    if ($Stage -eq 'PermissionDenied' -and $Page -eq 2) {throw [UnauthorizedAccessException]::new('Injected access denied')}
    # The persisted adapter simulates attributes; production uses actual NTFS attributes.
    Write-DpAtomicXml -Path (Join-Path $Root 'visibility.xml') -Value ([PSCustomObject]@{Page=$Page})
}
function Set-DpSystemIcons {param($Page)
    if ($Stage -eq 'SystemFailure' -and $Page -eq 2) {throw 'Injected registry failure'}
    Write-DpAtomicXml -Path (Join-Path $Root 'system.xml') -Value ([PSCustomObject]@{Page=$Page})
}
function Refresh-DpDesktop {}
function Restore-DpPageLayout {param($Page)
    if ($Stage -eq 'LayoutFailure' -and $Page -eq 2) {throw 'Injected incomplete Explorer view'}
    Write-DpAtomicXml -Path (Join-Path $Root 'view.xml') -Value ([PSCustomObject]@{Page=$Page})
}
function Invoke-DpFaultPoint {param([string]$Name)
    if ($Name -eq $Stage) {[Environment]::Exit(86)}
}
if ($Mode -eq 'Switch') {Invoke-DpPageTransaction -Page 2 -Animate:$false}
elseif ($Mode -eq 'Resume') {Resume-DpCommittedPage}
else {throw 'Unknown test mode'}
