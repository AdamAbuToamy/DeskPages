$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
foreach($name in @('System.Windows.Forms','System.Drawing','UIAutomationClient','UIAutomationTypes','WindowsBase')){Add-Type -AssemblyName $name}
. (Join-Path $root 'PackageIntegrity.ps1')
$temp=Join-Path ([IO.Path]::GetTempPath()) ('dp-groups-build-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try{
 $provider=[Microsoft.CSharp.CSharpCodeProvider]::new()
 $options=[CodeDom.Compiler.CompilerParameters]::new();$options.GenerateExecutable=$true;$options.GenerateInMemory=$false
 $options.OutputAssembly=Join-Path $temp 'DeskPagesGroups.exe'
 $options.CompilerOptions='/target:winexe /platform:x64 /optimize+ /win32icon:"'+(Join-Path $root 'Assets/DeskPages.ico')+'" /win32manifest:"'+(Join-Path $root 'Packaging/DeskPages.manifest')+'"'
 foreach($reference in @('System.dll','System.Core.dll',[Windows.Forms.Form].Assembly.Location,[Drawing.Bitmap].Assembly.Location,[Windows.Automation.AutomationElement].Assembly.Location,[Windows.Automation.ControlType].Assembly.Location,[Windows.Point].Assembly.Location)){$null=$options.ReferencedAssemblies.Add($reference)}
 $sources=@((Join-Path $root 'Native/DeskPagesDragProtocol.cs'))+@(Get-ChildItem (Join-Path $PSScriptRoot 'TaskbarGroups') -Filter '*.cs' | ForEach-Object {$_.FullName})
 try{$result=$provider.CompileAssemblyFromFile($options,[string[]]$sources);if($result.Errors.HasErrors){throw (($result.Errors | ForEach-Object {$_.ToString()}) -join "`n")}}finally{$provider.Dispose()}
 Assert-DpManagedIdentity -Path $options.OutputAssembly -Name 'DeskPagesGroups' -Version ([Version]'0.8.28.0')
 Move-Item -LiteralPath $options.OutputAssembly -Destination (Join-Path $root 'DeskPagesGroups.exe') -Force
}finally{Remove-Item -LiteralPath $temp -Recurse -Force}
