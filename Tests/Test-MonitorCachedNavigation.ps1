$ErrorActionPreference='Stop'
$base=Split-Path $PSScriptRoot -Parent
. (Join-Path $base 'StateEngine.ps1');. (Join-Path $base 'Workspace.ps1');. (Join-Path $base 'SnapshotCache.ps1')
Add-Type @'
using System;
public static class DeskPagesHost {
 public static bool SnapshotCacheEnabled=true;public static string One="1:resident",Four="4:resident";public static string Context;public static int ContextChanges,Previews;public static bool Available=true;
 public static void SnapshotSceneContext(string value){if(Context!=value){Context=value;ContextChanges++;}}
 public static string SavedSnapshotKey(int page){return Available?(page==1?One:Four):"";}
 public static bool TryBeginCachedPreview(int direction,string source,string target){Previews++;return source==One && target==Four;}
}
'@
function Assert($Value,$Message){if(-not $Value){throw $Message}}
function Get-DpMonitorKey {throw 'Hot cached navigation must not query native monitor geometry.'}
function Get-DpFixedMonitorPages {param($State,[object[]]$Screens) if(-not $Screens.Count){throw 'Cached preview must reuse the already verified display scope.'};[PSCustomObject]@{Page=2}}
$temp=Join-Path ([IO.Path]::GetTempPath()) ('dp-monitor-cache-'+[guid]::NewGuid().ToString('N'))
try{
 New-Item -ItemType Directory -Force $temp | Out-Null;$script:DpLayoutStore=$temp;$primary=[PSCustomObject]@{Device='main';Left=0;Top=0;Right=1920;Bottom=1080;Primary=$true};$secondary=[PSCustomObject]@{Device='second';Left=1920;Top=0;Right=3840;Bottom=1080;Primary=$false};$script:DpScopeScreens=@($primary,$secondary)
 $script:DpState=[PSCustomObject]@{Items=@([PSCustomObject]@{Path='fixed';Page=2;Pinned=$false},[PSCustomObject]@{Path='pin';Page=2;Pinned=$true});SystemIconBackup=@();Workspace=[PSCustomObject]@{PrimaryOnly=$true;MonitorPagesEnabled=$true;MonitorKey='verified-topology';SystemPinned=@();SecondaryIcons=@()}}
 $layout=[PSCustomObject]@{Page=2;Icons=@([PSCustomObject]@{Key='fixed';Name='fixed';X=17;Y=12},[PSCustomObject]@{Key='pin';Name='pin';X=100;Y=100})}
 $path=Join-Path $temp 'page-2.xml';Write-DpAtomicXml $path $layout
 $watch=[Diagnostics.Stopwatch]::StartNew()
 foreach($n in 1..100){Assert (Try-DpCachedPreview 1 4 1) 'Resident preview was lost without a desktop change.'}
 Assert ([DeskPagesHost]::Previews -eq 100 -and [DeskPagesHost]::ContextChanges -eq 1) 'Unchanged secondary page repeatedly invalidated the cache.'
 $layout.Icons[1].X=999;Write-DpAtomicXml $path $layout;Assert (Try-DpCachedPreview 1 4 1) 'Main pin metadata blocked a resident preview.'
 Assert ([DeskPagesHost]::ContextChanges -eq 1) 'A main-display pin copy invalidated every page image.'
 $layout.Icons[0].X=80;Write-DpAtomicXml $path $layout;Sync-DpSnapshotScene
 Assert ([DeskPagesHost]::ContextChanges -eq 1) 'A secondary-only edit invalidated primary-only frames.'
 $main=@([PSCustomObject]@{Key='main';Name='main';X=10;Y=20},[PSCustomObject]@{Key='pin';Name='pin';X=100;Y=100})
 $secondaryIcon=[PSCustomObject]@{Key='fixed';Name='fixed';X=2000;Y=12}
 $key=Get-DpSnapshotKey 1 @($main+$secondaryIcon)
 Assert ($key -eq (Get-DpSnapshotKey 1 $main)) 'Stationary secondary icons changed the primary frame key.'
 foreach($round in 1..50){
  $script:DpScopeScreens=@($primary);$script:DpState.Workspace.MonitorKey='disconnected-'+$round
  Assert (Try-DpCachedPreview 1 4 1) 'Disconnect lost resident primary images.'
  Assert ((Get-DpSnapshotKey 1 $main) -eq $key) 'Disconnect changed the primary frame key.'
  $script:DpScopeScreens=@($primary,$secondary);$script:DpState.Workspace.MonitorKey='reconnected-'+$round
  Assert (Try-DpCachedPreview 1 4 1) 'Reconnect lost resident primary images.'
  Assert ((Get-DpSnapshotKey 1 @($main+$secondaryIcon)) -eq $key) 'Reconnect changed the primary frame key.'
 }
 Assert ([DeskPagesHost]::ContextChanges -eq 1) 'Secondary hotplug changed the primary scene.'
 $layout.Icons=$main;Write-DpAtomicXml (Join-Path $temp 'page-1.xml') $layout
 Assert ((Get-DpSnapshotKey -Page 1) -eq $key) 'Source checkpoint and composed target use different primary keys.'
 [DeskPagesHost]::One=$key
 [DeskPagesHost]::Available=$false
 Assert (-not (Try-DpCachedPreview 1 4 1)) 'Obsolete script keys survived native scope invalidation.'
 Assert ($script:DpSnapshotKeys.Count -eq 0) 'Obsolete script keys were not removed.'
 [DeskPagesHost]::Available=$true
 Assert (Try-DpCachedPreview 1 4 1) 'Fresh native keys were not rediscovered.'
 $main[0].X=55;$layout.Icons=$main;Write-DpAtomicXml (Join-Path $temp 'page-1.xml') $layout
 Assert (-not (Try-DpCachedPreview 1 4 1)) 'An edited checkpoint reused its old resident image.'
 Assert ((Get-DpSnapshotKey 1 $main) -ne $key) 'A main icon edit did not change its page key.'
 Write-Host 'PASS: Stable cached previews plus 50 secondary disconnect/reconnect cycles retain primary scene and keys; source/target keys agree; real main edits and native invalidation are honored. Renderer stub only, no Windows latency claim.'
}finally{Remove-Item -LiteralPath $temp -Recurse -Force}
