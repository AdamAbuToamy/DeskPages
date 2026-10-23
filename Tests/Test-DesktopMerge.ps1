$ErrorActionPreference='Stop'
$base=Split-Path $PSScriptRoot -Parent
. (Join-Path $base 'Layouts.ps1')
. (Join-Path $base 'DesktopMerge.ps1')
$icons=@(
 [PSCustomObject]@{Id='new-a';Key='a';Name='a';X=0;Y=0},
 [PSCustomObject]@{Id='new-b';Key='b';Name='b';X=100;Y=0},
 [PSCustomObject]@{Id='new-c';Key='c';Name='c';X=0;Y=0},
 [PSCustomObject]@{Id='new-d';Key='d';Name='d';X=0;Y=100})
$pages=@(
 [PSCustomObject]@{Page=1;Icons=@([PSCustomObject]@{Key='a';Name='a';X=0;Y=0},[PSCustomObject]@{Key='b';Name='b';X=100;Y=0})},
 [PSCustomObject]@{Page=2;Icons=@([PSCustomObject]@{Key='c';Name='c';X=0;Y=0})},
 [PSCustomObject]@{Page=3;Icons=@([PSCustomObject]@{Key='d';Name='d';X=0;Y=0})})
$original=ConvertTo-Json -InputObject $pages -Depth 10 -Compress
$plan=@(Resolve-DpMergedDesktop -Icons $icons -Snapshots $pages -Width 400 -Height 300 -SpacingX 100 -SpacingY 100)
if($plan.Count -ne 4 -or $plan[0].Id -ne 'new-a' -or $plan[1].X -ne 100 -or $plan[1].Y -ne 0){throw 'Main-page anchors changed'}
if(@($plan | Group-Object {"$($_.X),$($_.Y)"} | Where-Object Count -gt 1).Count){throw 'Collision in uninstall layout'}
if((ConvertTo-Json -InputObject $pages -Depth 10 -Compress) -ne $original){throw 'Saved page layouts mutated'}
Write-Host 'PASS: uninstall preserves main-page anchors, resolves three-page collisions and does not overwrite page layouts'
$again=@(Resolve-DpMergedDesktop -Icons $icons -Snapshots $pages -Width 400 -Height 300 -SpacingX 100 -SpacingY 100)
if((ConvertTo-Json -InputObject $again -Compress) -ne (ConvertTo-Json -InputObject $plan -Compress)){throw 'Uninstall plan is not deterministic'}
Write-Host 'PASS: repeated recovery generates the same merged positions and uses current PIDLs'
$blocked=$false;try{Resolve-DpMergedDesktop -Icons $icons -Snapshots $pages -Width 100 -Height 100 -SpacingX 100 -SpacingY 100}catch{$blocked=$true}
if(-not $blocked){throw 'Insufficient space silently overlapped or dropped files'}
Write-Host 'PASS: insufficient space blocks success instead of overlapping/dropping icons'
