$ErrorActionPreference='Stop'
$base=Split-Path $PSScriptRoot -Parent
. (Join-Path $base 'StateEngine.ps1')
. (Join-Path $base 'FirstRun.ps1')
$root=Join-Path ([IO.Path]::GetTempPath()) ('DeskPages-main-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
try {
 $script:DpStore=$root;$script:DpManifest=Join-Path $root 'pages.xml'
 function Set-DpFilePage {param($Page,$Commit,$RegisterNew) if($Commit -or $RegisterNew){throw 'Unsafe registration/commit at startup'};$script:selected=$Page}
 function Set-DpSystemIcons {param($Page)}
 function Refresh-DpDesktop {}
 function Restore-DpPageLayout {param($Page) if($script:failLayout){throw 'Injected layout failure'};if((Read-DpManifest $script:DpManifest).Current -ne 2){throw 'Premature commit'}}
 function Reset-Test {$script:DpState=[PSCustomObject]@{Current=2;Pages=@(1,2);Items=@()};Save-DpState;$script:failLayout=$false}
 Reset-Test;Restore-DpStartupPage -Main:$true
 if($script:DpState.Current -ne 1 -or (Read-DpManifest $script:DpManifest).Current -ne 1 -or $script:selected -ne 1){throw 'Main page not committed'}
 Write-Host 'PASS: logon selects page 1 only after verified layout'
 Reset-Test;$script:failLayout=$true;$failed=$false
 try {Restore-DpStartupPage -Main:$true}catch{$failed=$true}
 if(-not $failed -or $script:DpState.Current -ne 2 -or (Read-DpManifest $script:DpManifest).Current -ne 2){throw 'Failure lost committed page'}
 Write-Host 'PASS: failed main-page restore preserves previous committed manifest'
 Reset-Test;Restore-DpStartupPage
 if($script:DpState.Current -ne 2 -or $script:selected -ne 2){throw 'Manual recovery must retain committed page'}
 Write-Host 'PASS: manual recovery retains committed page'
 $desktop=Join-Path $root 'Desktop';$common=Join-Path $root 'Public';New-Item -ItemType Directory -Path $desktop,$common | Out-Null
 [IO.File]::WriteAllText((Join-Path $desktop 'code.txt'),'unchanged');[IO.File]::WriteAllText((Join-Path $common 'app.lnk'),'shortcut')
 $initial=New-DpInitialState -Desktop $desktop -PublicDesktop $common -SystemBackup @()
 if($initial.PageCount -ne 1 -or @($initial.Pages).Count -ne 1 -or $initial.Current -ne 1 -or $initial.Items.Count -ne 2 -or @($initial.Items | Where-Object Page -ne 1).Count){throw 'Incorrect first-run membership'}
 if([IO.File]::ReadAllText((Join-Path $desktop 'code.txt')) -ne 'unchanged'){throw 'First run mutated file'}
 Write-Host 'PASS: generic first run preserves paths and contents'
}finally{Remove-Item -LiteralPath $root -Recurse -Force}
