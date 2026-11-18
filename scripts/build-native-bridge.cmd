@echo off
setlocal
for %%I in ("%~dp0..") do set "REPO=%%~fI"
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" exit /b 1
for /f "usebackq tokens=*" %%I in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSROOT=%%I"
if not defined VSROOT exit /b 1
call "%VSROOT%\VC\Auxiliary\Build\vcvars64.bat"
if errorlevel 1 exit /b 1
rem Build the adjacent codec and fixture CLI without either upstream UI target.
call "%REPO%\scripts\build-windows.cmd" core
if errorlevel 1 exit /b 1
powershell -NoProfile -ExecutionPolicy Bypass -File "%REPO%\scripts\build-native-sources.ps1"
exit /b %errorlevel%
