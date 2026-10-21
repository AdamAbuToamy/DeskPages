$ErrorActionPreference='Stop'
if(-not ('DeskPagesTouchpad' -as [type])) {
    Add-Type -Path (Join-Path (Split-Path $PSScriptRoot -Parent) 'Native\DeskPagesTouchpad.cs')
}
$script:hidCases=0
function Assert-Hid {param([bool]$Ok,[string]$Label)
    if(-not $Ok){throw ('HID test failed: '+$Label)}
    $script:hidCases++;Write-Host ('PASS: HID '+$Label)
}
function Contact {param([int]$Id,[double]$X,[double]$Y,[bool]$Tip=$true,[bool]$Confidence=$true)
    $c=[DeskPagesTouchpad+Contact]::new();$c.Id=$Id;$c.X=$X;$c.Y=$Y;$c.Tip=$Tip;$c.Confidence=$Confidence;return $c
}
function Frame {param([double]$Shift=0,[double]$Vertical=0)
    return [DeskPagesTouchpad+Contact[]]@((Contact 4 (0.25+$Shift) (0.3+$Vertical)),(Contact 9 (0.45+$Shift) (0.3+$Vertical)))
}
$g=[DeskPagesTouchpad+SwipeRecognizer]::new()
Assert-Hid ($g.Update((Frame),0,$true) -eq 0) 'two-finger baseline'
Assert-Hid ($g.Update((Frame 0.03),30,$true) -eq 0) 'jitter does not page'
Assert-Hid ($g.Update((Frame 0.10),60,$true) -eq 1) 'two contacts translate right'
Assert-Hid ($g.Update((Frame 0.20),90,$true) -eq 0) 'one page per contact sequence'
$null=$g.Update([DeskPagesTouchpad+Contact[]]@(),100,$true)
$null=$g.Update((Frame 0.20),150,$true)
Assert-Hid ($g.Update((Frame 0.10),180,$true) -eq -1) 'lift then left translation'
foreach($case in 'one-finger','vertical','pinch','three-fingers','palm','out-of-range','NaN') {
    $g=[DeskPagesTouchpad+SwipeRecognizer]::new();$null=$g.Update((Frame),0,$true)
    $points=Frame 0.15
    switch($case) {
        'one-finger' {$points=[DeskPagesTouchpad+Contact[]]@((Contact 4 0.6 0.3))}
        'vertical' {$points=Frame 0 0.2}
        'pinch' {$points=[DeskPagesTouchpad+Contact[]]@((Contact 4 0.10 0.3),(Contact 9 0.60 0.3))}
        'three-fingers' {$points=[DeskPagesTouchpad+Contact[]]@((Contact 4 0.4 0.3),(Contact 9 0.6 0.3),(Contact 12 0.8 0.3))}
        'palm' {$points[0].Confidence=$false}
        'out-of-range' {$points[0].X=2}
        'NaN' {$points[0].X=[double]::NaN}
    }
    Assert-Hid ($g.Update($points,30,$true) -eq 0) ($case+' ignored')
}
$g=[DeskPagesTouchpad+SwipeRecognizer]::new();$null=$g.Update((Frame),0,$false)
Assert-Hid ($g.Update((Frame 0.2),30,$true) -eq 0) 'gesture started outside desktop cannot enter and page'
$null=$g.Update([DeskPagesTouchpad+Contact[]]@(),50,$true)
$null=$g.Update((Frame),60,$true)
Assert-Hid ($g.Update((Frame 0.12),100,$true) -eq 1) 'new allowed gesture after lift'
$g=[DeskPagesTouchpad+SwipeRecognizer]::new();$null=$g.Update((Frame),0,$true)
$reversed=[DeskPagesTouchpad+Contact[]]@((Contact 9 0.57 0.3),(Contact 4 0.37 0.3))
Assert-Hid ($g.Update($reversed,40,$true) -eq 1) 'contact ordering does not change identity'
$g=[DeskPagesTouchpad+SwipeRecognizer]::new();$null=$g.Update((Frame),0,$true)
$changed=[DeskPagesTouchpad+Contact[]]@((Contact 4 0.5 0.3),(Contact 22 0.7 0.3))
Assert-Hid ($g.Update($changed,40,$true) -eq 0) 'new contact identity establishes a new baseline'
$g=[DeskPagesTouchpad+SwipeRecognizer]::new();$null=$g.Update((Frame),0,$true)
Assert-Hid ($g.Update((Frame 0.2),1000,$true) -eq 0) 'stale input gap cannot cause a jump'
$a=[DeskPagesTouchpad+FrameAssembler]::new();$frame=$null
Assert-Hid ($a.Add(10,2,(Frame),0,[ref]$frame) -and $frame.Count -eq 2) 'parallel contact frame'
$a=[DeskPagesTouchpad+FrameAssembler]::new();$frame=$null
Assert-Hid (-not $a.Add(20,2,[DeskPagesTouchpad+Contact[]]@((Contact 4 0.2 0.3)),0,[ref]$frame)) 'hybrid first packet is not a complete frame'
Assert-Hid ($a.Add(20,0,[DeskPagesTouchpad+Contact[]]@((Contact 9 0.4 0.3)),2,[ref]$frame) -and $frame.Count -eq 2) 'hybrid continuation completes matching scan time'
$null=$a.Add(21,2,[DeskPagesTouchpad+Contact[]]@((Contact 4 0.2 0.3)),10,[ref]$frame)
Assert-Hid (-not $a.Add(22,0,[DeskPagesTouchpad+Contact[]]@((Contact 9 0.4 0.3)),12,[ref]$frame) -and $a.Dropped -eq 1) 'different scan times are never merged'
$a=[DeskPagesTouchpad+FrameAssembler]::new()
$null=$a.Add(23,2,[DeskPagesTouchpad+Contact[]]@((Contact 4 0.2 0.3)),0,[ref]$frame)
Assert-Hid (-not $a.Add(23,0,[DeskPagesTouchpad+Contact[]]@((Contact 9 0.4 0.3)),200,[ref]$frame) -and $a.Dropped -eq 1) 'incomplete frame expires'
$a=[DeskPagesTouchpad+FrameAssembler]::new()
$padded=[DeskPagesTouchpad+Contact[]]@((Contact 4 0.2 0.3),(Contact 9 0.4 0.3),(Contact 0 0 0 $false $false))
Assert-Hid ($a.Add(24,2,$padded,0,[ref]$frame) -and $frame.Count -eq 2) 'parallel report padding is excluded'
$lift=[DeskPagesTouchpad+Contact[]]@((Contact 4 0.2 0.3 $false),(Contact 9 0.4 0.3 $false))
Assert-Hid ($a.Add(25,2,$lift,10,[ref]$frame) -and @($frame|Where-Object Tip).Count -eq 0) 'lifted contacts remain in reported frame count'
Assert-Hid ($a.Add(26,0,$lift,20,[ref]$frame) -and $frame.Count -eq 0) 'zero-count padding clears active frame'
Assert-Hid (-not $a.Add(27,6,$padded,30,[ref]$frame)) 'unsupported contact count rejected'
foreach($pointerBytes in 4,8) {
    $header=8+2*$pointerBytes;$bytes=[byte[]]::new($header+8+24)
    [BitConverter]::GetBytes([uint32]2).CopyTo($bytes,0)
    [BitConverter]::GetBytes([uint32]$bytes.Length).CopyTo($bytes,4)
    [BitConverter]::GetBytes([uint32]8).CopyTo($bytes,$header)
    [BitConverter]::GetBytes([uint32]3).CopyTo($bytes,$header+4)
    $offset=0;$size=0;$count=0
    Assert-Hid ([DeskPagesTouchpad]::GetReportBounds($bytes,$pointerBytes,[ref]$offset,[ref]$size,[ref]$count) -and $offset -eq ($header+8) -and $size -eq 8 -and $count -eq 3) ($pointerBytes.ToString()+'-byte pointer: all three HID reports framed')
    [BitConverter]::GetBytes([uint32]4).CopyTo($bytes,$header+4)
    Assert-Hid (-not [DeskPagesTouchpad]::GetReportBounds($bytes,$pointerBytes,[ref]$offset,[ref]$size,[ref]$count)) ($pointerBytes.ToString()+'-byte pointer: truncated HID reports rejected')
    [BitConverter]::GetBytes([uint32]::MaxValue).CopyTo($bytes,$header)
    Assert-Hid (-not [DeskPagesTouchpad]::GetReportBounds($bytes,$pointerBytes,[ref]$offset,[ref]$size,[ref]$count)) ($pointerBytes.ToString()+'-byte pointer: oversized HID data rejected')
}
$type=[DeskPagesTouchpad].GetNestedType('ValueCap',[Reflection.BindingFlags]::NonPublic)
Assert-Hid ([Runtime.InteropServices.Marshal]::SizeOf([Activator]::CreateInstance($type)) -eq 72) 'HIDP_VALUE_CAPS SDK ABI size'
Assert-Hid ([Runtime.InteropServices.Marshal]::OffsetOf($type,'LogicalMin').ToInt32() -eq 40) 'HIDP_VALUE_CAPS logical range ABI offset'
$type=[DeskPagesTouchpad].GetNestedType('Caps',[Reflection.BindingFlags]::NonPublic)
Assert-Hid ([Runtime.InteropServices.Marshal]::SizeOf([Activator]::CreateInstance($type)) -eq 64) 'HIDP_CAPS SDK ABI size'
Write-Host ('PASS: '+$script:hidCases+' touchpad HID framing/recognition/ABI checks. Native Windows HID parser and physical hardware NOT executed.')

$g=[DeskPagesTouchpad+SwipeRecognizer]::new()
for($i=0;$i -lt 1000;$i++){
 $t=$i*160;$a=2*$i+1;$b=$a+1
 $frame=[DeskPagesTouchpad+Contact[]]@((Contact $a 0.2 0.3),(Contact $b 0.4 0.3))
 if($g.Update($frame,$t,$true)){throw 'New fingers emitted without movement'}
 $frame=[DeskPagesTouchpad+Contact[]]@((Contact $a 0.32 0.3),(Contact $b 0.52 0.3))
 if($g.Update($frame,($t+30),$true) -ne 1){throw 'Rapid new-contact swipe was lost when no lift report arrived'}
 if($g.Update($frame,($t+60),$true)){throw 'Same contact sequence repeated'}
}
Write-Host 'PASS: 1000 rapid HID swipes without explicit lift reports; one page per contact sequence.'

$g=[DeskPagesTouchpad+SwipeRecognizer]::new();$g.DistanceThreshold=0.05;$null=$g.Update((Frame),0,$true)
Assert-Hid ($g.Update((Frame 0.055),32,$true) -eq 1) 'runtime two-finger distance recognizes without waiting for full old threshold'
