$ErrorActionPreference='Stop'
if($env:OS -ne 'Windows_NT'){Write-Host 'SKIP: posted-message host dispatch requires Windows.';return}
$root=Split-Path $PSScriptRoot -Parent
$runtime=Join-Path $root 'Bin/DeskPagesNative.dll'
$temp=Join-Path ([IO.Path]::GetTempPath()) ('dp-command-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try{
 $exe=Join-Path $temp 'CommandsTest.exe';$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
 & $compiler /noconfig /nologo /target:exe /platform:x64 /r:System.dll /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll "/r:$runtime" "/out:$exe" (Join-Path $PSScriptRoot 'CommandDispatchHarness.cs')
 if($LASTEXITCODE -ne 0){throw 'Command dispatch harness failed to compile.'}
 & $exe $runtime
 if($LASTEXITCODE -ne 0){throw 'Command dispatch regression failed.'}
}finally{Remove-Item -LiteralPath $temp -Recurse -Force}
