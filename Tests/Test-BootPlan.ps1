param([switch]$Child)
if(-not $Child){
 $exe=[Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
 & $exe -NoProfile -File $PSCommandPath -Child
 if($LASTEXITCODE -ne 0){throw 'Boot plan tests failed.'}
 return
}
$ErrorActionPreference='Stop'
$base=Split-Path $PSScriptRoot -Parent
Add-Type -Path @((Join-Path $base 'Native/DeskPagesInstallLocation.cs'),(Join-Path $base 'Native/DeskPagesStartupGate.cs'),(Join-Path $base 'Native/DeskPagesBootPlan.cs'))
$root=Join-Path ([IO.Path]::GetTempPath()) ('DeskPages-plan-'+[Guid]::NewGuid().ToString('N'))
$d=Join-Path $root 'Desktop';$p=Join-Path $root 'Public'
$encode={param($s)[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($s))}
$valid=@('DeskPagesMain1','ABC',(& $encode $d),(& $encode $p),('2'+"`t"+(& $encode (Join-Path $d 'file.txt'))))
if([DeskPagesBootPlan]::Parse($valid,'ABC',$d,$p).Length -ne 1){throw 'Valid plan rejected'}
Write-Host 'PASS: native preparation parses exact desktop membership'
$cases=@(
 [PSCustomObject]@{Lines=$valid;Hash='stale';D=$d},
 [PSCustomObject]@{Lines=$valid;Hash='ABC';D=(Join-Path $root 'other')},
 [PSCustomObject]@{Lines=@($valid)+@($valid[4]);Hash='ABC';D=$d},
 [PSCustomObject]@{Lines=@($valid[0..3])+@('1'+"`t"+(& $encode (Join-Path $root 'outside.txt')));Hash='ABC';D=$d},
 [PSCustomObject]@{Lines=@($valid[0..3])+@('0'+"`t"+(& $encode (Join-Path $d 'file.txt')));Hash='ABC';D=$d}
)
foreach($case in $cases){$failed=$false;try{[void][DeskPagesBootPlan]::Parse($case.Lines,$case.Hash,$case.D,$p)}catch{$failed=$true};if(-not $failed){throw 'Invalid boot plan accepted'}}
Write-Host 'PASS: stale hash, wrong account, duplicate path, outside path and invalid page rejected'
Write-Host 'No Windows file attributes or real logon were exercised by this fixture.'
