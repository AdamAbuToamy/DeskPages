param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
if($env:OS -ne 'Windows_NT'){throw 'Release builds require Windows x64.'}
$root=Split-Path $PSScriptRoot -Parent
if(-not $OutputDirectory){$OutputDirectory=Join-Path $root 'dist'}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if(-not(Test-Path -LiteralPath $vswhere)){throw 'Install Visual Studio Build Tools: Desktop development with C++, C# compiler, .NET Framework 4.8 and Windows SDK.'}
$vs=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(-not $vs){throw 'No x64 C++ build tools found.'}
$dev=Join-Path $vs 'Common7\Tools\VsDevCmd.bat'
$temp=Join-Path ([IO.Path]::GetTempPath()) ('DeskPages-build-'+[guid]::NewGuid().ToString('N')+'.cmd')
$command=@('@echo off','chcp 65001 >nul',('call "'+$dev+'" -arch=x64 -host_arch=x64'), 'if errorlevel 1 exit /b 1',('cd /d "'+$root+'"'),'call Packaging\Build-Launcher-Windows.cmd','if errorlevel 1 exit /b 1','call Native\Compositor\Build-Windows.cmd','if errorlevel 1 exit /b 1',('powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "'+(Join-Path $root 'Installer\Build-Installer.ps1')+'" -OutputDirectory "'+$OutputDirectory+'"'),'exit /b %errorlevel%')
try{
 [IO.File]::WriteAllLines($temp,$command,[Text.UTF8Encoding]::new($false))
 & $env:ComSpec /d /c $temp
 if($LASTEXITCODE -ne 0){throw 'Windows release build or validation failed.'}
 $version=([IO.File]::ReadAllText((Join-Path $root 'VERSION.txt')) -replace '^DeskPages\s+','').Trim()
 $name="DeskPages-Setup-$version.exe";Move-Item -LiteralPath (Join-Path $OutputDirectory 'DeskPages-Setup.exe') -Destination (Join-Path $OutputDirectory $name) -Force
 Write-Host ('Built and validated: '+(Join-Path $OutputDirectory $name))
}finally{Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue}
