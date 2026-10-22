param([switch]$Stress,[int]$Rounds=400,[int]$ItemCount=500)
$ErrorActionPreference='Stop'
$base=Split-Path $PSScriptRoot -Parent
$folder=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'DeskPages\Verification'
if(-not[Environment]::GetFolderPath('LocalApplicationData')){$folder=Join-Path ([IO.Path]::GetTempPath()) 'DeskPages-Verification'}
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$report=Join-Path $folder ('validation-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'.txt')
$exe=[Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
Start-Transcript -LiteralPath $report | Out-Null
try{
    $tests=@('Test-UpdatesWindows.ps1','Test-InstallerStateWindows.ps1','Test-SnapshotKeys.ps1','Test-SnapshotStoreWindows.ps1','Test-SnapshotSceneStability.ps1','Test-TourPolicy.ps1','Test-TourWindows.ps1','Test-SettingsLayoutWindows.ps1','Test-DisplayScopeTransaction.ps1','Test-PublicSource.ps1','Test-MonitorCompositor.ps1','Test-GraphicsSimulator.ps1','Test-MonitorScope.ps1','Test-DisplayArrangementFence.ps1','Test-MonitorPolicy.ps1','Test-Indicator.ps1','Test-FeedbackBridge.ps1','Test-FeedbackUiWindows.ps1','Test-ScriptEncoding.ps1','Test-SimplePreferences.ps1','Test-WorkspaceOpenReads.ps1','Test-EdgeAnimationDirection.ps1','Test-PowerShell51.ps1','Test-Workspace.ps1','Test-VisibilityCommit.ps1','Test-InputPressure.ps1','Test-WorkspaceWindows.ps1','Run-CoreTests.ps1','Test-DragPlacement.ps1','Test-DragMailbox.ps1','Test-DragImage.ps1','Test-DragStartup.ps1','Test-EdgeDrag.ps1','Test-DragRecoveryWindows.ps1','Test-TaskbarGroupsWindows.ps1','Test-EdgeDropTransaction.ps1','Test-LayoutGuards.ps1','Test-VisibilityWindows.ps1','Test-HiddenItemsWindows.ps1','Test-VisibilityPolicy.ps1','Test-MultiplePages.ps1','Test-MainStartup.ps1','Test-BootPlan.ps1','Test-DesktopMerge.ps1','Test-UpgradeCheckpoint.ps1','Test-RecoveryFlow.ps1','Test-Gesture.ps1','Test-TouchpadHid.ps1','Test-PanelWindows.ps1','Test-PackageIntegrity.ps1')
    $results=[Collections.Generic.List[object]]::new()
    foreach($test in $tests){
        $arguments=@('-NoProfile','-File',(Join-Path $PSScriptRoot $test))
        if($test -eq 'Test-Workspace.ps1'){$arguments+=@('-Rounds',$(if($Stress){$Rounds}else{30}),'-ItemCount',$(if($Stress){$ItemCount}else{100}))}
        $watch=[Diagnostics.Stopwatch]::StartNew()
        # PowerShell 5.1 promotes native stderr to ErrorRecord. Capture the full
        # child report and exit code before restoring the parent's fail-fast policy.
        $previousPreference=$ErrorActionPreference
        try{$ErrorActionPreference='Continue';$output=(& $exe @arguments 2>&1 | Out-String);$exitCode=$LASTEXITCODE}
        finally{$ErrorActionPreference=$previousPreference}
        Write-Host $output.TrimEnd()
        $results.Add([PSCustomObject]@{Test=$test;ExitCode=$exitCode;Milliseconds=$watch.ElapsedMilliseconds;Status=$(if($exitCode -ne 0){'FAIL'}elseif($output -match '(?m)^SKIP:'){'PARTIAL_OR_SKIP'}else{'PASS'});Output=$output})
        $results | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath ($report+'.json') -Encoding UTF8
        if($exitCode -ne 0){throw ('FAILED: '+$test+'; exit='+$exitCode)}
    }
    Write-Host 'PASS: all executable validation suites completed. SKIP lines remain unverified.'
    Write-Host 'Physical Explorer/OLE animation, real reboots, OneDrive and other devices are NOT certified by this result. See Release/ACCEPTANCE.md.'
}finally{Stop-Transcript | Out-Null;Write-Host ('Report: '+$report)}
