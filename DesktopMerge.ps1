# Show-all/uninstall has one physical desktop. Keep page 1 anchors and allocate
# other pages into free cells instead of replaying colliding page coordinates.
function Resolve-DpMergedDesktop {
    param([object[]]$Icons,[object[]]$Snapshots,[int]$Width,[int]$Height,[int]$SpacingX,[int]$SpacingY)
    if($SpacingX -lt 16 -or $SpacingY -lt 16 -or $Width -lt $SpacingX -or $Height -lt $SpacingY){throw 'Invalid desktop geometry.'}
    $preferences=[Collections.Generic.List[object]]::new();$seen=@{}
    foreach($snapshot in @($Snapshots | Sort-Object Page)){
        foreach($target in @(Resolve-DpLayoutTargets -Saved @($snapshot.Icons) -Current $Icons)){
            if(-not $seen.ContainsKey($target.Id)){$preferences.Add($target);$seen[$target.Id]=$true}
        }
    }
    foreach($icon in @($Icons | Sort-Object Key,Name)){
        if(-not $seen.ContainsKey($icon.Id)){$preferences.Add([PSCustomObject]@{Id=$icon.Id;X=[int]$icon.X;Y=[int]$icon.Y});$seen[$icon.Id]=$true}
    }
    $originX=0;$originY=0
    if($preferences.Count){
        $validX=@($preferences | Where-Object {$_.X -ge 0 -and $_.X -lt $SpacingX} | ForEach-Object {$_.X})
        $validY=@($preferences | Where-Object {$_.Y -ge 0 -and $_.Y -lt $SpacingY} | ForEach-Object {$_.Y})
        if($validX.Count){$originX=[int]($validX | Measure-Object -Minimum).Minimum}
        if($validY.Count){$originY=[int]($validY | Measure-Object -Minimum).Minimum}
    }
    $placed=[Collections.Generic.List[object]]::new()
    $free={param([int]$x,[int]$y)
        if($x -lt 0 -or $y -lt 0 -or $x+$SpacingX -gt $Width -or $y+$SpacingY -gt $Height){return $false}
        foreach($prior in $placed){if([Math]::Abs($prior.X-$x) -lt $SpacingX-2 -and [Math]::Abs($prior.Y-$y) -lt $SpacingY-2){return $false}}
        return $true
    }
    foreach($wanted in $preferences){
        $x=[int]$wanted.X;$y=[int]$wanted.Y
        if(-not (& $free $x $y)){
            $found=$false
            for($column=$originX;$column+$SpacingX -le $Width -and -not $found;$column+=$SpacingX){
                for($row=$originY;$row+$SpacingY -le $Height;$row+=$SpacingY){
                    if(& $free $column $row){$x=$column;$y=$row;$found=$true;break}
                }
            }
            if(-not $found){throw 'Not enough desktop space to show all pages without overlap. Saved page layouts are kept; reduce icon size or free desktop space before uninstalling.'}
        }
        $placed.Add([PSCustomObject]@{Id=$wanted.Id;X=$x;Y=$y})
    }
    return $placed.ToArray()
}
function Assert-DpAllFilesVisible {
    param([object[]]$Icons)
    $keys=@{};foreach($icon in $Icons){$keys[[string]$icon.Key]=$true}
    foreach($entry in $script:DpState.Items){
        if(-not (Test-Path -LiteralPath $entry.Path)){continue}
        $attributes=[IO.File]::GetAttributes($entry.Path)
        if(($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){continue}
        if(($attributes -band [IO.FileAttributes]::Hidden) -ne 0 -or -not $keys.ContainsKey([string]$entry.Path)){throw 'Explorer has not finished showing all managed desktop files.'}
    }
}
function Restore-DpMergedDesktop {
    $snapshots=@(foreach($page in @($script:DpState.Pages | Sort-Object)){
        $path=Join-Path $script:DpLayoutStore ("page-$page.xml")
        if(Test-Path -LiteralPath $path){
            try{Import-Clixml -LiteralPath $path}catch{if(Test-Path -LiteralPath ($path+'.previous')){Import-Clixml -LiteralPath ($path+'.previous')}else{throw}}
        }
    })
    $clock=[Diagnostics.Stopwatch]::StartNew();$prior=$null;$icons=@();$ready=$false
    while($clock.Elapsed.TotalSeconds -lt 8){
        $icons=@(Read-DpVisibleIcons)
        try{Assert-DpAllFilesVisible $icons;$valid=$true}catch{$valid=$false}
        $signature=($icons | Sort-Object Key | ForEach-Object {"$($_.Key)|$($_.Id)"}) -join "`n"
        if($valid -and $null -ne $prior -and $signature -eq $prior){$ready=$true;break}
        $prior=$signature;Start-Sleep -Milliseconds 50
    }
    if(-not $ready){throw 'Explorer did not finish displaying all desktop files. Saved layouts were preserved.'}
    $spacing=[DeskPagesLayout]::Spacing($script:originalDesktop)
    $area=[Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    $targets=@(Resolve-DpMergedDesktop -Icons $icons -Snapshots $snapshots -Width $area.Width -Height $area.Height -SpacingX $spacing.X -SpacingY $spacing.Y)
    Write-DpAtomicXml -Path (Join-Path $script:DpStore 'uninstall-layout-plan.xml') -Value ([PSCustomObject]@{At=[DateTime]::UtcNow;Targets=$targets;Icons=$icons})
    if($targets.Count){
        $null=[DeskPagesLayout]::Restore($script:originalDesktop,[string[]]@($targets.Id),[int[]]@($targets.X),[int[]]@($targets.Y))
        $clock.Restart();$verified=$false
        while($clock.Elapsed.TotalSeconds -lt 3){
            $actual=@(Read-DpVisibleIcons);Assert-DpAllFilesVisible $actual
            if((@($actual | Sort-Object Key | ForEach-Object {$_.Key}) -join "`n") -ne (@($icons | Sort-Object Key | ForEach-Object {$_.Key}) -join "`n")){throw 'Desktop contents changed during uninstall restoration. Saved layouts were preserved; retry after Explorer settles.'}
            $byId=@{};foreach($icon in $actual){$byId[$icon.Id]=$icon}
            $verified=$true
            foreach($target in $targets){$icon=$byId[$target.Id];if($null -eq $icon -or [Math]::Abs($icon.X-$target.X) -gt 2 -or [Math]::Abs($icon.Y-$target.Y) -gt 2){$verified=$false;break}}
            if($verified){break};Start-Sleep -Milliseconds 50
        }
        if(-not $verified){throw 'Explorer did not retain the merged desktop layout. Turn off Auto arrange icons; saved page layouts and uninstall plan were kept.'}
    }
    Write-DpLog ('Show-all layout verified; icons='+$targets.Count+'. Page 1 positions have priority; saved per-page layouts unchanged.')
}
