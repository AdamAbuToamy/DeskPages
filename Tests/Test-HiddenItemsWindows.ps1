$ErrorActionPreference='Stop'
if([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT){
    Write-Host 'SKIP: Hidden/System attribute regression requires Windows.'
    return
}
$base=Split-Path $PSScriptRoot -Parent
. (Join-Path $base 'StateEngine.ps1')
. (Join-Path $base 'Visibility.ps1')
. (Join-Path $base 'Layouts.ps1')
$root=Join-Path ([IO.Path]::GetTempPath()) ('DeskPages-hidden-items-'+[guid]::NewGuid().ToString('N'))
$script:DpStore=$root
$script:DpDesktop=Join-Path $root 'Desktop'
$script:DpPublic=Join-Path $root 'Public'
$script:DpManifest=Join-Path $root 'pages.xml'
New-Item -ItemType Directory -Force $script:DpDesktop,$script:DpPublic | Out-Null
try{
    $a=Join-Path $script:DpDesktop 'main.txt'
    $b=Join-Path $script:DpDesktop 'second.txt'
    $personalHidden=Join-Path $script:DpDesktop 'user-hidden.txt'
    $originalSystem=Join-Path $script:DpDesktop 'original-system.txt'
    foreach($path in @($a,$b,$personalHidden,$originalSystem)){[IO.File]::WriteAllText($path,'preserve content and path')}
    [IO.File]::SetAttributes($a,[IO.FileAttributes]::ReadOnly)
    [IO.File]::SetAttributes($b,[IO.FileAttributes]::Hidden) # 0.8.18 migration
    [IO.File]::SetAttributes($personalHidden,[IO.FileAttributes]::Hidden)
    [IO.File]::SetAttributes($originalSystem,[IO.FileAttributes]::System)
    $hashes=@{};foreach($path in @($a,$b,$personalHidden,$originalSystem)){$hashes[$path]=(Get-FileHash -LiteralPath $path).Hash}
    $script:superHidden=0
    function Get-ItemProperty {param($Path,$Name,$ErrorAction) [PSCustomObject]@{Hidden=1;ShowSuperHidden=$script:superHidden}}
    $script:DpState=[PSCustomObject]@{Current=1;Pages=@(1,2);Items=@(
        [PSCustomObject]@{Path=$a;Page=1},[PSCustomObject]@{Path=$b;Page=2},[PSCustomObject]@{Path=$originalSystem;Page=1})}
    Save-DpState
    Set-DpFilePage -Page 1 -Commit:$false
    if(([int][IO.File]::GetAttributes($b) -band 6) -ne 6){throw 'Legacy hidden item was not upgraded to Hidden+System.'}
    if(@($script:DpState.Items | Where-Object {$_.Path -eq $personalHidden}).Count){throw 'User hidden file was adopted.'}
    if(([int][IO.File]::GetAttributes($personalHidden) -band 6) -ne 2){throw 'User hidden file attributes changed.'}
    $owned=Import-Clixml -LiteralPath (Join-Path $root 'system-ownership.xml')
    if(-not $owned.Contains($b) -or $owned.Contains($originalSystem)){throw 'Ownership was not persisted correctly.'}
    foreach($page in @(2,1,2,1)){
        Set-DpFilePage -Page $page -Commit:$false -RegisterNew:$false
        Assert-DpPhysicalPage -Page $page
        foreach($entry in $script:DpState.Items){
            $attrs=[int][IO.File]::GetAttributes($entry.Path)
            if($entry.Page -ne $page -and ($attrs -band 6) -ne 6){throw 'Off-page item is visible to ordinary Show hidden items.'}
            if($entry.Page -eq $page -and ($attrs -band 2) -ne 0){throw 'Active page item stayed hidden.'}
        }
    }
    if(([int][IO.File]::GetAttributes($a) -band 1) -ne 1){throw 'ReadOnly bit was lost.'}
    if(([int][IO.File]::GetAttributes($originalSystem) -band 4) -ne 4){throw 'Preexisting System bit was cleared.'}
    # Losing only System is enough to expose an off-page item with Hidden ON.
    [IO.File]::SetAttributes($b,[IO.FileAttributes]::Hidden)
    $blocked=$false;try{Assert-DpPhysicalPage -Page 1}catch{$blocked=$true}
    if(-not $blocked){throw 'Animation guard accepted an exposed off-page item.'}
    Set-DpFilePage -Page 1 -Commit:$false -RegisterNew:$false
    # Re-load ownership as Rescue/a fresh process does, while membership is uncommitted.
    $script:DpState.Items[0].Page=2
    Set-DpFilePage -Page 1 -Commit:$false -RegisterNew:$false -PersistMembership:$false
    if((Read-DpManifest $script:DpManifest).Items[0].Page -ne 1){throw 'Ownership journal leaked pending page membership.'}
    $script:DpSystemOwnership=$null
    foreach($entry in $script:DpState.Items){Set-DpHidden -Path $entry.Path -Hidden $false}
    if(([int][IO.File]::GetAttributes($a) -band 7) -ne 1 -or ([int][IO.File]::GetAttributes($b) -band 6) -ne 0){throw 'Recovery did not clear app-owned bits.'}
    if(([int][IO.File]::GetAttributes($originalSystem) -band 6) -ne 4){throw 'Recovery cleared the original System bit.'}
    # A second-item failure must undo BOTH controlled bits of the first item.
    $script:DpState.Items[0].Page=1
    $script:DpRealSetHidden=(Get-Command Set-DpHidden).ScriptBlock
    $script:attributeWrites=0
    function Set-DpHidden {
        param([string]$Path,[bool]$Hidden)
        $script:attributeWrites++
        if($script:attributeWrites -eq 2){throw 'Injected second-file permission failure.'}
        & $script:DpRealSetHidden -Path $Path -Hidden $Hidden
    }
    $failed=$false
    try{Set-DpFilePage -Page 2 -Commit:$false -RegisterNew:$false -PersistMembership:$false}catch{$failed=$_.ToString().Contains('Injected second-file')}
    Set-Item -Path Function:Set-DpHidden -Value $script:DpRealSetHidden
    if(-not $failed -or ([int][IO.File]::GetAttributes($a) -band 7) -ne 1){throw 'Failure rollback did not restore exact Hidden/System/ReadOnly bits.'}
    # A protected-files preference is never silently rewritten by the app.
    $before=[int][IO.File]::GetAttributes($a)
    $script:superHidden=1
    $blocked=$false;try{Set-DpFilePage -Page 2}catch{$blocked=$true}
    if(-not $blocked -or [int][IO.File]::GetAttributes($a) -ne $before){throw 'Unsafe protected-files preference was not blocked before mutation.'}
    foreach($path in $hashes.Keys){if((Get-FileHash -LiteralPath $path).Hash -ne $hashes[$path]){throw 'User bytes changed.'}}
    Write-Host 'PASS: Windows attributes with ordinary Hidden ON, legacy migration, unrelated hidden items, original System/ReadOnly preservation, physical guard, durable recovery and deferred membership.'
    Write-Host 'Registry preferences were mocked; real Explorer rendering and a physical reboot remain acceptance checks.'
}finally{
    foreach($file in Get-ChildItem -LiteralPath $root -Recurse -Force -File -ErrorAction SilentlyContinue){[IO.File]::SetAttributes($file.FullName,[IO.FileAttributes]::Normal)}
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
