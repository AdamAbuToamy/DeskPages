$ErrorActionPreference='Stop'
$base=Split-Path $PSScriptRoot -Parent
. (Join-Path $base 'NewItemPlacement.ps1')
function Assert($condition,$message){if(-not $condition){throw $message}}
$root=Join-Path ([IO.Path]::GetTempPath()) ('DeskPages-newitem-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
try{
    $new=Join-Path $root 'new.txt';$old=Join-Path $root 'old.txt'
    [IO.File]::WriteAllText($new,'new');[IO.File]::WriteAllText($old,'old')
    $a=[PSCustomObject]@{Id='new';Key=$new;X=0;Y=0}
    $b=[PSCustomObject]@{Id='old';Key=$old;X=100;Y=0}
    $before=[DateTime]::UtcNow
    $candidate=Get-DpNewItemCandidate -Icons @($a,$b) -Baseline @{$old=$true} -Selected @($new) -Started $before -Folders @($root)
    Assert ($candidate.Id -eq 'new') 'Fresh selected item not recognized'
    Assert ($null -eq (Get-DpNewItemCandidate -Icons @($a,$b) -Baseline @{$old=$true} -Selected @($old) -Started $before -Folders @($root))) 'Unselected download was repositioned'
    Assert ($null -eq (Get-DpNewItemCandidate -Icons @($a,$b) -Baseline @{} -Selected @($new) -Started $before -Folders @($root))) 'Ambiguous multiple new items accepted'
    Assert ($null -eq (Get-DpNewItemCandidate -Icons @($a) -Baseline @{} -Selected @($new) -Started $before.AddMinutes(1) -Folders @($root))) 'Old file rename accepted'
    Assert ($null -eq (Get-DpNewItemCandidate -Icons @($a) -Baseline @{} -Selected @($new) -Started $before -Folders @((Join-Path $root 'elsewhere')))) 'Outside desktop accepted'
    Assert ($null -eq (Get-DpNewItemCandidate -Icons @($a) -Baseline @{$new=$true} -Selected @($new) -Started $before -Folders @($root))) 'Existing hidden/public item accepted'
    Write-Host 'PASS: new-item eligibility rejects old/renamed, unselected, ambiguous, existing and outside-desktop files'
    $target=Resolve-DpNewItemPosition -Geometry @(620,330,0,0,1920,1040) -SpacingX 100 -SpacingY 120 -Occupied @()
    Assert ([Math]::Abs($target.X-620) -le 100 -and [Math]::Abs($target.Y-330) -le 120) 'Position was sent to edge instead of click'
    $occupied=@([PSCustomObject]@{X=$target.X;Y=$target.Y})
    $other=Resolve-DpNewItemPosition -Geometry @(620,330,0,0,1920,1040) -SpacingX 100 -SpacingY 120 -Occupied $occupied
    Assert ([Math]::Abs($other.X-$target.X) -ge 98 -or [Math]::Abs($other.Y-$target.Y) -ge 118) 'New icon overlaps existing icon'
    Assert ($occupied[0].X -eq $target.X -and $occupied[0].Y -eq $target.Y) 'Existing icon moved'
    $negative=Resolve-DpNewItemPosition -Geometry @(-500,300,-1920,0,0,1040) -SpacingX 100 -SpacingY 120 -Occupied @()
    Assert ($negative.X -ge -1920 -and $negative.X+100 -le 0) 'Secondary monitor coordinate lost'
    $edge=Resolve-DpNewItemPosition -Geometry @(1919,1039,0,0,1920,1040) -SpacingX 100 -SpacingY 120 -Occupied @()
    Assert ($edge.X+100 -le 1920 -and $edge.Y+120 -le 1040) 'Target outside work area'
    Assert ($null -eq (Resolve-DpNewItemPosition -Geometry @(30,30,0,0,100,120) -SpacingX 100 -SpacingY 120 -Occupied @([PSCustomObject]@{X=0;Y=0}))) 'Full page allowed overlap'
    Write-Host 'PASS: placement near click, occupied cell avoidance, unchanged old icons, negative monitor coordinates, edges and full-page refusal'
}finally{Remove-Item -LiteralPath $root -Recurse -Force}
