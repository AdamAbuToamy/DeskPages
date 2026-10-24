$ErrorActionPreference='Stop'
$base=Split-Path $PSScriptRoot -Parent
. (Join-Path $base 'PackageIntegrity.ps1')
$root=Join-Path ([IO.Path]::GetTempPath()) ('DeskPages-package-fixture-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
function Reject([scriptblock]$Action,[string]$Reason){$failed=$false;try{& $Action | Out-Null}catch{$failed=$_.ToString().Contains($Reason)};if(-not $failed){throw ('Bad release not rejected: '+$Reason)}}
try{
    $guardian=Join-Path $base 'DeskPagesStartup.exe'
    $info=Read-DpPortableExecutable -Path $guardian -Machine 0x8664 -Managed
    Assert-DpManagedIdentity -Path $guardian -Name 'DeskPagesStartup' -Version ([Version]'0.8.36.0')
    $bad=Join-Path $root 'DeskPagesStartup.exe'
    [IO.File]::WriteAllBytes($bad,[byte[]]@())
    Reject {Read-DpPortableExecutable -Path $bad -Machine 0x8664 -Managed} 'empty or truncated'
    $bytes=[IO.File]::ReadAllBytes($guardian)
    [IO.File]::WriteAllBytes($bad,[byte[]]$bytes[0..4999])
    Reject {Read-DpPortableExecutable -Path $bad -Machine 0x8664 -Managed} 'truncated'
    [IO.File]::WriteAllBytes($bad,$bytes)
    $pe=[BitConverter]::ToInt32($bytes,0x3c)
    $bytes[$pe+4]=0x4c;$bytes[$pe+5]=0x01
    [IO.File]::WriteAllBytes($bad,$bytes)
    Reject {Read-DpPortableExecutable -Path $bad -Machine 0x8664 -Managed} 'wrong processor architecture'
    $bytes=[IO.File]::ReadAllBytes($guardian)
    $directory=$pe+24+112+14*8
    for($i=0;$i -lt 8;$i++){$bytes[$directory+$i]=0}
    [IO.File]::WriteAllBytes($bad,$bytes)
    Reject {Read-DpPortableExecutable -Path $bad -Machine 0x8664 -Managed} 'no CLR metadata header'
    Reject {Assert-DpManagedIdentity -Path $guardian -Name 'UnrelatedProgram'} 'identity/version'
    Reject {Assert-DpManagedIdentity -Path $guardian -Name 'DeskPagesStartup' -Version ([Version]'0.0.1.0')} 'identity/version'
    Assert-DpPackage -Root $base -Version ([Version]'0.8.36.0')
    Write-Host ('PASS: actual packaged guardian is '+$info.Length+' bytes; AMD64 PE32+, CLR metadata, exact DeskPagesStartup 0.8.36.0 identity verified')
    Write-Host 'PASS: empty, truncated, wrong-machine, missing-CLR, wrong-name and wrong-version fixtures rejected without executing any binary'
}finally{Remove-Item -LiteralPath $root -Recurse -Force}
