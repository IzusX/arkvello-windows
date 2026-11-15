# Windows x64 development build

## Prerequisites

- Windows x64; Windows 11 is the intended UI target.
- Visual Studio 2022 or newer / Build Tools with **Desktop development with C++**,
  MSVC x64/x86 tools (including MASM), and a Windows SDK.
- Git and PowerShell. No .NET, Rust, WinUI or third-party package manager is needed.

The build script locates the latest installed MSVC tools using `vswhere` and
initializes `vcvars64.bat`. It uses the original release NMAKE makefiles without
overriding their optimization, runtime, assembler or warning settings.

## Build and check

From the repository root, in a normal PowerShell terminal:

```powershell
.\scripts\build-windows.cmd
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
.\scripts\test-windows.ps1
```

If local PowerShell policy blocks scripts, run the test in a separate process:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-windows.ps1
```

Outputs in `build/windows-x64/`:

- `7zFM.exe`: original file manager.
- `7zG.exe`: GUI archive operations invoked by the file manager.
- `7z.dll`: full archive/codec library.
- `7z.exe`: console client used for functional smoke tests.
- Original license notices.

Keep these binaries together. Build logs are in `build/logs/`; intermediate
objects are in the four component directories under `x64/`. Repeated builds
are incremental. For a clean build, remove those four generated `x64/`
directories listed in `.gitignore`, then run the script again.

The source repository does not contain the release's translated `Lang/` files
or compiled help file. This baseline uses the built-in English interface;
translation/help packaging will be addressed separately. No installer or
Explorer registration is performed.

## Validation scope

The smoke script creates 7z, ZIP and header-encrypted 7z archives containing a
Unicode filename with a space. It tests each archive, extracts it and compares
SHA-256 with the original. Temporary results stay under `build/smoke-*` for
inspection. This checks real archive operations through the built `7z.dll`;
it does not verify GUI behavior.

For a manual baseline check, launch `build/windows-x64/7zFM.exe` and check disk
navigation, opening an archive, and Add/Extract dialogs. The current build still
shares original 7-Zip user settings; use disposable files for archive operations.

GitHub Actions (`Windows x64`) runs the same build and smoke scripts on
`windows-2022` and uploads binaries and logs. These are unsigned development
artifacts, not a product release. The runner image and installed toolset can
change: the build is repeatable from source, but byte-for-byte reproducibility
is not claimed. ARM64 and installer builds are outside this first milestone.

## Initial baseline verification (2026-11-15)

- Local: Visual Studio Build Tools 2026 18.10.2, MSVC 14.51.36231,
  Windows SDK 10.0.26100.0. All four components built successfully.
- Local smoke tests passed under Windows PowerShell 5.1.
- [Windows CI run](https://github.com/IzusX/arkvello-windows/actions/runs/43712956783):
  all four components built and all archive smoke tests passed on `windows-2022`.
- GUI interaction has not been verified: the desktop automation runtime failed
  to initialize (`failed to write kernel assets`, OS error 3). Manual window,
  navigation and dialog checks remain to be performed.

Subsequently, the user confirmed that `7zFM.exe` launches and looks identical
to the original file manager. This closes the startup/appearance baseline
check only. Navigation, dialogs and full GUI behavior remain unverified; use
the [UI regression checklist](UI-REGRESSION.md) to record further coverage.
