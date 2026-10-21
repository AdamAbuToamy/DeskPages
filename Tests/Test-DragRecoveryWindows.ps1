$ErrorActionPreference='Stop'
if($env:OS -ne 'Windows_NT'){Write-Host 'SKIP: real drag broker forms/hooks require Windows.';return}
$root=Split-Path $PSScriptRoot -Parent
$temp=Join-Path ([IO.Path]::GetTempPath()) ('dp-drag-recovery-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try{
 $sources=@('Tests/DragRecoveryHarness.cs','Native/DeskPagesMonitorPolicy.cs','Native/DeskPagesDragProtocol.cs','Native/DeskPagesDragMailbox.cs','Native/DeskPagesDragImageSession.cs','Interaction/DragBroker.cs','Interaction/ShellDragImage.cs') | ForEach-Object {Join-Path $root $_}
 $compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
 $exe=Join-Path $temp 'DragRecovery.exe'
 & $compiler /nologo /target:exe /platform:x64 /main:DeskPagesDragRecoveryHarness /r:System.Windows.Forms.dll /r:System.Drawing.dll "/out:$exe" @sources
 if($LASTEXITCODE -ne 0){throw 'Drag recovery harness did not compile.'}
 & $exe
 if($LASTEXITCODE -ne 0){throw 'Drag helper stopped or replayed a delayed command.'}
 Write-Host 'Physical Explorer and touchpad dragging remain manual acceptance.'
}finally{Remove-Item -LiteralPath $temp -Recurse -Force}
