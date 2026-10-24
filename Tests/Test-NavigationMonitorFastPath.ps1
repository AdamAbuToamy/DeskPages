$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. (Join-Path $root 'WorkspaceCommands.ps1')
$script:hostReady=$true;$script:DpSuspended=$false;$script:DpEdgeDrag=$false;$script:DpMonitorSyncBusy=$false
$script:DpStore=Join-Path ([IO.Path]::GetTempPath()) ('dp-no-journal-'+[guid]::NewGuid().ToString('N'))
$script:DpMonitorRevision=10;$script:DpMonitorPrimaryOnly=$true
$script:DpState=[PSCustomObject]@{Workspace=[PSCustomObject]@{PrimaryOnly=$true;MonitorKey='same'}}
$script:revision=10;$script:reads=0
function Get-DpDisplaySignal {return [long[]]@($script:revision,0,5000,0)}
function Get-DpMonitorKey {$script:reads++;if($script:revision -ne 10){throw 'changed-display-reviewed'};return 'same'}
foreach($i in 1..10000){Sync-DpMonitorProfile}
if($script:reads -ne 0){throw 'Stable navigation still polls COM monitor geometry.'}
Sync-DpMonitorProfile -VerifyGeometry
if($script:reads -ne 1){throw 'Periodic verification stopped checking geometry.'}
$script:revision=11;$caught=$false
try{Sync-DpMonitorProfile}catch{if($_ -match 'changed-display-reviewed'){$caught=$true}else{throw}}
if(-not $caught){throw 'Display revision change bypassed the geometry fence.'}
Write-Host 'PASS: 10,000 stable navigation checks without geometry polling; forced verification and changed native display revision retain geometry checks.'
