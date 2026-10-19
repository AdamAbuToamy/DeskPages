# Independent entry point: does not start Agent, compositor, gestures or OLE helper.
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
try{
    $base=$PSScriptRoot
    $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $mutex=[Threading.Mutex]::new($false,"Local\DeskPages-Agent-$sid")
    $owned=$false;try{$owned=$mutex.WaitOne(0)}catch [Threading.AbandonedMutexException] {$owned=$true}
    $script:DpStore=Join-Path $env:LOCALAPPDATA 'DeskPages\HiddenPages'
    if(-not $owned){
        [IO.File]::WriteAllText((Join-Path $script:DpStore 'recovery.request'),'recover')
        [Windows.Forms.MessageBox]::Show('Recovery requested from the running DeskPages. It will show managed files and stop. If it is unresponsive, close DeskPages in Task Manager and run Rescue again.','DeskPages Rescue') | Out-Null
        exit 0
    }
    $principal=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
    if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){
        $mutex.ReleaseMutex();$owned=$false;$mutex.Dispose()
        $child=Start-Process powershell.exe -Verb RunAs -Wait -PassThru -ArgumentList ('-NoProfile -STA -ExecutionPolicy Bypass -File "'+$PSCommandPath+'"')
        exit $child.ExitCode
    }
    $script:DpManifest=Join-Path $script:DpStore 'pages.xml'
    $script:DpLayoutStore=Join-Path $env:LOCALAPPDATA 'DeskPages\PageLayouts'
    $script:DpDesktop=[Environment]::GetFolderPath('Desktop');$script:DpPublic=[Environment]::GetFolderPath('CommonDesktopDirectory')
    $script:DpIconKey='Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel'
    function Write-DpLog {param($Message)}
    . (Join-Path $base 'StateEngine.ps1');. (Join-Path $base 'Workspace.ps1')
    $script:DpState=$null
    $candidates=@($script:DpManifest,($script:DpManifest+'.previous'))
    foreach($path in $candidates){try{
        if((Get-Item -LiteralPath $path).Length -gt 67108864){continue}
        $candidate=Import-Clixml -LiteralPath $path -ErrorAction Stop
        $script:DpState=$candidate;Initialize-DpWorkspace;Assert-DpWorkspaceState $candidate;break
    }catch{$script:DpState=$null}}
    if($null -eq $script:DpState){throw 'No readable page manifest was found. Your files were not changed. Preserve AppData\Local\DeskPages and request recovery from its safety backups.'}
    if([Windows.Forms.MessageBox]::Show('Show all managed files and restore the combined desktop arrangement? Saved page layouts will be kept. No file contents will be changed.','DeskPages Rescue',[Windows.Forms.MessageBoxButtons]::YesNo,[Windows.Forms.MessageBoxIcon]::Question,[Windows.Forms.MessageBoxDefaultButton]::Button2) -ne [Windows.Forms.DialogResult]::Yes){exit 0}
    . (Join-Path $base 'Visibility.ps1');. (Join-Path $base 'Layouts.ps1');. (Join-Path $base 'DesktopMerge.ps1')
    Suspend-DpSession
    $errors=[Collections.Generic.List[string]]::new()
    foreach($entry in $script:DpState.Items){try{Set-DpHidden $entry.Path $false}catch{$errors.Add([string]$_)}}
    $ids=@('{20D04FE0-3AEA-1069-A2D8-08002B30309D}','{645FF040-5081-101B-9F08-00AA002F954E}','{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}')
    foreach($entry in $script:DpState.SystemIconBackup){if($entry.Id -notin $ids){throw 'Unexpected system-icon record. File visibility was restored; registry was left untouched.'}}
    $null=Set-DpSystemIcons 1 -Original
    # Compile only the small Explorer adapter sources; independent of a broken runtime DLL.
    Add-Type -AssemblyName System.Drawing
    Add-Type -Path @((Join-Path $base 'Native\DeskPagesLayout.cs'),(Join-Path $base 'Native\OriginalDesktopReader.cs'),(Join-Path $base 'Native\DpOriginalViewRefresh.cs'),(Join-Path $base 'Native\DeskPagesVisibilitySync.cs')) -ReferencedAssemblies @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll')
    function Connect-DpDesktop {
        $shell=New-Object -ComObject Shell.Application;$windows=$shell.Windows();[object]$location=0;[object]$empty=$null;[int]$hwnd=0
        $script:originalDesktop=$windows.FindWindowSW([ref]$location,[ref]$empty,8,[ref]$hwnd,1)
        if($null -eq $script:originalDesktop){throw 'Explorer is not ready. Start Explorer and retry Rescue.'}
    }
    Connect-DpDesktop;Refresh-DpDesktop
    if($errors.Count){throw ('Some items could not be shown: '+($errors -join '; '))}
    Restore-DpMergedDesktop
    [Windows.Forms.MessageBox]::Show('Managed files are visible and the combined layout was verified. Page layouts were preserved. DeskPages is suspended until explicitly resumed.','DeskPages Rescue') | Out-Null
}catch{[Windows.Forms.MessageBox]::Show([string]$_,'DeskPages Rescue — needs attention') | Out-Null;exit 1}
finally{if($owned -and $null -ne $mutex){$mutex.ReleaseMutex()};if($null -ne $mutex){$mutex.Dispose()}}
