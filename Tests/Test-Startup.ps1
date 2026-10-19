param([switch]$Child)
$ErrorActionPreference='Stop'
if(-not $Child){
    $exe=[Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
    & $exe -NoProfile -File $PSCommandPath -Child
    if($LASTEXITCODE -ne 0){throw 'Startup/shutdown regression failed.'}
    return
}
$base=Split-Path $PSScriptRoot -Parent
Add-Type -Path @((Join-Path $base 'Native/DeskPagesInstallLocation.cs'),(Join-Path $base 'Native/DeskPagesStartupGate.cs'),(Join-Path $base 'Native/DeskPagesCheckpointPolicy.cs'),(Join-Path $base 'Native/DeskPagesSessionPolicy.cs'),(Join-Path $PSScriptRoot 'StartupFixtures.cs'))
$results=[DeskPagesStartupFixtures]::Run()
$results | ForEach-Object {Write-Host $_}
Write-Host ('PASS: '+$results.Count+' production checkpoint/session/gate cases with a simulated shell setting.')
Write-Host 'No actual Windows shutdown, Explorer first-paint, scheduled-task or physical display test was executed by these fixtures.'
