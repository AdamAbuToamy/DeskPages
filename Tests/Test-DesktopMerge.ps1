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

# Exit uses original positions even after every page has moved its own icons.
$screens=@([PSCustomObject]@{Device='main';Identity='panel-a';Left=300;Top=100;Right=1300;Bottom=900;Primary=$true},[PSCustomObject]@{Device='other';Identity='panel-b';Left=1300;Top=0;Right=2300;Bottom=800;Primary=$false})
$baseline=[PSCustomObject]@{Page=0;Original=$true;Screens=@([PSCustomObject]@{Device='old-main';Identity='panel-a';Left=0;Top=0;Right=1000;Bottom=800;Primary=$true},[PSCustomObject]@{Device='old-other';Identity='panel-b';Left=1000;Top=0;Right=2000;Bottom=800;Primary=$false});Icons=@([PSCustomObject]@{Key='a';Name='a';X=17;Y=12},[PSCustomObject]@{Key='b';Name='b';X=60;Y=62},[PSCustomObject]@{Key='c';Name='c';X=1017;Y=12})}
$exit=@(Resolve-DpMergedDesktop $icons (@($baseline)+$pages) 1000 800 100 100 -Screens $screens)
foreach($expected in @(@('new-a',317,112),@('new-b',360,162),@('new-c',1317,12))){$actual=@($exit | Where-Object Id -eq $expected[0])[0];if($actual.X -ne $expected[1] -or $actual.Y -ne $expected[2]){throw 'Exit changed original free-form or secondary-display anchors.'}}
$wrong=@(Resolve-DpLayoutTargets @([PSCustomObject]@{Key='other-file';Name='same label';X=400;Y=300}) @([PSCustomObject]@{Id='current';Key='actual-file';Name='same label';X=17;Y=12}))
if($wrong.Count){throw 'A shared display name assigned a different file the wrong page coordinates.'}
Write-Host 'PASS: original close-spaced anchors and secondary monitor positions override all page-local positions; shifted origin and monitor identity preserved; keyed items never inherit another file position by label.'

# Migrate an older installation once from its earliest retained page-1 history.
. (Join-Path $base 'StateEngine.ps1')
$temp=Join-Path ([IO.Path]::GetTempPath()) ('dp-original-layout-'+[guid]::NewGuid().ToString('N'))
try{
 $script:DpDesktop=Join-Path $temp 'Desktop';$script:DpStore=Join-Path $temp 'Store';$script:DpLayoutStore=Join-Path $temp 'Layouts'
 New-Item -ItemType Directory -Force $script:DpDesktop,$script:DpStore,(Join-Path $script:DpLayoutStore 'History') | Out-Null
 function Write-DpLog {param($Message)}
 Write-DpAtomicXml (Join-Path $script:DpLayoutStore 'History/page-1-100.xml') ([PSCustomObject]@{Page=1;Icons=@([PSCustomObject]@{Key='a';Name='a';X=17;Y=12},[PSCustomObject]@{Key='c';Name='c';X=217;Y=12})})
 Write-DpAtomicXml (Join-Path $script:DpLayoutStore 'page-1.xml') ([PSCustomObject]@{Page=1;Icons=@([PSCustomObject]@{Key='a';Name='a';X=700;Y=200})})
 $beforeEnv=$env:LOCALAPPDATA;$env:LOCALAPPDATA=$temp
 try{Initialize-DpOriginalDesktop;$path=Join-Path $script:DpStore 'original-desktop.xml';$saved=Import-Clixml $path;if(@($saved.Icons | Where-Object Key -eq 'a')[0].X -ne 17 -or @($saved.Icons | Where-Object Key -eq 'c')[0].X -ne 217){throw 'Migration used the latest page instead of original retained positions.'};$hash=(Get-FileHash $path).Hash;Initialize-DpOriginalDesktop;if((Get-FileHash $path).Hash -ne $hash){throw 'Original desktop checkpoint was overwritten.'}}finally{$env:LOCALAPPDATA=$beforeEnv}
 Write-Host 'PASS: original Desktop positions recovered from earliest existing history; original checkpoint remains unchanged on subsequent starts.'
}finally{Remove-Item -LiteralPath $temp -Recurse -Force}
