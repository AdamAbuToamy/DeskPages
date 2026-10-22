[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Installer,[string]$ExpectedThumbprint,[switch]$AllowUnsigned)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. (Join-Path $root 'PackageIntegrity.ps1')
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$Installer=[IO.Path]::GetFullPath($Installer)
function Assert-ReleaseSignature([string]$Path){
    $signature=Get-AuthenticodeSignature -LiteralPath $Path
    if($AllowUnsigned -and $signature.Status -eq 'NotSigned'){return}
    if($signature.Status -ne 'Valid' -or -not $signature.SignerCertificate){throw ('Untrusted signature: '+$Path+' '+$signature.Status)}
    if($ExpectedThumbprint -and $signature.SignerCertificate.Thumbprint -ne $ExpectedThumbprint.Replace(' ','')){throw ('Wrong signing identity: '+$Path)}
    if(-not $signature.TimeStamperCertificate){throw ('Missing trusted timestamp: '+$Path)}
}
$null=Read-DpPortableExecutable -Path $Installer -Machine 0x8664 -Managed
Assert-DpManagedIdentity -Path $Installer -Name 'DeskPages-Setup' -Version ([Version]'0.8.20.0')
Assert-ReleaseSignature $Installer
$assembly=[Reflection.Assembly]::LoadFile($Installer)
$stream=$assembly.GetManifestResourceStream('DeskPages.Payload')
if(-not $stream){throw 'Missing installer payload'}
$zip=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Read)
$temp=Join-Path ([IO.Path]::GetTempPath()) ('DeskPages-release-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try{
    foreach($entry in $zip.Entries){
        $target=[IO.Path]::GetFullPath((Join-Path $temp $entry.FullName))
        if(-not $target.StartsWith($temp+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe ZIP path'}
        if(-not $entry.Name){continue}
        New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
        $input=$entry.Open();$output=[IO.File]::Create($target)
        try{$input.CopyTo($output)}finally{$input.Dispose();$output.Dispose()}
    }
    Assert-DpPackage -Root $temp -Version ([Version]'0.8.20.0')
    foreach($file in Get-ChildItem -LiteralPath $temp -Recurse -File | Where-Object {$_.Extension -in @('.exe','.dll')}){Assert-ReleaseSignature $file.FullName}
    foreach($required in @('PRIVACY.txt','LICENSE.txt','THIRD-PARTY-NOTICES.md','ExportSettings.ps1','Native/DeskPagesPanel.cs','Native/DeskPagesPageNames.cs')){if(-not(Test-Path -LiteralPath (Join-Path $temp $required) -PathType Leaf)){throw ('Missing release document/source: '+$required)}}
}finally{$zip.Dispose();$stream.Dispose();Remove-Item -LiteralPath $temp -Recurse -Force}
if($AllowUnsigned){Write-Warning 'Preview verification only: unsigned files were allowed. Not a public signing PASS.'}else{Write-Host 'PASS: installer and embedded executable signatures, trusted timestamps, package identity, runtime source fingerprint and required documents.'}
Get-FileHash -LiteralPath $Installer -Algorithm SHA256
