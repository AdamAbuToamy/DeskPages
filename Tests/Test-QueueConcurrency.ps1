$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$exe=Join-Path ([IO.Path]::GetTempPath()) ('dp-queue-'+[guid]::NewGuid().ToString('N')+'.exe')
try {
 & (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo "/out:$exe" (Join-Path $root 'Native\DeskPagesCommandQueue.cs') (Join-Path $PSScriptRoot 'QueueConcurrencyHarness.cs')
 if($LASTEXITCODE -ne 0){throw 'Queue concurrency harness compile failed'}
 & $exe
 if($LASTEXITCODE -ne 0){throw 'Queue concurrency capacity regression'}
} finally {if(Test-Path -LiteralPath $exe){Remove-Item -LiteralPath $exe}}