$ErrorActionPreference='Stop'
$base=Split-Path $PSScriptRoot -Parent
. (Join-Path $base 'StateEngine.ps1');. (Join-Path $base 'Workspace.ps1');. (Join-Path $base 'SnapshotCache.ps1')
Add-Type @'
using System;
public static class DeskPagesHost {
 public static bool SnapshotCacheEnabled=true;public static string Context;public static int ContextChanges,Previews;
 public static void SnapshotSceneContext(string value){if(Context!=value){Context=value;ContextChanges++;}}
 public static string SavedSnapshotKey(int page){return page+":resident";}
 public static bool TryBeginCachedPreview(int direction,string source,string target){Previews++;return source=="1:resident" && target=="4:resident";}
}
'@
function Assert($Value,$Message){if(-not $Value){throw $Message}}
function Get-DpMonitorKey {throw 'Hot cached navigation must not query native monitor geometry.'}
function Get-DpFixedMonitorPages {param($State,[object[]]$Screens) if(-not $Screens.Count){throw 'Cached preview must reuse the already verified display scope.'};[PSCustomObject]@{Page=2}}
$temp=Join-Path ([IO.Path]::GetTempPath()) ('dp-monitor-cache-'+[guid]::NewGuid().ToString('N'))
try{
 New-Item -ItemType Directory -Force $temp | Out-Null;$script:DpLayoutStore=$temp;$script:DpScopeScreens=@('verified scope')
 $script:DpState=[PSCustomObject]@{Items=@([PSCustomObject]@{Path='fixed';Page=2;Pinned=$false},[PSCustomObject]@{Path='pin';Page=2;Pinned=$true});SystemIconBackup=@();Workspace=[PSCustomObject]@{MonitorPagesEnabled=$true;MonitorKey='verified-topology';SystemPinned=@();SecondaryIcons=@()}}
 $layout=[PSCustomObject]@{Page=2;Icons=@([PSCustomObject]@{Key='fixed';Name='fixed';X=17;Y=12},[PSCustomObject]@{Key='pin';Name='pin';X=100;Y=100})}
 $path=Join-Path $temp 'page-2.xml';Write-DpAtomicXml $path $layout
 $watch=[Diagnostics.Stopwatch]::StartNew()
 foreach($n in 1..100){Assert (Try-DpCachedPreview 1 4 1) 'Resident preview was lost without a desktop change.'}
 Assert ([DeskPagesHost]::Previews -eq 100 -and [DeskPagesHost]::ContextChanges -eq 1) 'Unchanged secondary page repeatedly invalidated the cache.'
 $layout.Icons[1].X=999;Write-DpAtomicXml $path $layout;Assert (Try-DpCachedPreview 1 4 1) 'Main pin metadata blocked a resident preview.'
 Assert ([DeskPagesHost]::ContextChanges -eq 1) 'A main-display pin copy invalidated every page image.'
 $layout.Icons[0].X=80;Write-DpAtomicXml $path $layout;Sync-DpSnapshotScene
 Assert ([DeskPagesHost]::ContextChanges -eq 2) 'An actual fixed-page icon change did not invalidate the composed scene.'
 Write-Host 'PASS: 100 production cached-preview requests retain resident keys and one stable scene without native geometry queries; main-pin copies preserve cache; actual secondary edits invalidate it. Renderer stub only, no Windows latency claim.'
}finally{Remove-Item -LiteralPath $temp -Recurse -Force}
