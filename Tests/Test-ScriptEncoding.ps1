$ErrorActionPreference='Stop'
$base=Split-Path $PSScriptRoot -Parent
Add-Type -Path (Join-Path $base 'Native/DeskPagesScriptEncoding.cs')
$count=[DeskPagesScriptEncoding]::ValidateDirectory($base)
$word=[string]::Concat([char[]]@(0x627,0x644,0x639,0x631,0x628,0x64a,0x629))
$utf8=[Text.UTF8Encoding]::new($false,$true)
$text="Write-Output '$word'"
$body=$utf8.GetBytes($text)
$bom=[byte[]]@(0xef,0xbb,0xbf)+$body
[DeskPagesScriptEncoding]::Validate([Text.Encoding]::ASCII.GetBytes("Write-Output 'ascii'"))
[DeskPagesScriptEncoding]::Validate($bom)
foreach($case in @(
    [PSCustomObject]@{Name='UTF8 Unicode without BOM';Bytes=$body},
    [PSCustomObject]@{Name='UTF16 script';Bytes=[Text.Encoding]::Unicode.GetBytes($text)},
    [PSCustomObject]@{Name='Invalid UTF8';Bytes=[byte[]]@(0xef,0xbb,0xbf,0xff)},
    [PSCustomObject]@{Name='Truncated UTF8';Bytes=[byte[]]@(0xef,0xbb,0xbf,0xd8)},
    [PSCustomObject]@{Name='Empty script';Bytes=[byte[]]@()}
)){
    $failed=$false;try{[DeskPagesScriptEncoding]::Validate([byte[]]$case.Bytes)}catch{$failed=$true}
    if(-not $failed){throw ('Unsafe encoding accepted: '+$case.Name)}
}
Write-Host ("PASS: $count shipped PowerShell files validated as raw bytes; ASCII/BOM UTF8 accepted, Unicode without BOM, UTF16, malformed/truncated UTF8 and empty scripts rejected by the installer's actual validator.")

# Reproduce the previous release's exact file, rather than only a generated sample.
$previous=Join-Path (Split-Path $base -Parent) 'DeskPages-v081/Tests/Test-PowerShell51.ps1'
if(Test-Path -LiteralPath $previous){
    $failed=$false;try{[DeskPagesScriptEncoding]::Validate([IO.File]::ReadAllBytes($previous))}catch{$failed=$true}
    if(-not $failed){throw 'The reported 0.8.1 file escaped the encoding gate.'}
    Write-Host 'PASS: exact reported 0.8.1 file reproduced and rejected before starting PowerShell.'
}
