# Original Desktop positions have priority over overlapping page-local layouts.
function Resolve-DpMergedDesktop {
    param([object[]]$Icons,[object[]]$Snapshots,[int]$Width,[int]$Height,[int]$SpacingX,[int]$SpacingY,[object[]]$Screens=$null)
    if($SpacingX -lt 16 -or $SpacingY -lt 16 -or $Width -lt $SpacingX -or $Height -lt $SpacingY){throw 'Invalid desktop geometry.'}
    if(-not $Screens.Count){$Screens=@([PSCustomObject]@{Left=0;Top=0;Right=$Width;Bottom=$Height;Primary=$true;Device='primary'})}
    $preferences=[Collections.Generic.List[object]]::new();$seen=@{}
    foreach($snapshot in @($Snapshots | Sort-Object Page)){
        foreach($target in @(Resolve-DpLayoutTargets -Saved @($snapshot.Icons) -Current $Icons)){
            if($seen.ContainsKey($target.Id)){continue}
            $x=[int]$target.X;$y=[int]$target.Y
            if($snapshot.Original -and @($snapshot.Screens).Count){
                $old=@($snapshot.Screens | Where-Object {$x -ge $_.Left -and $x -lt $_.Right -and $y -ge $_.Top -and $y -lt $_.Bottom} | Select-Object -First 1)
                if($old.Count){$o=$old[0];$next=@($Screens | Where-Object {($_.Identity -and $_.Identity -eq $o.Identity) -or (-not $o.Identity -and $_.Device -eq $o.Device)} | Select-Object -First 1);if(-not $next.Count){$next=@($Screens | Where-Object Primary | Select-Object -First 1)};if($next.Count){$x=[int]($next[0].Left+$x-$o.Left);$y=[int]($next[0].Top+$y-$o.Top)}}
            }
            $preferences.Add([PSCustomObject]@{Id=$target.Id;X=$x;Y=$y;Original=[bool]$snapshot.Original});$seen[$target.Id]=$true
        }
    }
    foreach($icon in @($Icons | Sort-Object Key,Name)){if(-not $seen.ContainsKey($icon.Id)){$preferences.Add([PSCustomObject]@{Id=$icon.Id;X=[int]$icon.X;Y=[int]$icon.Y;Original=$false});$seen[$icon.Id]=$true}}
    $placed=[Collections.Generic.List[object]]::new();$reserved=@{}
    # Reserve all valid original anchors before fitting new/displaced files.
    # Free-form original spacing belongs to the user and must not be snapped.
    foreach($wanted in $preferences){if($wanted.Original -and @($Screens | Where-Object {$wanted.X -ge $_.Left -and $wanted.X -lt $_.Right -and $wanted.Y -ge $_.Top -and $wanted.Y -lt $_.Bottom}).Count){$placed.Add([PSCustomObject]@{Id=$wanted.Id;X=$wanted.X;Y=$wanted.Y});$reserved[$wanted.Id]=$true}}
    $free={param([int]$x,[int]$y)
        if(-not @($Screens | Where-Object {$x -ge $_.Left -and $y -ge $_.Top -and $x+$SpacingX -le $_.Right -and $y+$SpacingY -le $_.Bottom}).Count){return $false}
        foreach($prior in $placed){if([Math]::Abs($prior.X-$x) -lt $SpacingX-2 -and [Math]::Abs($prior.Y-$y) -lt $SpacingY-2){return $false}};return $true
    }
    foreach($wanted in $preferences){
        if($reserved.ContainsKey($wanted.Id)){continue}
        $x=[int]$wanted.X;$y=[int]$wanted.Y
        if(-not (& $free $x $y)){
            $found=$false
            foreach($screen in @($Screens | Sort-Object -Property @{Expression='Primary';Descending=$true})){
                $xs=@($preferences | Where-Object {$_.X -ge $screen.Left -and $_.X -lt $screen.Left+$SpacingX} | ForEach-Object {$_.X});$ys=@($preferences | Where-Object {$_.Y -ge $screen.Top -and $_.Y -lt $screen.Top+$SpacingY} | ForEach-Object {$_.Y})
                $ox=$screen.Left;$oy=$screen.Top;if($xs.Count){$ox=[int]($xs | Measure-Object -Minimum).Minimum};if($ys.Count){$oy=[int]($ys | Measure-Object -Minimum).Minimum}
                for($column=$ox;$column+$SpacingX -le $screen.Right -and -not $found;$column+=$SpacingX){for($row=$oy;$row+$SpacingY -le $screen.Bottom;$row+=$SpacingY){if(& $free $column $row){$x=$column;$y=$row;$found=$true;break}}}
                if($found){break}
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
    $original=Join-Path $script:DpStore 'original-desktop.xml'
    if(Test-Path -LiteralPath $original){$saved=Import-Clixml -LiteralPath $original;if($saved.Desktop -eq $script:DpDesktop){$snapshots=@([PSCustomObject]@{Page=0;Original=$true;Icons=@($saved.Icons);Screens=@($saved.Screens)})+@($snapshots)}}
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
    $screens=@(Get-DpExitScreens)
    $targets=@(Resolve-DpMergedDesktop -Icons $icons -Snapshots $snapshots -Width $area.Width -Height $area.Height -SpacingX $spacing.X -SpacingY $spacing.Y -Screens $screens)
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
    Write-DpLog ('Show-all layout verified; icons='+$targets.Count+'. Original Desktop anchors have priority; saved per-page layouts unchanged.')
}

function Get-DpExitScreens {
    if(Get-Command Get-DpMonitorGeometry -ErrorAction SilentlyContinue){return @(Get-DpMonitorGeometry)}
    foreach($screen in [Windows.Forms.Screen]::AllScreens){$g=[DeskPagesLayout]::ContextGeometry($screen.WorkingArea.Left+1,$screen.WorkingArea.Top+1);[PSCustomObject]@{Device=$screen.DeviceName;Left=$g[2];Top=$g[3];Right=$g[4];Bottom=$g[5];Primary=$screen.Primary}}
}
function Initialize-DpOriginalDesktop {param([bool]$FirstRun=$false)
    $destination=Join-Path $script:DpStore 'original-desktop.xml'
    if(Test-Path -LiteralPath $destination){return}
    $screens=@();$icons=@();$source='Live original desktop'
    if($FirstRun){$icons=@(Read-DpVisibleIcons);$screens=@(Get-DpExitScreens)}
    else{
        # Earlier versions retained the initial full page in layout history.
        # Reconstruct earliest known positions once, never from the live mixed view.
        $source='Recovered earliest saved positions';$map=@{};$candidates=@()
        $history=Join-Path $script:DpLayoutStore 'History'
        if(Test-Path -LiteralPath $history){$candidates+=@(Get-ChildItem -LiteralPath $history -File -Filter 'page-1-*.xml' | Sort-Object Name | Select-Object -First 256)}
        $safety=Join-Path $env:LOCALAPPDATA 'DeskPages/SafetyBackups'
        if(Test-Path -LiteralPath $safety){foreach($folder in @(Get-ChildItem -LiteralPath $safety -Directory | Sort-Object Name | Select-Object -First 64)){$path=Join-Path $folder.FullName 'PageLayouts/page-1.xml';if(Test-Path -LiteralPath $path){$candidates+=@(Get-Item -LiteralPath $path)}}}
        $path=Join-Path $script:DpLayoutStore 'page-1.xml';if(Test-Path -LiteralPath $path){$candidates+=@(Get-Item -LiteralPath $path)}
        foreach($file in $candidates){try{$snapshot=Import-Clixml -LiteralPath $file.FullName;foreach($icon in @($snapshot.Icons)){if($icon.Key -and -not $map.ContainsKey([string]$icon.Key)){$map[[string]$icon.Key]=$icon}}}catch{Write-DpLog 'An unreadable old layout was skipped while recovering original positions.'}}
        $icons=@($map.Values)
    }
    if($icons.Count){Write-DpAtomicXml $destination ([PSCustomObject]@{Desktop=$script:DpDesktop;Source=$source;At=[DateTime]::UtcNow;Screens=$screens;Icons=$icons})}
}
