function Get-DpBootIdentity {
    (Get-CimInstance Win32_OperatingSystem -ErrorAction Stop).LastBootUpTime.ToUniversalTime().Ticks.ToString()
}
function Get-DpRestartFiles {
    foreach($entry in $script:DpState.Items) {
        if(-not (Test-Path -LiteralPath $entry.Path)){continue}
        $file=Get-Item -LiteralPath $entry.Path -Force
        [PSCustomObject]@{Path=$entry.Path;Page=$entry.Page;Directory=$file.PSIsContainer;Length=$(if($file.PSIsContainer){0}else{$file.Length});WriteTicks=$file.LastWriteTimeUtc.Ticks}
    }
}
function Start-DpRestartCheck {
    $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $task=Get-ScheduledTask -TaskName ('DeskPages-'+$sid) -ErrorAction SilentlyContinue
    if ($null -eq $task -or $task.State -eq 'Disabled' -or $task.Principal.RunLevel -ne 'Highest') {
        throw 'Install automatic startup first with Install-Startup.cmd; an enabled Highest-privilege logon task is required.'
    }
    if (-not (@($task.Actions | Where-Object {[IO.Path]::GetFileName($_.Execute) -ieq 'DeskPagesStartup.exe' -and $_.Arguments -eq '--logon'}).Count)) {
        throw 'The startup task is not configured for DeskPages logon restoration. Reinstall startup before this check.'
    }
    $record=[PSCustomObject]@{Boot=Get-DpBootIdentity;Current=$script:DpState.Current;ExpectedStartupPage=1;Pages=@($script:DpState.Pages);Files=@(Get-DpRestartFiles);At=[DateTime]::UtcNow}
    Write-DpAtomicXml -Path (Join-Path $script:DpStore 'restart-baseline.xml') -Value $record
    @('PENDING: actual Windows reboot has not been verified.',('Baseline UTC: '+$record.At.ToString('o')),('Page: '+$record.Current),'After restarting, run Restart-Report.cmd or use Open restart report in the tray.') |
        Set-Content -LiteralPath (Join-Path $script:DpStore 'restart-report.txt') -Encoding UTF8
}
function Write-DpRestartFailure {
    param([string]$Message)
    $path=Join-Path $script:DpStore 'restart-baseline.xml'
    if (-not (Test-Path -LiteralPath $path)) {return}
    @('FAIL: DeskPages could not finish startup restoration.',('Checked UTC: '+[DateTime]::UtcNow.ToString('o')),$Message,'Saved layouts and the restart baseline were preserved. Do not treat this as a passed reboot test.') |
        Set-Content -LiteralPath (Join-Path $script:DpStore 'restart-report.txt') -Encoding UTF8
}
function Complete-DpRestartCheck {
    $path=Join-Path $script:DpStore 'restart-baseline.xml'
    if(-not (Test-Path -LiteralPath $path)){return}
    try {
        $before=Import-Clixml -LiteralPath $path
        if($before.Boot -eq (Get-DpBootIdentity)){return}
        $errors=[Collections.Generic.List[string]]::new()
        $expected=[int]$before.Current
        if($before.PSObject.Properties['ExpectedStartupPage']){$expected=[int]$before.ExpectedStartupPage}
        if($expected -ne [int]$script:DpState.Current){$errors.Add('Active page differs from the expected startup page.')}
        if((@($before.Pages) -join ',') -ne (@($script:DpState.Pages) -join ',')){$errors.Add('Page list changed.')}
        $map=@{};foreach($entry in $script:DpState.Items){$map[$entry.Path]=$entry}
        foreach($file in $before.Files) {
            if(-not (Test-Path -LiteralPath $file.Path)){$errors.Add('Missing path: '+$file.Path);continue}
            if(-not $map.ContainsKey($file.Path) -or $map[$file.Path].Page -ne $file.Page){$errors.Add('Membership changed: '+$file.Path)}
            $now=Get-Item -LiteralPath $file.Path -Force
            if(-not $file.Directory -and ($now.Length -ne $file.Length -or $now.LastWriteTimeUtc.Ticks -ne $file.WriteTicks)){$errors.Add('Size/time changed: '+$file.Path)}
            if(($now.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){continue}
            $hidden=($now.Attributes -band [IO.FileAttributes]::Hidden) -ne 0
            if($hidden -ne (-not (Test-DpFileVisible $map[$file.Path] ([int]$script:DpState.Current)))){$errors.Add('Wrong visibility: '+$file.Path)}
        }
        $icons=@(Read-DpVisibleIcons)
        try {Assert-DpPageView -Page $script:DpState.Current -Icons $icons} catch {$errors.Add([string]$_)}
        $lines=@('DeskPages real Windows restart verification',('Checked: '+[DateTime]::UtcNow.ToString('o')),('Previous boot: '+$before.Boot),('Current boot: '+(Get-DpBootIdentity)),('Tracked existing items checked: '+@($before.Files).Count))
        if($errors.Count){$lines+=('FAIL / CHANGED: '+$errors.Count);$lines+=@($errors)}else{$lines+='PASS: page, membership, expected visibility, file size/time and Explorer view checks.'}
        $lines+='Layout positions were verified by startup restoration before this check.'
        $lines+='This report does not certify file contents, all future boots, touchpad behavior or animation smoothness.'
        $lines | Set-Content -LiteralPath (Join-Path $script:DpStore 'restart-report.txt') -Encoding UTF8
        Move-Item -LiteralPath $path -Destination ($path+'.completed-'+[DateTime]::UtcNow.Ticks)
    } catch {Write-DpLog ('Restart verification could not finish: '+[string]$_)}
}
