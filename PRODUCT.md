# Arkvello for Windows

## Purpose

Modernize the appearance of 7-Zip File Manager for Windows 11 while preserving
the functionality and working habits that make the original useful. The primary
user already knows 7zFM: dense file lists, direct commands, keyboard shortcuts,
two panels when needed, and complete archive options are product requirements.

The chosen implementation is a fork of the original C++/Win32 application.
Keep its file/archive operations and command routing; improve the presentation
around them. Rust, C#, WinUI 3 and a replacement frontend are not selected.

## Interface constraints

- Aim for a coherent Windows 11 appearance across the main window, settings,
  operation dialogs and secondary prompts in both GUI executables.
- Keep the menu, command availability, editable paths, list modes, columns,
  selection modes, shortcuts and advanced options accessible.
- Retain the information density of a desktop file manager. Decoration must not
  displace useful rows or conceal controls behind a simplified workflow.
- Design for light, dark and system themes, display scaling, keyboard use,
  contrast themes, long filenames and translated labels. Detailed visual rules
  belong to [DESIGN.md](DESIGN.md); native coverage is recorded per surface.
- Treat Windows-owned dialogs and shell extensions as integration surfaces;
  their exact rendering is not fully controlled by this application.

## Project boundaries

Product name: **Arkvello for Windows**. Repository:
[IzusX/arkvello-windows](https://github.com/IzusX/arkvello-windows).
[Arkvello for macOS](https://github.com/IzusX/arkvello) is an independent project
and must not be changed as part of Windows work.

The pinned base is 7-Zip 26.06; see [upstream policy](docs/UPSTREAM.md).
Initial development targets x64. Installer, associations, Explorer registration,
independent settings and release branding require their own implementation and
validation before distribution.

## Current evidence

Stage 1 established local and CI builds with archive round-trip smoke tests.
The user confirmed that the development file manager launches and looks like
the original 7zFM. This confirms startup and the unchanged appearance, not a
complete manual regression pass.

Stage 2 records the [UI source map](docs/UI-MAP.md) and
[behavior checks](docs/UI-REGRESSION.md). Stage 3 defines the
[Windows visual system](DESIGN.md) and [interface studies](design/preview/README.md).
Stage 4 implements the main window, Add to Archive and nested Options as a
working Win32 prototype. The shared presentation adapter reads Windows app
theme and personalization accent through UISettings, with contrast-theme
priority. See [native evidence and remaining checks](docs/NATIVE-PROTOTYPE.md).
Other dialogs, full DPI/accessibility validation and release packaging remain.
