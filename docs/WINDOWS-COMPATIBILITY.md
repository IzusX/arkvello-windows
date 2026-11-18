# Windows compatibility target

2026-11-16: user requested preserving Windows 10 compatibility where practical.

- Current project minimum: Windows 10 2004, build 19041, x64. The newer Windows
  SDK target describes available compile-time APIs; it does not itself raise
  that minimum. Do not raise the minimum without documenting why and revisiting
  the product decision.
- Windows 11 is the visual reference. Windows 10 uses native solid fallback
  for Mica. Caption, snapping and other system-owned behavior follows the OS.
- Fluent System Icons geometry is bundled; there is no requirement to install
  Segoe Fluent Icons or copy Windows 11 fonts onto Windows 10.
- Before each new OS-specific API/dependency, verify the minimum and use a
  version/capability guard when needed. The .NET 10 / Windows App SDK / Windows
  servicing policies are separate from whether a binary can technically run.

## Release checks still required

No Windows 10 execution was available for this change. Test on Windows 10 22H2
x64 and Windows 11, including a clean machine:

1. App-local WinAppSDK and .NET deployment, missing prerequisites and startup.
2. Folder/archive browse, extraction/test, passwords, conflicts, cancel/close.
3. Solid background fallback, light/dark and actual contrast themes.
4. Bundled icons, disabled/hover/focus, overflow and four list modes.
5. DPI/text scaling, keyboard/screen reader and long paths.
6. Future installer, Explorer integration and associations when implemented.

Windows 10 support remains a target, not a completed compatibility certification.

2026-11-18: a self-contained x64 development folder now includes .NET and
WinAppSDK, generated app PRI/XBF resources and dependency notices. Local native
client diagnostics confirm app-local coreclr.dll loading with an invalid
DOTNET_ROOT. This does not establish a clean-machine or Windows 10 run.
