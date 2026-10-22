param([string]$InstallDir,[string]$CancelEventName)
$ErrorActionPreference='Stop'
$script:SetupCancellation=$null
function Confirm-DpSetupContinue {
    if($script:SetupCancellation -and $script:SetupCancellation.WaitOne(0)){exit 1223}
}
if($CancelEventName){
    if($CancelEventName -notmatch '^Local\\DeskPagesSetupCancel-[a-f0-9]{32}$'){throw 'Invalid setup cancellation handle.'}
    $script:SetupCancellation=[Threading.EventWaitHandle]::OpenExisting($CancelEventName)
}
Confirm-DpSetupContinue
try {
    . (Join-Path $PSScriptRoot 'Maintenance.ps1')
    $powershell=Join-Path $PSHOME 'powershell.exe'
    if (-not (Test-DpAdministrator)) {
        $arguments='-NoProfile -STA -ExecutionPolicy Bypass -File "'+$PSCommandPath+'"'
        if($InstallDir){$arguments+=' -InstallDir '+[char]34+$InstallDir+[char]34}
        $process=Start-Process -FilePath $powershell -Verb RunAs -ArgumentList $arguments -Wait -PassThru
        exit $process.ExitCode
    }
    . (Join-Path $PSScriptRoot 'PackageIntegrity.ps1')
    Assert-DpPackage -Root $PSScriptRoot -Version ([Version]'0.8.28.0')
    & $powershell -NoProfile -STA -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Tests\Test-PowerShell51.ps1') -RequireNative
    if($LASTEXITCODE -ne 0){throw 'Windows PowerShell 5.1 compatibility preflight failed. Startup configuration was not changed.'}
    # Verify the new visibility adapter on temporary Windows files before installation.
    & $powershell -NoProfile -File (Join-Path $PSScriptRoot 'Tests\Test-HiddenItemsWindows.ps1')
    if($LASTEXITCODE -ne 0){throw 'Hidden/System recovery preflight failed. Installation was not changed.'}
    Confirm-DpSetupContinue
    Assert-DpInstallAccount
    $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $taskName="DeskPages-$sid"
    Add-Type -Path (Join-Path $PSScriptRoot 'Native\DeskPagesInstallLocation.cs')
    if(-not $InstallDir){
        $key=Get-ItemProperty -LiteralPath ('HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\DeskPages-'+$sid) -ErrorAction SilentlyContinue
        $InstallDir=$key.InstallLocation
        if(-not $InstallDir){$InstallDir=Join-Path $env:ProgramFiles "DeskPages-$sid"}
    }
    $target=[DeskPagesInstallLocation]::Resolve($InstallDir)
    $sourceAgent=Join-Path $PSScriptRoot 'Agent.ps1'
    $targetAgent=Join-Path $target 'Agent.ps1'

    Write-Host 'Checking verified package and native runtime...'
    & $powershell -NoProfile -STA -ExecutionPolicy Bypass -File $sourceAgent -QuickCheck
    if ($LASTEXITCODE -ne 0) {throw 'Preflight failed. Startup configuration was not changed.'}
    if(-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'DeskPagesStartup.exe'))){throw 'Startup guardian EXE missing. Extract the complete package.'}
    $guardianIdentity=[Reflection.AssemblyName]::GetAssemblyName((Join-Path $PSScriptRoot 'DeskPagesStartup.exe'))
    if($guardianIdentity.Name -ne 'DeskPagesStartup' -or $guardianIdentity.Version -lt [Version]'0.8.28.0'){throw 'Incorrect startup guardian build.'}

    $existing=Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    Confirm-DpSetupContinue
    Write-Host 'DESKPAGES_COMMIT_STARTED'
    Stop-DpKnownAgents -CheckpointFallback
    Stop-DpDragBroker -Directory $target
    if ($existing) {Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue}
    Start-Sleep -Milliseconds 300
    New-Item -ItemType Directory -Force $target | Out-Null
    if ((Get-Item -LiteralPath $target -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw 'Installation directory must not be a junction or symbolic link.'
    }
    # Elevated autostart must not execute code from a user-writable download folder.
    $acl=[Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true,$false)
    $admins=[Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
    $system=[Security.Principal.SecurityIdentifier]::new('S-1-5-18')
    $user=[Security.Principal.SecurityIdentifier]::new($sid)
    $acl.SetOwner($admins)
    $inherit=[Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit'
    $propagate=[Security.AccessControl.PropagationFlags]::None
    $allow=[Security.AccessControl.AccessControlType]::Allow
    foreach ($who in @($admins,$system)) {
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
            $who,[Security.AccessControl.FileSystemRights]::FullControl,$inherit,$propagate,$allow))
    }
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
        $user,[Security.AccessControl.FileSystemRights]::ReadAndExecute,$inherit,$propagate,$allow))
    Set-Acl -LiteralPath $target -AclObject $acl
    [DeskPagesInstallLocation]::CheckAncestors($target,$true)
    if ([IO.Path]::GetFullPath($PSScriptRoot) -ne [IO.Path]::GetFullPath($target)) {
        foreach ($file in Get-ChildItem -LiteralPath $PSScriptRoot -File) {
            if ($file.Extension -in @('.ps1','.cmd','.md','.txt','.exe')) {
                Copy-Item -LiteralPath $file.FullName -Destination $target -Force
            }
        }
        foreach ($folder in @('Native','Bin','Tests','Assets','ThirdParty','Guard','Packaging','Installer','Interaction','Release')) {
            $destination=Join-Path $target $folder
            if (Test-Path -LiteralPath $destination) {Remove-Item -LiteralPath $destination -Recurse -Force}
            Copy-Item -LiteralPath (Join-Path $PSScriptRoot $folder) -Destination $target -Recurse
        }
    }
    # Verify exact installed bytes before removing download-zone metadata.
    foreach($entry in Get-ChildItem -LiteralPath $PSScriptRoot -Recurse -File){
        $relative=$entry.FullName.Substring($PSScriptRoot.Length).TrimStart('\','/')
        $installed=Join-Path $target $relative
        if(Test-Path -LiteralPath $installed -PathType Leaf){
            if((Get-FileHash -LiteralPath $entry.FullName).Hash -ne (Get-FileHash -LiteralPath $installed).Hash){throw ('Installed file verification failed: '+$relative)}
            Unblock-File -LiteralPath $installed
        }
    }
    Write-Host 'Checking the precompiled installed copy...' 
    & $powershell -NoProfile -STA -ExecutionPolicy Bypass -File $targetAgent -QuickCheck
    if ($LASTEXITCODE -ne 0) {throw 'Installed build failed; the task was not replaced.'}

    [IO.File]::WriteAllText((Join-Path $target 'installed-account.txt'),$sid)
    $guardian=Join-Path $target 'DeskPagesStartup.exe'
    $action=New-ScheduledTaskAction -Execute $guardian -Argument '--logon' -WorkingDirectory $target
    $trigger=New-ScheduledTaskTrigger -AtLogOn -User $sid
    $principal=New-ScheduledTaskPrincipal -UserId $sid -LogonType Interactive -RunLevel Highest
    $settings=New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
        -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit ([TimeSpan]::Zero) `
        -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger `
        -Principal $principal -Settings $settings `
        -Description 'DeskPages: restore main desktop page 1 at this user logon.' -Force | Out-Null
    Enable-ScheduledTask -TaskName $taskName | Out-Null
    # Independent per-user fallback can release an owned gate even if the
    # elevated logon task was disabled or could not start. No elevation here.
    $run=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
    try{$run.SetValue('DeskPagesGateRecovery','"'+$guardian+'" --recover-gate',[Microsoft.Win32.RegistryValueKind]::String);$run.Flush()}finally{$run.Dispose()}
    $store=Join-Path $env:LOCALAPPDATA 'DeskPages\HiddenPages'
    New-Item -ItemType Directory -Force $store | Out-Null
    [IO.File]::WriteAllText((Join-Path $store 'startup-enabled.txt'),$guardian)
    $configStream=[IO.File]::Open((Join-Path $store 'startup-enabled.txt'),[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    try{$configStream.Flush($true)}finally{$configStream.Dispose()}
    $readyToken=[Guid]::NewGuid().ToString('N')
    $readyPath=Join-Path $store ('install-ready-'+$readyToken+'.txt')
    $startArguments='-NoProfile -STA -ExecutionPolicy Bypass -WindowStyle Hidden -File "'+$targetAgent+'" -Resume -EnableAnimation -InstallReadyToken '+$readyToken
    $agentProcess=Start-Process -FilePath $powershell -ArgumentList $startArguments -WindowStyle Hidden -PassThru
    . (Join-Path $PSScriptRoot 'InstallationReadiness.ps1')
    Wait-DpInstalledAgent -Process $agentProcess -ReadyPath $readyPath -Token $readyToken
    $shell=New-Object -ComObject WScript.Shell
    $linkPath=Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs\DeskPages.lnk'
    $shortcut=$shell.CreateShortcut($linkPath)
    $shortcut.TargetPath=Join-Path $target 'Run-Admin.cmd'
    if(Test-Path -LiteralPath (Join-Path $target 'DeskPages.exe')){$shortcut.TargetPath=Join-Path $target 'DeskPages.exe'}
    $shortcut.IconLocation=(Join-Path $target 'Assets\DeskPages.ico')+',0'
    $shortcut.WorkingDirectory=$target;$shortcut.Description='DeskPages native desktop pages';$shortcut.Save()
    Write-Host 'Installed: early sign-in guardian, independent recovery, and main-page restoration.' -ForegroundColor Green
    Write-Host 'To remove automatic startup, run Uninstall-Startup.cmd. Your saved page data is kept.'
}
catch {Write-Host ([string]$_) -ForegroundColor Red;exit 1}
finally {if($script:SetupCancellation){$script:SetupCancellation.Dispose()}}
