@echo off
setlocal
for %%I in ("%~dp0..") do set "REPO=%%~fI"
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
for /f "usebackq tokens=*" %%I in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSROOT=%%I"
if not defined VSROOT exit /b 1
call "%VSROOT%\VC\Auxiliary\Build\vcvars64.bat"
if errorlevel 1 exit /b 1
set "OUT=%REPO%\build\native-bridge"
if not exist "%OUT%" mkdir "%OUT%"
rem Reuse pinned upstream objects through an explicit non-UI source whitelist.
rem Agent and bridge must match FM's layout-affecting defines (Z7_DEVICE_FILE).
call "%REPO%\scripts\build-windows.cmd"
if errorlevel 1 exit /b 1
rem Agent's default loader references the old FM hash GUI. In this host only,
rem require the injected private codec catalog; retain upstream behavior for FM.
cl /nologo /c /EHsc /W4 /WX /O2 /Gy /GR- /MT /DUNICODE /D_UNICODE /DZ7_EXTERNAL_CODECS /DZ7_DEVICE_FILE /DZ7_LANG /DARKVELLO_NATIVE /DWIN32 /D_WIN64 "%REPO%\CPP\7zip\UI\Agent\Agent.cpp" /Fo:"%OUT%\Agent.obj"
if errorlevel 1 exit /b 1
powershell -NoProfile -ExecutionPolicy Bypass -File "%REPO%\scripts\native-object-list.ps1"
if errorlevel 1 exit /b 1
lib /nologo /out:"%OUT%\upstream.lib" @"%OUT%\upstream-objects.rsp"
if errorlevel 1 exit /b 1
cl /nologo /std:c++17 /EHsc /W4 /WX /O2 /GR- /MT /DUNICODE /D_UNICODE /DZ7_EXTERNAL_CODECS /DZ7_DEVICE_FILE /DZ7_LANG /DWIN32 /D_WIN64 /LD "%REPO%\src\Arkvello.Native\bridge.cpp" /Fo:"%OUT%\bridge.obj" /link /IMPLIB:"%OUT%\Arkvello.Native.lib" /OUT:"%OUT%\Arkvello.Native.dll" "%OUT%\upstream.lib" oleaut32.lib ole32.lib advapi32.lib user32.lib shell32.lib
exit /b %errorlevel%
