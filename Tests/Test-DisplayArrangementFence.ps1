$ErrorActionPreference='Stop'
$base=Split-Path $PSScriptRoot -Parent
Add-Type -Path (Join-Path $base 'Native/DeskPagesPageNames.cs')
Add-Type @'
using System;
public sealed class FenceSpacing {public int X=80,Y=100;}
public delegate void FenceRestore(string[] ids,int[] xs,int[] ys);
public static class DeskPagesLayout {
 public static FenceRestore Apply;
 public static FenceSpacing Spacing(object desktop){return new FenceSpacing();}
 public static int Restore(object desktop,string[] ids,int[] xs,int[] ys){Apply(ids,xs,ys);return ids.Length;}
}
public static class OriginalDesktopReader {
 public static object[] Icons;public static Action OnRead;
 public static object[] Read(object desktop){if(OnRead!=null)OnRead();return Icons;}
}
'@
. (Join-Path $base 'StateEngine.ps1');. (Join-Path $base 'Workspace.ps1');. (Join-Path $base 'WorkspaceCommands.ps1');. (Join-Path $base 'NewItemPlacement.ps1');. (Join-Path $base 'Layouts.ps1')
function Assert($Pass,$Why){if(-not $Pass){throw $Why}}
function Get-DpMonitorGeometry {return $script:screens}
function Get-DpDisplaySignal {return $script:signal}
function Set-DpMonitorWatch {param($Enabled) $script:watching=$Enabled}
function Get-DpWorkspaceIdentity {param($Path) return [IO.File]::GetCreationTimeUtc($Path).Ticks.ToString()}
function Publish-DpWorkspacePreferences {}
function Connect-DpDesktop {}
function Write-DpLog {param($Message)}
function Resume-DpCommittedPage {Restore-DpPageLayout ([int]$script:DpState.Current)}
$temp=Join-Path ([IO.Path]::GetTempPath()) ('dp-display-fence-'+[guid]::NewGuid().ToString('N'))
try{
 $script:DpDesktop=Join-Path $temp 'Desktop';$script:DpPublic=Join-Path $temp 'Public';$script:DpStore=Join-Path $temp 'Store';$script:DpLayoutStore=Join-Path $temp 'Layouts';$script:DpManifest=Join-Path $script:DpStore 'pages.xml'
 New-Item -ItemType Directory -Force $script:DpDesktop,$script:DpPublic,$script:DpStore,$script:DpLayoutStore | Out-Null
 $a=Join-Path $script:DpDesktop 'a.txt';$b=Join-Path $script:DpDesktop 'b.txt'
 foreach($f in @($a,$b)){[IO.File]::WriteAllText($f,'unchanged content')}
 $hash=(Get-FileHash $a).Hash
 $script:DpState=[PSCustomObject]@{Desktop=$script:DpDesktop;Current=1;Pages=@(1,2);PageCount=2;Items=@([PSCustomObject]@{Path=$a;Page=1},[PSCustomObject]@{Path=$b;Page=1});SystemIconBackup=@()}
 Initialize-DpWorkspace
 $main=[PSCustomObject]@{Device='main';Left=0;Top=0;Right=1920;Bottom=1080;Primary=$true}
 $right=[PSCustomObject]@{Device='other';Left=1920;Top=0;Right=3840;Bottom=1080;Primary=$false}
 $script:screens=@($main);$script:signal=[long[]]@(0,0,2000,0)
 $icons=@([PSCustomObject]@{Id='a';Key=$a;Name='a';X=17;Y=12},[PSCustomObject]@{Id='b';Key=$b;Name='b';X=60;Y=62})
 [OriginalDesktopReader]::Icons=Copy-DpData $icons
 [DeskPagesLayout]::Apply=[FenceRestore]{param($ids,$xs,$ys)
  for($i=0;$i -lt $ids.Length;$i++){foreach($icon in [OriginalDesktopReader]::Icons){if($icon.Id -eq $ids[$i]){$icon.X=$xs[$i];$icon.Y=$ys[$i]}}}
 }
 $path=Join-Path $script:DpLayoutStore 'page-1.xml'
 Write-DpAtomicXml $path ([PSCustomObject]@{Page=1;Icons=$icons})
 Write-DpAtomicXml (Join-Path $script:DpLayoutStore 'page-2.xml') ([PSCustomObject]@{Page=2;Icons=@()})
 Sync-DpMonitorProfile
 $singleKey=$script:DpState.Workspace.MonitorKey
 # A previous dual-monitor profile is deliberately stale.
 $script:screens=@($main,$right);$dualKey=Get-DpMonitorKey
 $stale=Copy-DpData ([PSCustomObject]@{Page=1;Icons=$icons});$stale.Icons[0].X=700
 Write-DpAtomicXml (Join-Path $script:DpWorkspaceRoot "Profiles/$dualKey.xml") ([PSCustomObject]@{Screens=$script:screens;Layouts=@($stale)})
 $script:signal[0]++;Sync-DpMonitorProfile
 Assert ([OriginalDesktopReader]::Icons[0].X -eq 17 -and $script:watching) 'Replug loaded a stale topology profile.'
 # Delayed Explorer reflows remain inside Primary, including after a long idle.
 foreach($age in @(2000,10000,600000)){
  $script:signal[2]=$age;[OriginalDesktopReader]::Icons[0].X=400;[OriginalDesktopReader]::Icons[1].X=800
  Save-DpPageLayout 1
  $saved=Import-Clixml $path
  Assert ($saved.Icons[0].X -eq 17 -and $saved.Icons[1].X -eq 60) 'Delayed in-bounds reflow replaced trusted coordinates.'
 }
 Write-Host 'PASS: stale dual-display profile ignored; delayed in-bounds Explorer rearrangements never become the saved user layout, even long after hotplug.'
 # A completed deliberate drag releases the fence and saves the new positions.
 $script:signal[1]++;[OriginalDesktopReader]::Icons[0].X=333;[OriginalDesktopReader]::Icons[0].Y=444
 Save-DpPageLayout 1
 Assert ((Import-Clixml $path).Icons[0].X -eq 333 -and -not $script:watching) 'Protection prevented intentional manual arrangement.'
 $script:screens=@($main);$script:signal[0]++;Sync-DpMonitorProfile
 Assert ([OriginalDesktopReader]::Icons[0].X -eq 333) 'Unplug reverted to a stale single-monitor profile.'
 # Explorer client coordinates shift when the new monitor is to the left.
 $shifted=[PSCustomObject]@{Device='main';Left=1920;Top=0;Right=3840;Bottom=1080;Primary=$true}
 $left=[PSCustomObject]@{Device='other';Left=0;Top=0;Right=1920;Bottom=1080;Primary=$false}
 $script:screens=@($shifted,$left);$script:signal[0]++;Sync-DpMonitorProfile
 Assert ([OriginalDesktopReader]::Icons[0].X -eq 2253 -and [OriginalDesktopReader]::Icons[0].Y -eq 444) 'Left monitor attachment lost primary-local coordinates.'
 # A fast disconnect/reconnect can have identical final geometry and key.
 $key=$script:DpState.Workspace.MonitorKey;$script:signal[0]+=2
 [OriginalDesktopReader]::Icons[0].X=2500;Sync-DpMonitorProfile
 Assert ($script:DpState.Workspace.MonitorKey -eq $key -and [OriginalDesktopReader]::Icons[0].X -eq 2253) 'Same-key display broadcasts failed to restore the canonical arrangement.'
 Write-Host 'PASS: manual moves retained across unplug/replug and shifted origins; rapid same-key display roundtrips also restore trusted coordinates.'
 $bytes=(Get-FileHash $path).Hash
 $script:signal[3]=1;[OriginalDesktopReader]::Icons[0].X=2700;Save-DpPageLayout 1
 Assert ((Get-FileHash $path).Hash -eq $bytes -and [OriginalDesktopReader]::Icons[0].X -eq 2700) 'An active held drag was saved or disturbed.'
 $script:signal[3]=0;$script:signal[1]++;Save-DpPageLayout 1
 Assert ((Import-Clixml $path).Icons[0].X -eq 2700) 'Released user drag was not saved.'
 # A display event during the actual read invalidates that save, not the file.
 Enable-DpMonitorProtection $script:signal;$bytes=(Get-FileHash $path).Hash
 [OriginalDesktopReader]::OnRead=[Action]{$script:signal[0]++}
 $failed=$false;try{Save-DpPageLayout 1}catch{$failed=$true}finally{[OriginalDesktopReader]::OnRead=$null}
 Assert ($failed -and (Get-FileHash $path).Hash -eq $bytes) 'Mid-capture display event overwrote the trusted file.'
 # This guard must work even after the user had released layout protection.
 $script:DpMonitorProtection=$null;$script:DpMonitorRevision=$script:signal[0];[OriginalDesktopReader]::Icons[0].X=2800
 [OriginalDesktopReader]::OnRead=[Action]{$script:signal[0]++}
 $failed=$false;try{Save-DpPageLayout 1}catch{$failed=$true}finally{[OriginalDesktopReader]::OnRead=$null}
 Assert ($failed -and (Get-FileHash $path).Hash -eq $bytes) 'Unprotected mid-read display event bypassed the capture stamp.'
 Assert ((Get-FileHash $a).Hash -eq $hash) 'Display handling changed file content.'
 Write-Host 'PASS: held drag stays untouched, completed drag saves normally, and mid-read display changes cannot overwrite the canonical layout.'
 Write-Host 'Simulated Explorer/input/display adapters; no physical monitor or touchpad certification.'
}finally{[OriginalDesktopReader]::OnRead=$null;Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue}
