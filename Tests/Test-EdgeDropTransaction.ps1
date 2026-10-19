param([switch]$Child)
$ErrorActionPreference='Stop'
if(-not $Child){$exe=[Diagnostics.Process]::GetCurrentProcess().MainModule.FileName;& $exe -NoProfile -File $PSCommandPath -Child;if($LASTEXITCODE -ne 0){throw 'Edge drop transaction fixture failed'};return}
$root=Split-Path $PSScriptRoot -Parent
Add-Type -Path (Join-Path $root 'Native/DeskPagesDragPlacement.cs')
. (Join-Path $root 'StateEngine.ps1')
. (Join-Path $root 'NewItemPlacement.ps1')
. (Join-Path $root 'EdgeDrag.ps1')
Add-Type @'
using System;using System.Collections.Generic;
public sealed class DragIcon{public string Id,Key,Name;public int X,Y;}
public sealed class DragSpacing{public int X=80,Y=100;}
public static class OriginalDesktopReader {public static List<DragIcon> Icons=new List<DragIcon>();public static DragIcon[] Read(object d){return Icons.ToArray();}}
public static class DeskPagesLayout{
 public static bool Grid=true;public static bool SnapToGrid(object d){return Grid;}
 public static bool Snap;public static int Fault;
 public static int[] ContextGeometry(int x,int y){return new[]{x,y,0,0,800,600};}
 public static DragSpacing Spacing(object d){return new DragSpacing();}
 public static int Restore(object d,string[] ids,int[] xs,int[] ys){for(int i=0;i<ids.Length;i++)foreach(var icon in OriginalDesktopReader.Icons)if(icon.Id==ids[i]){icon.X=Snap?(int)Math.Round((xs[i]-17)/80.0)*80+17:xs[i];icon.Y=Snap?(int)Math.Round((ys[i]-2)/100.0)*100+2:ys[i];}if(Fault==1)foreach(var icon in OriginalDesktopReader.Icons)if(Array.IndexOf(ids,icon.Id)>=0)icon.X+=160;
 if(Fault==2)foreach(var icon in OriginalDesktopReader.Icons)if(icon.Name=="existing.txt")icon.X+=80;
 if(Fault==3)foreach(var icon in OriginalDesktopReader.Icons)if(Array.IndexOf(ids,icon.Id)>=0){icon.X=360;icon.Y=300;}
 if(Fault==4)foreach(var icon in OriginalDesktopReader.Icons)if(icon.Id==ids[1])icon.X+=20;
 return ids.Length;}
}
'@
$temp=Join-Path ([IO.Path]::GetTempPath()) ('dp-edge-drop-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $temp 'Desktop'),(Join-Path $temp 'Public'),(Join-Path $temp 'Store'),(Join-Path $temp 'Layouts') -Force | Out-Null
try{
 $script:DpDesktop=Join-Path $temp 'Desktop';$script:DpPublic=Join-Path $temp 'Public';$script:DpStore=Join-Path $temp 'Store';$script:DpLayoutStore=Join-Path $temp 'Layouts';$script:DpManifest=Join-Path $script:DpStore 'pages.xml';$script:hostReady=$false;$script:DpSuspended=$false
 $one=Join-Path $script:DpDesktop 'one.txt';$two=Join-Path $script:DpDesktop 'two.lnk';$old=Join-Path $script:DpDesktop 'existing.txt'
 foreach($file in @($one,$two,$old)){[IO.File]::WriteAllText($file,'test contents')}
 $before=@{};foreach($file in @($one,$two,$old)){$before[$file]=(Get-FileHash $file).Hash}
 function Get-DpDragIdentity {param($Path) if($script:wrongIdentity){return 'changed'};return 'identity:'+$Path}
 function Write-DpLog {param($Message)}
 function Save-DpVerifiedSourceLayout {param($Page)}
 function Save-DpSafetySnapshot {$script:safetyCalled=$true}
 function Set-DpFilePage {param($Page,$Commit,$RegisterNew)
  [OriginalDesktopReader]::Icons.Clear()
  foreach($item in $script:DpState.Items | Where-Object {$_.Page -eq $Page}){$icon=[DragIcon]::new();$icon.Id=$item.Path;$icon.Key=$item.Path;$icon.Name=[IO.Path]::GetFileName($item.Path);$icon.X=0;$icon.Y=0;if($script:arming){if($item.Path -eq $one){$icon.X=17;$icon.Y=2};if($item.Path -eq $two){$icon.X=177;$icon.Y=102}};if($item.Path -eq $old){$icon.X=80;$icon.Y=100};[OriginalDesktopReader]::Icons.Add($icon)}
 }
 function Refresh-DpDesktop {if($script:failRefresh){throw 'fixture refresh failure'}}
 function Restore-DpPageLayout {param($Page)}
 function Assert-DpPageView {param($Page,$Icons)
  $expected=@($script:DpState.Items | Where-Object {$_.Page -eq $Page} | ForEach-Object {$_.Path} | Sort-Object);$actual=@($Icons | ForEach-Object {$_.Key} | Sort-Object)
  if(($expected -join '|') -ne ($actual -join '|')){throw 'Mixed fixture view'}
 }
 function Resume-DpCommittedPage {Set-DpFilePage -Page $script:DpState.Current;Remove-Item -LiteralPath (Join-Path $script:DpStore 'transition.xml') -ErrorAction SilentlyContinue}
 function Connect-DpDesktop {}
 function Set-DeskPage {param($Page) $script:DpState.Current=$Page;Set-DpFilePage -Page $Page}
 $script:arming=$true
 $script:DpState=[PSCustomObject]@{Current=1;Pages=@(1,2,3);Items=@([PSCustomObject]@{Path=$one;Page=1},[PSCustomObject]@{Path=$two;Page=1},[PSCustomObject]@{Path=$old;Page=2})}
 $document=[PSCustomObject]@{}
 $document | Add-Member -MemberType ScriptMethod -Name SelectedItems -Value {return @([PSCustomObject]@{Path=(Join-Path $script:DpDesktop 'one.txt')},[PSCustomObject]@{Path=(Join-Path $script:DpDesktop 'two.lnk')})}
 $script:originalDesktop=[PSCustomObject]@{Document=$document}
 Start-DpEdgeDrag -Paths @($two,$one) -GrabX 200 -GrabY 130
 Move-DpEdgeDragPage -Direction 1;Move-DpEdgeDragPage -Direction 1
 $a=@($script:DpEdgeDrag.Files | Where-Object Path -eq $one)[0];$b=@($script:DpEdgeDrag.Files | Where-Object Path -eq $two)[0]
 if($script:DpEdgeDrag.Current -ne 3 -or $script:DpEdgeDrag.GrabX -ne 200 -or $script:DpEdgeDrag.GrabY -ne 130 -or $a.SourceX -ne 17 -or $a.SourceY -ne 2 -or $b.SourceX -ne 177 -or $b.SourceY -ne 102){throw 'Source offsets/grab point lost across navigation'}
 if(@($script:DpState.Items | Where-Object Page -eq 1).Count -ne 2){throw 'Holding selection committed membership before physical drop'}
 Write-Host 'PASS: production arm captures keyed native source positions and grab point; retained across two page switches without committing membership.'
 $script:arming=$false
 foreach($case in @('Success','SnappedSuccess','FreePlacementSuccess','WrongPosition','StationaryMoved','CollapsedCells','DistortedGroup','FailedRefresh','IdentityChanged','UnmanagedPath')){
  [DeskPagesLayout]::Grid=$case -ne 'FreePlacementSuccess'
  [DeskPagesLayout]::Snap=$case -eq 'SnappedSuccess'
  [DeskPagesLayout]::Fault=0
  if($case -eq 'WrongPosition'){[DeskPagesLayout]::Fault=1}
  if($case -eq 'StationaryMoved'){[DeskPagesLayout]::Fault=2}
  if($case -eq 'CollapsedCells'){[DeskPagesLayout]::Fault=3}
  if($case -eq 'DistortedGroup'){[DeskPagesLayout]::Fault=4}
  $script:wrongIdentity=$false;$script:failRefresh=$false;$script:safetyCalled=$false
  $script:DpState=[PSCustomObject]@{Current=2;Pages=@(1,2,3);Items=@([PSCustomObject]@{Path=$one;Page=1},[PSCustomObject]@{Path=$two;Page=1},[PSCustomObject]@{Path=$old;Page=2})}
  Save-DpState;$script:DpEdgeDrag=[PSCustomObject]@{Source=1;Current=2;GrabX=40;GrabY=16;Files=@([PSCustomObject]@{Path=$one;SourceX=0;SourceY=0;Identity=('identity:'+$one)},[PSCustomObject]@{Path=$two;SourceX=80;SourceY=0;Identity=('identity:'+$two)})}
  if($case -eq 'FailedRefresh'){$script:failRefresh=$true}
  if($case -eq 'IdentityChanged'){$script:wrongIdentity=$true}
  if($case -eq 'UnmanagedPath'){$script:DpEdgeDrag.Files[0].Path=Join-Path $temp 'outside.txt'}
  $failed=$false;try{Complete-DpEdgeDrop -X 420 -Y 320}catch{$failed=$true}
  $saved=Read-DpManifest $script:DpManifest
  if($case -in @('Success','SnappedSuccess','FreePlacementSuccess')){
   if($failed -or -not $script:safetyCalled -or @($saved.Items | Where-Object {$_.Page -eq 2}).Count -ne 3){throw 'Drop did not commit target membership'}
   $icons=@([OriginalDesktopReader]::Read($null));$existing=@($icons | Where-Object {$_.Key -eq $old})[0]
   if($existing.X -ne 80 -or $existing.Y -ne 100){throw 'Existing target icon moved'}
   if($case -eq 'FreePlacementSuccess'){$first=@($icons | Where-Object {$_.Key -eq $one})[0];if($first.X -ne 380 -or $first.Y -ne 304){throw 'Free placement was forced onto a grid'}}
   $snapshot=Import-Clixml -LiteralPath (Join-Path $script:DpLayoutStore 'page-2.xml')
   foreach($icon in $icons){$stored=@($snapshot.Icons | Where-Object {$_.Key -eq $icon.Key})[0];if($stored.X -ne $icon.X -or $stored.Y -ne $icon.Y){throw 'Requested positions saved instead of actual Explorer positions'}}
   if(Test-Path (Join-Path $script:DpStore 'transition.xml')){throw 'Committed drop left journal'}
  }else{if(-not $failed -or @($saved.Items | Where-Object {$_.Page -eq 1}).Count -ne 2){throw 'Failed drop changed saved membership'}}
  foreach($file in $before.Keys){if(-not (Test-Path $file) -or (Get-FileHash $file).Hash -ne $before[$file]){throw 'Drop modified/moved user file bytes'}}
  Write-Host ('PASS: edge drop transaction '+$case)
 }
 Write-Host 'Mocked Explorer adapter: selection membership, same file paths/bytes, collision-aware placement, old icon positions and durable rollback verified; no actual Windows drag executed.'
}finally{Remove-Item -LiteralPath $temp -Recurse -Force}
