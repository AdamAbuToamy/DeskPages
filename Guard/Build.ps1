$ErrorActionPreference='Stop'
$base=Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$provider=[Microsoft.CSharp.CSharpCodeProvider]::new()
$parameters=[CodeDom.Compiler.CompilerParameters]::new()
$parameters.GenerateExecutable=$true
$parameters.GenerateInMemory=$false
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('DeskPages-guard-build-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
$parameters.OutputAssembly=Join-Path $temporary 'DeskPagesStartup.exe'
$parameters.CompilerOptions='/target:winexe /platform:x64 /optimize+ /win32icon:"'+(Join-Path $base 'Assets\DeskPages.ico')+'" /win32manifest:"'+(Join-Path $base 'Packaging\DeskPages.manifest')+'"'
foreach($reference in @('System.dll','System.Core.dll',[Windows.Forms.Form].Assembly.Location,[Drawing.Bitmap].Assembly.Location)){$null=$parameters.ReferencedAssemblies.Add($reference)}
try {
    $result=$provider.CompileAssemblyFromFile($parameters,[string[]]@((Join-Path $base 'Native\DeskPagesInstallLocation.cs'),(Join-Path $base 'Native\DeskPagesStartupGate.cs'),(Join-Path $base 'Native\DeskPagesBootPlan.cs'),(Join-Path $PSScriptRoot 'Startup.cs')))
    if($result.Errors.HasErrors){throw (($result.Errors | ForEach-Object {$_.ToString()}) -join "`n")}
    . (Join-Path $base 'PackageIntegrity.ps1')
    $null=Read-DpPortableExecutable -Path $parameters.OutputAssembly -Machine 0x8664 -Managed
    Assert-DpManagedIdentity -Path $parameters.OutputAssembly -Name 'DeskPagesStartup' -Version ([Version]'0.8.26.0')
    Move-Item -LiteralPath $parameters.OutputAssembly -Destination (Join-Path $base 'DeskPagesStartup.exe') -Force
    Write-Host 'PASS: valid startup guardian EXE built; PE architecture, CLR metadata and release identity verified.'
}finally{$provider.Dispose();Remove-Item -LiteralPath $temporary -Recurse -Force -ErrorAction SilentlyContinue}
