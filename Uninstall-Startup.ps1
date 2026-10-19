$ErrorActionPreference='Stop'
try {
    . (Join-Path $PSScriptRoot 'Maintenance.ps1')
    $powershell=Join-Path $PSHOME 'powershell.exe'
    if (-not (Test-DpAdministrator)) {
        $arguments='-NoProfile -STA -ExecutionPolicy Bypass -File "'+$PSCommandPath+'"'
        $process=Start-Process -FilePath $powershell -Verb RunAs -ArgumentList $arguments -Wait -PassThru
        exit $process.ExitCode
    }
    Assert-DpInstallAccount
    Stop-DpKnownAgents
    $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $taskName="DeskPages-$sid"
    $installed=Get-ItemProperty -LiteralPath ('HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\DeskPages-'+$sid) -ErrorAction SilentlyContinue
    $target=$installed.InstallLocation
    if(-not $target){$target=Join-Path $env:ProgramFiles "DeskPages-$sid"}
    Stop-DpDragBroker -Directory $target
    $targetAgent=Join-Path $target 'Agent.ps1'
    Start-Sleep -Milliseconds 300
    if (-not (Test-Path -LiteralPath $targetAgent)) {$targetAgent=Join-Path $PSScriptRoot 'Agent.ps1'}
    & $powershell -NoProfile -STA -ExecutionPolicy Bypass -File $targetAgent -Recover
    if ($LASTEXITCODE -ne 0) {throw 'Desktop restoration was not verified. Uninstall did not remove the startup registration or saved layouts; see agent.log.'}
    Remove-Item -LiteralPath (Join-Path $env:LOCALAPPDATA 'DeskPages\HiddenPages\startup-enabled.txt') -ErrorAction SilentlyContinue
    $run=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run',$true)
    if($null -ne $run){try{$run.DeleteValue('DeskPagesGateRecovery',$false)}finally{$run.Dispose()}}
    if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
        Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
    }

    $linkPath=Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs\DeskPages.lnk'
    Remove-Item -LiteralPath $linkPath -ErrorAction SilentlyContinue
    $appKey='HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\DeskPages-'+$sid
    Remove-Item -LiteralPath $appKey -ErrorAction SilentlyContinue
    Write-Host 'Automatic startup removed and managed files shown. Page data and recovery tools were kept.' -ForegroundColor Green
}
catch {Write-Host ([string]$_) -ForegroundColor Red;exit 1}
