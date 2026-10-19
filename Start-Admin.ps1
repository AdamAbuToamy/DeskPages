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
    $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $agent=Join-Path $PSScriptRoot 'Agent.ps1'
    $installed=Join-Path $env:ProgramFiles ("DeskPages-$sid\Agent.ps1")
    Stop-DpKnownAgents
    # Resuming must never show all pages or save the mixed recovery view.
    $arguments='-NoProfile -STA -ExecutionPolicy Bypass -WindowStyle Hidden -File "'+$agent+'" -Resume'
    Start-Process -FilePath $powershell -ArgumentList $arguments | Out-Null
} catch {Write-Host ([string]$_) -ForegroundColor Red;exit 1}
