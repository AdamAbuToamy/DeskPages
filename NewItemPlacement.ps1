# New items created from a desktop background menu inherit that menu's location.
# Only one freshly created, selected physical item is eligible; existing icons
# are never repositioned. Explorer label editing is allowed to finish first.
function Stop-DpNewItemPlacement {
    $script:DpNewItemContext=$null
    [DeskPagesHost]::WatchNewItem($false)
}
function Resolve-DpNewItemPosition {
    param([int[]]$Geometry,[int]$SpacingX,[int]$SpacingY,[object[]]$Occupied,[switch]$GridOnly)
    if($Geometry.Count -ne 6 -or $SpacingX -lt 16 -or $SpacingY -lt 16){throw 'Invalid new-item geometry.'}
    $left=$Geometry[2];$top=$Geometry[3];$right=$Geometry[4]-$SpacingX;$bottom=$Geometry[5]-$SpacingY
    if($right -lt $left -or $bottom -lt $top){return}
    $x=[Math]::Max($left,[Math]::Min($right,$Geometry[0]-[int]($SpacingX/2)))
    $y=[Math]::Max($top,[Math]::Min($bottom,$Geometry[1]-16))
    $choices=[Collections.Generic.List[object]]::new()
    # Search near the click first, confined to the monitor which was clicked.
    for($column=$left;$column -le $right;$column+=$SpacingX){
        for($row=$top;$row -le $bottom;$row+=$SpacingY){
            $choices.Add([PSCustomObject]@{X=$column;Y=$row;Distance=([double]($column-$x)*($column-$x)+[double]($row-$y)*($row-$y))})
        }
    }
    if(-not $GridOnly){$choices.Add([PSCustomObject]@{X=$x;Y=$y;Distance=-1})}
    foreach($choice in @($choices | Sort-Object Distance,X,Y)){
        $free=$true
        foreach($icon in $Occupied){
            if([Math]::Abs($icon.X-$choice.X) -lt $SpacingX-2 -and [Math]::Abs($icon.Y-$choice.Y) -lt $SpacingY-2){$free=$false;break}
        }
        if($free){return [PSCustomObject]@{X=[int]$choice.X;Y=[int]$choice.Y}}
    }
}
function Get-DpNewItemCandidate {
    param([object[]]$Icons,[hashtable]$Baseline,[string[]]$Selected,[DateTime]$Started,[string[]]$Folders,[string]$SeenIdentity='')
    $fresh=@($Icons | Where-Object {-not $Baseline.ContainsKey([string]$_.Key)})
    if($fresh.Count -ne 1){return}
    $icon=$fresh[0];$path=[string]$icon.Key
    if([IO.Path]::GetDirectoryName($path) -notin $Folders){return}
    $item=Get-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
    if($null -eq $item -or ([int]$item.Attributes -band (2 -bor 4 -bor 1024)) -ne 0){return}
    if($item.CreationTimeUtc -lt $Started.AddSeconds(-2)){return}
    if($SeenIdentity){
        # Same file after Explorer renames it or clears its selection.
        try{$identity=[DeskPagesLayout]::CreationIdentity($path)}catch{return}
        if($identity -ne $SeenIdentity){return}
    }elseif($path -notin $Selected){return}
    return $icon
}
function Set-DpNewItemStage {
    param([string]$Stage)
    if($script:DpNewItemContext -and $script:DpNewItemContext.Stage -eq $Stage){return}
    if($script:DpNewItemContext){$script:DpNewItemContext.Stage=$Stage}
    Write-DpLog ('New item: '+$Stage)
}
function Start-DpNewItemPlacement {
    Stop-DpNewItemPlacement
    $point=[DeskPagesHost]::TakeDesktopContextClick()
    if($null -eq $point -or $script:DpSuspended -or (Test-Path -LiteralPath (Join-Path $script:DpStore 'transition.xml'))){return}
    Connect-DpDesktop
    # Auto-arrange menu state on an empty page is not a prerequisite.
    # Do not change any Explorer sort/arrange preference here.
    $geometry=[DeskPagesLayout]::ContextGeometry($point[0],$point[1])
    if($script:DpState.PSObject.Properties['Workspace'] -and $script:DpState.Workspace.PrimaryOnly){
        $primary=@(Get-DpPageScreens)[0]
        $geometry=[int[]]@($geometry[0],$geometry[1],$primary.Left,$primary.Top,$primary.Right,$primary.Bottom)
    }
    $icons=@(Read-DpVisibleIcons)
    Assert-DpPageView -Page ([int]$script:DpState.Current) -Icons $icons
    $baseline=@{};foreach($icon in $icons){$baseline[[string]$icon.Key]=$true}
    foreach($item in @(Get-ChildItem -LiteralPath $script:DpDesktop,$script:DpPublic -Force -ErrorAction Stop)){$baseline[$item.FullName]=$true}
    $script:DpNewItemContext=[PSCustomObject]@{Page=[int]$script:DpState.Current;Started=[DateTime]::UtcNow;Geometry=$geometry;Baseline=$baseline;Candidate='';CandidateAt=[DateTime]::UtcNow;SeenIdentity='';LastX=$null;LastY=$null;StableAt=[DateTime]::UtcNow;Requested=$null;Attempts=0;Stage=''}
    Set-DpNewItemStage ('context armed; page='+$script:DpState.Current+'; visible='+$icons.Count)
    try{Write-DpLog ('Desktop view: '+[DeskPagesLayout]::DescribeView($script:originalDesktop))}catch{Write-DpLog 'Desktop view diagnostics unavailable; creation tracking continues.'}
    [DeskPagesHost]::WatchNewItem($true)
}
function Update-DpNewItemPlacement {
    $context=$script:DpNewItemContext
    if($null -eq $context){[DeskPagesHost]::WatchNewItem($false);return}
    if($script:DpSuspended -or $context.Page -ne [int]$script:DpState.Current -or ([DateTime]::UtcNow-$context.Started).TotalSeconds -gt 60 -or (Test-Path -LiteralPath (Join-Path $script:DpStore 'transition.xml'))){Set-DpNewItemStage 'cancelled: page/session changed or timeout';Stop-DpNewItemPlacement;return}
    $editing=[DeskPagesLayout]::IsLabelEditing()
    $icons=@(Read-DpVisibleIcons)
    $selected=@()
    try{$selected=@($script:originalDesktop.Document.SelectedItems() | ForEach-Object {[string]$_.Path})}catch{Set-DpNewItemStage 'selection temporarily unavailable; checking edit identity'}
    if($editing){
        # Observe the actual focused PIDL while label editing is active.
        # It remains identifiable after clicking blank space to end rename.
        try{$editingId=[DeskPagesLayout]::EditingId($script:originalDesktop)}catch{Set-DpNewItemStage 'waiting for Explorer editing identity';return}
        $selected+=@($icons | Where-Object {$_.Id -eq $editingId} | ForEach-Object {[string]$_.Key})
    }
    $icon=Get-DpNewItemCandidate -Icons $icons -Baseline $context.Baseline -Selected $selected -Started $context.Started -Folders @($script:DpDesktop,$script:DpPublic) -SeenIdentity $context.SeenIdentity
    if($null -eq $icon){Set-DpNewItemStage 'waiting: one new selected or edited physical item';return}
    if(-not $context.SeenIdentity){
        try{$context.SeenIdentity=[DeskPagesLayout]::CreationIdentity([string]$icon.Key)}catch{Set-DpNewItemStage 'waiting for file identity after rename';return}
        Set-DpNewItemStage 'new item identified by file ID'
    }
    if($context.Candidate -ne $icon.Key){$context.Candidate=$icon.Key;$context.CandidateAt=[DateTime]::UtcNow;$context.Requested=$null;$context.Attempts=0}
    if($editing){Set-DpNewItemStage 'identity retained; waiting for label edit to finish';return}
    if([DeskPagesHost]::CreationInputBusy){Set-DpNewItemStage 'waiting for mouse button release';return}
    if($null -eq $context.Requested){
        # Explorer may relocate a provisional new icon. Wait for stable native
        # coordinates; neither coordinate changes nor menu input cancel it.
        if($context.LastX -ne $icon.X -or $context.LastY -ne $icon.Y){
            $context.LastX=$icon.X;$context.LastY=$icon.Y;$context.StableAt=[DateTime]::UtcNow
            Set-DpNewItemStage 'waiting for provisional icon position to settle';return
        }
        if(([DateTime]::UtcNow-$context.StableAt).TotalMilliseconds -lt 250){return}
    }
    if(([DateTime]::UtcNow-$context.CandidateAt).TotalMilliseconds -lt 250){return}
    if($null -ne $context.Requested){
        $spacing=[DeskPagesLayout]::Spacing($script:originalDesktop)
        if([Math]::Abs($icon.X-$context.Requested.X) -le $spacing.X/2 -and [Math]::Abs($icon.Y-$context.Requested.Y) -le $spacing.Y/2){
            Assert-DpPageView -Page $context.Page -Icons $icons
            Set-DpFilePage -Page $context.Page -Commit:$false
            Save-DpPageLayout $context.Page
            Set-DpNewItemStage 'position verified and page layout saved'
            try{Write-DpLog ('Desktop view after creation: '+[DeskPagesLayout]::DescribeView($script:originalDesktop))}catch{}
            Stop-DpNewItemPlacement
            return
        }
        if($context.Attempts -ge 3){Set-DpNewItemStage 'Explorer rejected requested position; no full-layout restore attempted';Stop-DpNewItemPlacement;return}
    }
    Assert-DpPageView -Page $context.Page -Icons $icons
    $spacing=[DeskPagesLayout]::Spacing($script:originalDesktop)
    $target=Resolve-DpNewItemPosition -Geometry $context.Geometry -SpacingX $spacing.X -SpacingY $spacing.Y -Occupied @($icons | Where-Object {$_.Id -ne $icon.Id})
    if($null -eq $target){Set-DpNewItemStage 'no free nearby position';Stop-DpNewItemPlacement;return}
    $null=[DeskPagesLayout]::Restore($script:originalDesktop,[string[]]@($icon.Id),[int[]]@($target.X),[int[]]@($target.Y))
    $context.Requested=$target;$context.Attempts++
    Set-DpNewItemStage ('requested position '+$target.X+','+$target.Y)
}
