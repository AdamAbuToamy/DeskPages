$ErrorActionPreference='Stop'
$base=Split-Path $PSScriptRoot -Parent
. (Join-Path $base 'StateEngine.ps1')
. (Join-Path $base 'Layouts.ps1')
$root=Join-Path ([IO.Path]::GetTempPath()) ('DeskPages-layout-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
try {
    $script:DpStore=$root;$script:DpLayoutStore=$root
    $path=Join-Path $root 'page-1.xml';[IO.File]::WriteAllText($path,'preserve original layout bytes')
    $hash=(Get-FileHash $path).Hash
    function Connect-DpDesktop {throw 'Desktop adapter must not be touched by a blocked save.'}
    $script:DpSuspended=$true
    Save-DpPageLayout 1
    if((Get-FileHash $path).Hash -ne $hash){throw 'Recovery overwrote the saved layout.'}
    Write-Host 'PASS: suspended recovery cannot save mixed layout'
    $script:DpSuspended=$false
    Write-DpAtomicXml -Path (Join-Path $root 'transition.xml') -Value ([PSCustomObject]@{From=1;To=2})
    $blocked=$false;try {Save-DpPageLayout 1} catch {$blocked=$_.ToString().Contains('incomplete transition')}
    if(-not $blocked -or (Get-FileHash $path).Hash -ne $hash){throw 'Pending journal did not block layout saving.'}
    Write-Host 'PASS: incomplete transition cannot overwrite layout'
    $a=Join-Path $root 'a.txt';$b=Join-Path $root 'b.txt'
    [IO.File]::WriteAllText($a,'a');[IO.File]::WriteAllText($b,'b')
    $script:DpState=[PSCustomObject]@{Items=@([PSCustomObject]@{Path=$a;Page=1},[PSCustomObject]@{Path=$b;Page=2})}
    Assert-DpPageView -Page 1 -Icons @([PSCustomObject]@{Key=$a})
    Write-Host 'PASS: correct page membership accepted'
    $blocked=$false;try {Assert-DpPageView -Page 1 -Icons @([PSCustomObject]@{Key=$a},[PSCustomObject]@{Key=$b})} catch {$blocked=$true}
    if(-not $blocked){throw 'Mixed page view accepted.'}
    Write-Host 'PASS: mixed page view rejected'
    $blocked=$false;try {Assert-DpPageView -Page 1 -Icons @()} catch {$blocked=$true}
    if(-not $blocked){throw 'Incomplete empty startup view accepted.'}
    Write-Host 'PASS: empty startup view rejected when files are expected'
    Assert-DpPageView -Page 3 -Icons @()
    Write-Host 'PASS: genuinely empty page accepted'
    $guid='{645FF040-5081-101B-9F08-00AA002F954E}'
    $script:DpState=[PSCustomObject]@{Items=@();SystemIconBackup=@([PSCustomObject]@{Id=$guid;Exists=$true;Value=0})}
    $blocked=$false;try {Assert-DpPageView -Page 2 -Icons @([PSCustomObject]@{Key=('::'+$guid)})} catch {$blocked=$true}
    if(-not $blocked){throw 'System icon leaked onto an empty page.'}
    Write-Host 'PASS: page 2 cannot settle while page 1 system icons are visible'
    $blocked=$false;try {Assert-DpPageView -Page 1 -Icons @()} catch {$blocked=$true}
    if(-not $blocked){throw 'Missing system icon incorrectly considered ready.'}
    Assert-DpPageView -Page 1 -Icons @([PSCustomObject]@{Key=('::'+$guid)})
    Write-Host 'PASS: explicitly visible system icon must settle before page 1 is ready'

} finally {Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue}
