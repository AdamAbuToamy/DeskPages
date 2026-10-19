param([switch]$Interactive)
$ErrorActionPreference='Stop'
if($env:OS -ne 'Windows_NT' -or -not $Interactive){Write-Host 'SKIP: native GPU/DWM monitor simulator requires an interactive Windows session and -Interactive. No desktop changes.';return}
$base=Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -Path (Join-Path $base 'Bin/DeskPagesNative.dll')
[DeskPagesHost]::ConfigureDpi();[DeskPagesAnimation]::Configure($base)
$screen=[Windows.Forms.Screen]::PrimaryScreen.WorkingArea
if($screen.Width -lt 740 -or $screen.Height -lt 440){throw 'Simulator needs at least 740 x 440 working pixels.'}
$bounds=[Drawing.Rectangle]::new($screen.Left+10,$screen.Top+10,720,400)
$layouts=@(@(0,0,360,400,1,360,0,360,400,1),@(0,0,720,200,1,0,200,720,200,1),@(0,0,300,350,1,380,40,340,360,1))
$checks=0;$runs=0
try{
 foreach($layout in $layouts){foreach($primaryOnly in @($false,$true)){foreach($direction in @(-1,1)){
  $regions=[int[]]$layout.Clone();if($primaryOnly){$regions[9]=0}
  $a=[Drawing.Bitmap]::new(720,400);$b=[Drawing.Bitmap]::new(720,400)
  $colors=@([Drawing.Color]::FromArgb(30,70,160),[Drawing.Color]::FromArgb(20,130,60),[Drawing.Color]::FromArgb(180,50,40),[Drawing.Color]::FromArgb(190,140,20))
  try{
   for($page=0;$page -lt 2;$page++){$bitmap=if($page -eq 0){$a}else{$b};$g=[Drawing.Graphics]::FromImage($bitmap);try{$g.Clear([Drawing.Color]::Black);for($i=0;$i -lt 2;$i++){$offset=$i*5;$colorIndex=if($page -eq 1 -and $regions[$offset+4] -ne 0){$i+2}else{$i};$brush=[Drawing.SolidBrush]::new($colors[$colorIndex]);try{$g.FillRectangle($brush,$regions[$offset],$regions[$offset+1],$regions[$offset+2],$regions[$offset+3])}finally{$brush.Dispose()}}}finally{$g.Dispose()}}
   if(-not[DeskPagesAnimation]::BeginRegions($a,$bounds,$regions)){throw [DeskPagesAnimation]::Metrics}
   [DeskPagesAnimation]::Play($b,$direction)
   $watch=[Diagnostics.Stopwatch]::StartNew();$samples=0
   while([DeskPagesAnimation]::IsPlaying -and $watch.ElapsedMilliseconds -lt 2500){
    foreach($i in @(0,1)){$o=$i*5;$x=$bounds.Left+$regions[$o]+[int]($regions[$o+2]/2);$y=$bounds.Top+$regions[$o+1]+[int]($regions[$o+3]/2);$pixel=[Drawing.Bitmap]::new(1,1);try{$g=[Drawing.Graphics]::FromImage($pixel);try{$g.CopyFromScreen($x,$y,0,0,[Drawing.Size]::new(1,1))}finally{$g.Dispose()};$rgb=$pixel.GetPixel(0,0).ToArgb() -band 0xffffff;$allowed=@($colors[$i].ToArgb() -band 0xffffff);if($regions[$o+4]){$allowed+=@($colors[$i+2].ToArgb() -band 0xffffff)};if($rgb -notin $allowed){throw 'Unexpected/blank/foreign-monitor pixels in the synthetic transition.'};$checks++}finally{$pixel.Dispose()}}
    $samples++;Start-Sleep -Milliseconds 12
   }
   if([DeskPagesAnimation]::IsVisible -or $samples -eq 0){throw 'Synthetic animation did not retire or no visible frames were sampled.'};$runs++
  }finally{[DeskPagesAnimation]::Abort();$a.Dispose();$b.Dispose()}
 }}}
 Write-Host "PASS: native DWM/GPU monitor simulator; $runs transitions, $checks pixel checks. Side-by-side, stacked and gaps, both directions and Primary-only. Does not certify physical multi-adapter hardware."
}finally{[DeskPagesAnimation]::Shutdown()}
