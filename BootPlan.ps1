function Write-DpMainBootPlan {
    if($script:DpSuspended -or -not (Test-Path -LiteralPath $script:DpManifest)){return}
    $encode={param([string]$s)[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($s))}
    $lines=@('DeskPagesMain1',(Get-FileHash -LiteralPath $script:DpManifest -Algorithm SHA256).Hash,(& $encode $script:DpDesktop),(& $encode $script:DpPublic))
    foreach($entry in $script:DpState.Items){$lines+=([string]$(if($entry.PSObject.Properties['Pinned'] -and [bool]$entry.Pinned){1}else{$entry.Page})+"`t"+(& $encode $entry.Path))}
    [DeskPagesStartupGate]::AtomicText((Join-Path $script:DpStore 'startup-main.plan'),($lines -join "`n"))
}
