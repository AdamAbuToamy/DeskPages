$ErrorActionPreference='Stop'
if([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT){Write-Host 'SKIP: real Windows tour forms require an interactive Windows desktop.';return}
$root=Split-Path $PSScriptRoot -Parent
$temp=Join-Path ([IO.Path]::GetTempPath()) ('DeskPages-tour-test-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try{
 $sources=@('Tests/TourUiHarness','Native/DeskPagesTour','Native/DeskPagesTourPolicy','Native/DeskPagesTheme','Native/DeskPagesWorkspaceControls','Native/DeskPagesWorkspace','Native/DeskPagesSimpleSettings','Native/DeskPagesMonitorPages','Native/DeskPagesModernSettings','Native/DeskPagesSimplePreferences','Native/DeskPagesWorkspaceRequest','Native/DeskPagesPageNames') | ForEach-Object {Join-Path $root ($_+'.cs')}
 $compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
 if(-not(Test-Path $compiler)){throw '.NET Framework x64 compiler not found.'}
 $exe=Join-Path $temp 'TourUi.exe'
 & $compiler /nologo /target:winexe /r:System.Windows.Forms.dll /r:System.Drawing.dll "/out:$exe" @sources
 if($LASTEXITCODE -ne 0){throw 'Tour UI compilation failed.'}
 $process=Start-Process -FilePath $exe -ArgumentList ('"'+$temp+'"') -PassThru
 if(-not $process.WaitForExit(60000)){$process.Kill();throw 'Tour UI test timed out.'}
 if($process.ExitCode -ne 0){throw ('Tour UI test failed: '+$process.ExitCode)}
 Write-Host 'PASS: real tour controls, EN/AR/HE, actual add button, replay, reduced motion, persistence and policy stress. Fixture only; no Explorer changes.'
}finally{Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue}
