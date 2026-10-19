$ErrorActionPreference='Continue'
Write-Host 'DeskPages workspace crash check (read only)'
$log=Join-Path $env:LOCALAPPDATA 'DeskPages\HiddenPages\agent.log'
Write-Host "`nRecent agent log:"
if(Test-Path -LiteralPath $log){Get-Content -LiteralPath $log -Tail 60}
Write-Host "`nRecent Windows application errors:"
Get-WinEvent -FilterHashtable @{LogName='Application';Id=1000,1001,1026;StartTime=(Get-Date).AddHours(-2)} -MaxEvents 100 -ErrorAction SilentlyContinue |
    Where-Object {$_.Message -match 'DeskPages|powershell.exe'} |
    Select-Object -First 6 TimeCreated,Id,Message | Format-List
Write-Host 'No settings or desktop files were changed.'
