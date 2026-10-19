$ErrorActionPreference='Stop'
if($env:OS -ne 'Windows_NT'){Write-Host 'SKIP: feedback thread/input-style UI acceptance requires Windows.';return}
$root=Split-Path $PSScriptRoot -Parent
$runtime=Join-Path $root 'Bin/DeskPagesNative.dll'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Path $runtime
Add-Type -Path (Join-Path $PSScriptRoot 'FeedbackUiHarness.cs') -ReferencedAssemblies @('System.dll','System.Core.dll','System.Drawing','System.Windows.Forms',$runtime)
$folder=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) ('DeskPages/Verification/feedback-ui-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$result=[FeedbackUiHarness]::Main([string[]]@((Join-Path $folder 'hud'),(Join-Path $root 'DeskPagesDrag.exe')))
if($result -ne 0){throw 'Feedback UI acceptance failed. See rendered images in Verification.'}
