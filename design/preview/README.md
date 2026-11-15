# Windows 11 interface studies

Open [index.html](index.html) directly in a desktop browser; no server, package
installation or network connection is required. Source is plain HTML/CSS/JS.
The window selection, theme/scale controls and explanatory text belong to the
study viewer, not the proposed application chrome.

The native C++ executables remain unchanged. This is a visual reference for
stage 4, not a web frontend proposal or a replacement archive implementation.
Sample file names, counts, sizes, paths, memory estimates and enabled/disabled
controls are illustrative. Typed values stay in the current page only.

## Review

- Switch between File Manager and Add to Archive. The Add toolbar button opens
  the archive study; Cancel/Escape returns to the manager.
- Compare System, Light and Dark with the same layout. The contrast example is
  illustrative; actual forced-colors support uses browser/system colors.
- Toggle two panels (or F9). Click rows or navigate with Up/Down/Home/End and
  compare focused/inactive selection. This sample navigation does not emulate
  all original 7zFM selection modes or shortcuts.
- Compare normal, empty and long-name data. Full sample names remain available
  in native browser title tooltips; production retains original path/name access.
- Switch 100/125/150/200%. The dialog form scrolls while the action footer
  remains visible. This is CSS scaling, not a Windows mixed-DPI test.
- In Add, open Options to inspect all NTFS/time controls. Explicit override
  flags independently enable their associated values. Escape closes the modal
  and restores focus. The study does not persist/apply these archive settings.
- Show/hide the two sample password fields; the Dialog selector can illustrate
  the original password-mismatch error. No actual codec/password validation is
  implemented. OK announces this instead of creating files.
- Test Tab focus and control hover/pressed/disabled states. Shell menus,
  clipboard, file operations, resizing via the caption and OS file pickers are
  outside this HTML artifact.

Menus/command buttons announce the study boundary where no interaction is
implemented. Dropdown options are abbreviated examples; the native application
must retain upstream's complete options and dependencies.

## Captures

Captured from the actual HTML at 1280 x 920, 100% CSS scale:

- [Main window, light, one panel](captures/main-light.png).
- [Main window, dark, two panels](captures/main-dark.png).
- [Add to Archive, light](captures/archive-light.png).
- [Add to Archive, dark](captures/archive-dark.png).

The captures are design-study renders, not screenshots of `7zFM.exe`.
Visual rules are in [DESIGN.md](../../DESIGN.md); measured checks and remaining
native validation are in [DESIGN-VALIDATION.md](../../docs/DESIGN-VALIDATION.md).
