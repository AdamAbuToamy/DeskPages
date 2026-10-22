$ErrorActionPreference='Stop'
if($env:OS -ne 'Windows_NT'){Write-Host 'SKIP: native cross-process Shell grouping requires Windows.';return}
$root=Split-Path $PSScriptRoot -Parent
foreach($name in @('System.Windows.Forms','System.Drawing','UIAutomationClient','UIAutomationTypes','WindowsBase')){Add-Type -AssemblyName $name}
$temp=Join-Path ([IO.Path]::GetTempPath()) ('dp-group-tests-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try{
 $sources=@((Join-Path $PSScriptRoot 'TaskbarGroupsHarness.cs'),(Join-Path $root 'Native/DeskPagesDragProtocol.cs'))+@(Get-ChildItem (Join-Path $root 'Interaction/TaskbarGroups') -Filter '*.cs' | ForEach-Object {$_.FullName})
 $compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe';$exe=Join-Path $temp 'GroupsTest.exe'
 $references=@([Windows.Forms.Form].Assembly.Location,[Drawing.Bitmap].Assembly.Location,[Windows.Automation.AutomationElement].Assembly.Location,[Windows.Automation.ControlType].Assembly.Location,[Windows.Point].Assembly.Location) | ForEach-Object {'/r:'+$_}
 & $compiler /noconfig /nologo /target:exe /platform:x64 /r:System.dll /r:System.Core.dll /main:TaskbarGroupsHarness "/out:$exe" @references @sources
 if($LASTEXITCODE -ne 0){throw 'Group regression harness did not compile.'}
 & $exe
 if($LASTEXITCODE -ne 0){throw 'Open-window group or recovery regression failed.'}
 Write-Host 'Physical Windows 11 taskbar icon dragging, native numbered button appearance and Store/Chrome behavior remain device acceptance.'
}finally{Remove-Item -LiteralPath $temp -Recurse -Force}
