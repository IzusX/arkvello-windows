# UI source map and modernization boundaries

Stage 2, inspected 2026-11-15. Source baseline: 7-Zip 26.06,
`d3296e39e1f1f92c7b41c0c3848e65640428d42d`.
This is a source inspection, not a claim that every window has been exercised.
Product constraints are in [PRODUCT.md](../PRODUCT.md); executable scenarios
and their evidence status are in [UI-REGRESSION.md](UI-REGRESSION.md).

Scope clarification, 2026-11-16: this map describes the original implementation
and the former in-place modernization plan. Instructions below to preserve
control IDs, notifications, handlers or window structure apply when reusing
those components. They do not prohibit replacement UI under the revised product
brief. Preserve capabilities and operation semantics; replace/adapt UI-bound
code deliberately and validate equivalent behavior.

The revised stage 1 [capability/reuse assessment](CAPABILITY-ASSESSMENT.md)
extends this historical source map with operation routes, hidden/conditional
features, complete behavior obligations and risks. Its
[technology options](TECHNOLOGY-OPTIONS.md) supersede the old default choice
below; no replacement stack has yet been selected.

## 1. Executables and operation paths

The baseline build contains two separate GUI processes. Styling only `7zFM.exe`
would leave core archive dialogs unchanged.

- **7zFM.exe** owns the main window, panels, menus, Options and file-manager
  dialogs. The component list is [FM.mak](../CPP/7zip/UI/FileManager/FM.mak)
  plus the [file-manager makefile](../CPP/7zip/UI/FileManager/makefile).
- **7zG.exe** owns Add to Archive, Extract and Benchmark when invoked through
  the external operation path. See the
  [GUI makefile](../CPP/7zip/UI/GUI/makefile) and
  [GUI.cpp](../CPP/7zip/UI/GUI/GUI.cpp).
- **7z.dll** supplies archive handlers/codecs. It is not the file-manager UI.
  **7z.exe** is the console client used by the existing smoke tests.

For Add, the chain is `CPanel::AddToArchive` in
[Panel.cpp](../CPP/7zip/UI/FileManager/Panel.cpp), then `CompressFiles` /
`Call7zGui` in [CompressCall.cpp](../CPP/7zip/UI/Common/CompressCall.cpp), then
`7zG.exe` and [UpdateGUI.cpp](../CPP/7zip/UI/GUI/UpdateGUI.cpp).
`Call7zGui` starts the sibling executable and transfers file selections through
a mapping/event handshake. Preserve its quoting, file lists, switches and wait
semantics during visual work.

Extract/Test on selected archive files also use `CompressCall.cpp`. Copying
items out of an already open archive is another path:
[App.cpp](../CPP/7zip/UI/FileManager/App.cpp) and
[PanelCopy.cpp](../CPP/7zip/UI/FileManager/PanelCopy.cpp) execute folder/archive
operations inside the manager with callbacks and progress. Hashing likewise
has internal and external paths; see
[PanelCrc.cpp](../CPP/7zip/UI/FileManager/PanelCrc.cpp).
Do not assume every archive operation launches the helper.

[CompressCall2.cpp](../CPP/7zip/UI/Common/CompressCall2.cpp) is an alternate
implementation guarded by `#ifndef Z7_EXTERNAL_CODECS`; it is not the object
selected by the current file-manager makefile. Do not base this fork's UI work
on that alternate configuration.

## 2. Main window and file panels

**Frame, menu routing, splitter and window geometry.**
[FM.cpp](../CPP/7zip/UI/FileManager/FM.cpp): `InitInstance`, `WndProc`,
`ExecuteCommand`, `CApp::MoveSubWindows`.
The native overlapped frame owns the menu and one/two-panel layout. Preserve
resize/maximize behavior, panel focus, splitter hit testing, saved geometry and
command delivery. Styling the title bar must not replace native window behavior.

**Shared command toolbar.**
[App.cpp](../CPP/7zip/UI/FileManager/App.cpp): `CreateToolbar`, `AddButton`,
`ReloadToolbars`, `OnNotify`; [App.h](../CPP/7zip/UI/FileManager/App.h).
One Win32 toolbar combines the archive group (Add, Extract, Test) and standard
group (Copy, Move, Delete, Info). Both groups can be hidden; button size and
labels are configurable. Bitmap resources are in
[resource.rc](../CPP/7zip/UI/FileManager/resource.rc), with small/large BMP
variants and a magenta mask. Modern icons must retain command IDs, labels,
tooltips and all visibility/size combinations. Reapply styling after recreation
in `ReloadToolbars`, including language changes.

**Each panel's path header.**
[Panel.cpp](../CPP/7zip/UI/FileManager/Panel.cpp): `OnCreate`, `ChangeWindowSize`,
`OnSize`, `CMyComboBoxEdit::OnMessage`.
The header consists of Rebar, parent-folder Toolbar and ComboBoxEx with an edit
child and shell icons. The dropdown is a separate surface to check. Preserve
typed paths, history, Enter/Escape behavior, icon alignment and panel-specific
focus. A breadcrumb-only replacement would remove an existing interaction.

**File list, column header, inline rename and scrollbars.**
[Panel.cpp](../CPP/7zip/UI/FileManager/Panel.cpp),
[PanelItems.cpp](../CPP/7zip/UI/FileManager/PanelItems.cpp),
[PanelListNotify.cpp](../CPP/7zip/UI/FileManager/PanelListNotify.cpp),
[PanelSort.cpp](../CPP/7zip/UI/FileManager/PanelSort.cpp) and
[PanelSelect.cpp](../CPP/7zip/UI/FileManager/PanelSelect.cpp).
The control is a standard ListView, subclassed as `CMyListView`. It supports
large icons, small icons, list and details; dynamic archive properties supply
columns. The creation code does not enable `LVS_OWNERDATA`.
Preserve shell icon/image-list ownership, column order/widths, sorting,
incremental search, drag/drop, inline edit, focus and selection as separate
states. `OnCustomDraw` already paints alternative selections pink and deleted
items red; a palette change must account for those semantics rather than
overwrite them. Do not replace the ListView solely to restyle it.

**Panel status bars and active panel.**
[Panel.cpp](../CPP/7zip/UI/FileManager/Panel.cpp): status creation and sizing;
[PanelListNotify.cpp](../CPP/7zip/UI/FileManager/PanelListNotify.cpp):
`Refresh_StatusBar`; [App.cpp](../CPP/7zip/UI/FileManager/App.cpp): focus callbacks.
Each panel owns its status bar. Preserve counts, sizes, focus return after
dialogs and a clear distinction between the active and inactive panels.

**Menus, context menus and keyboard input.**
[resource.rc](../CPP/7zip/UI/FileManager/resource.rc) defines File, Edit, View,
Favorites, Tools and Help, including advanced Open Inside variants, CRC,
streams and links. [MyLoadMenu.cpp](../CPP/7zip/UI/FileManager/MyLoadMenu.cpp)
localizes and dynamically prepares menus and dispatches commands.
[PanelMenu.cpp](../CPP/7zip/UI/FileManager/PanelMenu.cpp) integrates context
menus, including shell commands. Preserve dynamic enable/check state and shell
extension message handling. Menu captions alone are not the shortcut source:
[PanelKey.cpp](../CPP/7zip/UI/FileManager/PanelKey.cpp), ListView/edit subclasses
in `Panel.cpp`, and the accelerator resource all handle input.

## 3. Dialog inventory

The following groups cover the dialog resources compiled by the two GUI
makefiles. Names describe baseline behavior, not proposed replacements.
Each resource has its corresponding C++ class/handler unless noted otherwise.

### File-manager dialogs

All links below point into `CPP/7zip/UI/FileManager`.

- [CopyDialog](../CPP/7zip/UI/FileManager/CopyDialog.rc): destination/history and
  copy/move options. Entry: `CApp::OnCopy` in `App.cpp`; also used for Combine.
- [ComboDialog](../CPP/7zip/UI/FileManager/ComboDialog.rc): reusable editable
  input, including create file/folder, selection masks and comments.
  Labels/title vary by caller; inspect `PanelOperations.cpp`,
  `PanelSelect.cpp` and `BrowseDialog.cpp`.
- [EditDialog](../CPP/7zip/UI/FileManager/EditDialog.rc): reusable text view/edit
  dialog; preserve multiline content, copying and resize behavior.
- [ListViewDialog](../CPP/7zip/UI/FileManager/ListViewDialog.rc): reusable list,
  including Properties populated in `PanelMenu.cpp` and folder history in
  `PanelFolderChange.cpp`; keep all property values and history actions.
- [MessagesDialog](../CPP/7zip/UI/FileManager/MessagesDialog.rc): diagnostic
  messages; preserve complete text and scrolling.
- [SplitDialog](../CPP/7zip/UI/FileManager/SplitDialog.rc): output location and
  volume sizes. Split/Combine execution is in `PanelSplitFile.cpp`.
- [LinkDialog](../CPP/7zip/UI/FileManager/LinkDialog.rc): link creation; preserve
  link type, target, validation and cancellation.
- [BrowseDialog2](../CPP/7zip/UI/FileManager/BrowseDialog2.rc): temporary-file
  browser/cleanup, entered via Tools and `MyBrowseForTempFolder`.
- [AboutDialog](../CPP/7zip/UI/FileManager/AboutDialog.rc): version and project
  information; branding and acknowledgments belong to the later branding stage.

### Dialog code shared by both GUI executables

These are compiled from FileManager sources into both programs; resource
inclusion is explicit in the two `resource.rc` files. Visual changes must be
validated in both processes, including helper dialogs with no manager open.

- [ProgressDialog2](../CPP/7zip/UI/FileManager/ProgressDialog2.cpp),
  [resource](../CPP/7zip/UI/FileManager/ProgressDialog2a.rc): progress, elapsed/
  remaining time, speed, sizes, messages, Pause, Background and cancellation.
  Preserve thread synchronization, close/cancel races, final status and error
  details. `ProgressDialog.cpp/.rc` is older code not selected by these makefiles.
- [OverwriteDialog](../CPP/7zip/UI/FileManager/OverwriteDialog.rc): old/new file
  information, Yes, Yes to All, No, No to All, Auto Rename and Cancel. These
  actions are not interchangeable and must remain individually available.
- [PasswordDialog](../CPP/7zip/UI/FileManager/PasswordDialog.rc): password entry
  and show/hide behavior; also appears while opening archives in the manager.
- [MemDialog](../CPP/7zip/UI/FileManager/MemDialog.rc): memory request and its
  operation choices. Preserve limits and exact results returned to the caller.
- [BrowseDialog](../CPP/7zip/UI/FileManager/BrowseDialog.cpp): file/folder picker
  wrappers and custom fallback. `MyBrowseForFolder` and `BrowseForFile` select
  native/custom paths according to path characteristics, errors and build
  guards. Preserve long/device path behavior; do not replace only the common
  case and lose the fallback.
- `ComboDialog`, `EditDialog` and `ListViewDialog` resources/code are also shared.
  Resource inclusion does not mean every caller uses every template.

### Archive helper dialogs

- [CompressDialog.cpp](../CPP/7zip/UI/GUI/CompressDialog.cpp) and
  [resource](../CPP/7zip/UI/GUI/CompressDialog.rc): Add to Archive. Keep archive
  path/history, format, level, method, dictionary, word size, solid size,
  threads, memory settings/estimates, volumes, raw parameters, update/path
  modes, SFX, shared files, delete-after-compression and encryption.
  `OnCommand`, `FormatChanged`, `MethodChanged`, `SetMemoryUsage` and `OnOK`
  couple these fields. Reuse those handlers; preserve IDs and notifications.
  Switching format must retain its existing enabled/hidden state transitions.
- [CompressOptionsDialog.rc](../CPP/7zip/UI/GUI/CompressOptionsDialog.rc), handled
  in `CompressDialog.cpp`: nested Options for NTFS links/streams/security and
  timestamp storage/precision. Explicit override flags and values are separate
  controls; preserving the visible checkbox alone is insufficient.
- [ExtractDialog](../CPP/7zip/UI/GUI/ExtractDialog.rc): output path/history,
  optional output name, path mode, root-duplication handling, overwrite mode,
  password and file security. Logic is in `ExtractDialog.cpp` / `ExtractGUI.cpp`.
- [BenchmarkDialog](../CPP/7zip/UI/GUI/BenchmarkDialog.cpp) and
  [resource](../CPP/7zip/UI/GUI/BenchmarkDialog.rc): ordinary Benchmark and total
  mode (`IDD_BENCH_TOTAL`); preserve threads/dictionary, results/log, Stop/
  Restart and resizing. Do not treat only the ordinary template as the whole UI.

### Options property sheet

[OptionsDialog.cpp](../CPP/7zip/UI/FileManager/OptionsDialog.cpp) builds six pages:

- `SystemPage`: file associations and application choices.
- `MenuPage`: shell integration/context-menu settings (page caption is 7-Zip).
- `FoldersPage`: working/temp folder settings.
- `EditPage`: viewer/editor/diff program settings.
- `SettingsPage`: dots, real icons, full-row selection, grid, single-click,
  alternative selection, system menu, large pages and extraction memory limit.
- `LangPage`: language selection and subsequent menu/toolbar reload.

Their `.rc` files include layout fragments named `*Page2.rc`; those fragments
are also used in normal desktop pages. Do not classify all `*2.rc` files as
unused CE UI. `DialogSize.h` selects desktop IDs; small dialog variants guarded
by `UNDER_CE` are outside the Windows x64 target.

Property sheets use a separate creation procedure in
[PropertyPage.cpp](../CPP/Windows/Control/PropertyPage.cpp), including Apply,
activation and reset notifications. A hook only in modal dialog creation will
miss the sheet container and can miss its page initialization.

## 4. Cross-cutting presentation boundaries

**Resources and fonts.** [GuiCommon.rc](../CPP/7zip/GuiCommon.rc) defines shared
spacing/button macros and `FONT 8, "MS Shell Dlg"`. Dialog units and translated
labels depend on that font. A global font change is not sufficient: inspect
each layout, dynamically moved controls and minimum sizes. This resource is
used beyond the two GUI executables; constrain Arkvello changes to intended
targets instead of unintentionally restyling SFX/tools.

**Initialization and recreation.**
[Dialog.cpp](../CPP/Windows/Control/Dialog.cpp) attaches the HWND and invokes
the virtual `OnMessage`/`OnInit`. Several dialogs override those functions;
some call the base after their own initialization. Any common styling hook
must have a defined ordering and must not consume command/notify messages.
Include dynamically created children, dropdowns, ListView headers/rename edits,
tooltips, destroyed/recreated toolbars and property sheets in the coverage plan.

**Theme support.** Both executables use Common Controls v6. `FM.cpp` contains
commented-out dark-mode experiments; they are not a working theme layer.
There is no active `WM_THEMECHANGED`/`WM_DPICHANGED` handling in the inspected UI
tree; the `WM_SETTINGCHANGE` case in `FM.cpp` is also commented out.
Microsoft's [Win32 theme guidance](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/ui/apply-windows-themes)
separates title-bar theming from repainting application content. A dark caption
alone will not style the client controls. Do not adopt the old commented
UxTheme ordinal calls as a supported foundation.

**DPI.** Both [FM manifest](../CPP/7zip/UI/FileManager/7zFM.exe.manifest) and
[GUI manifest](../CPP/7zip/UI/GUI/7zG.exe.manifest) declare `dpiAware=true`,
not Per-Monitor V2. Main-window, header and status layout contain fixed pixel
metrics; toolbar bitmaps have fixed dimensions. Treat per-monitor scaling as
layout work in both processes, with checks for dialogs already open when moved.
[Microsoft DPI guidance](https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows)
explains the required response to DPI changes. Do not change the manifest mode
in isolation and assume the existing layout is ready.

**System-owned surfaces.** `MessageBoxW`, folder/file pickers and shell extension
menus use Windows behavior outside the application's resource layouts. Keep
correct ownership, modal behavior, keyboard input, cancellation and path
support. Document any observed theme mismatch; do not promise exact visual
identity for third-party menus or OS dialogs. Changing picker APIs is a
separate behavioral change, not a prerequisite to palette work.

**Settings and localization.**
[RegistryUtils.cpp](../CPP/7zip/UI/FileManager/RegistryUtils.cpp),
[ViewSettings.cpp](../CPP/7zip/UI/FileManager/ViewSettings.cpp) and
[ZipRegistry.cpp](../CPP/7zip/UI/Common/ZipRegistry.cpp) still use the original
7-Zip registry namespace. Do not store prototype theme settings there.
Independent Arkvello settings must cover both GUI processes before adding a
persistent appearance preference; full branding/association migration is later.
[LangUtils.cpp](../CPP/7zip/UI/FileManager/LangUtils.cpp) and `MyLoadMenu.cpp`
load strings by resource ID. Preserve IDs/mnemonics. Translations and compiled
help are not packaged in the baseline; do not count them as verified.

## 5. Implementation order and scope for the prototype

Stage 3 defines the visual contract: type/density, palette/state colors, icon
rules, spacing, focus, theme/contrast behavior and scaled layouts. Main window
and Add to Archive are the reference surfaces. No numeric design tokens or
mockup approval are implied by this inventory.

Stage 4 should prove the following on the existing Win32 implementation:

1. A small shared presentation module compiled into `7zFM.exe` and `7zG.exe`,
   handling colors, font/metric lifetime and window/control attachment. Use
   targeted adapters for controls with their own drawing. These are proposed
   responsibilities, not classes that already exist in the repository.
2. Main frame, toolbar, path headers, list/header/rename, status bars and menus
   in light/dark/system modes, with clear normal/alternative selection states.
3. Add to Archive and its nested Options, plus a real operation through progress
   and password/overwrite prompts. Secondary prompts need a shared treatment
   early so the prototype exposes theme gaps rather than hides them.
4. Runtime theme changes and scale/monitor changes without losing typed text,
   current selection, list position or the running operation.
5. A feasibility review using the P0 cases in the regression checklist and a
   list of actual remaining control/OS exceptions. Continue with path 1 unless
   measured limitations justify discussing a different implementation.

Later coverage groups are Extract, remaining shared prompts, Options pages,
file-operation utilities, diagnostics/About and distribution surfaces. Preserve
the codec/archive core, command routing and callback synchronization throughout.
Prefer local presentation changes over broad wrapper refactors, so future
upstream merges remain reviewable.

## Stage 2 completion evidence

- The executable/makefile/resource boundaries and dialog families above were
  inspected against the pinned source.
- Main-window launch and original appearance were confirmed by the user.
- Full interactive baseline, screenshots and appearance measurements remain
  unrecorded; the checklist marks these as pending rather than passed.
- This stage changes documentation only. No source, resources, manifests,
  registry settings or macOS files are modified.
