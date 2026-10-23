function Update-DpIndex {

    $known = @{}
    foreach ($entry in $script:DpState.Items) {
        $known[$entry.Path] = $true
    }

    $files = Get-ChildItem -LiteralPath $script:DpDesktop `
        -Force -ErrorAction Stop

    foreach ($file in $files) {
        if ($known.ContainsKey($file.FullName)) { continue }

        $attributes = [int]$file.Attributes

        # Leave existing hidden/system items and links alone.
        if (($attributes -band (2 -bor 4 -bor 1024)) -ne 0) {
            continue
        }

        if ($file.Name -ieq "desktop.ini") { continue }

        $script:DpState.Items = @($script:DpState.Items) + @(
            [PSCustomObject]@{
                Path = $file.FullName
                Page = [int]$script:DpState.Current
            }
        )
    }

    Save-DpState

}
# Ownership is separate from pages.xml: uncommitted drag membership must never
# be persisted merely to record an attribute change. Keep this journal with the
# page manifest, including during upgrades, undo and rescue.
function Get-DpSystemOwnership {
    $path=Join-Path $script:DpStore 'system-ownership.xml'
    if($script:DpSystemOwnershipPath -ne $path -or $null -eq $script:DpSystemOwnership){
        $map=@{}
        if(Test-Path -LiteralPath $path){
            $map=Import-Clixml -LiteralPath $path -ErrorAction Stop
            if($map -isnot [Collections.IDictionary]){throw 'Invalid attribute ownership journal. Preserve DeskPages data and request recovery.'}
            foreach($key in $map.Keys){if($map[$key] -isnot [bool] -or -not $map[$key]){throw 'Invalid attribute ownership record.'}}
        }
        $script:DpSystemOwnership=$map
        $script:DpSystemOwnershipPath=$path
    }
    return $script:DpSystemOwnership
}
function Get-DpVisibilityAttributes {
    param([string]$Path,[int]$Attributes,[bool]$Hidden)
    $owned=Get-DpSystemOwnership
    if($Hidden){return (($Attributes -band (-bnot 128)) -bor 6)}
    $result=$Attributes -band (-bnot 2)
    # Never clear a System attribute that DeskPages did not add.
    if($owned.Contains($Path)){$result=$result -band (-bnot 4)}
    if($result -eq 0){$result=128}
    return $result
}
function Register-DpSystemOwnership {
    param([string[]]$Paths)
    $owned=Get-DpSystemOwnership
    $next=@{};foreach($key in $owned.Keys){$next[$key]=$true}
    $changed=$false
    foreach($path in $Paths){
        if([IO.Path]::GetDirectoryName($path) -notin @($script:DpDesktop,$script:DpPublic)){throw 'Ownership path is outside desktop folders.'}
        $attributes=[int][IO.File]::GetAttributes($path)
        if(($attributes -band (4 -bor 1024)) -eq 0 -and -not $next.Contains($path)){$next[$path]=$true;$changed=$true}
    }
    if($changed){
        # One durable write per page transition, never one full journal per icon.
        Write-DpAtomicXml -Path $script:DpSystemOwnershipPath -Value $next
        $script:DpSystemOwnership=$next
    }
}
function Set-DpHidden {
    param([string]$Path, [bool]$Hidden)
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $parent = [IO.Path]::GetDirectoryName($Path)
    if ($parent -notin @($script:DpDesktop, $script:DpPublic)) {
        throw "Refusing a path outside desktop folders: $Path"
    }
    $attributes = [int][IO.File]::GetAttributes($Path)
    if (($attributes -band 1024) -ne 0) { return }
    $target=Get-DpVisibilityAttributes $Path $attributes $Hidden
    if($target -eq $attributes){return}
    if($Hidden -and ($attributes -band 4) -eq 0){
        $owned=Get-DpSystemOwnership
        if(-not $owned.Contains($Path)){Register-DpSystemOwnership -Paths @($Path)}
    }
    [IO.File]::SetAttributes($Path, [IO.FileAttributes]$target)
}
function Set-DpFilePage {

    param(
        [Parameter(Mandatory)]
        [ValidateRange(1,99)]
        [int]$Page,
        [bool]$Commit=$true,
        [bool]$RegisterNew=$true,
        [bool]$PersistMembership=$true
    )

    $setting = Get-ItemProperty `
        "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced" `
        -ErrorAction SilentlyContinue

    if ($setting.ShowSuperHidden -eq 1) {
        throw "Keep Explorer > Folder Options > View > Hide protected operating system files enabled. Ordinary Hidden items may stay ON."
    }

    if($script:DpState.PSObject.Properties['Workspace'] -and $script:DpState.Workspace.PSObject.Properties['PrimaryOnly'] -and $script:DpState.Workspace.PrimaryOnly){$script:DpScopeScreens=@(Get-DpMonitorGeometry)}
    $script:DpVisibilityContext=$null
    if($script:DpState.Workspace.MonitorPagesEnabled){$visiblePages=@{};foreach($id in @(Get-DpVisiblePages $Page $script:DpScopeScreens)){$visiblePages[[int]$id]=$true};$script:DpVisibilityContext=[PSCustomObject]@{State=$script:DpState;Page=$Page;Pages=$visiblePages}}
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $oldPage = $script:DpState.Current
    $oldItems = @($script:DpState.Items)

    $known = @{}
    foreach ($entry in $oldItems) {
        $known[$entry.Path] = $entry
    }

    $diskItems = @(
        Get-ChildItem -LiteralPath $script:DpDesktop, $script:DpPublic `
            -Force -ErrorAction Stop
    )

    $entries = [Collections.Generic.List[object]]::new()
    foreach ($entry in $oldItems) {
        $entries.Add($entry)
    }

    $changes = [Collections.Generic.List[object]]::new()

    foreach ($file in $diskItems) {
        $attributes = [int]$file.Attributes
        # Never change attributes through symlinks/junctions/cloud reparse points.
        if (($attributes -band 1024) -ne 0) { continue }
        $entry = $known[$file.FullName]

        if ($null -eq $entry) {
            if (-not $RegisterNew) {continue}
            if (($attributes -band (2 -bor 4 -bor 1024)) -ne 0) {
                continue
            }
            if ($file.Name -ieq "desktop.ini") { continue }

            $entry = [PSCustomObject]@{
                Path = $file.FullName
                Page = $(if(Get-Command Get-DpIncomingPage -ErrorAction SilentlyContinue){Get-DpIncomingPage}else{[int]$oldPage})
            }
            $entries.Add($entry)
        }

        $shouldHide = -not (Test-DpFileVisible $entry $Page)

        if ((Get-DpVisibilityAttributes $file.FullName $attributes $shouldHide) -eq $attributes) { continue }

        $changes.Add([PSCustomObject]@{
            Path = $file.FullName
            AttributesBefore = $attributes
            HiddenAfter = $shouldHide
        })
    }

    $applied = [Collections.Generic.List[object]]::new()

    try {
        # Save membership before changing visibility.
        $script:DpState.Items = $entries.ToArray()
        if($PersistMembership){Save-DpState}

        Register-DpSystemOwnership -Paths @($changes | Where-Object {$_.HiddenAfter} | ForEach-Object {$_.Path})
        foreach ($change in $changes) {
            # Capture both controlled bits immediately before the write.
            $change.AttributesBefore = [int][IO.File]::GetAttributes($change.Path)
            Set-DpHidden -Path $change.Path -Hidden $change.HiddenAfter
            $applied.Add($change)
        }

        if ($Commit) {
            $script:DpState.Current = $Page
            Save-DpState
        }

        $watch.Stop()
        Write-Host (
            "Page {0} | Changed: {1} | Time: {2:N2} seconds" -f `
            $Page, $applied.Count, $watch.Elapsed.TotalSeconds
        ) -ForegroundColor Cyan
    }
    catch {
        $failure = $_

        foreach ($change in $applied) {
            try {
                $attributes=[int][IO.File]::GetAttributes($change.Path)
                if(($attributes -band 1024) -ne 0){throw 'Path became a reparse point during rollback.'}
                $attributes=($attributes -band (-bnot (2 -bor 4 -bor 128))) -bor ($change.AttributesBefore -band 6)
                if($attributes -eq 0){$attributes=128}
                [IO.File]::SetAttributes($change.Path,[IO.FileAttributes]$attributes)
            }
            catch {
                Write-Warning "Could not restore: $($change.Path)"
            }
        }

        $script:DpState.Current = $oldPage
        $script:DpState.Items = $oldItems
        try { if($PersistMembership){Save-DpState} } catch {
            Write-Warning "Could not restore the saved page state."
        }

        throw $failure
    }

}
function Set-DpSystemIcons {
    param([int]$Page,[switch]$Original)
    $key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($script:DpIconKey)
    $changed=$false
    try {
        foreach($entry in $script:DpState.SystemIconBackup) {
            $exists=$entry.Id -in @($key.GetValueNames())
            $shared=-not $Original -and $script:DpState.PSObject.Properties['Workspace'] -and ($entry.Id -in @($script:DpState.Workspace.SystemPinned) -or ((Get-Command Test-DpSecondarySystem -ErrorAction SilentlyContinue) -and (Test-DpSecondarySystem $entry.Id)))
            $showSystem=$Page -eq 1 -or $shared -or (-not $Original -and (Get-Command Get-DpVisiblePages -ErrorAction SilentlyContinue) -and 1 -in @(Get-DpVisiblePages $Page))
            if($shared){
                if(-not $exists -or $key.GetValue($entry.Id) -ne 0 -or $key.GetValueKind($entry.Id) -ne [Microsoft.Win32.RegistryValueKind]::DWord){$key.SetValue($entry.Id,0,[Microsoft.Win32.RegistryValueKind]::DWord);$changed=$true}
            } elseif(-not $showSystem) {
                if(-not $exists -or $key.GetValue($entry.Id) -ne 1 -or $key.GetValueKind($entry.Id) -ne [Microsoft.Win32.RegistryValueKind]::DWord) {
                    $key.SetValue($entry.Id,1,[Microsoft.Win32.RegistryValueKind]::DWord);$changed=$true
                }
            } elseif($entry.Exists) {
                $kind=[Enum]::Parse([Microsoft.Win32.RegistryValueKind],[string]$entry.Kind)
                if(-not $exists -or $key.GetValue($entry.Id) -ne $entry.Value -or $key.GetValueKind($entry.Id) -ne $kind) {
                    $key.SetValue($entry.Id,$entry.Value,$kind);$changed=$true
                }
            } elseif($exists) {$key.DeleteValue($entry.Id,$false);$changed=$true}
        }
    } finally {$key.Dispose()}
    return $changed
}
function Refresh-DpDesktop {

    $method = [DeskPagesLayout].GetMethod(
        "GetView",
        ([Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static)
    )

    $view = $null
    try {
        $view = $method.Invoke($null, [object[]]@($originalDesktop))
        [DpOriginalViewRefresh]::Run($view)
    }
    finally {
        if ($null -ne $view) {
            [void][Runtime.InteropServices.Marshal]::ReleaseComObject($view)
        }
    }

}
function Show-DeskFiles {

    foreach ($entry in $script:DpState.Items) {
        try {
            Set-DpHidden -Path $entry.Path -Hidden $false
        }
        catch {
            Write-Warning "Could not show: $($entry.Path)"
        }
    }

    Set-DpSystemIcons -Page 1 -Original
    Refresh-DpDesktop
    Write-Host "Managed files and original system-icon settings restored."

}
