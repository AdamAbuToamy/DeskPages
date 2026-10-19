$ErrorActionPreference='Stop'
if([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    Write-Host 'SKIP: real Windows file-attribute fixture (requires Windows).'
    return
}
$root=Join-Path ([IO.Path]::GetTempPath()) ('DeskPages-NTFS-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
try {
    & {
        param($FixtureRoot)
        $script:DpStore=$FixtureRoot
        $script:DpDesktop=Join-Path $FixtureRoot 'Personal'
        $script:DpPublic=Join-Path $FixtureRoot 'Public'
        $script:DpManifest=Join-Path $FixtureRoot 'pages.xml'
        New-Item -ItemType Directory -Path $script:DpDesktop,$script:DpPublic | Out-Null
        $a=Join-Path $script:DpDesktop 'a.txt';$b=Join-Path $script:DpDesktop 'b.txt'
        $shared=Join-Path $script:DpPublic 'test.lnk';$folder=Join-Path $script:DpDesktop 'folder'
        foreach($path in @($a,$b,$shared)){[IO.File]::WriteAllText($path,'fixture content')}
        New-Item -ItemType Directory -Path $folder | Out-Null
        $hashes=@{};foreach($path in @($a,$b,$shared)){$hashes[$path]=(Get-FileHash $path).Hash}
        . (Join-Path (Split-Path $PSScriptRoot -Parent) 'StateEngine.ps1')
        . (Join-Path (Split-Path $PSScriptRoot -Parent) 'Visibility.ps1')
        . (Join-Path (Split-Path $PSScriptRoot -Parent) 'Layouts.ps1')
        # Only this fixture's registry preference is mocked; no real registry is changed.
        function Get-ItemProperty {param($Path,$Name,$ErrorAction) [PSCustomObject]@{Hidden=2}}
        $script:DpState=[PSCustomObject]@{Current=1;Pages=@(1,2);Items=@(
            [PSCustomObject]@{Path=$a;Page=1},[PSCustomObject]@{Path=$b;Page=2},
            [PSCustomObject]@{Path=$shared;Page=1},[PSCustomObject]@{Path=$folder;Page=1})}
        Save-DpState
        foreach($page in @(2,1,2,1)) {
            Set-DpFilePage -Page $page -Commit:$false -RegisterNew:$false
            Assert-DpPhysicalPage -Page $page
            foreach($entry in $script:DpState.Items) {
                $hidden=([IO.File]::GetAttributes($entry.Path) -band [IO.FileAttributes]::Hidden) -ne 0
                if($hidden -ne ($entry.Page -ne $page)){throw 'Wrong actual Windows Hidden attribute.'}
            }
            if($script:DpState.Current -ne 1){throw 'Uncommitted transition changed current page.'}
        }
        [IO.File]::SetAttributes($b, [IO.FileAttributes]::Normal)
        $blocked=$false;try {Assert-DpPhysicalPage -Page 1} catch {$blocked=$true}
        if(-not $blocked){throw 'An unhidden off-page file passed the physical animation guard.'}
        Set-DpFilePage -Page 1 -Commit:$false -RegisterNew:$false
        foreach($path in $hashes.Keys){if((Get-FileHash $path).Hash -ne $hashes[$path]){throw 'Fixture file content changed.'}}
        Write-Host 'PASS: real Windows attributes, public-folder fixture, folders, repeated visibility switches and unchanged file hashes.'
    } $root
} finally {Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue}
