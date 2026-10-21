$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. (Join-Path $root 'SnapshotCache.ps1')
$icons=@([PSCustomObject]@{Key='id-a';Name='Alpha';X=10;Y=20},[PSCustomObject]@{Key='id-b';Name='Beta';X=30;Y=40})
$key=Get-DpSnapshotKey -Page 1 -Icons $icons
if($key -cne (Get-DpSnapshotKey -Page 1 -Icons @($icons[1],$icons[0]))){throw 'Snapshot key depends on enumeration order.'}
if($key -ceq (Get-DpSnapshotKey -Page 2 -Icons $icons)){throw 'Different pages share a key.'}
$icons[0].X=99
if($key -ceq (Get-DpSnapshotKey -Page 1 -Icons $icons)){throw 'Moved icon reused its old snapshot.'}
$icons[0].X=10;$icons[0].Name='Renamed'
if($key -ceq (Get-DpSnapshotKey -Page 1 -Icons $icons)){throw 'Renamed icon reused its old snapshot.'}
if((Get-DpSnapshotKey -Page 1 -Icons @()) -ceq ''){throw 'An empty page must still have a cache key.'}
Write-Host 'PASS: page identity, deterministic ordering, moved/renamed icons and empty-page keys.'

Add-Type -Path (Join-Path $root 'Native/DeskPagesSnapshotInvalidation.cs')
foreach($evt in @(0x8002,0x8003,0x8005,0x8006,0x800a,0x8017,0x8018)){if([DeskPagesSnapshotInvalidation]::ChangesImage($evt)){throw 'Focus/show/hide event invalidated page images.'}}
foreach($evt in @(0x8000,0x8001,0x8004,0x800b,0x800c,0x8015)){if(-not [DeskPagesSnapshotInvalidation]::ChangesImage($evt)){throw 'A visual desktop change was ignored.'}}
Write-Host 'PASS: focus/show/hide preserve cached pages; visual edits invalidate the current page only.'
