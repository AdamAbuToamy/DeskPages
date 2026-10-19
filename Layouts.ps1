function Save-DpPageLayout {
    param([int]$Page)
    if ($script:DpSuspended) {return}
    if (Test-Path -LiteralPath (Join-Path $script:DpStore 'transition.xml')) {throw 'Cannot save a layout during an incomplete transition.'}
    if($script:DpState.PSObject.Properties['Workspace'] -and $Page -in @($script:DpState.Workspace.Locked) -and (Test-Path -LiteralPath (Join-Path $script:DpLayoutStore "page-$Page.xml"))){Restore-DpPageLayout $Page}
    Connect-DpDesktop
    $icons = @([OriginalDesktopReader]::Read($script:originalDesktop))
    Assert-DpPageView -Page $Page -Icons $icons
    if((Get-Command Sync-DpSecondaryIcons -ErrorAction SilentlyContinue) -and -not(Test-Path -LiteralPath (Join-Path $script:DpStore 'transition.xml'))){Sync-DpSecondaryIcons $icons}
    if (-not $script:DpLastLayoutSignature) {$script:DpLastLayoutSignature=@{}}
    $signature=($icons | Sort-Object -Property @('Key','Name') | ForEach-Object {"$($_.Key)|$($_.Name)|$($_.X)|$($_.Y)"}) -join "`n"
    $path = Join-Path $script:DpLayoutStore "page-$Page.xml"
    if ($script:DpLastLayoutSignature.ContainsKey($Page) -and
        $script:DpLastLayoutSignature[$Page] -eq $signature -and
        (Test-Path -LiteralPath $path)) {return}
    $snapshot = [PSCustomObject]@{Page=$Page; SavedAt=[DateTime]::UtcNow; Icons=$icons}
    $history=Join-Path $script:DpLayoutStore 'History'
    New-Item -ItemType Directory -Path $history -Force | Out-Null
    if (Test-Path -LiteralPath $path) {
        Copy-Item -LiteralPath $path -Destination (Join-Path $history ("page-$Page-"+[DateTime]::UtcNow.Ticks+'.xml'))
    }
    Write-DpAtomicXml -Path $path -Value $snapshot
    $script:DpLastLayoutSignature[$Page]=$signature
}

function Resolve-DpLayoutTargets {
    param([object[]]$Saved, [object[]]$Current)
    $saved=@($Saved); $now=@($Current)
    $byKey = @{}; $byName = @{}
    foreach ($icon in $saved) {
        if ($icon.PSObject.Properties['Key'] -and $icon.Key) {$byKey[$icon.Key] = $icon}
    }
    foreach ($group in ($saved | Group-Object Name)) {
        if ($group.Count -eq 1) {$byName[$group.Name] = $group.Group[0]}
    }
    $duplicates = @{}
    foreach ($group in ($now | Group-Object Name)) {$duplicates[$group.Name]=$group.Count}
    foreach ($icon in $now) {
            $old = $null
            if ($icon.Key) {$old=$byKey[$icon.Key]}
            if ($null -eq $old -and $duplicates[$icon.Name] -eq 1) {$old=$byName[$icon.Name]}
            if ($null -ne $old) {
                [PSCustomObject]@{Id=$icon.Id; X=[int]$old.X; Y=[int]$old.Y}
            }
    }
}

function Restore-DpPageLayout {
    param([int]$Page,[bool]$AllowMixed=$false)
    $path = Join-Path $script:DpLayoutStore "page-$Page.xml"
    $saved=@()
    if (Test-Path -LiteralPath $path) {
        try {$saved=@((Import-Clixml -LiteralPath $path).Icons | Where-Object {$null -ne $_})}
        catch {
            if (-not (Test-Path -LiteralPath ($path+'.previous'))) {throw}
            $saved=@((Import-Clixml -LiteralPath ($path+'.previous')).Icons | Where-Object {$null -ne $_})
        }
    }

    # Require a stable complete page, rather than two equally empty startup samples.
    $lastSignature=$null; $now=@();$ready=$false
    $settle=[Diagnostics.Stopwatch]::StartNew()
    while($settle.ElapsedMilliseconds -lt 5000) {
        $now=@([OriginalDesktopReader]::Read($script:originalDesktop))
        $signature=($now | ForEach-Object {$_.Key} | Sort-Object) -join "`n"
        try {if(-not $AllowMixed){Assert-DpPageView -Page $Page -Icons $now};$valid=$true} catch {$valid=$false}
        if ($valid -and $signature -eq $lastSignature) {$ready=$true;break}
        $lastSignature=$signature
        Start-Sleep -Milliseconds 20
    }
    if (-not $ready) {throw 'Explorer did not finish displaying the expected page. Saved layouts were preserved.'}
    if(Get-Command Get-DpScopedLayoutIcons -ErrorAction SilentlyContinue){$saved=@(Get-DpScopedLayoutIcons -Saved $saved -Current $now)}
    $targets = @(Resolve-DpLayoutTargets -Saved $saved -Current $now)
    if ($targets.Count) {
        $null=[DeskPagesLayout]::Restore($script:originalDesktop,
            [string[]]@($targets | ForEach-Object {$_.Id}),
            [int[]]@($targets | ForEach-Object {$_.X}),
            [int[]]@($targets | ForEach-Object {$_.Y}))
    }
    if ($targets.Count) {
        $positions=[Diagnostics.Stopwatch]::StartNew();$positioned=$false
        while($positions.ElapsedMilliseconds -lt 1200){
            $actual=@([OriginalDesktopReader]::Read($script:originalDesktop))
            $byId=@{};foreach($icon in $actual){$byId[$icon.Id]=$icon}
            $positioned=$true
            foreach($target in $targets) {
                $icon=$byId[$target.Id]
                if ($null -eq $icon -or [Math]::Abs($icon.X-$target.X) -gt 2 -or [Math]::Abs($icon.Y-$target.Y) -gt 2) {$positioned=$false;break}
            }
            if($positioned){break}
            Start-Sleep -Milliseconds 20
        }
        if(-not $positioned){throw 'Windows did not retain the requested icon positions. Turn off Auto arrange icons; no saved layout was overwritten.'}
    }

}

function Set-DeskPage {
    param([ValidateRange(1,99)][int]$Page,[bool]$Animate=$true,[ValidateRange(-1,1)][int]$Direction=0)
    $feedback=$script:hostReady -and [int]$script:DpState.Current -ne $Page
    if($feedback){[DeskPagesHost]::BeginPageFeedback($Page,$Direction)}
    try {
        Invoke-DpPageTransaction -Page $Page -Animate $Animate -Direction $Direction
        if($feedback){[DeskPagesHost]::CompletePageFeedback()}
    } catch {
        if($feedback){[DeskPagesHost]::FailPageFeedback()}
        throw
    }
}

function Recover-DpDesktop {
    if(-not $script:DpSuspended -and -not (Test-Path -LiteralPath (Join-Path $script:DpStore 'transition.xml'))){
        try{Save-DpPageLayout ([int]$script:DpState.Current)}catch{Write-DpLog 'Show-all kept existing snapshots because the current view could not be verified.'}
    }
    Suspend-DpSession
    Connect-DpDesktop
    $errors=[Collections.Generic.List[string]]::new()
    foreach ($entry in $script:DpState.Items) {
        try {Set-DpHidden -Path $entry.Path -Hidden $false}
        catch {$errors.Add([string]$_)}
    }
    Set-DpSystemIcons 1 -Original
    Refresh-DpDesktop
    # Show-all is a suspended view, never page 1 membership.
    # Preserve every saved page layout and leave manifest.Current intact.
    if ($errors.Count) {throw ($errors -join "`n")}
    Restore-DpMergedDesktop
    Remove-Item -LiteralPath (Join-Path $script:DpStore 'transition.xml') -ErrorAction SilentlyContinue
}

function Read-DpVisibleIcons { [OriginalDesktopReader]::Read($script:originalDesktop) }
function Assert-DpPhysicalPage {
    param([int]$Page)
    foreach($entry in $script:DpState.Items){
        if(-not (Test-Path -LiteralPath $entry.Path)){continue}
        $attrs=[int][IO.File]::GetAttributes($entry.Path)
        if(($attrs -band 1024) -ne 0){continue}
        $hidden=($attrs -band 2) -ne 0
        if($hidden -ne (-not (Test-DpFileVisible $entry $Page))){
            throw 'Physical file visibility does not match the target page; transition blocked.'
        }
    }
}
function Confirm-DpAnimationView {
    param([int]$Page)
    $prior=$null
    $settle=[Diagnostics.Stopwatch]::StartNew()
    while($settle.ElapsedMilliseconds -lt 1200){
        Assert-DpPhysicalPage -Page $Page
        $icons=@([OriginalDesktopReader]::Read($script:originalDesktop))
        Assert-DpPageView -Page $Page -Icons $icons
    if((Get-Command Sync-DpSecondaryIcons -ErrorAction SilentlyContinue) -and -not(Test-Path -LiteralPath (Join-Path $script:DpStore 'transition.xml'))){Sync-DpSecondaryIcons $icons}
        $signature=($icons | Sort-Object Key | ForEach-Object {"$($_.Key)|$($_.X)|$($_.Y)"}) -join "`n"
        if($null -ne $prior -and $signature -eq $prior){return $icons}
        $prior=$signature
        Start-Sleep -Milliseconds 15
    }
    throw 'Target icon positions did not settle; transition blocked.'
}
function Assert-DpPageView {
    param([int]$Page,[object[]]$Icons)
    $keys=@{};foreach($icon in $Icons){$keys[[string]$icon.Key]=$true}
    foreach($system in $script:DpState.SystemIconBackup) {
        $shown=$false
        $aliases=@([string]$system.Id)
        if($system.Id -eq '{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}'){$aliases+=@('{26EE0668-A00A-44D7-9371-BEB064C98683}')}
        foreach($icon in $Icons) {
            foreach($id in $aliases){if(([string]$icon.Key).IndexOf($id,[StringComparison]::OrdinalIgnoreCase) -ge 0){$shown=$true;break}}
            if($shown){break}
        }
        $shared=$script:DpState.PSObject.Properties['Workspace'] -and ($system.Id -in @($script:DpState.Workspace.SystemPinned) -or ((Get-Command Test-DpSecondarySystem -ErrorAction SilentlyContinue) -and (Test-DpSecondarySystem $system.Id)))
        if($shared -and -not $shown){throw 'A pinned system icon is not ready; layout saving is blocked.'}
        if($Page -ne 1 -and -not $shared -and $shown){throw 'System icons from page 1 are still being displayed; layout saving is blocked.'}
        if(($Page -eq 1 -or $shared) -and $system.Exists -and [int]$system.Value -eq 0 -and -not $shown){throw 'An explicitly visible system icon is not ready; layout saving is blocked.'}
    }
    foreach($entry in $script:DpState.Items) {
        if (-not (Test-Path -LiteralPath $entry.Path)) {continue}
        $attrs=[int][IO.File]::GetAttributes($entry.Path)
        if (($attrs -band 1024) -ne 0) {continue}
        $shown=$keys.ContainsKey([string]$entry.Path)
        if ((Test-DpFileVisible $entry $Page) -and -not $shown) {throw 'Page view is incomplete; layout saving is blocked.'}
        if (-not (Test-DpFileVisible $entry $Page) -and $shown) {throw 'Page view contains another page; layout saving is blocked.'}
    }
}
