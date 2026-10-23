$ErrorActionPreference='Stop'
if([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT){Write-Host 'SKIP: Windows drawing and display scope fixture.';return}
$base=Split-Path $PSScriptRoot -Parent
$temp=Join-Path ([IO.Path]::GetTempPath()) ('dp-snapshot-scope-'+[guid]::NewGuid().ToString('N')+'.cs')
try{
 [IO.File]::WriteAllText($temp,'public static class DeskPagesWorkspaceApi {public static bool PrimaryOnly=true;}')
 Add-Type -Path @($temp,(Join-Path $base 'Native/DeskPagesAnimation.cs'),(Join-Path $base 'Native/DeskPagesSnapshotScope.cs'),(Join-Path $base 'Native/DeskPagesSnapshotFiles.cs')) -ReferencedAssemblies System.Drawing,System.Windows.Forms
}finally{Remove-Item -LiteralPath $temp -Force}
function Assert($Value,$Message){if(-not $Value){throw $Message}}
$main=[Drawing.Rectangle]::new(0,0,1920,1040)
$secondary=[Drawing.Rectangle]::new(-2560,-200,2560,1440)
$third=[Drawing.Rectangle]::new(1920,0,3840,2120)
$scene=[DeskPagesSnapshotScope]::DisplayContext($true,'main:1920x1040:96',@('main','secondary'))
foreach($displays in @(@($main),@($main,$secondary),@($main,$secondary,$third))){
 Assert ([DeskPagesAnimation]::SelectCaptureBounds($main,[Drawing.Rectangle[]]$displays,$true) -eq $main) 'Primary frame geometry changed on hotplug.'
 Assert ([DeskPagesSnapshotScope]::DisplayContext($true,'main:1920x1040:96',[string[]]@($displays | ForEach-Object {$_.ToString()})) -eq $scene) 'Secondary displays changed primary scope.'
}
Assert ([DeskPagesAnimation]::SelectCaptureBounds($main,@($main,$secondary,$third),$false).Width -eq 8320) 'All-display mode lost its atlas.'
Assert ([DeskPagesSnapshotScope]::DisplayContext($true,'main:1920x1040:144',@('main')) -ne $scene) 'Primary DPI change retained stale images.'
Assert ([DeskPagesSnapshotScope]::DisplayContext($false,'main',@('main','secondary')) -ne [DeskPagesSnapshotScope]::DisplayContext($false,'main',@('main'))) 'All-display mode ignored topology.'
Write-Host 'PASS: production bounds/scope selectors preserve primary frames across 1/2/3 displays; primary DPI and all-display topology changes still invalidate. No physical hotplug simulated.'
