$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName System.Drawing
Add-Type -Path (Join-Path $root 'Native/DeskPagesCapturePlan.cs') -ReferencedAssemblies ([Drawing.Rectangle].Assembly.Location)
$random=[Random]::new(836)
$checks=0
foreach($primary in @([Drawing.Rectangle]::new(0,0,1920,1040),[Drawing.Rectangle]::new(-2560,200,2560,1400),[Drawing.Rectangle]::new(400,-2160,3840,2080))){
 for($i=0;$i -lt 1000;$i++){
  $areas=[Drawing.Rectangle[]]@($primary,[Drawing.Rectangle]::new($random.Next(-12000,12000),$random.Next(-6000,6000),$random.Next(800,7680),$random.Next(600,4320)))
  $actual=[DeskPagesCapturePlan]::Bounds($primary,$areas,$true)
  if($actual -ne $primary){throw 'Primary capture expanded across secondary monitors.'}
  $all=[DeskPagesCapturePlan]::Bounds($primary,$areas,$false)
  foreach($area in $areas){if(-not $all.Contains($area)){throw 'All-screen compatibility crop lost a monitor.'}}
  $crop=[DeskPagesCapturePlan]::Intersection($areas[1],$actual)
  if(-not $crop.IsEmpty -and -not $actual.Contains($crop)){throw 'Region escaped the capture texture.'}
  $checks++
 }
}
Write-Host "PASS: $checks randomized primary-only/all-screen capture plans; negative coordinates, stacked displays, large gaps and mixed resolutions. Geometry only, not physical display certification."
