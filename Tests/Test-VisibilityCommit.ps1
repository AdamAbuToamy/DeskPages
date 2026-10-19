$ErrorActionPreference='Stop'
$base=Split-Path $PSScriptRoot -Parent
. (Join-Path $base 'StateEngine.ps1');. (Join-Path $base 'Visibility.ps1')
$root=Join-Path ([IO.Path]::GetTempPath()) ('DeskPages-visibility-commit-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force (Join-Path $root 'Desktop'),(Join-Path $root 'Public') | Out-Null
try{
    $script:DpDesktop=Join-Path $root 'Desktop';$script:DpPublic=Join-Path $root 'Public';$script:DpStore=$root;$script:DpManifest=Join-Path $root 'pages.xml'
    $path=Join-Path $script:DpDesktop 'sentinel.txt';[IO.File]::WriteAllText($path,'unchanged');$hash=(Get-FileHash $path).Hash
    function Get-ItemProperty {param($Path,$Name,$ErrorAction) [PSCustomObject]@{Hidden=2}}
    $script:DpState=[PSCustomObject]@{Current=2;Pages=@(1,2);Items=@([PSCustomObject]@{Path=$path;Page=1})};Save-DpState
    $script:DpState.Items[0].Page=2
    Set-DpFilePage -Page 2 -Commit:$false -RegisterNew:$false -PersistMembership:$false
    if([int](Read-DpManifest $script:DpManifest).Items[0].Page -ne 1){throw 'Unverified drag membership leaked into the manifest.'}
    Save-DpState
    if([int](Read-DpManifest $script:DpManifest).Items[0].Page -ne 2){throw 'Final verified membership did not commit.'}
    if((Get-FileHash $path).Hash -ne $hash){throw 'Visibility update changed user bytes.'}
    Write-Host 'PASS: production visibility adapter defers drag membership persistence until final commit; original sentinel path/bytes preserved. This is not a Windows Hidden-bit rendering test.'
}finally{Remove-Item -LiteralPath $root -Recurse -Force}
