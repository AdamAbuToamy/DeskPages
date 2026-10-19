@echo off
if exist "%LOCALAPPDATA%\DeskPages\HiddenPages\restart-report.txt" (
    start "" notepad.exe "%LOCALAPPDATA%\DeskPages\HiddenPages\restart-report.txt"
) else (
    echo No restart baseline or completed report exists. Prepare a restart check from the DeskPages tray menu first.
    pause
)
