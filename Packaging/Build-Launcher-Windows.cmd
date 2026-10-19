@echo off
rem Use an x64 Native Tools Command Prompt for Visual Studio Build Tools.
pushd "%~dp0.."
rc /nologo /fo Packaging\DeskPages.res Packaging\DeskPages.rc
if errorlevel 1 goto failed
cl /nologo /std:c++17 /EHsc /O2 /W4 /MT /DUNICODE /D_UNICODE Packaging\Launcher.cpp Packaging\DeskPages.res /link /OUT:DeskPages.exe /SUBSYSTEM:WINDOWS shell32.lib user32.lib
set result=%errorlevel%
popd
exit /b %result%
:failed
popd
exit /b 1
