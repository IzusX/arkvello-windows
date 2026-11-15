@echo off
setlocal
for %%I in ("%~dp0..") do set "REPO=%%~fI"
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
for /f "usebackq tokens=*" %%I in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSROOT=%%I"
if not defined VSROOT exit /b 1
call "%VSROOT%\VC\Auxiliary\Build\vcvars64.bat"
if errorlevel 1 exit /b 1
if not exist "%REPO%\build" mkdir "%REPO%\build"
cl /nologo /EHsc /std:c++17 /W4 /WX /MT /Fe:"%REPO%\build\test-native-ui.exe" /Fo:"%REPO%\build\test-native-ui.obj" "%REPO%\scripts\test-native-ui.cpp" /link user32.lib gdi32.lib gdiplus.lib advapi32.lib comctl32.lib
exit /b %errorlevel%
