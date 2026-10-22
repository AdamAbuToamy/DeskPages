$ErrorActionPreference='Stop'
$base=Split-Path $PSScriptRoot -Parent
Add-Type -Path (Join-Path $base 'Native/DeskPagesPageNames.cs')
Add-Type @'
public sealed class ScopeSpacing {public int X=80,Y=100;}
public delegate void ScopeRestore(string[] ids,int[] xs,int[] ys);
public static class DeskPagesLayout {
 public static ScopeRestore Apply;
 public static ScopeSpacing Spacing(object value){return new ScopeSpacing();}
 public static int Restore(object value,string[] ids,int[] xs,int[] ys){Apply(ids,xs,ys);return ids.Length;}
}
public static class OriginalDesktopReader {public static object[] Icons;public static object[] Read(object value){return Icons;}}
public static class DeskPagesAnimation {public static bool IsVisible {get{return false;}}}
'@
. (Join-Path $base 'StateEngine.ps1');. (Join-Path $base 'Workspace.ps1');. (Join-Path $base 'WorkspaceCommands.ps1');. (Join-Path $base 'NewItemPlacement.ps1')
function Assert($Pass,$Reason){if(-not $Pass){throw $Reason}}
function Get-DpMonitorGeometry {return $script:screens}
function Get-DpWorkspaceIdentity {param([string]$Path) return [IO.File]::GetCreationTimeUtc($Path).Ticks.ToString()}
function Save-DpVerifiedSourceLayout {param($Page)}
function Publish-DpWorkspacePreferences {}
function Write-DpLog {param($Message)}
function Resume-DpCommittedPage {
 $script:visible=@($script:DpState.Items | Where-Object {Test-DpItemVisible $_ ([int]$script:DpState.Current)} | ForEach-Object {$_.Path})
}
$temp=Join-Path ([IO.Path]::GetTempPath()) ('DeskPages-primary-'+[guid]::NewGuid().ToString('N'))
try{
 $script:DpDesktop=Join-Path $temp 'Desktop';$script:DpPublic=Join-Path $temp 'Public';$script:DpStore=Join-Path $temp 'Store';$script:DpLayoutStore=Join-Path $temp 'Layouts';$script:DpManifest=Join-Path $script:DpStore 'pages.xml'
 New-Item -ItemType Directory -Force $script:DpDesktop,$script:DpPublic,$script:DpStore,$script:DpLayoutStore | Out-Null
 $a=Join-Path $script:DpDesktop 'a.txt';$b=Join-Path $script:DpPublic 'b.txt'
 foreach($f in @($a,$b)){[IO.File]::WriteAllText($f,'unchanged bytes and path')}
 $hash=(Get-FileHash $b).Hash
 $script:DpState=[PSCustomObject]@{Desktop=$script:DpDesktop;Current=1;Pages=@(1,2,3);PageCount=3;Items=@([PSCustomObject]@{Path=$a;Page=1},[PSCustomObject]@{Path=$b;Page=2;Pinned=$true});SystemIconBackup=@()}
 Initialize-DpWorkspace
 Assert ($script:DpState.Workspace.PrimaryOnly) 'Upgrade must enable primary confinement.'
 $w=$script:DpState.Workspace
 $w.SecondaryIcons=@([PSCustomObject]@{Key=$a;Name='a';X=2017;Y=2})
 Assert (-not(Test-DpSecondaryKey $a)) 'Legacy displaced icon became shared across pages.'
 Assert (-not(Test-DpItemVisible $script:DpState.Items[0] 2)) 'Hotplug altered page visibility.'
 Assert (Test-DpItemVisible $script:DpState.Items[1] 3) 'Permanent user pin was lost.'
 $count=0
 foreach($scale in @(1,1.25,1.5,2,2.5)){
  foreach($side in @(-1,1)){
   $width=[int](1920*$scale);$height=[int](1080*$scale)
   # Explorer client origin shifts when the new display is to the left or above.
   $offset=0;if($side -lt 0){$offset=$width}
   $main=[PSCustomObject]@{Device='main';Left=$offset;Top=200;Right=($offset+$width);Bottom=($height+200);Primary=$true}
   $second=[PSCustomObject]@{Device='second';Left=($offset+$side*$width);Top=0;Right=($offset+$side*$width+$width);Bottom=$height;Primary=$false}
   $script:screens=@($main,$second)
   foreach($page in @(1,2,3)){
    $icons=@([PSCustomObject]@{Key=$a;Name='a';X=($second.Left+17);Y=2},[PSCustomObject]@{Key=$b;Name='b';X=($second.Left+100);Y=102})
    $layout=[PSCustomObject]@{Page=$page;Icons=$icons}
    $converted=@(Convert-DpMonitorLayouts @($layout) @($second) $script:screens 80 100)
    foreach($icon in $converted[0].Icons){Assert ($icon.X -ge $main.Left -and $icon.Y -ge $main.Top -and $icon.X+80 -le $main.Right -and $icon.Y+100 -le $main.Bottom) 'Conversion spilled onto the attached display.'}
    $scoped=@(Get-DpScopedLayoutIcons $icons @())
    foreach($icon in $scoped){Assert ($icon.X -ge $main.Left -and $icon.X+80 -le $main.Right -and $icon.Y -ge $main.Top -and $icon.Y+100 -le $main.Bottom) 'Restore retained secondary coordinates.'}
    Write-DpAtomicXml (Join-Path $script:DpLayoutStore "page-$page.xml") $layout
    $count++
   }
   # Real profile journal, hotplug, unplug, replug, changed primary display.
   $w.MonitorKey='';Sync-DpMonitorProfile
   $key=$script:DpState.Workspace.MonitorKey
   foreach($layout in (Get-DpWorkspaceSnapshot).Layouts){foreach($icon in $layout.Icons){Assert ($icon.X -ge $main.Left -and $icon.X+80 -le $main.Right) 'Committed profile escaped Primary.'}}
   $script:screens=@($main);Sync-DpMonitorProfile
   $script:screens=@($main,$second);Sync-DpMonitorProfile
   Assert ($script:DpState.Workspace.MonitorKey -eq $key) 'Replug did not select the same profile.'
   $main.Primary=$false;$second.Primary=$true;Sync-DpMonitorProfile
   foreach($layout in (Get-DpWorkspaceSnapshot).Layouts){foreach($icon in $layout.Icons){Assert ($icon.X -ge $second.Left -and $icon.X+80 -le $second.Right -and $icon.Y -ge $second.Top -and $icon.Y+100 -le $second.Bottom) 'Changed primary was ignored.'}}
   $main.Primary=$true;$second.Primary=$false
   $w=$script:DpState.Workspace
  }
 }
 # Full Primary cannot fall back to an empty secondary screen.
 $script:screens=@([PSCustomObject]@{Device='tiny';Left=0;Top=0;Right=80;Bottom=100;Primary=$true},[PSCustomObject]@{Device='big';Left=80;Top=0;Right=2000;Bottom=1080;Primary=$false})
 $before=(Get-FileHash (Join-Path $script:DpLayoutStore 'page-1.xml')).Hash
 $failed=$false;try{Convert-DpMonitorLayouts @([PSCustomObject]@{Page=1;Icons=@([PSCustomObject]@{X=0;Y=0},[PSCustomObject]@{X=100;Y=0})}) @() $script:screens 80 100}catch{$failed=$true}
 Assert ($failed -and (Get-FileHash (Join-Path $script:DpLayoutStore 'page-1.xml')).Hash -eq $before) 'Overflow used secondary or overwrote saved layout.'
 Assert ((Get-FileHash $b).Hash -eq $hash -and $script:DpState.Items[1].Page -eq 2 -and $script:DpState.Items[1].Pinned) 'Hotplug mutated bytes, membership or permanent pins.'
 # Exercise the real autosave guard against a delayed Explorer rearrangement.
 . (Join-Path $base 'Layouts.ps1')
 function Connect-DpDesktop {}
 $script:screens=@([PSCustomObject]@{Device='main';Left=0;Top=0;Right=1920;Bottom=1080;Primary=$true},[PSCustomObject]@{Device='second';Left=1920;Top=0;Right=3840;Bottom=1080;Primary=$false})
 $script:DpState.Current=1;$script:DpState.Workspace.MonitorKey=Get-DpMonitorKey
 $icons=@([PSCustomObject]@{Id='a';Key=$a;Name='a';X=17;Y=2},[PSCustomObject]@{Id='b';Key=$b;Name='b';X=200;Y=202})
 Write-DpAtomicXml (Join-Path $script:DpLayoutStore 'page-1.xml') ([PSCustomObject]@{Page=1;Icons=$icons})
 [OriginalDesktopReader]::Icons=Copy-DpData $icons
 [OriginalDesktopReader]::Icons[0].X=2100
 [DeskPagesLayout]::Apply=[ScopeRestore]{param($ids,$xs,$ys)
   for($i=0;$i -lt $ids.Length;$i++){foreach($icon in [OriginalDesktopReader]::Icons){if($icon.Id -eq $ids[$i]){$icon.X=$xs[$i];$icon.Y=$ys[$i]}}}
 }
 Save-DpPageLayout 1
 $saved=Import-Clixml (Join-Path $script:DpLayoutStore 'page-1.xml')
 Assert (@($saved.Icons | Where-Object {$_.X -ge 1920}).Count -eq 0) 'Autosave persisted delayed secondary spillover.'
 Assert (@($saved.Icons | Where-Object {$_.Key -eq $a})[0].X -eq 17) 'Displaced icon did not recover its saved coordinate.'
 Assert (@($saved.Icons | Where-Object {$_.Key -eq $b})[0].X -eq 200) 'Repair moved an unaffected primary icon.'
 $hashBefore=(Get-FileHash (Join-Path $script:DpLayoutStore 'page-1.xml')).Hash
 [OriginalDesktopReader]::Icons[0].X=2200
 [DeskPagesLayout]::Apply=[ScopeRestore]{param($ids,$xs,$ys)}
 $failed=$false;try{Save-DpPageLayout 1}catch{$failed=$true}
 Assert ($failed -and (Get-FileHash (Join-Path $script:DpLayoutStore 'page-1.xml')).Hash -eq $hashBefore) 'Explorer rejection overwrote trusted layout.'
 Write-Host 'PASS: real autosave repairs delayed spillover, retains trusted/unaffected coordinates, and preserves saved bytes if Explorer rejects placement.'
 Write-Host "PASS: $count primary page cases, five scales, both sides/shifted origins, real profile commits, unplug/replug/primary changes, legacy metadata, overflow preservation and user pins. Explorer adapter simulated."
}finally{Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue}
