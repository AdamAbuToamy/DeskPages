function Wait-DpInstalledAgent {
    param([Parameter(Mandatory)]$Process,[Parameter(Mandatory)][string]$ReadyPath,[Parameter(Mandatory)][string]$Token,[double]$TimeoutSeconds=30)
    if($Token -notmatch '^[a-f0-9]{32}$'){throw 'Invalid installation readiness token.'}
    $clock=[Diagnostics.Stopwatch]::StartNew()
    while(-not [IO.File]::Exists($ReadyPath)){
        if($Process.HasExited){throw 'The installed agent exited before becoming ready. See agent.log.'}
        if($clock.Elapsed.TotalSeconds -gt $TimeoutSeconds){throw 'Files installed, but the tray agent did not confirm readiness. See agent.log.'}
        Start-Sleep -Milliseconds 50
    }
    $receipt=[IO.File]::ReadAllLines($ReadyPath)
    if($receipt.Length -ne 3 -or $receipt[0] -ne 'DeskPagesInstallReady1' -or $receipt[1] -ne $Token -or $receipt[2] -ne 'Animation=True'){throw 'The installed application did not confirm animation readiness.'}
    if($Process.HasExited){throw 'The installed agent stopped after initialization. See agent.log.'}
    [IO.File]::Delete($ReadyPath)
}
