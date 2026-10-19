param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(-not $OutputDirectory){$OutputDirectory=Join-Path $root 'dist'}
$version=([IO.File]::ReadAllText((Join-Path $root 'VERSION.txt')) -replace '^DeskPages\s+','').Trim()
$installer=Join-Path $OutputDirectory "DeskPages-Setup-$version.exe"
. (Join-Path $root 'PackageIntegrity.ps1')
$null=Read-DpPortableExecutable -Path $installer -Machine 0x8664 -Managed
Assert-DpManagedIdentity -Path $installer -Name 'DeskPages-Setup' -Version ([Version]($version+'.0'))
# Source archive contains the exact committed tree, not logs or user state.
$source=Join-Path $OutputDirectory "DeskPages-Source-$version.zip"
& git -C $root archive --format=zip --output=$source HEAD
if($LASTEXITCODE -ne 0){throw 'Source archive failed. Commit the reviewed source first.'}
$sum=@(foreach($file in @($installer,$source)){(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($file)})
[IO.File]::WriteAllLines((Join-Path $OutputDirectory 'SHA256SUMS.txt'),$sum,[Text.Encoding]::ASCII)
Copy-Item -LiteralPath (Join-Path $root 'Release/NOTES.md') -Destination (Join-Path $OutputDirectory 'RELEASE-NOTES.md') -Force
