param([ValidateSet('All','CrashWrite')][string]$Mode='All',[string]$Fixture,[string]$Stage,[int]$Rounds=120,[int]$ItemCount=250)
$ErrorActionPreference='Stop'
$base=Split-Path $PSScriptRoot -Parent
Add-Type -Path (Join-Path $base 'Native/DeskPagesPageNames.cs')
Add-Type -Path (Join-Path $base 'Native/DeskPagesDragPlacement.cs')
Add-Type @'
using System;
public sealed class SpacingInfo {public int X=80,Y=100;}
public static class DeskPagesLayout {public static SpacingInfo Spacing(object desktop){return new SpacingInfo();}}
public static class DeskPagesAnimation {public static bool IsVisible {get{return false;}}}
public static class DeskPagesHost {public static void UpdatePages(int[] pages,int current){} public static void InputSettings(){} public static string Diagnostics(){return "fixture";}}
'@
. (Join-Path $base 'StateEngine.ps1');. (Join-Path $base 'Workspace.ps1');. (Join-Path $base 'WorkspaceCommands.ps1');. (Join-Path $base 'NewItemPlacement.ps1');. (Join-Path $base 'PageSafety.ps1')
function Get-DpMonitorGeometry { [PSCustomObject]@{Device='fixture';Left=0;Top=0;Right=3840;Bottom=2160;Primary=$true} }
function Get-DpWorkspaceIdentity {param([string]$Path) if(Test-Path -LiteralPath $Path){return [IO.File]::GetCreationTimeUtc($Path).Ticks.ToString()};return ''}
function Write-DpLog {param($Message)}
function Save-DpVerifiedSourceLayout {param($Page)}
function Publish-DpWorkspacePreferences {}
function Resume-DpCommittedPage {
    Assert-DpWorkspaceState $script:DpState
    $script:fixtureVisible=@($script:DpState.Items | Where-Object {Test-DpItemVisible $_ ([int]$script:DpState.Current)} | ForEach-Object {$_.Path})
}
function Assert([bool]$Pass,[string]$Reason){if(-not $Pass){throw $Reason}}
function Bind-Fixture([string]$Root){
    $script:DpDesktop=Join-Path $Root 'Desktop';$script:DpPublic=Join-Path $Root 'Public';$script:DpStore=Join-Path $Root 'Store';$script:DpLayoutStore=Join-Path $Root 'Layouts';$script:DpManifest=Join-Path $script:DpStore 'pages.xml';$script:hostReady=$false;$script:DpSuspended=$false;$script:DpEdgeDrag=$null
    New-Item -ItemType Directory -Force $script:DpDesktop,$script:DpPublic,$script:DpStore,$script:DpLayoutStore | Out-Null
}
if($Mode -eq 'CrashWrite'){
    Bind-Fixture $Fixture;$script:DpState=Read-DpManifest $script:DpManifest;Initialize-DpWorkspace
    function Invoke-DpFaultPoint {param($At) if($At -eq $Stage){[Environment]::Exit(76)}}
    $edit=New-DpWorkspaceEdit;$edit.After.State.Items[0].Page=2;$edit.After.Layouts[0].Icons[0].X=333
    Invoke-DpWorkspaceCommit 'Crash injection' $edit.After -Before $edit.Before
    throw 'Crash point was not reached.'
}
$temp=Join-Path ([IO.Path]::GetTempPath()) ('DeskPages-workspace-'+[guid]::NewGuid().ToString('N'))
$clock=[Diagnostics.Stopwatch]::StartNew()
try{
    Bind-Fixture $temp
    $items=@(for($i=0;$i -lt $ItemCount;$i++){
        $path=Join-Path $script:DpDesktop ('ملف-קובץ-'+$i+'.txt');[IO.File]::WriteAllText($path,('unchanged payload '+$i+' '+('x'*1024)))
        [PSCustomObject]@{Path=$path;Page=1}
    })
    $hashes=@{};foreach($entry in $items){$hashes[$entry.Path]=(Get-FileHash -LiteralPath $entry.Path).Hash}
    $script:DpState=[PSCustomObject]@{Desktop=$script:DpDesktop;Current=1;PageCount=3;Pages=@(1,2,3);Items=$items;SystemIconBackup=@()}
    Initialize-DpWorkspace;Assert-DpWorkspaceState $script:DpState;Save-DpState
    Write-DpAtomicXml (Join-Path $script:DpLayoutStore 'page-1.xml') ([PSCustomObject]@{Page=1;Icons=@([PSCustomObject]@{Key=$items[0].Path;Name='fixture';X=17;Y=2});SavedAt=[DateTime]::UtcNow})
    $one=$items[0].Path;$two=$items[1].Path
    # Simple settings must not enter the visibility/layout transaction pipeline.
    $layoutHash=(Get-FileHash (Join-Path $script:DpLayoutStore 'page-1.xml')).Hash
    $beforeMembership=(@($script:DpState.Items | ForEach-Object {$_.Path+'|'+$_.Page}) -join "`n")
    $nativeResume=(Get-Command Resume-DpCommittedPage).ScriptBlock
    function Resume-DpCommittedPage {throw 'Preferences must not refresh the desktop.'}
    try{Invoke-DpWorkspaceRequest -Command 'preferences-save' -Arguments @('en','True','True','False','True','False','True','False','0')}finally{Set-Item Function:Resume-DpCommittedPage $nativeResume}
    Assert (-not $script:DpState.Workspace.Input[3] -and $script:DpState.Workspace.Input[0] -and $script:DpState.Workspace.Input[5]) 'Simple toggle argument mapping failed.'
    Assert ((Read-DpManifest $script:DpManifest).Workspace.Input[3] -eq $false) 'Simple preferences were not persisted.'
    Assert ($beforeMembership -eq (@($script:DpState.Items | ForEach-Object {$_.Path+'|'+$_.Page}) -join "`n")) 'Preferences changed membership.'
    Assert ((Get-FileHash (Join-Path $script:DpLayoutStore 'page-1.xml')).Hash -eq $layoutHash) 'Preferences rewrote icon positions.'
    $manifestHash=(Get-FileHash $script:DpManifest).Hash
    $failed=$false;try{Invoke-DpWorkspaceRequest -Command 'preferences-save' -Arguments @('en','True','True','False','True','False','True','False','98')}catch{$failed=$true}
    Assert ($failed -and (Get-FileHash $script:DpManifest).Hash -eq $manifestHash) 'Invalid preference destination changed durable state.'
    $atomic=(Get-Command Write-DpAtomicXml).ScriptBlock
    function Write-DpAtomicXml {param($Path,$Value) throw 'Injected preference write failure.'}
    $failed=$false;try{Invoke-DpWorkspaceRequest -Command 'preferences-save' -Arguments @('en','True','True','True','True','False','True','False','0')}catch{$failed=$true}finally{Set-Item Function:Write-DpAtomicXml $atomic}
    Assert ($failed -and -not $script:DpState.Workspace.Input[3] -and (Get-FileHash $script:DpManifest).Hash -eq $manifestHash) 'Failed atomic save changed memory or disk preferences.'
    Write-Host 'PASS: simple preference mapping/durable save, unchanged membership/layout, no desktop refresh, invalid destination rejection and injected-write rollback.'
    Invoke-DpWorkspaceRequest -Command 'save-layout' -Arguments @()
    $savedLayoutFile=Join-Path $script:DpWorkspaceRoot 'saved-layout.xml';$savedLayoutHash=(Get-FileHash $savedLayoutFile).Hash
    Invoke-DpWorkspaceRequest -Command 'preferences-save' -Arguments @('en','True','True','True','True','False','True','False','0')
    Set-DpWorkspaceItems @($one) 3
    Invoke-DpWorkspaceRequest -Command 'restore-layout' -Arguments @()
    Assert (@($script:DpState.Items | Where-Object {$_.Path -eq $one})[0].Page -eq 1) 'Named layout did not restore page membership.'
    Assert ($script:DpState.Workspace.Input[3]) 'Layout restore reverted current preferences.'
    Assert ((Get-FileHash $savedLayoutFile).Hash -eq $savedLayoutHash) 'Restore overwrote the manual recovery point.'
    $selection=[PSCustomObject]@{Path=$one};$script:selection=@($selection)
    $doc=[PSCustomObject]@{};$doc | Add-Member ScriptMethod SelectedItems {return $script:selection}
    $script:originalDesktop=[PSCustomObject]@{Document=$doc}
    function Connect-DpDesktop {}
    Invoke-DpWorkspaceRequest -Command 'pin-selected' -Arguments @()
    Assert (Test-DpItemVisible @($script:DpState.Items | Where-Object Path -eq $one)[0] 3) 'Selected shortcut did not pin across pages.'
    Invoke-DpWorkspaceRequest -Command 'unpin-selected' -Arguments @()
    Assert (-not(Test-DpItemVisible @($script:DpState.Items | Where-Object Path -eq $one)[0] 3)) 'Selected shortcut failed to unpin.'
    $script:selection=@();$failed=$false;try{Invoke-DpWorkspaceRequest -Command 'pin-selected' -Arguments @()}catch{$failed=$true};Assert $failed 'Empty desktop selection was accepted.'
    Write-Host 'PASS: persistent manual layout save/restore and unchanged recovery point; pin/unpin real fixture Desktop selection without path changes; empty selection rejected.'
    Set-DpWorkspaceItems @($one,$two) 2
    Assert (@($script:DpState.Items | Where-Object Page -eq 2).Count -eq 2) 'Move did not change membership.'
    Invoke-DpWorkspaceUndo
    Assert (@($script:DpState.Items | Where-Object Page -eq 2).Count -eq 0) 'Undo did not restore membership.'
    Invoke-DpWorkspaceUndo -Redo $true
    Assert (@($script:DpState.Items | Where-Object Page -eq 2).Count -eq 2) 'Redo did not restore membership.'
    Set-DpWorkspaceItems @($one) 2 Pin
    Assert (Test-DpItemVisible $script:DpState.Items[0] 3) 'Pinned item missing on another page.'
    Set-DpWorkspaceItems @($one) 2 Unpin
    Assert (-not(Test-DpItemVisible $script:DpState.Items[0] 3)) 'Unpin failed.'
    $null=Invoke-DpWorkspaceRequest 'rename' @('2','مشروعي שלי')
    Assert ((Get-DpWorkspaceName 2) -eq 'مشروعي שלי') 'Unicode name failed.'
    $null=Invoke-DpWorkspaceRequest 'archive' @('2')
    Assert (2 -notin @(Get-DpActivePages)) 'Archived page remains navigable.'
    Assert (@(Get-DpSearchResults 'مشروعي').Count -eq 2) 'Archived page search failed.'
    $null=Invoke-DpWorkspaceRequest 'unarchive' @('2')
    $null=Invoke-DpWorkspaceRequest 'down' @('2')
    Assert ((@(Get-DpActivePages) -join ',') -eq '1,3,2') 'Page order failed.'
    foreach($page in @('1','2')){$failed=$false;try{$null=Invoke-DpWorkspaceRequest 'delete' @($page)}catch{$failed=$true};Assert $failed 'Protected/nonempty page deleted.'}
    $null=Invoke-DpWorkspaceRequest 'lock' @('2');Assert (2 -in @($script:DpState.Workspace.Locked)) 'Lock failed.'
    $null=Invoke-DpWorkspaceRequest 'unlock' @('2')
    $null=Invoke-DpWorkspaceRequest 'settings-save' @('3','ar','True','25','200','70','250')
    Assert ((Get-DpIncomingPage) -eq 3 -and $script:DpState.Workspace.Language -eq 'ar') 'Inbox/preferences failed.'
    $settings=@(Invoke-DpWorkspaceRequest 'settings' @())
    Assert ($settings.Count -eq 16) 'Incomplete settings model.'
    $settings[14]='True';$settings[7]='False';$null=Invoke-DpWorkspaceRequest 'settings-save' $settings
    Assert ('{645FF040-5081-101B-9F08-00AA002F954E}' -in @($script:DpState.Workspace.SystemPinned) -and -not $script:DpState.Workspace.Input[0]) 'Pinned system icon/input preference failed.'
    $null=Invoke-DpWorkspaceRequest 'reorder' @('2','1')
    Assert ((@(Get-DpActivePages) -join ',') -eq '2,1,3') 'Page-card drag reorder failed.'
    $null=Invoke-DpWorkspaceRequest 'rule-add' @('Extension','.txt','3')
    $review=@(Invoke-DpWorkspaceRequest 'rules-preview' @())
    $failed=$false;try{$null=Invoke-DpWorkspaceRequest 'rules-apply' @('stale-preview')}catch{$failed=$true};Assert $failed 'Unreviewed suggestions were applied.'
    $null=Invoke-DpWorkspaceRequest 'rules-apply' $review
    Assert (@($script:DpState.Items | Where-Object Page -ne 3).Count -eq 0) 'Reviewed rules failed.'
    $null=Invoke-DpWorkspaceRequest 'rule-remove' @('0');Assert (@($script:DpState.Workspace.Rules).Count -eq 0) 'Rule removal failed.'
    $layoutBefore=Get-DpWorkspaceSnapshot
    $layout=Import-Clixml (Join-Path $script:DpLayoutStore 'page-3.xml');$layout.Icons[0].X=777
    Write-DpAtomicXml (Join-Path $script:DpLayoutStore 'page-3.xml') $layout
    Save-DpAppliedWorkspaceOperation $layoutBefore 'Arrange desktop icons'
    Invoke-DpWorkspaceUndo
    $restored=Import-Clixml (Join-Path $script:DpLayoutStore 'page-3.xml')
    Assert ([int]$restored.Icons[0].X -ne 777) 'Manual layout undo failed.'
    $saved=Get-DpWorkspaceSnapshot
    $newPath=Join-Path $script:DpDesktop 'new-after-checkpoint.txt';[IO.File]::WriteAllText($newPath,'preserve me')
    $script:DpState.Items=@($script:DpState.Items)+@([PSCustomObject]@{Path=$newPath;Page=3});Save-DpState
    Restore-DpWorkspaceSnapshot $saved
    Assert (@($script:DpState.Items | Where-Object {$_.Path -eq $newPath -and $_.Page -eq 1}).Count -eq 1) 'Recovery lost a newer file.'
    foreach($bad in @('outside','duplicate','archive-main','unknown-page','invalid-threshold')){
        $invalid=Copy-DpData $script:DpState
        switch($bad){'outside'{$invalid.Items[0].Path=Join-Path $temp 'outside.txt'};'duplicate'{$invalid.Items=@($invalid.Items)+@($invalid.Items[0])};'archive-main'{$invalid.Workspace.Archived=@(1)};'unknown-page'{$invalid.Items[0].Page=99};'invalid-threshold'{$invalid.Workspace.EdgeDwell=1}}
        $failed=$false;try{Assert-DpWorkspaceState $invalid}catch{$failed=$true};Assert $failed ('Unsafe state accepted: '+$bad)
    }
    Write-Host 'PASS: move/undo/redo/pin/unpin/Unicode names/archive/order/delete guards/lock/settings/inbox/reviewed rules/new-file-preserving restore/invalid-state rejection.'
    $rng=[Random]::new(8031)
    for($round=0;$round -lt $Rounds;$round++){
        $path=$items[$rng.Next($items.Count)].Path;$page=$rng.Next(1,4)
        Set-DpWorkspaceItems @($path) $page
        if($round%7 -eq 0){Invoke-DpWorkspaceUndo;Invoke-DpWorkspaceUndo -Redo $true}
        Assert-DpWorkspaceState (Read-DpManifest $script:DpManifest)
    }
    foreach($entry in $items){Assert ((Get-FileHash -LiteralPath $entry.Path).Hash -eq $hashes[$entry.Path]) 'Stress changed a file path or contents.'}
    Assert (([IO.File]::ReadAllText($newPath)) -eq 'preserve me') 'Stress changed the newer file.'
    Write-Host "PASS: $Rounds seeded randomized durable operations, $ItemCount real files, periodic undo/redo and content hashes preserved. Mocked Explorer adapter."
    $original=Copy-DpData $script:DpState
    $script:DpState.Items=@(for($i=0;$i -lt 10000;$i++){[PSCustomObject]@{Path=(Join-Path $script:DpDesktop ('stress-'+$i+'.lnk'));Page=1}})
    $watch=[Diagnostics.Stopwatch]::StartNew();$found=@(Get-DpSearchResults 'stress-999');$watch.Stop()
    Assert ($found.Count -eq 11) '10k-item search returned wrong results.'
    $script:DpState=$original
    Write-Host ('PASS: 10,000 metadata entries searched in '+$watch.ElapsedMilliseconds+' ms on this host; not a Windows UI latency measurement.')
    foreach($fault in @('WorkspaceJournal','WorkspaceLayouts','WorkspaceManifest','WorkspaceVisible')){
        $dir=Join-Path $temp $fault;Bind-Fixture $dir
        $file=Join-Path $script:DpDesktop 'sentinel.txt';[IO.File]::WriteAllText($file,'sentinel');$hash=(Get-FileHash $file).Hash
        $script:DpState=[PSCustomObject]@{Desktop=$script:DpDesktop;Current=1;PageCount=2;Pages=@(1,2);Items=@([PSCustomObject]@{Path=$file;Page=1});SystemIconBackup=@()}
        Initialize-DpWorkspace;Save-DpState
        Write-DpAtomicXml (Join-Path $script:DpLayoutStore 'page-1.xml') ([PSCustomObject]@{Page=1;Icons=@([PSCustomObject]@{Key=$file;X=17;Y=2})})
        $exe=[Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
        & $exe -NoProfile -File $PSCommandPath -Mode CrashWrite -Fixture $dir -Stage $fault
        Assert ($LASTEXITCODE -eq 76) ('Crash worker did not stop at '+$fault)
        $script:DpState=Read-DpManifest $script:DpManifest;Initialize-DpWorkspace;Repair-DpWorkspaceTransaction;Resume-DpCommittedPage
        $committed=$fault -in @('WorkspaceManifest','WorkspaceVisible')
        Assert ([int]$script:DpState.Items[0].Page -eq $(if($committed){2}else{1})) 'Wrong membership after process death.'
        $layout=Import-Clixml (Join-Path $script:DpLayoutStore 'page-1.xml')
        Assert ([int]$layout.Icons[0].X -eq $(if($committed){333}else{17})) 'Torn layout after process death.'
        Assert ((Get-FileHash $file).Hash -eq $hash) 'Crash changed sentinel contents.'
        Assert (-not(Test-Path (Join-Path $script:DpWorkspaceRoot 'pending.xml'))) 'Recovery left pending journal.'
        Write-Host ('PASS: real process terminated at '+$fault+'; committed metadata/layout agreement and sentinel content hash verified after recovery.')
    }
    $old=@([PSCustomObject]@{Device='external';Left=-1920;Top=0;Right=0;Bottom=1080})
    $now=@([PSCustomObject]@{Device='laptop';Left=0;Top=0;Right=1280;Bottom=720;Primary=$true})
    $converted=@(Convert-DpMonitorLayouts @([PSCustomObject]@{Page=1;Icons=@([PSCustomObject]@{X=-1800;Y=100},[PSCustomObject]@{X=-1700;Y=200})}) $old $now 80 100)
    foreach($icon in $converted[0].Icons){Assert ($icon.X -ge 0 -and $icon.X -le 1200 -and $icon.Y -ge 0 -and $icon.Y -le 620) 'Display conversion placed icon offscreen.'}
    Assert ([Math]::Abs($converted[0].Icons[0].X-$converted[0].Icons[1].X) -ge 78 -or [Math]::Abs($converted[0].Icons[0].Y-$converted[0].Icons[1].Y) -ge 98) 'Display conversion overlapped icons.'
    Write-Host 'PASS: negative-coordinate external monitor to laptop layout conversion, bounds and non-overlap.'
    $clock.Stop();Write-Host ('WORKSPACE SUITE PASS; elapsed seconds='+[Math]::Round($clock.Elapsed.TotalSeconds,2))
}finally{if(Test-Path -LiteralPath $temp){Remove-Item -LiteralPath $temp -Recurse -Force}}
