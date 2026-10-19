$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path (Split-Path $PSScriptRoot -Parent) 'Native/DeskPagesTourPolicy.cs')
$p=[DeskPagesTourPolicy]::new()
for($i=0;$i -lt 10000;$i++){
 $p.Start(1)
 if($p.Step -ne 1 -or $p.PageAdded(1)){throw 'Tour advanced without a successful add.'}
 if(-not $p.PageAdded(2) -or $p.Step -ne 2 -or $p.PageAdded(3)){throw 'Wrong progression after add.'}
 $p.Finish();if($p.Step -ne 0){throw 'Tour did not finish.'}
 $p.Start(4);if($p.Step -ne 2){throw 'Replay with existing pages must not require an extra page.'}
}
Write-Host 'PASS: 10,000 tour add/replay/completion cycles; no workspace or file mutations.'
