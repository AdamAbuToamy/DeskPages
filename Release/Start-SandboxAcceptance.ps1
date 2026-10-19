# Optional real Explorer isolation. Requires Windows Sandbox enabled by the owner.
$ErrorActionPreference='Stop'
if($env:OS -ne 'Windows_NT'){throw 'Windows Sandbox is only available on supported Windows editions.'}
$root=Split-Path $PSScriptRoot -Parent
$runner=Get-Command WindowsSandbox.exe -ErrorAction SilentlyContinue
if(-not $runner){throw 'Windows Sandbox is not enabled. Use a disposable Windows VM or enable Sandbox through Windows optional features.'}
$reports=Join-Path $env:LOCALAPPDATA 'DeskPages\Verification\Sandbox'
New-Item -ItemType Directory -Force $reports | Out-Null
$escape={param($Value) [Security.SecurityElement]::Escape($Value)}
$command='cmd.exe /d /c powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File C:\DeskPagesSource\Tests\Test-WindowsShellAcceptance.ps1 -DisposableDesktop -Cycles 200 &gt; C:\DeskPagesReports\shell-acceptance.txt 2&gt;&amp;1'
$xml='<Configuration><Networking>Disable</Networking><ClipboardRedirection>Disable</ClipboardRedirection><MappedFolders><MappedFolder><HostFolder>'+(& $escape $root)+'</HostFolder><SandboxFolder>C:\DeskPagesSource</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder><MappedFolder><HostFolder>'+(& $escape $reports)+'</HostFolder><SandboxFolder>C:\DeskPagesReports</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder></MappedFolders><LogonCommand><Command>'+$command+'</Command></LogonCommand></Configuration>'
$config=Join-Path $reports 'DeskPages-acceptance.wsb'
[IO.File]::WriteAllText($config,$xml,[Text.UTF8Encoding]::new($false))
Start-Process -FilePath $runner.Source -ArgumentList ('"'+$config+'"')
Write-Host ('Isolated shell acceptance launched. Results: '+(Join-Path $reports 'shell-acceptance.txt'))
Write-Host 'Source is mapped read-only; only the dedicated reports folder is writable. This does not test reboots or physical hardware.'
