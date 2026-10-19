@echo off
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0Tests\Test-GraphicsSimulator.ps1" -Interactive
pause
