@echo off
rem Run from the x64 Native Tools Command Prompt for Visual Studio Build Tools.
pushd "%~dp0"
cl /nologo /std:c++17 /EHsc /O2 /W4 /MT /LD Compositor.cpp /link /OUT:..\DeskPagesCompositor.dll d3d11.lib dxgi.lib d2d1.lib dwmapi.lib dcomp.lib gdi32.lib user32.lib ole32.lib uuid.lib
set result=%errorlevel%
popd
exit /b %result%
