@echo off
setlocal
if not "%~1"=="" if /i not "%~1"=="core" (
  echo Usage: build-windows.cmd [core]
  exit /b 1
)
for %%I in ("%~dp0..") do set "REPO=%%~fI"
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
  echo Visual Studio Installer is missing. See docs/BUILDING.md.
  exit /b 1
)
for /f "usebackq tokens=*" %%I in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSROOT=%%I"
if not defined VSROOT (
  echo MSVC x64 tools are missing. See docs/BUILDING.md.
  exit /b 1
)
call "%VSROOT%\VC\Auxiliary\Build\vcvars64.bat"
if errorlevel 1 exit /b 1
set "OUT=%REPO%\build\windows-x64"
set "LOGS=%REPO%\build\logs"
if not exist "%OUT%" mkdir "%OUT%"
if not exist "%LOGS%" mkdir "%LOGS%"
call :build CPP\7zip\Bundles\Format7zF 7z.dll
if errorlevel 1 exit /b 1
call :build CPP\7zip\UI\Console 7z.exe
if errorlevel 1 exit /b 1
if /i "%~1"=="core" goto notices
call :build CPP\7zip\UI\GUI 7zG.exe
if errorlevel 1 exit /b 1
call :build CPP\7zip\UI\FileManager 7zFM.exe
if errorlevel 1 exit /b 1
:notices
copy /y "%REPO%\DOC\License.txt" "%OUT%\License.txt" >nul
if errorlevel 1 exit /b 1
copy /y "%REPO%\DOC\copying.txt" "%OUT%\copying.txt" >nul
if errorlevel 1 exit /b 1
copy /y "%REPO%\DOC\unRarLicense.txt" "%OUT%\unRarLicense.txt" >nul
if errorlevel 1 exit /b 1
echo Build succeeded: %OUT%
exit /b 0

:build
echo Building %~2...
pushd "%REPO%\%~1"
if errorlevel 1 exit /b 1
nmake /nologo PLATFORM=x64 > "%LOGS%\%~2.log" 2>&1
if errorlevel 1 (
  type "%LOGS%\%~2.log"
  popd
  exit /b 1
)
copy /y "x64\%~2" "%OUT%\%~2" >nul
if errorlevel 1 (
  popd
  exit /b 1
)
popd
exit /b 0
