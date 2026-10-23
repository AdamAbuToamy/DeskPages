$ErrorActionPreference='Stop'
if([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT){Write-Host 'SKIP: real Windows settings controls require Windows.';return}
$root=Split-Path $PSScriptRoot -Parent
$temp=Join-Path ([IO.Path]::GetTempPath()) ('DeskPages-settings-layout-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $temp | Out-Null
try{
    Copy-Item -LiteralPath (Join-Path $root 'Assets') -Destination $temp -Recurse
    $sources=@('Tests/SettingsUiHarness.cs','Native/DeskPagesTheme.cs','Native/DeskPagesModernSettings.cs','Native/DeskPagesWorkspaceControls.cs','Native/DeskPagesSimpleSettings.cs','Native/DeskPagesSimplePreferences.cs') | ForEach-Object {Join-Path $root $_}
    $compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    $exe=Join-Path $temp 'SettingsUi.exe'
    & $compiler /nologo /target:exe /r:System.Windows.Forms.dll /r:System.Drawing.dll "/out:$exe" @sources
    if($LASTEXITCODE -ne 0){throw 'Settings layout harness did not compile.'}
    $start=New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName=$exe
    $start.Arguments='"'+$temp+'"'
    $start.UseShellExecute=$false
    $start.CreateNoWindow=$true
    $start.RedirectStandardOutput=$true
    $start.RedirectStandardError=$true
    $process=New-Object System.Diagnostics.Process
    $process.StartInfo=$start
    try{
        [void]$process.Start()
        if(-not $process.WaitForExit(60000)){$process.Kill();throw 'Settings layout harness timed out.'}
        $stdout=$process.StandardOutput.ReadToEnd()
        $stderr=$process.StandardError.ReadToEnd()
        if($stdout){Write-Host $stdout}
        if($process.ExitCode -ne 0){throw ('Settings clipping regression failed (exit '+$process.ExitCode+'): '+$stderr)}
    }finally{$process.Dispose()}
    Write-Host 'PASS: settings text paint and switch bounds; EN/AR/HE at four font sizes and normal/narrow widths. Physical Windows DPI/monitor acceptance remains separate.'
}finally{Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue}
