param([switch]$Child)
$ErrorActionPreference='Stop'
if(-not $Child){$exe=[Diagnostics.Process]::GetCurrentProcess().MainModule.FileName;& $exe -NoProfile -File $PSCommandPath -Child;if($LASTEXITCODE -ne 0){throw 'Drag group placement fixture failed'};return}
$root=Split-Path $PSScriptRoot -Parent
Add-Type -Path (Join-Path $root 'Native/DeskPagesDragPlacement.cs')
function Item($key,$x,$y){[DeskPagesDragPlacement+Item]@{Key=$key;X=$x;Y=$y}}
$source=[DeskPagesDragPlacement+Item[]]@((Item 'bottom-left' 20 230),(Item 'top-right' 180 30),(Item 'top-left' 20 30),(Item 'bottom-right' 180 230))
$empty=[DeskPagesDragPlacement+Item[]]@()
function Check-Shape($plan,$geometry,$source,$occupied){
 if($null -eq $plan -or $plan.Count -ne $source.Count){throw 'Group was lost or split'}
 $dx=$null;$dy=$null
 foreach($item in $source){$placed=@($plan | Where-Object {$_.Key -ceq $item.Key});if($placed.Count -ne 1){throw 'Path-to-position mapping lost'}
  if($null -eq $dx){$dx=$placed[0].X-$item.X;$dy=$placed[0].Y-$item.Y}
  if($placed[0].X-$item.X -ne $dx -or $placed[0].Y-$item.Y -ne $dy){throw 'Relative arrangement changed'}
  if($placed[0].X -lt $geometry[2] -or $placed[0].Y -lt $geometry[3] -or $placed[0].X+80 -gt $geometry[4] -or $placed[0].Y+100 -gt $geometry[5]){throw 'Group clipped by work area'}
  foreach($old in $occupied){if([Math]::Abs($placed[0].X-$old.X) -lt 78 -and [Math]::Abs($placed[0].Y-$old.Y) -lt 98){throw 'Group overlaps existing icon'}}
 }
}
$g=[int[]]@(900,500,0,0,1600,1000)
$before=ConvertTo-Json -InputObject $source -Compress
$p=[DeskPagesDragPlacement]::Plan($g,80,100,$false,17,2,205,242,$source,$empty)
Check-Shape $p $g $source $empty
$bottom=@($p | Where-Object Key -eq 'bottom-right')[0]
if($bottom.X -ne 875 -or $bottom.Y -ne 488){throw 'Original mouse grab offset was lost'}
Write-Host 'PASS: four-item rectangle, scrambled file order and exact original mouse grab offset retained.'
$p=[DeskPagesDragPlacement]::Plan($g,80,100,$true,17,2,205,242,$source,$empty)
Check-Shape $p $g $source $empty
foreach($item in $p){if(($item.X-17)%80 -ne 0 -or ($item.Y-2)%100 -ne 0){throw 'Native grid origin not respected'}}
Write-Host 'PASS: snapped group retains spatial shape with nonzero native grid origin.'
$occupied=[DeskPagesDragPlacement+Item[]]@((Item 'old' $p[0].X $p[0].Y))
$old=ConvertTo-Json -InputObject $occupied -Compress
$p=[DeskPagesDragPlacement]::Plan($g,80,100,$true,17,2,205,242,$source,$occupied)
Check-Shape $p $g $source $occupied
if((ConvertTo-Json -InputObject $occupied -Compress) -ne $old -or (ConvertTo-Json -InputObject $source -Compress) -ne $before){throw 'Planner changed existing/source icons'}
Write-Host 'PASS: collision shifts entire group together; source and stationary coordinates unchanged.'
$negative=[int[]]@(-400,300,-1920,0,0,1080)
$p=[DeskPagesDragPlacement]::Plan($negative,80,100,$false,17,2,205,242,$source,$empty);Check-Shape $p $negative $source $empty
$edge=[int[]]@(1599,999,0,0,1600,1000)
$p=[DeskPagesDragPlacement]::Plan($edge,80,100,$false,17,2,205,242,$source,$empty);Check-Shape $p $edge $source $empty
Write-Host 'PASS: negative monitor coordinates and edge clamping preserve whole selection.'
$small=[int[]]@(50,50,0,0,160,200)
if($null -ne [DeskPagesDragPlacement]::Plan($small,80,100,$true,17,2,205,242,$source,$empty)){throw 'Oversized group was repacked'}
$one=[DeskPagesDragPlacement+Item[]]@((Item 'one' 0 0));$full=[DeskPagesDragPlacement+Item[]]@((Item 'old' 0 0))
if($null -ne [DeskPagesDragPlacement]::Plan([int[]]@(40,50,0,0,80,100),80,100,$true,0,0,10,10,$one,$full)){throw 'Full page accepted overlapping placement'}
Write-Host 'PASS: insufficient group space refuses transfer instead of splitting/repacking selection.'
$rng=[Random]::new(7415)
for($i=0;$i -lt 300;$i++){
 $g=[int[]]@($rng.Next(-100,1800),$rng.Next(-100,1200),0,0,1600,1000)
 $p=[DeskPagesDragPlacement]::Plan($g,80,100,($i%2 -eq 0),17,2,205,242,$source,$empty);Check-Shape $p $g $source $empty
}
Write-Host 'PASS: 300 seeded grab/drop cases retain key associations, exact relative positions and complete on-screen bounds.'
