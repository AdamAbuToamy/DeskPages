$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
. (Join-Path $root 'PackageIntegrity.ps1')
$temp=Join-Path ([IO.Path]::GetTempPath()) ('DeskPages-drag-build-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try{
    $provider=[Microsoft.CSharp.CSharpCodeProvider]::new()
    $options=[CodeDom.Compiler.CompilerParameters]::new();$options.GenerateExecutable=$true;$options.GenerateInMemory=$false
    $options.OutputAssembly=Join-Path $temp 'DeskPagesRescue.exe'
    $options.CompilerOptions='/target:winexe /platform:x64 /optimize+ /win32icon:"'+(Join-Path $root 'Assets/DeskPages.ico')+'" /win32manifest:"'+(Join-Path $root 'Packaging/DeskPages.manifest')+'"'
    foreach($reference in @('System.dll','System.Core.dll',[Windows.Forms.Form].Assembly.Location,[Drawing.Bitmap].Assembly.Location)){$null=$options.ReferencedAssemblies.Add($reference)}
    try{$result=$provider.CompileAssemblyFromFile($options,[string[]]@((Join-Path $PSScriptRoot 'RescueLauncher.cs')));if($result.Errors.HasErrors){throw (($result.Errors | ForEach-Object {$_.ToString()}) -join "`n")}}finally{$provider.Dispose()}
    $null=Read-DpPortableExecutable -Path $options.OutputAssembly -Machine 0x8664 -Managed
    Assert-DpManagedIdentity -Path $options.OutputAssembly -Name 'DeskPagesRescue' -Version ([Version]'0.8.36.0')
    Move-Item -LiteralPath $options.OutputAssembly -Destination (Join-Path $root 'DeskPagesRescue.exe') -Force
}finally{Remove-Item -LiteralPath $temp -Recurse -Force}
