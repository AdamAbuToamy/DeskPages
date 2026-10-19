$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$root=Split-Path $PSScriptRoot -Parent
$output=Join-Path $root 'docs/assets/ui'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$temp=Join-Path $env:RUNNER_TEMP ('deskpages-site-ui-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try {
 $provider=[Microsoft.CSharp.CSharpCodeProvider]::new()
 $options=[CodeDom.Compiler.CompilerParameters]::new()
 $options.GenerateExecutable=$true
 $options.OutputAssembly=Join-Path $temp 'WebsiteUi.exe'
 foreach($reference in @('System.dll','System.Core.dll',[Windows.Forms.Form].Assembly.Location,[Drawing.Bitmap].Assembly.Location)){$null=$options.ReferencedAssemblies.Add($reference)}
 $paths=@('Tests/WebsiteUiCapture.cs','Native/DeskPagesTheme.cs','Native/DeskPagesWorkspaceControls.cs','Native/DeskPagesWorkspace.cs','Native/DeskPagesSimpleSettings.cs','Native/DeskPagesSimplePreferences.cs','Native/DeskPagesWorkspaceRequest.cs','Native/DeskPagesPageNames.cs') | ForEach-Object {Join-Path $root $_}
 try {$result=$provider.CompileAssemblyFromFile($options,[string[]]$paths);if($result.Errors.HasErrors){throw ($result.Errors | Out-String)}} finally {$provider.Dispose()}
 & $options.OutputAssembly $output
 if($LASTEXITCODE -ne 0){throw 'Windows UI capture failed.'}
 foreach($name in @('pages-en.png','settings-en.png','pages-ar.png','settings-ar.png')){if(!(Test-Path (Join-Path $output $name))){throw "Missing screenshot: $name"}}
} finally {Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue}
