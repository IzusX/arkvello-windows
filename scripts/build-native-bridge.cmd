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
rem Reuse pinned upstream objects. The static librarian/linker selects only the
rem referenced Common/Windows code; no FM window or Win32 dialog is used.
call "%REPO%\scripts\build-windows.cmd"
if errorlevel 1 exit /b 1
lib /nologo /out:"%OUT%\upstream.lib" "%REPO%\CPP\7zip\UI\FileManager\x64\*.obj"
if errorlevel 1 exit /b 1
cl /nologo /std:c++17 /EHsc /W4 /WX /O2 /MT /DUNICODE /D_UNICODE /DZ7_EXTERNAL_CODECS /DWIN32 /D_WIN64 /LD "%REPO%\src\Arkvello.Native\bridge.cpp" /Fo:"%OUT%\bridge.obj" /link /IMPLIB:"%OUT%\Arkvello.Native.lib" /OUT:"%OUT%\Arkvello.Native.dll" "%OUT%\upstream.lib" oleaut32.lib ole32.lib advapi32.lib user32.lib shell32.lib
exit /b %errorlevel%
