$ErrorActionPreference='Stop'
if($env:OS -ne 'Windows_NT'){Write-Host 'SKIP: installer controls require Windows.';return}
$root=Split-Path $PSScriptRoot -Parent
$temp=Join-Path ([IO.Path]::GetTempPath()) ('DeskPages-setup-ui-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try{
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 $payload=Join-Path $temp 'brand.zip';$zip=[IO.Compression.ZipFile]::Open($payload,[IO.Compression.ZipArchiveMode]::Create)
 try{[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,(Join-Path $root 'Assets/DeskPages.ico'),'Assets/DeskPages.ico') | Out-Null}finally{$zip.Dispose()}
 $exe=Join-Path $temp 'SetupState.exe';$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
 $sources=@('Installer/SetupWindow.cs','Native/DeskPagesInstallLocation.cs','Tests/InstallerStateHarness.cs') | ForEach-Object {Join-Path $root $_}
 & $compiler /nologo /target:exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll "/resource:$payload,DeskPages.Payload" "/out:$exe" @sources
 if($LASTEXITCODE -ne 0){throw 'Installer UI fixture compilation failed.'}
 & $exe
 if($LASTEXITCODE -ne 0){throw 'Installer UI state regression.'}
}finally{Remove-Item -LiteralPath $temp -Recurse -Force}
