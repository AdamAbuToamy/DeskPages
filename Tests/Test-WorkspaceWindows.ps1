$ErrorActionPreference='Stop'
if($env:OS -ne 'Windows_NT'){Write-Host 'SKIP: actual Windows compact-page/settings forms require Windows.';return}
$root=Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$temp=Join-Path ([IO.Path]::GetTempPath()) ('DeskPages-ui-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try{
 $provider=[Microsoft.CSharp.CSharpCodeProvider]::new();$options=[CodeDom.Compiler.CompilerParameters]::new();$options.GenerateExecutable=$true;$options.OutputAssembly=Join-Path $temp 'SimpleUi.exe'
 foreach($reference in @('System.dll','System.Core.dll',[Windows.Forms.Form].Assembly.Location,[Drawing.Bitmap].Assembly.Location)){$null=$options.ReferencedAssemblies.Add($reference)}
 $paths=@('Native/DeskPagesTour.cs','Native/DeskPagesTourPolicy.cs','Tests/SimpleUiHarness.cs','Native/DeskPagesTheme.cs','Native/DeskPagesWorkspaceControls.cs','Native/DeskPagesWorkspace.cs','Native/DeskPagesSimpleSettings.cs','Native/DeskPagesModernSettings.cs','Native/DeskPagesSimplePreferences.cs','Native/DeskPagesWorkspaceRequest.cs','Native/DeskPagesPageNames.cs') | ForEach-Object{Join-Path $root $_}
 try{$result=$provider.CompileAssemblyFromFile($options,[string[]]$paths);if($result.Errors.HasErrors){throw (($result.Errors | Out-String))}}finally{$provider.Dispose()}
 & $options.OutputAssembly
 if($LASTEXITCODE -ne 0){throw 'Isolated native Windows UI fixture failed.'}
 Write-Host 'PASS: compact pages, independent basic settings and auto-save/advanced visibility in en/ar/he. Fake data only; no Explorer changes.'
}finally{Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue}
