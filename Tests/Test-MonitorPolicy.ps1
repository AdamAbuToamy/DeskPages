$ErrorActionPreference='Stop'
$base=Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName System.Drawing
Add-Type -Path (Join-Path $base 'Native/DeskPagesMonitorPolicy.cs') -ReferencedAssemblies @([Drawing.Rectangle].Assembly.Location)
$main=[Drawing.Rectangle]::new(0,0,1920,1040);$right=[Drawing.Rectangle]::new(1920,0,2560,1400);$left=[Drawing.Rectangle]::new(-1920,0,1920,1040)
foreach($other in @($left,$right)){
 $areas=[Drawing.Rectangle[]]@($main,$other)
 foreach($x in @(0,1919)){$expected=if(($x -eq 0 -and $other.Left -lt 0) -or ($x -eq 1919 -and $other.Left -gt 0)){0}elseif($x -eq 0){-1}else{1};if([DeskPagesMonitorPolicy]::EdgeDirection($x,500,$main,$areas,48,$false,$true) -ne $expected){throw 'Internal monitor boundary intercepted a drag.'}}
 if([DeskPagesMonitorPolicy]::EdgeDirection($other.Left,500,$other,$areas,48,$true,$false) -ne 0){throw 'Secondary monitor accepted Primary-only navigation.'}
}
Write-Host 'PASS: shared monitor boundaries remain available for normal Explorer dragging; external edges navigate; Primary-only rejects secondary navigation.'
