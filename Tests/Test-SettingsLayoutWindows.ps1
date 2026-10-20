$ErrorActionPreference='Stop'
if([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT){Write-Host 'SKIP: real Windows settings controls require Windows.';return}
$root=Split-Path $PSScriptRoot -Parent
$temp=Join-Path ([IO.Path]::GetTempPath()) ('DeskPages-settings-layout-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $temp | Out-Null
try{
    $sources=@('Tests/SettingsUiHarness.cs','Native/DeskPagesTheme.cs','Native/DeskPagesModernSettings.cs','Native/DeskPagesWorkspaceControls.cs','Native/DeskPagesSimpleSettings.cs','Native/DeskPagesSimplePreferences.cs') | ForEach-Object {Join-Path $root $_}
    $compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    $exe=Join-Path $temp 'SettingsUi.exe'
    & $compiler /nologo /target:winexe /r:System.Windows.Forms.dll /r:System.Drawing.dll "/out:$exe" @sources
    if($LASTEXITCODE -ne 0){throw 'Settings layout harness did not compile.'}
    $process=Start-Process -FilePath $exe -ArgumentList ('"'+$temp+'"') -PassThru
    if(-not $process.WaitForExit(60000)){$process.Kill();throw 'Settings layout harness timed out.'}
    if($process.ExitCode -ne 0){throw 'Settings clipping regression failed.'}
    Write-Host 'PASS: settings text paint and switch bounds; EN/AR/HE at four font sizes and normal/narrow widths. Physical Windows DPI/monitor acceptance remains separate.'
}finally{Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue}
