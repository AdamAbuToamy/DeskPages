$ErrorActionPreference='Stop'
$base=Split-Path $PSScriptRoot -Parent
. (Join-Path $base 'EdgeDrag.ps1')
# Exercise the production edge-navigation entry point. Only Explorer/transaction
# side effects are substituted; page order and motion are observed independently.
function Assert-DpDragItems {param($Files,$Source)}
function Get-DpActivePages {return @(1,7,3)}
function Set-DeskPage {
    param([int]$Page,[int]$Direction)
    $script:observedPage=$Page;$script:observedMotion=$Direction
    $script:DpState.Current=$Page
}
foreach($case in @(
    @{From=7;Edge=-1;To=1;Motion=1},
    @{From=7;Edge=1;To=3;Motion=-1},
    @{From=1;Edge=-1;To=3;Motion=1},
    @{From=3;Edge=1;To=1;Motion=-1}
)){
    $script:DpState=[PSCustomObject]@{Current=$case.From;Pages=@(1,3,7)}
    $script:DpEdgeDrag=[PSCustomObject]@{Current=$case.From;Source=$case.From;Files=@()}
    Move-DpEdgeDragPage -Direction $case.Edge
    if($script:observedPage -ne $case.To){throw 'Edge changed destination or ignored user page order.'}
    if($script:observedMotion -ne $case.Motion){throw 'Slide moved with the edge rather than exposing that neighboring page.'}
    if($script:DpEdgeDrag.Current -ne $case.To){throw 'Drag session did not follow the page.'}
}
Write-Host 'PASS: production edge navigation, left/right and wraparound, custom page order and drag-session continuity. Left edge slides outgoing page right; right edge slides outgoing page left.'
Write-Host 'This verifies the compositor direction argument. Physical rendering and OLE smoothness still require Windows.'
