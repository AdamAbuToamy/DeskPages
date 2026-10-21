$ErrorActionPreference='Stop'
if([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT){Write-Host 'SKIP: encrypted snapshot persistence, DPAPI, compression and Win32 sharing locks require Windows.';return}
$exe=Join-Path $PSScriptRoot 'DeskPagesSnapshotStoreTest.exe'
if(-not(Test-Path -LiteralPath $exe)){throw 'Build the native snapshot-store fixture first.'}
& $exe
if($LASTEXITCODE -ne 0){throw 'Protected snapshot-store acceptance failed.'}
