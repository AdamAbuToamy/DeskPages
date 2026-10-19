@echo off
setlocal
set "dpPowerShell=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if exist "%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe" set "dpPowerShell=%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe"
"%dpPowerShell%" -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0Agent.ps1" -SelfTest
set "dpResult=%errorlevel%"
pause
exit /b %dpResult%
