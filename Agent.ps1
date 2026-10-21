[CmdletBinding()]
param([switch]$Recover, [switch]$SelfTest, [switch]$BuildNative, [switch]$Logon, [switch]$Resume, [switch]$AnimationTest, [switch]$InputTest, [switch]$EnableAnimation, [string]$StartupToken, [switch]$QuickCheck, [string]$InstallReadyToken)
$ErrorActionPreference='Stop'
$startupClock=[Diagnostics.Stopwatch]::StartNew()
$script:hostReady=$false
$script:DpSuspended=$false
$script:base=$PSScriptRoot
$script:DpStore=Join-Path $env:LOCALAPPDATA 'DeskPages\HiddenPages'
$script:DpManifest=Join-Path $script:DpStore 'pages.xml'
$script:DpLayoutStore=Join-Path $env:LOCALAPPDATA 'DeskPages\PageLayouts'
$script:DpDesktop=[Environment]::GetFolderPath('Desktop')
$script:DpPublic=[Environment]::GetFolderPath('CommonDesktopDirectory')
$script:DpIconKey='Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel'
$script:DpSystemIds=@(
    '{20D04FE0-3AEA-1069-A2D8-08002B30309D}',
    '{645FF040-5081-101B-9F08-00AA002F954E}',
    '{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}'
)
function Write-DpLog {
    param([string]$Message)
    try {
        New-Item -ItemType Directory -Force $script:DpStore | Out-Null
        Add-Content -LiteralPath (Join-Path $script:DpStore 'agent.log') `
            -Value ("{0:o} {1}" -f [DateTime]::UtcNow,$Message) -Encoding UTF8
    } catch { } # Diagnostics must not turn a successful checkpoint into failure.
}
function Connect-DpDesktop {
    $shell=New-Object -ComObject Shell.Application
    $windows=$shell.Windows()
    [object]$location=0; [object]$empty=$null; [int]$hwnd=0
    $desktop=$windows.FindWindowSW([ref]$location,[ref]$empty,8,[ref]$hwnd,1)
    if ($null -eq $desktop) {throw 'Explorer desktop is not available. Wait and retry.'}
    $script:originalDesktop=$desktop
}

$mutex=$null; $owned=$false
$transcript=$false
try {
    if ($SelfTest) {
        $reports=Join-Path $env:LOCALAPPDATA 'DeskPages\Verification'
        New-Item -ItemType Directory -Force $reports | Out-Null
        $transcriptPath=Join-Path $reports ('check-'+[DateTime]::UtcNow.Ticks+'.txt')
        Start-Transcript -LiteralPath $transcriptPath | Out-Null
        $transcript=$true
        Write-Host ('Check report: '+$transcriptPath)
    }
    if (-not $SelfTest -and -not $BuildNative -and -not $AnimationTest -and -not $InputTest -and -not $QuickCheck) {
        . (Join-Path $PSScriptRoot 'Maintenance.ps1')
        if (-not (Test-DpAdministrator)) {
            if ($Logon) {throw 'Logon task must use Highest privileges. Startup stopped before changing any files.'}
            $arguments='-NoProfile -STA -ExecutionPolicy Bypass -WindowStyle Hidden -File "'+$PSCommandPath+'"'
            if ($Recover) {$arguments+=' -Recover'}
            if ($Resume) {$arguments+=' -Resume'}
            if ($EnableAnimation) {$arguments+=' -EnableAnimation'}
            $child=Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -Verb RunAs -ArgumentList $arguments -Wait -PassThru
            exit $child.ExitCode
        }
    }
    if($SelfTest){. (Join-Path $script:base 'PackageIntegrity.ps1');Assert-DpPackage -Root $script:base -Version ([Version]'0.8.16.0')}
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    $references=@(
        [Windows.Forms.Form].Assembly.Location,
        [Drawing.Bitmap].Assembly.Location,
        [System.Linq.Enumerable].Assembly.Location,
        [ComponentModel.Component].Assembly.Location
    ) | Select-Object -Unique
    $nativeFiles=@(Get-ChildItem (Join-Path $script:base 'Native') -Filter '*.cs' -File | Sort-Object Name)
    $fingerprint=($nativeFiles | ForEach-Object {(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}) -join ':'
    $binary=Join-Path $script:base 'Bin\DeskPagesNative.dll'
    $stamp=Join-Path $script:base 'Bin\sources.sha256'
    if ($BuildNative) {
        New-Item -ItemType Directory -Force (Split-Path $binary) | Out-Null
        if (Test-Path -LiteralPath $binary) {Remove-Item -LiteralPath $binary -Force}
        Add-Type -Path @($nativeFiles.FullName) -ReferencedAssemblies $references -OutputAssembly $binary
        Add-Type -Path $binary
        Set-Content -LiteralPath $stamp -Value $fingerprint -Encoding ASCII
    }
    elseif ((Test-Path -LiteralPath $binary) -and
        (Test-Path -LiteralPath $stamp) -and (Get-Content -LiteralPath $stamp -Raw).Trim() -eq $fingerprint) {
        Add-Type -Path $binary
    }
    else {Add-Type -Path @($nativeFiles.FullName) -ReferencedAssemblies $references}

    [DeskPagesHost]::ConfigureRenderer($script:base)
    [DeskPagesHost]::ConfigureDpi()
    if($QuickCheck){[DeskPagesAnimation]::VerifyNativeAvailable();[DeskPagesAnimation]::Shutdown();exit 0}
    if($InstallReadyToken -and $InstallReadyToken -notmatch '^[a-f0-9]{32}$'){throw 'Invalid installer readiness token.'}
    if($StartupToken -and $StartupToken -notmatch '^[a-f0-9]{32}$'){throw 'Invalid startup guard token.'}
    if(-not $StartupToken -and -not $SelfTest -and -not $BuildNative -and -not $InputTest -and -not $AnimationTest){[DeskPagesStartupGate]::RecoverOwnedGate()}
    if ($InputTest) {
        $report=[DeskPagesHost]::RunInputProbe()
        $os=Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
        $report+="`r`nWindows build: $($os.CurrentBuildNumber).$($os.UBR) | $($os.DisplayVersion)"
        $prefs=Join-Path $env:LOCALAPPDATA 'DeskPages\HiddenPages\input-settings.txt'
        if(Test-Path -LiteralPath $prefs) {
            $lines=@(Get-Content -LiteralPath $prefs)
            $report+="`r`nSaved settings: Swipe=$($lines[0]); Animation=$($lines[3]); Legacy overlay preference (ignored)=$($lines[4])"
        }
        $report+="`r`nScreens: $([Windows.Forms.Screen]::AllScreens.Count)"

        $folder=Join-Path $env:LOCALAPPDATA 'DeskPages\Verification'
        New-Item -ItemType Directory -Path $folder -Force | Out-Null
        $path=Join-Path $folder 'input-probe.txt'
        Set-Content -LiteralPath $path -Value $report -Encoding UTF8
        Start-Process notepad.exe -ArgumentList ('"'+$path+'"')
        exit 0
    }
    if ($AnimationTest) {
        & (Join-Path $script:base 'Tests\Test-AnimationWindows.ps1')
        if (-not $?) {throw 'Animation benchmark failed.'}
        exit 0
    }
    if ($SelfTest -or $BuildNative) {
        Write-Host ('Read-only startup shell API available. Icons hidden: '+[DeskPagesStartupGate]::Hidden)
        if($BuildNative){
            Write-Host 'SKIP: GPU initialization is a device check, not a source-build prerequisite. Use Simulate-Monitors.cmd in an interactive Windows session.'
        }else{
            [DeskPagesAnimation]::VerifyNativeAvailable()
            Write-Host ('Native renderer: '+[DeskPagesAnimation]::Metrics)
            [DeskPagesAnimation]::Shutdown()
        }
        foreach ($file in Get-ChildItem -LiteralPath $script:base -Filter '*.ps1' -Recurse -File) {
            $tokens=$null; $errors=$null
            $null=[Management.Automation.Language.Parser]::ParseFile(
                $file.FullName,[ref]$tokens,[ref]$errors)
            if ($errors.Count) {throw ($errors | Out-String)}
        }
        # Pure identity migration tests; no Explorer, attributes or registry mutation.
        . (Join-Path $script:base 'Test-Identity.ps1')
        . (Join-Path $script:base 'Test-PageSafety.ps1')
        & (Join-Path $script:base 'Tests\Run-CoreTests.ps1')
        if (-not $?) {throw 'Core crash tests failed.'}
        & (Join-Path $script:base 'Tests\Test-RefreshPolicy.ps1')
        if (-not $?) {throw 'Refresh-policy tests failed.'}
        & (Join-Path $script:base 'Tests\Test-AnimationSequence.ps1')
        if (-not $?) {throw 'Animation sequence tests failed.'}
        & (Join-Path $script:base 'Tests\Test-Startup.ps1')
        if (-not $?) {throw 'Startup/shutdown regression tests failed.'}
        & (Join-Path $script:base 'Tests\Test-LayoutGuards.ps1')
        if (-not $?) {throw 'Layout guard tests failed.'}
        & (Join-Path $script:base 'Tests\Test-RestartReport.ps1')
        if (-not $?) {throw 'Restart-report tests failed.'}
        & (Join-Path $script:base 'Tests\Test-Gesture.ps1')
        if (-not $?) {throw 'Gesture tests failed.'}
        & (Join-Path $script:base 'Tests\Test-TouchpadHid.ps1')
        if (-not $?) {throw 'Precision Touchpad HID framing/recognition tests failed.'}
        & (Join-Path $script:base 'Tests\Test-VisibilityWindows.ps1')
        if (-not $?) {throw 'Windows attribute tests failed.'}
        & (Join-Path $script:base 'Tests\Test-MainStartup.ps1')
        if (-not $?) {throw 'Main page startup tests failed.'}
        & (Join-Path $script:base 'Tests\Test-BootPlan.ps1')
        if (-not $?) {throw 'Native boot plan tests failed.'}
        & (Join-Path $script:base 'Tests\Test-InstallLocation.ps1')
        if (-not $?) {throw 'Install location tests failed.'}
        & (Join-Path $script:base 'Tests\Test-InstallationReadiness.ps1')
        if (-not $?) {throw 'Installation readiness tests failed.'}
        foreach($test in @('Test-MultiplePages.ps1','Test-DesktopMerge.ps1','Test-UpgradeCheckpoint.ps1','Test-RecoveryFlow.ps1')){
            & (Join-Path $script:base ('Tests\'+$test))
            if(-not $?){throw ('Regression failed: '+$test)}
        }
        Write-Host 'PASS: compilation, identity, deletion, crash-recovery, gesture and Windows-attribute fixture tests.' -ForegroundColor Green
        Write-Host 'Windows interaction and touchpad/animation still require a device test.'
        exit 0
    }

    $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $mutex=[Threading.Mutex]::new($false,"Local\DeskPages-Agent-$sid")
    try {$owned=$mutex.WaitOne(0)} catch [Threading.AbandonedMutexException] {$owned=$true}
    if (-not $owned) {
        if ($Recover) {
            New-Item -ItemType Directory -Force $script:DpStore | Out-Null
            Set-Content -LiteralPath (Join-Path $script:DpStore 'recovery.request') -Value 'recover'
            Write-Host 'Recovery requested from the running DeskPages agent.'
            exit 0
        }
        throw 'DeskPages is already running. Use its notification icon.'
    }

    New-Item -ItemType Directory -Force $script:DpStore,$script:DpLayoutStore | Out-Null
    . (Join-Path $script:base 'StateEngine.ps1')
    . (Join-Path $script:base 'FirstRun.ps1')
    Save-DpSafetySnapshot
    $firstRun=-not (Test-Path -LiteralPath $script:DpManifest)
    if (Test-Path -LiteralPath $script:DpManifest) {
        $script:DpState=Read-DpManifest $script:DpManifest
    } else {$script:DpState=New-DpInitialState -Desktop $script:DpDesktop -PublicDesktop $script:DpPublic -SystemBackup @(Get-DpInitialSystemBackup)}
    if ($script:DpState.Desktop -ne $script:DpDesktop) {throw 'Saved desktop does not match this account.'}
    if (-not $script:DpState.PSObject.Properties['Pages']) {
        $script:DpState | Add-Member -NotePropertyName Pages -NotePropertyValue @(
            1..([Math]::Max(2,[int]$script:DpState.PageCount)))
    }
    $pages=@($script:DpState.Pages | ForEach-Object {[int]$_})
    if (1 -notin $pages -or $pages.Count -ne @($pages | Select-Object -Unique).Count -or
        @($pages | Where-Object {$_ -lt 1 -or $_ -gt 99}).Count) {throw 'Invalid page list.'}
    if ([int]$script:DpState.Current -notin $pages) {throw 'Invalid active page.'}
    foreach ($entry in $script:DpState.Items) {
        $parent=[IO.Path]::GetDirectoryName([IO.Path]::GetFullPath([string]$entry.Path))
        if ($parent -notin @($script:DpDesktop,$script:DpPublic)) {throw 'Saved item outside desktop folders. Nothing changed.'}
        if ([int]$entry.Page -notin $pages) {throw 'Saved item refers to an unknown page.'}
    }
    foreach ($entry in $script:DpState.SystemIconBackup) {
        if ($entry.Id -notin $script:DpSystemIds) {throw 'Unexpected system-icon setting in saved data.'}
    }
    . (Join-Path $script:base 'Visibility.ps1')
    . (Join-Path $script:base 'Layouts.ps1')
    . (Join-Path $script:base 'SnapshotCache.ps1')
    . (Join-Path $script:base 'NewItemPlacement.ps1')
    . (Join-Path $script:base 'DesktopMerge.ps1')
    . (Join-Path $script:base 'PageSafety.ps1')
    . (Join-Path $script:base 'BootPlan.ps1')
    . (Join-Path $script:base 'Workspace.ps1')
    . (Join-Path $script:base 'WorkspaceCommands.ps1')
    Initialize-DpWorkspace
    Assert-DpWorkspaceState $script:DpState
    Repair-DpWorkspaceTransaction
    # Record startup state; never overwrite the known original layout.
    $checkpoint=Join-Path $script:DpStore ('checkpoint-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.xml')
    Export-Clixml -InputObject $script:DpState -LiteralPath $checkpoint
    $connected=$false
    for ($attempt=0; $attempt -lt $(if($Logon){60}else{1}); $attempt++) {
        try {Connect-DpDesktop; $connected=$true; break}
        catch {if(-not $Logon){throw};Start-Sleep -Milliseconds 250}
    }
    if (-not $connected) {throw 'Explorer was not ready at logon; automatic task restart can retry.'}
    # Repair stale live FWF_NOICONS only when the global Shell choice is visible.
    # No change to Auto arrange, grid, sort, membership or saved coordinates.
    try{
        Write-DpLog ('Desktop view before visibility sync: '+[DeskPagesLayout]::DescribeView($script:originalDesktop))
        if([DeskPagesLayout]::RepairVisibleState($script:originalDesktop,[DeskPagesStartupGate]::Hidden)){
            Write-DpLog 'Repaired stale Explorer FWF_NOICONS; all other folder flags verified unchanged.'
        }
        Write-DpLog ('Desktop view after visibility sync: '+[DeskPagesLayout]::DescribeView($script:originalDesktop))
    }catch{Write-DpLog ('Live Explorer visibility sync unavailable: '+[string]$_)}
    if($firstRun){Save-DpPageLayout 1}
    if ($Recover) {
        Recover-DpDesktop
        Write-Host 'Managed files shown. Session suspended; page membership and saved layouts were preserved.' -ForegroundColor Green
        exit 0
    }
    $suspension=Join-Path $script:DpStore 'suspended.xml'
    if ((Test-Path -LiteralPath $suspension) -and -not $Resume) {
        throw 'DeskPages was stopped for recovery. Files are left visible; run Run-Admin.cmd to resume the saved page explicitly.'
    }
    if (-not (Test-Path -LiteralPath $script:DpManifest)) {Save-DpState}
    Sync-DpMonitorProfile
    Restore-DpStartupPage -Main:$Logon
    if($StartupToken){
        $readyIcons=@(Confirm-DpAnimationView -Page ([int]$script:DpState.Current))
        [DeskPagesStartupGate]::PaintOriginalView($readyIcons.Count)
    }
    if (Test-Path -LiteralPath $suspension) {[IO.File]::Delete($suspension)}
    # First page layouts are captured only after a verified committed view exists.
    if (-not (Test-Path -LiteralPath (Join-Path $script:DpLayoutStore ("page-$($script:DpState.Current).xml")))) {
        Save-DpPageLayout ([int]$script:DpState.Current)
    }
    . (Join-Path $script:base 'RestartVerification.ps1')
    Complete-DpRestartCheck
    $script:DpState.PageCount=@($script:DpState.Pages).Count
    Save-DpState

    function Invoke-DpAction {
        param([int]$Code)
        try {
            if ($script:DpSuspended -and $Code -lt 9000) {throw 'Session suspended; switching is blocked.'}
            if($Code -lt 9000 -or $Code -in @(9000,9002,9101,9102,9300)){Stop-DpNewItemPlacement}
            $pages=@(Get-DpActivePages | ForEach-Object {[int]$_})
            $current=[int]$script:DpState.Current
            $index=[Array]::IndexOf([int[]]$pages,$current)
            if ($Code -eq -1 -or $Code -eq 1) {
                $target=$pages[($index+$Code+$pages.Count)%$pages.Count]
                Set-DeskPage $target -Direction $Code
            }
            elseif ($Code -gt 1000 -and $Code -le 1099) {Set-DeskPage ($Code-1000)}
            elseif ($Code -in @(100,101,200) -and $script:DpState.PSObject.Properties['Workspace']) {
                if($Code -eq 100){$null=Invoke-DpWorkspaceRequest 'add' @('New page')}
                elseif($Code -eq 101){if([Windows.Forms.MessageBox]::Show('Delete this empty page?','DeskPages',[Windows.Forms.MessageBoxButtons]::YesNo) -eq [Windows.Forms.DialogResult]::Yes){$null=Invoke-DpWorkspaceRequest 'delete' @([string]$current)}}
                else{Connect-DpDesktop;$paths=@($script:originalDesktop.Document.SelectedItems() | ForEach-Object {[string]$_.Path});Set-DpWorkspaceItems $paths $pages[($index+1)%$pages.Count]}
            }
            elseif ($Code -eq 100) {
                $id=1+[int]($pages | Measure-Object -Maximum).Maximum
                if ($id -gt 99) {throw 'Maximum page ID reached.'}
                $script:DpState.Pages=@($pages)+@($id)
                $script:DpState.PageCount=@($script:DpState.Pages).Count
                Save-DpState
                Set-DeskPage $id
            }
            elseif ($Code -eq 101) {
                if ($current -eq 1) {throw 'Page 1 is the recovery page and cannot be deleted.'}
                Set-DpFilePage $current
                $blockers=@(Get-DpPageDeleteBlockers -Items @($script:DpState.Items) -Page $current)
                if ($blockers.Count) {throw "This page contains $($blockers.Count) file(s) or folder(s). Move them to another page before deleting it."}
                $answer=[Windows.Forms.MessageBox]::Show(
                    'Delete this empty page? No files will be deleted.',
                    'Delete page',[Windows.Forms.MessageBoxButtons]::YesNo)
                if ($answer -ne [Windows.Forms.DialogResult]::Yes) {return}
                Set-DpFilePage $current
                if (@(Get-DpPageDeleteBlockers -Items @($script:DpState.Items) -Page $current).Count) {throw 'A file appeared on this page. Deletion was cancelled.'}
                Set-DeskPage 1
                if (@(Get-DpPageDeleteBlockers -Items @($script:DpState.Items) -Page $current).Count) {throw 'Page content changed during the switch. Deletion cancelled.'}
                $script:DpState.Items=@($script:DpState.Items | Where-Object {$_.Page -ne $current})
                $script:DpState.Pages=@($pages | Where-Object {$_ -ne $current})
                $script:DpState.PageCount=@($script:DpState.Pages).Count
                Save-DpState
                $layout=Join-Path $script:DpLayoutStore "page-$current.xml"
                if (Test-Path -LiteralPath $layout) {
                    Move-Item -LiteralPath $layout -Destination ($layout+'.archived-'+(Get-Date -Format 'yyyyMMddHHmmss'))
                }
            }
            elseif ($Code -eq 200) {
                Connect-DpDesktop
                $selection=@($script:originalDesktop.Document.SelectedItems())
                $paths=@($selection | ForEach-Object {$_.Path})
                $entries=@($script:DpState.Items | Where-Object {$_.Path -in $paths})
                if (-not $entries.Count) {throw 'Select physical files/shortcuts on the desktop first. System icons stay on page 1.'}
                $target=$pages[($index+1)%$pages.Count]
                if ($target -eq $current) {throw 'Add another page first.'}
                Save-DpPageLayout $current
                try {
                    foreach ($entry in $entries) {$entry.Page=$target}
                    Save-DpState; Set-DpFilePage $current
                    Refresh-DpDesktop; Restore-DpPageLayout $current
                } catch {
                    foreach ($entry in $entries) {$entry.Page=$current}
                    Save-DpState; Set-DpFilePage $current
                    Refresh-DpDesktop; Restore-DpPageLayout $current
                    throw
                }
            }
            elseif ($Code -eq 9000 -or $Code -eq 9002) {
                try {Recover-DpDesktop}
                finally {[DeskPagesHost]::RequestExit()}
            }
            elseif ($Code -eq 9001) {
                [Windows.Forms.MessageBox]::Show([DeskPagesHost]::Diagnostics(),'DeskPages input') | Out-Null
            }
            elseif ($Code -eq 9700) {[DeskPagesWorkspaceApi]::Open()}
            elseif ($Code -eq 9701) {Sync-DpMonitorProfile}
            elseif ($Code -eq 9600) { . (Join-Path $script:base 'EdgeDrag.ps1'); Invoke-DpEdgeRequest }
            elseif ($Code -eq 9401) {
                Start-Process explorer.exe -ArgumentList ('"'+[Environment]::GetFolderPath('Desktop')+'"')
            }
            elseif ($Code -eq 9400) {
                if($script:DpSuspended){throw 'Recover the suspended session before exporting.'}
                Save-DpVerifiedSourceLayout $current; Save-DpState
                . (Join-Path $script:base 'ExportSettings.ps1')
                $picker=[Windows.Forms.SaveFileDialog]::new()
                try{
                    $picker.Filter='DeskPages settings (*.zip)|*.zip';$picker.FileName='DeskPages-settings-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.zip'
                    if($picker.ShowDialog() -eq [Windows.Forms.DialogResult]::OK){
                        Export-DpSettings -Store $script:DpStore -Layouts $script:DpLayoutStore -Destination $picker.FileName
                        [Windows.Forms.MessageBox]::Show('Settings exported. This backup contains file paths and layouts, but does not include your files.','DeskPages') | Out-Null
                    }
                }finally{$picker.Dispose()}
            }
            elseif ($Code -eq 9003) {
                Start-Process notepad.exe -ArgumentList ('"'+(Join-Path $script:DpStore 'agent.log')+'"')
            }
            elseif ($Code -eq 9100 -or $Code -eq 9300) {
                if(-not $script:DpSuspended){
                    Sync-DpMonitorProfile
                    $layoutBefore=$null;$layoutPath=Join-Path $script:DpLayoutStore ("page-$current.xml");$layoutHash=''
                    if(-not $script:DpEdgeDrag -and -not $script:DpNewItemContext){$layoutBefore=Get-DpWorkspaceSnapshot;if(Test-Path -LiteralPath $layoutPath){$layoutHash=(Get-FileHash -LiteralPath $layoutPath).Hash}}
                    if(-not [DeskPagesLayout]::IsLabelEditing() -and -not [DeskPagesHost]::CreationInputBusy -and -not $script:DpNewItemContext -and -not $script:DpEdgeDrag){
                        if([int]$script:DpState.Workspace.Inbox -gt 0 -and [int]$script:DpState.Workspace.Inbox -ne $current){
                            $known=@{};foreach($entry in $script:DpState.Items){$known[$entry.Path]=$true}
                            $new=@(Get-ChildItem -LiteralPath $script:DpDesktop,$script:DpPublic -Force | Where-Object {-not $known.ContainsKey($_.FullName) -and ([int]$_.Attributes -band 1030) -eq 0 -and $_.Name -ine 'desktop.ini'})
                            if($new.Count){Save-DpVerifiedSourceLayout $current;Set-DpFilePage -Page $current -Commit:$false;Refresh-DpDesktop;Restore-DpPageLayout $current}
                        }
                    }
                    Save-DpPageLayout $current
                    if($null -ne $layoutBefore -and (Test-Path -LiteralPath $layoutPath) -and (Get-FileHash -LiteralPath $layoutPath).Hash -ne $layoutHash){Save-DpAppliedWorkspaceOperation $layoutBefore 'Arrange desktop icons'}
                    if($Code -eq 9300){Save-DpState;Write-DpMainBootPlan;Write-DpLog 'Verified session-end checkpoint and main-page boot plan saved.'}
                    [DeskPagesHost]::CheckpointSaved()
                }
            }
            elseif ($Code -eq 9200) {
                Save-DpPageLayout $current
                Start-DpRestartCheck
                [Windows.Forms.MessageBox]::Show('Restart baseline saved. Restart Windows yourself, then open the restart report from the tray menu.','DeskPages') | Out-Null
            }
            elseif ($Code -eq 9201) {
                $report=Join-Path $script:DpStore 'restart-report.txt'
                if (-not (Test-Path -LiteralPath $report)) {throw 'No completed restart check yet. Choose Prepare restart check first.'}
                Start-Process notepad.exe -ArgumentList ('"'+$report+'"')
            }
            elseif ($Code -eq 9101) {
                Save-DpVerifiedSourceLayout $current; Save-DpState
                [DeskPagesHost]::RequestExit()
            }
            elseif ($Code -eq 9750) {Warm-DpCurrentSnapshot}
            elseif ($Code -eq 9500) {Start-DpNewItemPlacement}
            elseif ($Code -eq 9501) {Update-DpNewItemPlacement}
            elseif ($Code -eq 9102) {
                Connect-DpDesktop
                try{$null=[DeskPagesLayout]::RepairVisibleState($script:originalDesktop,[DeskPagesStartupGate]::Hidden)}catch{Write-DpLog ('Live view sync after Explorer restart: '+[string]$_)}
                Set-DpFilePage $current; Set-DpSystemIcons $current
                Refresh-DpDesktop; Restore-DpPageLayout $current
            }
        } catch {
            Write-DpLog ([string]$_)
            if($Code -in @(9500,9501)){Stop-DpNewItemPlacement}
            elseif ($Code -ne 9100 -and $Code -ne 9300 -and $Code -ne 9750) {[DeskPagesHost]::ShowError([string]$_)}
        } finally {
            if($Code -notin @(9500,9501,9750)){[DeskPagesHost]::UpdatePages([int[]]@(Get-DpActivePages),[int]$script:DpState.Current)}
        }
    }
    $script:hostReady=$true
    Write-DpLog ('Desktop prepared; host readiness pending. Preparation took '+$startupClock.ElapsedMilliseconds+' ms; page='+$script:DpState.Current+'. No file contents are logged.')
    # Host calls this delegate on this same STA thread/runspace, never on hook threads.
    $action=[Action[int]]{param($code) Invoke-DpAction $code}
    if($EnableAnimation){[DeskPagesHost]::EnableAnimationOnLaunch()}
    if($StartupToken){[DeskPagesHost]::SetStartupToken($StartupToken)}
    if($InstallReadyToken){[DeskPagesHost]::SetInstallReadyToken($InstallReadyToken)}
    Publish-DpWorkspacePreferences
    [DeskPagesWorkspaceApi]::Request=[DeskPagesWorkspaceRequest]{param($command,$arguments) [string[]]@(Invoke-DpWorkspaceRequest $command $arguments)}
    try{
        $dragToken=[guid]::NewGuid().ToString('N')
        $broker=Join-Path $script:base 'DeskPagesDrag.exe'
        if(Test-Path -LiteralPath $broker -PathType Leaf){
            # Use the Shell object obtained from the original Explorer desktop:
            # the normal-user OLE broker must not inherit this elevated token.
            $script:originalDesktop.Document.Application.ShellExecute($broker,$dragToken,$script:base,'open',0)
            [DeskPagesHost]::ConfigureDragSession($dragToken)
        }
    }catch{[DeskPagesHost]::ConfigureDragSession($null);Write-DpLog ('Drag interaction helper could not start: '+[string]$_)}
    [DeskPagesHost]::Run($action)
}
catch {
    $failureText=[string]$_
    if (-not $SelfTest -and -not $BuildNative -and -not $AnimationTest -and -not $InputTest) {
        try {
            . (Join-Path $script:base 'RestartVerification.ps1')
            Write-DpRestartFailure $failureText
        } catch { }
    }
    Write-DpLog $failureText
    if ($SelfTest -or $BuildNative -or $Logon -or $AnimationTest -or $InputTest -or $QuickCheck) {Write-Error $failureText -ErrorAction Continue; exit 1}
    $message=$failureText
    Add-Type -AssemblyName System.Windows.Forms
    [Windows.Forms.MessageBox]::Show($message,'DeskPages startup') | Out-Null
    exit 1
}
finally {
    if ($transcript) {Stop-Transcript | Out-Null}
    if ($owned -and $null -ne $mutex) {$mutex.ReleaseMutex()}
    if ($null -ne $mutex) {$mutex.Dispose()}
}
