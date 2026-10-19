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
function Set-DpHidden {

    param([string]$Path, [bool]$Hidden)

    if (-not (Test-Path -LiteralPath $Path)) { return }

    $parent = [IO.Path]::GetDirectoryName($Path)
    if ($parent -notin @($script:DpDesktop, $script:DpPublic)) {
        throw "Refusing a path outside desktop folders: $Path"
    }

    $attributes = [int][IO.File]::GetAttributes($Path)
    if (($attributes -band 1024) -ne 0) { return }
    # Do not require write permission just to leave an already-correct item alone.
    if ((($attributes -band 2) -ne 0) -eq $Hidden) { return }

    if ($Hidden) {
        $attributes = ($attributes -band (-bnot 128)) -bor 2
    }
    else {
        $attributes = $attributes -band (-bnot 2)
        if ($attributes -eq 0) { $attributes = 128 }
    }

    [IO.File]::SetAttributes($Path, [IO.FileAttributes]$attributes)

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
        -Name Hidden -ErrorAction SilentlyContinue

    if ($setting.Hidden -eq 1) {
        throw "Turn OFF Explorer > View > Show > Hidden items first."
    }

    if($script:DpState.PSObject.Properties['Workspace'] -and $script:DpState.Workspace.PSObject.Properties['PrimaryOnly'] -and $script:DpState.Workspace.PrimaryOnly){$script:DpScopeScreens=@(Get-DpMonitorGeometry)}
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

        $isHidden = ($attributes -band 2) -ne 0
        $shouldHide = -not (Test-DpFileVisible $entry $Page)

        if ($isHidden -eq $shouldHide) { continue }

        $changes.Add([PSCustomObject]@{
            Path = $file.FullName
            HiddenBefore = $isHidden
            HiddenAfter = $shouldHide
        })
    }

    $applied = [Collections.Generic.List[object]]::new()

    try {
        # Save membership before changing visibility.
        $script:DpState.Items = $entries.ToArray()
        if($PersistMembership){Save-DpState}

        foreach ($change in $changes) {
            # Read the latest attributes and change only Hidden.
            $attributes = [int][IO.File]::GetAttributes($change.Path)
            $change.HiddenBefore = ($attributes -band 2) -ne 0

            if ($change.HiddenAfter) {
                $attributes = ($attributes -band (-bnot 128)) -bor 2
            }
            else {
                $attributes = $attributes -band (-bnot 2)
                if ($attributes -eq 0) { $attributes = 128 }
            }

            [IO.File]::SetAttributes(
                $change.Path, [IO.FileAttributes]$attributes
            )
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
                Set-DpHidden -Path $change.Path `
                    -Hidden $change.HiddenBefore
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
            $showSystem=$Page -eq 1 -or $shared
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
