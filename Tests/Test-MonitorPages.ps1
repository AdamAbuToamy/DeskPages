$ErrorActionPreference='Stop'
$base=Split-Path $PSScriptRoot -Parent
Add-Type -Path (Join-Path $base 'Native/DeskPagesPageNames.cs')
Add-Type @'
using System;
public sealed class MonitorSpacing {public int X=80,Y=100;}
public delegate void MonitorRestore(string[] ids,int[] xs,int[] ys);
public static class DeskPagesLayout {
 public static MonitorRestore Apply;
 public static MonitorSpacing Spacing(object desktop){return new MonitorSpacing();}
 public static int Restore(object desktop,string[] ids,int[] xs,int[] ys){Apply(ids,xs,ys);return ids.Length;}
}
public static class OriginalDesktopReader {public static object[] Icons;public static object[] Read(object desktop){return Icons;}}
public static class DeskPagesAnimation {public static bool IsVisible {get{return false;}}}
'@
. (Join-Path $base 'StateEngine.ps1');. (Join-Path $base 'Workspace.ps1');. (Join-Path $base 'WorkspaceCommands.ps1');. (Join-Path $base 'Layouts.ps1');. (Join-Path $base 'NewItemPlacement.ps1')
function Assert($Value,$Why){if(-not $Value){throw $Why}}
function Get-DpMonitorGeometry {return $script:screens}
function Get-DpDisplaySignal {return $script:signal}
function Publish-DpWorkspacePreferences {}
function Connect-DpDesktop {}
function Write-DpLog {param($Text)}
function Get-DpWorkspaceIdentity {param($Path) return [IO.File]::GetCreationTimeUtc($Path).Ticks.ToString()}
function Save-DpVerifiedSourceLayout {param($Page)}
function Resume-DpCommittedPage {
 $page=[int]$script:DpState.Current
 [OriginalDesktopReader]::Icons=@($script:allIcons | Where-Object {$entry=@($script:DpState.Items | Where-Object Path -eq $_.Key)[0];Test-DpFileVisible $entry $page} | ForEach-Object {Copy-DpData $_})
 Restore-DpPageLayout $page
}
$temp=Join-Path ([IO.Path]::GetTempPath()) ('dp-monitor-pages-'+[guid]::NewGuid().ToString('N'))
try{
 $script:DpDesktop=Join-Path $temp 'Desktop';$script:DpPublic=Join-Path $temp 'Public';$script:DpStore=Join-Path $temp 'Store';$script:DpLayoutStore=Join-Path $temp 'Layouts';$script:DpManifest=Join-Path $script:DpStore 'pages.xml'
 New-Item -ItemType Directory -Force $script:DpDesktop,$script:DpPublic,$script:DpStore,$script:DpLayoutStore | Out-Null
 $items=@();$script:allIcons=@()
 foreach($page in 1..4){$path=Join-Path $script:DpDesktop "page-$page.txt";[IO.File]::WriteAllText($path,"unchanged payload $page");$items+=@([PSCustomObject]@{Path=$path;Page=$page});$script:allIcons+=@([PSCustomObject]@{Id=[string]$page;Key=$path;Name="page-$page";X=17;Y=12})}
 $pin=Join-Path $script:DpDesktop 'pinned.txt';[IO.File]::WriteAllText($pin,'shared payload');$items+=@([PSCustomObject]@{Path=$pin;Page=2;Pinned=$true});$script:allIcons+=@([PSCustomObject]@{Id='pin';Key=$pin;Name='pin';X=111;Y=122})
 $script:DpState=[PSCustomObject]@{Desktop=$script:DpDesktop;Current=1;Pages=@(1,2,3,4);PageCount=4;Items=$items;SystemIconBackup=@()};Initialize-DpWorkspace
 $main=[PSCustomObject]@{Device='DISPLAY1';Identity='panel-main';Left=0;Top=0;Right=1920;Bottom=1080;Primary=$true}
 $right=[PSCustomObject]@{Device='DISPLAY2';Identity='panel-right';Left=1920;Top=0;Right=3200;Bottom=1024;Primary=$false}
 $left=[PSCustomObject]@{Device='DISPLAY3';Identity='panel-left';Left=-1280;Top=100;Right=0;Bottom=1124;Primary=$false}
 $script:screens=@($main,$right,$left);$script:signal=[long[]]@(0,0,2000,0)
 [DeskPagesLayout]::Apply=[MonitorRestore]{param($ids,$xs,$ys) for($i=0;$i -lt $ids.Length;$i++){foreach($icon in [OriginalDesktopReader]::Icons){if($icon.Id -eq $ids[$i]){$icon.X=$xs[$i];$icon.Y=$ys[$i]}}}}
 foreach($page in 1..4){Write-DpAtomicXml (Join-Path $script:DpLayoutStore "page-$page.xml") ([PSCustomObject]@{Page=$page;Icons=@($script:allIcons | Where-Object {$_.Id -eq [string]$page -or $_.Id -eq 'pin'})})}
 Save-DpState
 Invoke-DpWorkspaceRequest 'monitor-pages-save' @('True',"panel-right`t2","panel-left`t3")
 Assert ((@(Get-DpNavigationPages) -join ',') -eq '1,4') 'Fixed pages remain in main navigation.'
 Assert ([OriginalDesktopReader]::Icons.Count -eq 4) 'Composed view lost a page or duplicated a pin.'
 $two=@([OriginalDesktopReader]::Icons | Where-Object Id -eq '2')[0];$three=@([OriginalDesktopReader]::Icons | Where-Object Id -eq '3')[0]
 Assert ($two.X -eq 1937 -and $three.X -eq -1263 -and $three.Y -eq 112) 'Pages were not translated to their individual monitors.'
 Assert (@([OriginalDesktopReader]::Icons | Where-Object Id -eq 'pin')[0].X -eq 111) 'Pinned icon left Primary.'
 # Save a deliberate secondary-screen move and keep every other layout independent.
 Sync-DpMonitorProfile
 $two=@([OriginalDesktopReader]::Icons | Where-Object Id -eq '2')[0];$two.X=2400
 Save-DpPageLayout 1
 Assert (@([OriginalDesktopReader]::Icons | Where-Object Id -eq '2')[0].X -eq 1937) 'Secondary in-bounds hotplug drift overwrote canonical positions.'
 $script:signal[1]++;$two=@([OriginalDesktopReader]::Icons | Where-Object Id -eq '2')[0];$two.X=2000;Save-DpPageLayout 1
 $saved=Import-Clixml (Join-Path $script:DpLayoutStore 'page-2.xml')
 Assert (@($saved.Icons | Where-Object Id -eq '2')[0].X -eq 80) 'Secondary position was not saved in canonical page coordinates.'
 Assert (-not @((Import-Clixml (Join-Path $script:DpLayoutStore 'page-1.xml')).Icons | Where-Object Id -in @('2','3')).Count) 'Combined Explorer view contaminated main page.'
 # Automatic secondary reflow is repaired even after the primary fence releases.
 $secondaryPath=Join-Path $script:DpLayoutStore 'page-2.xml';$secondaryHash=(Get-FileHash $secondaryPath).Hash
 function Get-DpDesktopEditPoint {return $script:editPoint}
 $script:signal[1]++;$script:editPoint=[PSCustomObject]@{Revision=$script:signal[1];DisplayRevision=$script:signal[0];X=100;Y=100}
 $mainIcon=@([OriginalDesktopReader]::Icons | Where-Object Id -eq '1')[0];$mainIcon.X=333;$two.X=2500
 Save-DpPageLayout 1
 Assert (@([OriginalDesktopReader]::Icons | Where-Object Id -eq '2')[0].X -eq 2000 -and @([OriginalDesktopReader]::Icons | Where-Object Id -eq '1')[0].X -eq 333) 'A primary edit legitimized automatic secondary reflow or discarded the primary edit.'
 Assert ((Get-FileHash $secondaryPath).Hash -eq $secondaryHash) 'Ordinary main-screen save rewrote the fixed page.'
 $script:signal[1]++;$script:editPoint=[PSCustomObject]@{Revision=$script:signal[1];DisplayRevision=$script:signal[0];X=2010;Y=12}
 $two=@([OriginalDesktopReader]::Icons | Where-Object Id -eq '2')[0];$two.X=2010;Save-DpPageLayout 1
 Assert (@((Import-Clixml $secondaryPath).Icons | Where-Object Id -eq '2')[0].X -eq 90) 'A real secondary-screen edit was rejected.'
 $script:signal[1]++;$script:editPoint=[PSCustomObject]@{Revision=$script:signal[1];DisplayRevision=$script:signal[0];X=2000;Y=12};$two.X=2000;Save-DpPageLayout 1
 $two.X=2700;Save-DpPageLayout 1
 Assert (@([OriginalDesktopReader]::Icons | Where-Object Id -eq '2')[0].X -eq 2000) 'A consumed secondary gesture allowed a later automatic rearrangement.'
 $two=@([OriginalDesktopReader]::Icons | Where-Object Id -eq '2')[0]
 $clone=(Get-Command Copy-DpIcon).ScriptBlock
 function Copy-DpIcon {throw 'Unchanged composed checkpoint must not clone or serialize icons.'}
 try{Save-DpMonitorPageLayouts 1 @([OriginalDesktopReader]::Icons)}finally{Set-Item Function:Copy-DpIcon $clone}
 Write-Host 'PASS: main edits cannot overwrite secondary coordinates; actual secondary edits save once; later automatic reflow is repaired; unchanged checkpoint avoids per-icon copying.'
 # A display change during canonical splitting must not write any page.
 $beforeHashes=@{};foreach($file in Get-ChildItem $script:DpLayoutStore -Filter 'page-*.xml'){$beforeHashes[$file.FullName]=(Get-FileHash $file.FullName).Hash}
 $signalReader=(Get-Command Get-DpDisplaySignal).ScriptBlock;$script:signalReads=0
 function Get-DpDisplaySignal {$script:signalReads++;if($script:signalReads -eq 2){$script:signal[0]++};return $script:signal}
 $two.X=2010;$rejected=$false
 try{Save-DpMonitorPageLayouts 1 @([OriginalDesktopReader]::Icons)}catch{$rejected=$_.ToString().Contains('layout saving is blocked.')}finally{Set-Item Function:Get-DpDisplaySignal $signalReader}
 Assert $rejected 'Composed checkpoint accepted a mid-capture display change.'
 foreach($path in $beforeHashes.Keys){Assert ((Get-FileHash $path).Hash -eq $beforeHashes[$path]) 'Mid-capture display change rewrote a page layout.'}
 $two.X=2000
 $script:DpState.Current=4;Resume-DpCommittedPage
 Assert (@([OriginalDesktopReader]::Icons | Where-Object Id -eq '2')[0].X -eq 2000 -and -not @([OriginalDesktopReader]::Icons | Where-Object Id -eq '1').Count) 'Main navigation moved a fixed page or retained previous membership.'
 $script:screens=@($main,$left)
 Assert ((@(Get-DpNavigationPages) -join ',') -eq '1,2,4') 'Detached page did not return to main navigation.'
 $script:screens=@($main,$right,$left);$right.Device='DISPLAY9'
 Assert ((@(Get-DpNavigationPages) -join ',') -eq '1,4') 'Display renumbering lost persistent assignment.'
 # Choosing the current page for a secondary display selects another main page.
 Invoke-DpWorkspaceRequest 'monitor-pages-save' @('True',"panel-right`t4","panel-left`t3")
 Assert ($script:DpState.Current -eq 1 -and (@(Get-DpNavigationPages) -join ',') -eq '1,2') 'Assigning Current did not select an available primary page.'
 # Replaying a partially written canonical checkpoint is complete and idempotent.
 $layout=Import-Clixml (Join-Path $script:DpLayoutStore 'page-4.xml');$layout.Icons[0].X=199
 Write-DpAtomicXml (Join-Path $script:DpWorkspaceRoot 'monitor-layout-checkpoint.xml') ([PSCustomObject]@{Revision=$script:DpState.Revision;Layouts=@($layout)})
 Repair-DpMonitorLayoutCheckpoint;Repair-DpMonitorLayoutCheckpoint
 Assert ((Import-Clixml (Join-Path $script:DpLayoutStore 'page-4.xml')).Icons[0].X -eq 199) 'Interrupted multi-page checkpoint was not replayed.'
 Invoke-DpWorkspaceRequest 'monitor-pages-save' @('False',"panel-right`t4","panel-left`t3")
 Assert ((@(Get-DpNavigationPages) -join ',') -eq '1,2,3,4' -and [OriginalDesktopReader]::Icons.Count -eq 2) 'Turning off fixed pages did not restore ordinary primary-only behavior.'
 foreach($page in 1..4){Assert ([IO.File]::ReadAllText($items[$page-1].Path) -eq "unchanged payload $page") 'Monitor pages changed file contents.'}
 Write-Host 'PASS: production three-screen composition, independent canonical saves, pinned icons on Primary, fixed-page navigation, unplug/replug/renumber, current-page reassignment, interrupted checkpoint replay and mode disable. Simulated geometry/Explorer; physical display acceptance remains separate.'
}finally{Remove-Item -LiteralPath $temp -Recurse -Force}
