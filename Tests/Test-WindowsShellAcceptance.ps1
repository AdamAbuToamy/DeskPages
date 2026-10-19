# Opt-in acceptance test for a disposable Windows VM/account with an EMPTY Desktop.
# It never accepts an existing DeskPages profile or pre-existing physical Desktop files.
param([switch]$DisposableDesktop,[ValidateRange(1,500)][int]$Cycles=30)
$ErrorActionPreference='Stop'
if($env:OS -ne 'Windows_NT'){Write-Host 'SKIP: real Explorer/NTFS/COM acceptance requires Windows.';return}
if(-not $DisposableDesktop){throw 'Run only in a disposable Windows VM/account, explicitly adding -DisposableDesktop.'}
$base=Split-Path $PSScriptRoot -Parent
$script:DpDesktop=[Environment]::GetFolderPath('Desktop');$script:DpPublic=[Environment]::GetFolderPath('CommonDesktopDirectory')
foreach($folder in @($script:DpDesktop,$script:DpPublic)){
    if(@(Get-ChildItem -LiteralPath $folder -Force | Where-Object {$_.Name -ine 'desktop.ini'}).Count){throw 'Desktop/Public Desktop is not empty. Acceptance stopped before changing anything.'}
}
if(Test-Path -LiteralPath (Join-Path $env:LOCALAPPDATA 'DeskPages\HiddenPages\pages.xml')){throw 'Existing DeskPages profile detected. Use a fresh disposable account.'}
Add-Type -AssemblyName System.Windows.Forms;Add-Type -AssemblyName System.Drawing
Add-Type -Path (Join-Path $base 'Bin\DeskPagesNative.dll')
$mutex=[Threading.Mutex]::new($false,('Local\DeskPages-Agent-'+[Security.Principal.WindowsIdentity]::GetCurrent().User.Value))
$owned=$mutex.WaitOne(0);if(-not $owned){$mutex.Dispose();throw 'DeskPages is running.'}
$temp=Join-Path ([IO.Path]::GetTempPath()) ('DeskPages-shell-acceptance-'+[guid]::NewGuid().ToString('N'))
$created=[Collections.Generic.List[string]]::new();$script:DpState=$null;$originalIcons=@()
try{
    $script:DpStore=Join-Path $temp 'Store';$script:DpLayoutStore=Join-Path $temp 'Layouts';$script:DpManifest=Join-Path $script:DpStore 'pages.xml';$script:DpSuspended=$false;$script:hostReady=$false
    $script:DpIconKey='Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel'
    $script:DpSystemIds=@('{20D04FE0-3AEA-1069-A2D8-08002B30309D}','{645FF040-5081-101B-9F08-00AA002F954E}','{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}')
    New-Item -ItemType Directory -Force $script:DpStore,$script:DpLayoutStore | Out-Null
    function Write-DpLog {param($Message) Write-Host $Message}
    function Connect-DpDesktop {
        $shell=New-Object -ComObject Shell.Application;$windows=$shell.Windows();[object]$location=0;[object]$empty=$null;[int]$hwnd=0
        $script:originalDesktop=$windows.FindWindowSW([ref]$location,[ref]$empty,8,[ref]$hwnd,1)
        if($null -eq $script:originalDesktop){throw 'Explorer is unavailable.'}
    }
    foreach($file in @('StateEngine.ps1','FirstRun.ps1','Visibility.ps1','Layouts.ps1','NewItemPlacement.ps1','PageSafety.ps1','Workspace.ps1','WorkspaceCommands.ps1')){. (Join-Path $base $file)}
    Connect-DpDesktop;$originalIcons=@([OriginalDesktopReader]::Read($script:originalDesktop))
    for($i=0;$i -lt 12;$i++){$path=Join-Path $script:DpDesktop ('DeskPages-Acceptance-'+$i+'.txt');$stream=[IO.File]::Open($path,[IO.FileMode]::CreateNew);try{$bytes=[Text.Encoding]::UTF8.GetBytes(('Sentinel '+$i+' '+('x'*4096)));$stream.Write($bytes,0,$bytes.Length)}finally{$stream.Dispose()};$created.Add($path)}
    $hashes=@{};foreach($path in $created){$hashes[$path]=(Get-FileHash -LiteralPath $path).Hash}
    $script:DpState=New-DpInitialState $script:DpDesktop $script:DpPublic @(Get-DpInitialSystemBackup)
    $script:DpState.Pages=@(1,2,3,4);$script:DpState.PageCount=4;Initialize-DpWorkspace;Save-DpState;Refresh-DpDesktop;Restore-DpPageLayout 1;Save-DpPageLayout 1
    for($i=0;$i -lt $created.Count;$i++){Set-DpWorkspaceItems @($created[$i]) (1+($i%4))}
    $clock=[Diagnostics.Stopwatch]::StartNew()
    for($i=0;$i -lt $Cycles;$i++){
        $page=1+($i%4);Set-DeskPage $page -Animate $false
        Assert-DpPhysicalPage $page
        Assert-DpPageView $page @([OriginalDesktopReader]::Read($script:originalDesktop))
        if($i%5 -eq 0){Save-DpPageLayout $page}
    }
    Set-DpWorkspaceItems @($created[0]) 1 Pin;Set-DeskPage 4 -Animate $false;Assert-DpPageView 4 @([OriginalDesktopReader]::Read($script:originalDesktop));Invoke-DpWorkspaceUndo
    foreach($path in $created){if((Get-FileHash -LiteralPath $path).Hash -ne $hashes[$path]){throw 'Sentinel path or file content changed.'}}
    Write-Host ('PASS: actual Explorer COM/NTFS visibility, four pages, pinning/undo, '+$Cycles+' settled transitions and all sentinel hashes; elapsed='+$clock.Elapsed.TotalSeconds+' seconds.')
    Write-Host 'This does not test physical OLE dragging, touchpad input, frame pacing, OneDrive or a real reboot.'
}finally{
    # Never recurse/delete folders; remove only exact test files created by this run.
    if($null -ne $script:DpState){foreach($path in $created){try{Set-DpHidden $path $false}catch{}};try{$null=Set-DpSystemIcons 1 -Original;Refresh-DpDesktop}catch{}}
    foreach($path in $created){if([IO.File]::Exists($path)){[IO.File]::Delete($path)}}
    if($originalIcons.Count){try{Refresh-DpDesktop;$now=@([OriginalDesktopReader]::Read($script:originalDesktop));$targets=@(Resolve-DpLayoutTargets $originalIcons $now);if($targets.Count){$null=[DeskPagesLayout]::Restore($script:originalDesktop,[string[]]@($targets.Id),[int[]]@($targets.X),[int[]]@($targets.Y))}}catch{Write-Warning 'Original system-icon positions need review.'}}
    if($owned){$mutex.ReleaseMutex()};$mutex.Dispose()
    if(Test-Path -LiteralPath $temp){Remove-Item -LiteralPath $temp -Recurse -Force}
}
