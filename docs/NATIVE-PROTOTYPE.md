# Native Win32 prototype

Stage 4, 2026-11-16. This is a development checkpoint for the main window,
Add to Archive and its nested Options dialog. The C++/Win32 control hierarchy,
command IDs, archive engine and helper-process protocol remain in use.

## Status after user review

The prototype is not accepted. The user reported incorrect file-selection
geometry; toolbar icons becoming dark on hover, failing to restore and shifting
between buttons; and visual differences from the approved second Explorer-based
study. These issues remain unresolved. Message-driven checks and static captures
below do not verify ordinary pointer interaction or establish design fidelity.

The revised [product brief](../PRODUCT.md) permits redesigned composition and
selective UI replacement with full functional parity. This prototype supplies
evidence and reusable work; its window/control structure is not a constraint on
the final interface. Reassess the architecture before extending its styling.

## What is implemented

- Shared `ArkvelloTheme.cpp` presentation adapter in both `7zFM.exe` and
  `7zG.exe`: Segoe UI, semantic colors, themed control drawing and DPI-scaled
  dimensions. No new frontend framework or archive implementation.
- Windows app light/dark preference and accent read through the documented
  WinRT UISettings ABI. Subtle navigation/address tint follows personalization;
  focus/default-action colors are adjusted for contrast. System setting, theme,
  colorization and contrast changes trigger a refresh.
- View > Theme > System / Light / Dark. The preference is shared through
  `HKCU\Software\Arkvello\Windows\Appearance\Theme` (DWORD 0/1/2).
  Contrast themes take priority. Other upstream settings still use the original
  7-Zip location; their separation remains planned work.
- Main menu, horizontal toolbar with original commands and scalable authored
  line icons, fixed path band with native editable ComboBoxEx/history,
  neutral selections, one/two panels, shell file icons and status information.
- Details mode retains the native list control and draws its report rows at
  28 DIP. Native selection, editing, keyboard routing and data callbacks remain;
  shell icon indexes, overlays, cut state, column order/alignment and alternative
  marked/current-item semantics are carried into drawing. Other list modes
  remain available.
- Add/Options retain the original controls, fields, format dependencies and
  validation. Forms use scaled layouts, a fixed action footer and scrolling;
  a narrow Add form stacks the right column below the left. Focus scrolls a
  field into view without reparenting controls or changing their IDs.

The [HTML studies](../design/preview/README.md) remain the visual reference.
Existing saved column widths are respected; the prototype does not replace
the user's layout. Native default column sizes apply to newly initialized
column configurations.

## Reproduce integration checks

After building the applications:

```powershell
.\scripts\build-native-ui-test.cmd
.\scripts\test-native-ui.ps1
```

The test launches only the development executables on a private Windows
desktop, without switching the user's desktop or injecting keyboard/mouse
input. It inspects only its own windows. It exercises real Win32 messages and
the real dialog manager, not a simulated archive form.

Coverage: native list modes and two panels, real editable path focus, the FM
Add command starting sibling `7zG.exe` and cancellation through its mapped-file
handshake, format-dependent encrypted-name controls, independent timestamp
override/value state, Options modal ownership, constrained-form scrolling and
visible footer, actual encrypted 7z and ZIP creation. The PowerShell runner
tests and extracts both archives, compares SHA-256, and rejects a wrong header
password. The original CLI/DLL round-trip smoke suite is retained separately.

Evidence stays in `build/native-ui-*` (or the supplied evidence directory).
PNG captures reject blank output. The test restores its Arkvello theme
preference and terminates its own FM instance without saving test geometry or
column changes. Successful GUI archive operations can update the original
7-Zip compression history/settings, just as a manual development run does.
Use disposable test data and keep the four development binaries together.

## Evidence and limits

Local toolchain: VS Build Tools 2026, MSVC 14.51.36231, SDK 10.0.26100.0;
Windows display DPI 144 (150%). Details rows measured 42 physical pixels,
matching 28 DIP. Both themes render in the real executables; GUI-created
encrypted 7z and ZIP archives complete successfully.

Private-desktop captures use legacy non-client rendering. They establish the
client-area appearance and geometry, **not** Windows 11 DWM caption fidelity.
The adapter requests dark/tinted native captions through DWM; verify those on
the normal Windows 11 desktop. No custom caption or snapping code is added.

Remaining validation is explicit:

- Real personalization color changes and automatic light/dark switching on
  the normal desktop; actual Windows contrast themes.
- 100/125/200% and mixed-monitor DPI, large text, long translated labels and
  screen-reader names/state/reading order. The original system-DPI-aware
  manifests are retained; per-monitor awareness is not claimed.
- Full keyboard/mouse, drag/drop, label editing, alternative selection,
  uncommon shell overlays and very long/deep paths against the regression
  checklist. Message-based integration checks do not replace these checks.
- Context/pop-up menus, system scrollbars, native file pickers and message
  boxes can retain OS rendering. Only the app's top menu bar is drawn here;
  it supplies MSAA menu labels and leaves foreign shell owner-draw callbacks
  alone. Styling remaining app dialogs is stage 5 work.
- Packaging, translated Lang files/help, independent upstream settings,
  executable/product branding, installer and Explorer integration are later
  milestones. These binaries retain their upstream filenames and notices.

The fork approach is workable for these surfaces. A scoped drawing adapter is
needed for consistent report rows, headers, buttons and combo edges; blanket
theme calls alone do not achieve the approved appearance. This checkpoint is
not a declaration of release readiness or complete visual parity.

## P0 checkpoint outcomes

These outcomes refer to [UI-REGRESSION.md](UI-REGRESSION.md); partial evidence
does not close a whole scenario. Full manual acceptance remains open.

- **NAV-02/03: partial.** Real editable path child receives focus; original
  one/two-panel command works. Enter/Escape/history, Tab and Alt+F1/F2 remain
  unperformed. **NAV-01/04: not run** beyond starting at a real local fixture.
- **LIST-01/02: partial.** All four native modes retain real item counts;
  report row height, selected/current-item drawing and column resizing are
  exercised. Full sort, reordered columns, persisted widths and alternative
  mouse/keyboard selection remain open. **LIST-03: not run.**
- **CMD-01: partial.** Original FM Add command starts sibling GUI helper and
  cancellation returns through its original handshake. Physical menu/toolbar
  input, accelerators, tooltips and other commands remain open.
  **CMD-02, FILE-01/02/03: not run.**
- **ADD-01/02: partial.** FM invocation/cancellation and direct helper creation
  work; ZIP/7z format-dependent encrypted-name controls are checked. Broader
  codec/level/memory/dependency combinations remain open.
  **ADD-03: not run** as a complete browse/path scenario.
- **ADD-04/05: partial.** Matching password/header encryption, wrong-password
  rejection by CLI, independent timestamp override/value state and modal
  cancellation pass. Password mismatch/show-hide and supported NTFS flag
  combinations remain open. **ADD-06: not run.**
- **OP-01: partial.** CLI Test/Extract and SHA-256 verify GUI-created archives;
  manager-initiated Test/Extract and open-archive copy-out remain open.
  **OP-02/03/04: not run** as GUI progress/conflict/password scenarios.
- **VIS-01/02: partial.** Light/dark main, Add and Options are captured; app
  preference changes reach both open GUI processes. Real system theme/accent
  changes, popups, tooltips and remaining dialogs remain open.
- **VIS-03/04: partial.** 150% display DPI, fixed footer, narrow stacked form,
  scroll-to-focus and restore are checked. Other scales, monitors, large text,
  main-window maximize/restore and long-dropdown scenarios remain open.
- **A11Y-01: partial.** The actual dialog manager's focus path scrolls the target
  field into view; disabled checked values remain visible in both themes.
  Complete physical keyboard flows remain open. **A11Y-02/03 and PERF-01:
  not run.** System-color fallback and MSAA labels are implemented, but this
  does not establish a contrast-theme, Narrator or performance pass.

An independent visual review of the scoped prototype found four drawing/state
issues (disabled marks, combo focus/edges and navigation glyph size). A verdict
pass scored all four resolved in the refreshed native captures at 144 DPI.

API references: [UISettings colors](https://learn.microsoft.com/en-us/uwp/api/windows.ui.viewmanagement.uisettings.getcolorvalue),
[Win32 theme guidance](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/ui/apply-windows-themes),
[DWM color fallback](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmgetcolorizationcolor).
