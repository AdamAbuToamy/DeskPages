@echo off
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0Tests\Run-Validation.ps1" -Stress
pause
