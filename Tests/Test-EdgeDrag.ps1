$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Add-Type -Path (Join-Path $root 'Native/DeskPagesDragProtocol.cs')
$inputState=[DeskPagesDragInputState]::new();$x=0;$y=0
$inputState.Press(20,30)
if(-not $inputState.Observe($true,90,100,[ref]$x,[ref]$y) -or $x -ne 20 -or $y -ne 30){throw 'Original drag press position lost'}
if($inputState.Observe($true,200,300,[ref]$x,[ref]$y)){throw 'Held mouse restarted drag'}
$null=$inputState.Observe($false,200,300,[ref]$x,[ref]$y)
# No hook event: a held press must still recover on the next 40 ms poll.
if(-not $inputState.Observe($true,40,50,[ref]$x,[ref]$y) -or $x -ne 40 -or $y -ne 50){throw 'Missing hook event permanently disabled dragging'}
$null=$inputState.Observe($false,40,50,[ref]$x,[ref]$y)
$inputState.Press(1,2);$null=$inputState.Observe($false,4,5,[ref]$x,[ref]$y)
if(-not $inputState.Observe($true,6,7,[ref]$x,[ref]$y) -or $x -ne 6){throw 'Released stale press was reused'}
Write-Host 'PASS: captured grab point, missing-hook polling recovery, held-button deduplication and stale press cleanup.'
$policy=[DeskPagesEdgeDragPolicy]::new()
if($policy.Observe(0,999,0,1000,$false) -ne 0){throw 'Non-drag activated edge'}
if($policy.Observe(0,999,0,1000,$true) -ne 0 -or $policy.Observe(319,999,0,1000,$true) -ne 0 -or $policy.Observe(320,999,0,1000,$true) -ne 1){throw 'Edge dwell incorrect'}
if($policy.Observe(5000,999,0,1000,$true) -ne 0){throw 'Holding at edge repeatedly switched'}
$null=$policy.Observe(5100,500,0,1000,$true);$null=$policy.Observe(5200,1,0,1000,$true)
if($policy.Observe(5520,1,0,1000,$true) -ne -1){throw 'Return edge failed'}

$early=[DeskPagesEdgeDragPolicy]::new()
if($early.Observe(0,960,0,1000,$true,$false) -ne 0 -or $early.Observe(319,960,0,1000,$true,$false) -ne 0 -or $early.Observe(320,960,0,1000,$true,$false) -ne 0){throw 'Unapproved edge switched page'}
if($early.Observe(321,960,0,1000,$true,$true) -ne 1){throw 'Approval reset accumulated near-edge dwell'}
if($early.Observe(2000,940,0,1000,$true) -ne 0 -or $early.Observe(2500,960,0,1000,$true) -ne 0){throw 'Latched edge rearmed without leaving hysteresis zone'}
$null=$early.Observe(2600,500,0,1000,$true);$null=$early.Observe(2700,40,0,1000,$true)
if($early.Observe(3020,40,0,1000,$true) -ne -1){throw 'Earlier left edge recognition failed'}
Write-Host 'PASS: 48px edge recognition; pre-approval dwell retained; no unapproved navigation; 80px rearm hysteresis.'
$packet=[DeskPagesDragRequest]::new();$packet.Id=[guid]::NewGuid().ToString('N');$packet.Kind='Arm';$packet.Paths=[string[]]@('C:\Desktop\ملف.txt','C:\Desktop\a.lnk')
$encoded=[DeskPagesDragProtocol]::Encode($packet);$decoded=[DeskPagesDragProtocol]::Decode($encoded)
if($decoded.Paths[0] -ne $packet.Paths[0] -or $decoded.Id -ne $packet.Id){throw 'IPC Unicode roundtrip failed'}
foreach($bad in @($encoded.Replace('Arm','Execute'),$encoded.Replace("`n2`n","`n257`n"),$encoded.Replace('DeskPagesDrag1','Unknown'),('x'*262145))){$rejected=$false;try{$null=[DeskPagesDragProtocol]::Decode($bad)}catch{$rejected=$true};if(-not $rejected){throw 'Malformed IPC accepted'}}
$rejected=$false;try{$null=[DeskPagesDragProtocol]::SessionPath('../outside')}catch{$rejected=$true};if(-not $rejected){throw 'Session directory escaped'}
Write-Host 'PASS: real-drag gate policy, 320 ms dwell, edge latch/rearm, Unicode data-only IPC, bounds, command allow-list and session path validation.'
Write-Host 'Actual Explorer OLE/UIPI, drag ghost continuity, Escape and real drop handling require Windows acceptance; not exercised here.'
