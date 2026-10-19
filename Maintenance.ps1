function Assert-DpInstallAccount {
    $manifest=Join-Path $env:LOCALAPPDATA 'DeskPages\HiddenPages\pages.xml'
    if(Test-Path -LiteralPath $manifest){
        $state=Import-Clixml -LiteralPath $manifest
        if($state.Desktop -ne [Environment]::GetFolderPath('Desktop')){throw 'Saved DeskPages data belongs to another desktop. Installation stopped.'}
    }
}
function Test-DpAdministrator {
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
    return ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}
function Stop-DpExactAgents {
    param([string[]]$AgentPaths)
    foreach ($agent in ($AgentPaths | Select-Object -Unique)) {
        $escaped=[Regex]::Escape([IO.Path]::GetFullPath($agent))
        $pattern='(?i)(?:^|\s)-File\s+(?:"'+$escaped+'"|'+$escaped+')(?=\s|$)'
        $copies=@(Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" |
            Where-Object {$_.ProcessId -ne $PID -and $_.CommandLine -match $pattern -and
                $_.CommandLine -notmatch '(?i)(?:^|\s)-(?:Recover|SelfTest|BuildNative)(?=\s|$)'})
        foreach ($copy in $copies) {
            Stop-Process -Id $copy.ProcessId -Force -ErrorAction Stop
            Wait-Process -Id $copy.ProcessId -Timeout 5 -ErrorAction SilentlyContinue
        }
    }
}

function Copy-DpUpgradeCheckpoint {
    param([string]$DataRoot=(Join-Path $env:LOCALAPPDATA 'DeskPages'))
    $store=Join-Path $DataRoot 'HiddenPages';$layouts=Join-Path $DataRoot 'PageLayouts'
    $manifest=Join-Path $store 'pages.xml'
    $state=Import-Clixml -LiteralPath $manifest
    if(-not $state.PSObject.Properties['Current'] -or -not $state.PSObject.Properties['Items']){throw 'No verified saved state for upgrade fallback.'}
    if(-not (Test-Path -LiteralPath (Join-Path $layouts 'page-1.xml'))){throw 'Main-page layout is missing; upgrade did not stop the old process.'}
    if(@($state.Items | Where-Object {$_.Page -eq $state.Current}).Count -gt 0 -and -not (Test-Path -LiteralPath (Join-Path $layouts ("page-$($state.Current).xml")))){throw 'Active-page layout is missing; old process was not stopped.'}
    $requiredPages=@(@(1)+@($state.Items | ForEach-Object {[int]$_.Page}) | Sort-Object -Unique)
    if(Test-Path -LiteralPath (Join-Path $layouts ("page-$($state.Current).xml"))){$requiredPages=@($requiredPages)+@([int]$state.Current) | Sort-Object -Unique}
    foreach($page in $requiredPages){
        $snapshot=Import-Clixml -LiteralPath (Join-Path $layouts ("page-$page.xml"))
        if(-not $snapshot.PSObject.Properties['Icons'] -or [int]$snapshot.Page -ne $page){throw 'A saved page layout is not readable; upgrade did not stop the old process.'}
    }
    $backup=Join-Path (Join-Path $DataRoot 'UpgradeBackups') ([Guid]::NewGuid().ToString('N'))
    foreach($pair in @(@($store,'HiddenPages'),@($layouts,'PageLayouts'))){
        $destination=Join-Path $backup $pair[1];New-Item -ItemType Directory -Force $destination | Out-Null
        foreach($file in Get-ChildItem -LiteralPath $pair[0] -File | Where-Object {$_.Extension -in @('.xml','.previous','.txt','.plan')}){
            $copy=Join-Path $destination $file.Name;Copy-Item -LiteralPath $file.FullName -Destination $copy
            if((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $copy).Hash){throw 'Saved state changed during upgrade backup; no process was stopped.'}
        }
    }
    return $backup
}
function Stop-DpKnownAgents {
    param([switch]$CheckpointFallback)
    $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    foreach($process in @(Get-CimInstance Win32_Process -Filter "Name='powershell.exe'")) {
        if($process.ProcessId -eq $PID){continue}
        $match=[Regex]::Match([string]$process.CommandLine,'(?i)(?:^|\s)-File\s+(?:"(?<quoted>[^"]+)"|(?<bare>\S+))')
        if(-not $match.Success){continue}
        $file=$match.Groups['quoted'].Value
        if(-not $file){$file=$match.Groups['bare'].Value}
        if([IO.Path]::GetFileName($file) -ine 'Agent.ps1' -or -not (Test-Path -LiteralPath $file -PathType Leaf)){continue}
        $source=Get-Content -LiteralPath $file -Raw
        if(-not $source.Contains('DeskPages-Agent-') -or -not $source.Contains('DpStore')){continue}
        $owner=Invoke-CimMethod -InputObject $process -MethodName GetOwnerSid
        if($owner.Sid -ne $sid){continue}
        if($process.CommandLine -match '(?i)(?:^|\s)-(?:Recover|SelfTest|BuildNative)(?=\s|$)'){continue}
        $request=Join-Path $env:LOCALAPPDATA 'DeskPages\HiddenPages\exit-save.request'
        [IO.File]::WriteAllText($request,'save and exit for upgrade')
        $exited=$false
        for($wait=0;$wait -lt 50;$wait++){
            if(-not (Get-Process -Id $process.ProcessId -ErrorAction SilentlyContinue)){$exited=$true;break}
            Start-Sleep -Milliseconds 100
        }
        if(-not $exited){
            Remove-Item -LiteralPath $request -ErrorAction SilentlyContinue
            if(-not $CheckpointFallback){throw 'Close DeskPages from its tray menu: Exit (keep active page), then retry.'}
            $backup=Copy-DpUpgradeCheckpoint
            $upgradeTask="DeskPages-$sid"
            if(Get-ScheduledTask -TaskName $upgradeTask -ErrorAction SilentlyContinue){Disable-ScheduledTask -TaskName $upgradeTask | Out-Null}
            $still=Get-CimInstance Win32_Process -Filter ("ProcessId="+$process.ProcessId)
            if($null -ne $still){
                $stillOwner=Invoke-CimMethod -InputObject $still -MethodName GetOwnerSid
                if($still.CreationDate -ne $process.CreationDate -or $still.CommandLine -ne $process.CommandLine -or $stillOwner.Sid -ne $sid){throw 'Agent process identity changed during upgrade; no process was stopped.'}
                Write-Host ('Old agent could not save its mixed view; saved manifests/layouts verified in '+$backup+'. Closing only this owned process.')
                Stop-Process -Id $process.ProcessId -Force -ErrorAction Stop
                Wait-Process -Id $process.ProcessId -Timeout 5 -ErrorAction SilentlyContinue
            }
        }
    }
}

function Stop-DpDragBroker {
    param([Parameter(Mandatory=$true)][string]$Directory)
    $expected=Join-Path ([IO.Path]::GetFullPath($Directory)) 'DeskPagesDrag.exe'
    $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    foreach($process in @(Get-CimInstance Win32_Process -Filter "Name='DeskPagesDrag.exe'")){
        if($process.ExecutablePath -ine $expected){continue}
        $owner=Invoke-CimMethod -InputObject $process -MethodName GetOwnerSid
        if($owner.Sid -ne $sid){continue}
        $still=Get-CimInstance Win32_Process -Filter ("ProcessId="+$process.ProcessId)
        if($null -eq $still){continue}
        $stillOwner=Invoke-CimMethod -InputObject $still -MethodName GetOwnerSid
        if($still.ExecutablePath -ine $expected -or $still.CreationDate -ne $process.CreationDate -or $stillOwner.Sid -ne $sid){throw 'Drag helper identity changed; upgrade stopped.'}
        Stop-Process -Id $process.ProcessId -Force -ErrorAction Stop
        Wait-Process -Id $process.ProcessId -Timeout 5 -ErrorAction SilentlyContinue
    }
}
