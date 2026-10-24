$ErrorActionPreference='Stop'
if($env:OS -ne 'Windows_NT'){Write-Host 'SKIP: native D3D11/DWM handoff fixture requires Windows.';return}
$root=Split-Path $PSScriptRoot -Parent
$temp=Join-Path ([IO.Path]::GetTempPath()) ('dp-graphics-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try{
 $vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
 if(-not(Test-Path -LiteralPath $vswhere)){throw 'Native graphics validation requires Visual Studio C++ build tools.'}
 $vs=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
 if(-not $vs){throw 'Native graphics validation requires the C++ toolchain.'}
 $exe=Join-Path $temp 'GraphicsTest.exe';$cmd=Join-Path $temp 'build.cmd'
 $lines=@('@echo off',('call "'+(Join-Path $vs 'Common7/Tools/VsDevCmd.bat')+'" -arch=x64 -host_arch=x64 >nul'),'if errorlevel 1 exit /b 1',('cd /d "'+$temp+'"'),('cl /nologo /std:c++17 /EHsc /O2 /W4 /MT "'+(Join-Path $root 'Native/Compositor/Test-GraphicsWindows.cpp')+'" /Fe:"'+$exe+'" /link d3d11.lib dxgi.lib d2d1.lib dwmapi.lib dcomp.lib gdi32.lib user32.lib ole32.lib uuid.lib crypt32.lib cabinet.lib comctl32.lib'),'exit /b %errorlevel%')
 [IO.File]::WriteAllLines($cmd,$lines,[Text.UTF8Encoding]::new($false))
 & $env:ComSpec /d /c $cmd
 if($LASTEXITCODE -ne 0){throw 'Native graphics fixture failed to compile.'}
 $info=[Diagnostics.ProcessStartInfo]::new($exe);$info.UseShellExecute=$false;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
 $child=[Diagnostics.Process]::Start($info)
 try{
  $stdout=$child.StandardOutput.ReadToEndAsync();$stderr=$child.StandardError.ReadToEndAsync()
  if(-not $child.WaitForExit(90000)){$child.Kill();$child.WaitForExit();throw 'Native graphics fixture exceeded 90 seconds.'}
  Write-Host ($stdout.GetAwaiter().GetResult());Write-Host ($stderr.GetAwaiter().GetResult())
  if($child.ExitCode -ne 0){throw 'Production graphics handoff regression failed.'}
 }finally{$child.Dispose()}
}finally{Remove-Item -LiteralPath $temp -Recurse -Force}
