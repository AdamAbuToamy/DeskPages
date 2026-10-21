$ErrorActionPreference='Stop'
if(-not ('DeskPagesGesture' -as [type])) {
    Add-Type -Path (Join-Path (Split-Path $PSScriptRoot -Parent) 'Native\DeskPagesGesture.cs')
}
function Assert-Gesture {param([bool]$Ok,[string]$Name)
    if(-not $Ok){throw ('Gesture test failed: '+$Name)}
    Write-Host ('PASS: gesture '+$Name)
}
$g=[DeskPagesGesture]::new()
Assert-Gesture ($g.Pointer(1,0,0,0) -eq 0) 'one finger ignored'
Assert-Gesture ($g.Pointer(2,0,0,0) -eq 0) 'two finger baseline'
Assert-Gesture ($g.Pointer(2,10,500,20) -eq 0) 'vertical ignored'
$g.Cancel()
$null=$g.Pointer(2,0,0,50)
Assert-Gesture ($g.Pointer(2,500,30,100) -eq 1) 'right swipe'
Assert-Gesture ($g.Pointer(2,1000,30,150) -eq 0) 'one switch per contact sequence'
$null=$g.Pointer(0,0,0,200)
$null=$g.Pointer(2,0,0,500)
Assert-Gesture ($g.Pointer(2,-500,30,550) -eq -1) 'left swipe after release'
$g=[DeskPagesGesture]::new()
Assert-Gesture ($g.Wheel(20,0,0) -eq 0) 'partial horizontal wheel'
Assert-Gesture ($g.Wheel(20,1,1) -eq 0) 'duplicate source suppressed'
Assert-Gesture ($g.Wheel(20,25,0) -eq 1) 'wheel threshold'
Assert-Gesture ($g.Wheel(120,50,2) -eq 0) 'wheel inertia suppressed'
Assert-Gesture ($g.Wheel(-120,600,2) -eq -1) 'wheel next gesture'
$g=[DeskPagesGesture]::new()
Assert-Gesture ($g.Pointer(3,500,0,0) -eq 0) 'three fingers ignored'
Assert-Gesture ($g.Pointer(2,0,0,20) -eq 0) 'pinch baseline'
Assert-Gesture ($g.Pointer(2,0,0,50) -eq 0) 'stationary centroid does not page'

# Raw-input delivery like the user's 507-packet report must recognize a page swipe.
$g=[DeskPagesGesture]::new();$events=@()
for($i=0;$i -lt 507;$i++){$result=$g.Wheel(1,$i*2,1);if($result){$events+=@($result)}}
Assert-Gesture ($events.Count -eq 1 -and $events[0] -eq 1) '507 high-resolution raw packets recognize exactly one right swipe'
$events=@()
for($i=0;$i -lt 507;$i++){$result=$g.Wheel(-1,1600+$i*2,1);if($result){$events+=@($result)}}
Assert-Gesture ($events.Count -eq 1 -and $events[0] -eq -1) 'next raw gesture recognizes one left swipe'

Assert-Gesture (-not [DeskPagesGesture]::HidOwnsWheel(10,1,500)) 'one-finger HID traffic does not black out wheel fallback'
Assert-Gesture ([DeskPagesGesture]::HidOwnsWheel(10,2,500)) 'active two-finger HID owns duplicate wheel route'
Assert-Gesture ([DeskPagesGesture]::HidOwnsWheel(40,0,40)) 'recognized HID movement suppresses immediate wheel duplicate'
Assert-Gesture (-not [DeskPagesGesture]::HidOwnsWheel(200,0,200)) 'no one-second wheel blackout after HID'
$g=[DeskPagesGesture]::new();$events=@()
for($i=0;$i -lt 1000;$i++){$t=$i*220;if($g.Wheel(60,$t,1) -ne 1){throw 'A distinct repeated wheel gesture was lost'};if($g.Wheel(60,$t+20,1)){throw 'Wheel inertia paged twice'}}
Write-Host 'PASS: 1000 successive short wheel gestures and their inertia tails.'
