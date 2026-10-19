$ErrorActionPreference='Stop'
if($env:OS -ne 'Windows_NT'){Write-Host 'SKIP: native file identity/rename fixture requires Windows.';return}
$base=Split-Path $PSScriptRoot -Parent
if(-not ('DeskPagesLayout' -as [type])){Add-Type -Path (Join-Path $base 'Native/DeskPagesLayout.cs') -ReferencedAssemblies @('System.Windows.Forms','System.Drawing')}
$root=Join-Path ([IO.Path]::GetTempPath()) ('DeskPages-fileid-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
try{
    foreach($folder in @($false,$true)){
        $a=Join-Path $root ('original-'+$folder);$b=Join-Path $root ('renamed-'+$folder);$other=Join-Path $root ('other-'+$folder)
        if($folder){New-Item -ItemType Directory -Path $a,$other | Out-Null}else{[IO.File]::WriteAllText($a,'preserve bytes');[IO.File]::WriteAllText($other,'different file')}
        $id=[DeskPagesLayout]::CreationIdentity($a)
        Move-Item -LiteralPath $a -Destination $b
        if([DeskPagesLayout]::CreationIdentity($b) -ne $id){throw 'File identity changed during same-directory rename'}
        if([DeskPagesLayout]::CreationIdentity($other) -eq $id){throw 'Distinct items share a creation identity'}
        if(-not $folder -and [IO.File]::ReadAllText($b) -ne 'preserve bytes'){throw 'File contents changed'}
    }
    Write-Host 'PASS: native file and folder identities survive rename; distinct items differ; file bytes preserved'
}finally{Remove-Item -LiteralPath $root -Recurse -Force}
