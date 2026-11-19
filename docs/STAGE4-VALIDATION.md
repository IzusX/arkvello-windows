# Stage 4 — main window and filesystem workflows

2026-11-18. The main-window implementation is ready for desktop acceptance.
The published build runs on the development machine (Windows x64, build
26300.9974). Windows 10, clean-machine and real desktop input acceptance remain
open. This checkpoint does not advance the project to stage 5.

## Navigation, selection and window state

- The address field accepts absolute paths, paths relative to the current
  filesystem directory or an open archive's containing directory, and folders
  in the currently open archive. Escape
  restores the displayed address. The Back stack remains independent per pane.
  View provides a drive picker and a path history (`Alt+F12`).
- The last 20 successful explicit locations and up to 20 destinations each for
  Copy/Move and Extract are saved with window preferences. The location history
  holds directory and root archive paths. It can be cleared from its dialog.
  Destinations are offered in their respective operation dialogs. The next
  launch starts fresh pane contents. Live archive sessions and passwords never
  enter these histories.
- Edit provides select/deselect by mask, select/deselect by current item type,
  and inversion. Masks support `*` and `?` with case-insensitive matching.
  The list supports keypad shortcuts and type-ahead navigation. Independent
  mark mode keeps existing marks when typing moves the current item.
- Details offers optional filesystem creation/access times and attributes.
  Sort choices include provider order. Built-in timestamp columns support date,
  minute, second, 100 ns precision and UTC. Column settings remain per pane.
- Auto-refresh watches visible filesystem directories with a short debounce and
  retains surviving marks and current item. It pauses while a dialog, rename
  or transfer owns the interaction. An unavailable current folder disables
  mutations until a successful retry. View can turn auto-refresh off.
- The next launch restores the last normal window bounds and maximized state.
  Restored bounds are constrained to the nearest display's working area.

The final Release publish produced zero warnings and errors. All 553 portable
payload hashes matched. The [81 WinUI checks](validation/stage4-main-window.json)
include real directory changes, retry after temporary loss of a directory,
four selection layouts, archive path editing, and cancelling a real overwrite
prompt by requesting window close. The [18 preference checks](validation/stage4-preferences-final.json)
used four separate launches, including a file from the previous settings schema
and a truncated file. All 33 stage 3 regression checks passed again.

The native Shell adapter sent one uniquely named fixture to the actual Windows
Recycle Bin. A separate Shell probe found it by name and original directory,
invoked Restore for that exact entry, and verified the restored bytes. All
three [recycling checks](validation/stage4-recycle.json) passed. The fixture
was created under `build/`. The built-in WinUI dialog still requires real-input
verification.

## Shell icons and window preferences

All four filesystem views use Windows Shell icons. Arkvello requests icons and
available overlays for ordinary local items on a background STA thread as rows
are realized. The icon slot stays the same size when the bitmap arrives. The
request queue and bitmap cache are bounded. A recycled row cannot accept a
result for its previous item. Filesystem refresh invalidates Arkvello's cache.

UNC, non-fixed drives, long paths, offline items and reparse points request
association icons by attributes without opening the item. Their per-item
overlays are unavailable on this route. This path requests icons only, never
thumbnails. Archive rows and Shell failures use the Fluent fallback. See
[SHGetFileInfoW](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shgetfileinfow).

On normal window close, changed preferences are saved to
`%LOCALAPPDATA%\Arkvello\window.json`. The next launch restores the theme,
one/two-panel mode, split ratio, and each panel's view, sort, mark mode, column
order and widths. The final settings schema also includes the histories,
window bounds and view options described above. Existing scale and archive
memory preferences keep their files.
The preferences file is replaced in one rename. An unchanged window does not
rewrite it. A missing or unreadable file falls back to the regular defaults.

The published build passed 48 stage 4 client checks and 33 stage 3 regression
checks. Nine new checks cover all four filesystem views, the overlay index for
a real shortcut, the attributes-only policy, loading and recycling icons in a
601-entry directory, and the archive fallback. The local Windows configuration
supplies a transparent shortcut overlay. See the [Shell icon results](validation/stage4-shell-icons.json).
Ten [preference checks](validation/stage4-window-preferences.json) used three
separate client launches with isolated settings and covered save, restore, and
startup after a truncated file. No user preferences were written by these checks.

## Two panels

`Вид → Две панели` (F9) opens a second list. Click either list or path to
select the pane for the shared toolbar and menus. Tab switches panes from the
file list. Shift+Tab leaves the list through normal keyboard navigation.
Drag the divider to adjust widths, or focus it and use the arrow keys. Home
and double-click reset the split.

Each pane owns its address, back history, current item, marks, view, sorting,
columns, status, and nested archive chain. F9 keeps the active pane visible
when switching to one list. It retains the other pane's controls and archive
handles until that pane navigates away or the window closes.

F5/F6 suggest the other pane's filesystem directory, with an editable
destination. Shift+F5/F6 suggest the current directory. The extraction dialog
also suggests the other filesystem directory when available. Transfers refresh
both visible filesystem lists. With focus in a filesystem list, Alt+Up opens
the current directory in the other pane. Alt+Left/Right send the focused folder
there. The Back button remains available independently in each pane.

The current client suite passed 39 checks, including 16 new split-view
checks with real files and a TAR containing a nested archive. They cover
independent navigation, archive lifetime on hide/show, per-pane view and columns,
copy and move in both directions, a narrow window, and aligned bands in both
themes. See [client evidence](validation/stage4-panels.json).
The tests invoke application code. Real keyboard gestures, divider dragging,
and drag/drop with Explorer still need desktop acceptance.

## Runnable result

`build/portable-x64/Arkvello.exe`, with the entire adjacent folder. Native ABI 3.
Self-contained, unpackaged and unsigned; no installation or shell registration.
The macOS repository and original 7-Zip preferences are untouched.

- Filesystem Copy/Move from toolbar/menu (F5/F6), exact selection/current-item
  precedence, editable destination plus native folder browser, existing overwrite
  answers, real progress, pause and cancellation through the original FS provider.
- F2 inline rename in all four views, Enter/Escape, basename selection, invalid/
  occupied-name retention, refresh/selection/focus restoration. New file/folder
  from File; F7/Ctrl+Shift+N creates a folder. Creation never truncates an existing file.
- Delete confirmation requests the Windows Recycle Bin; Shift+Delete explicitly
  requests permanent deletion. Windows owns delete progress and safety dialogs;
  the adapter waits for its STA worker before release/exit.
- Ctrl+C/X/V, Ctrl+Insert/Shift+Insert and context actions use StorageItems;
  filesystem drag data is deferred, drops target a folder/current directory,
  copy by default or move with Shift. Archive/virtual/link effects remain unavailable.
- Filesystem refresh retains marks/current item. Mutation controls stay disabled
  in archive contexts. Ordinary filesystem file opening uses the engine's explicit
  non-archive result before handing the user-selected file to its Windows association.

See [decision 0004](decisions/0004-filesystem-operations.md) for route choices,
upstream guards, cancellation/partial-output behavior and integration limits.

## Executed evidence

- Native Release build: **61 source units**, W4/WX, no inherited FM/GUI objects.
  Log: `build/stage4-native-build.log`.
- **47 filesystem adapter checks passed**, on new disposable inputs. Unicode,
  invalid/reserved names, creation/rename conflicts and case-only rename, recursive
  copy/move, six overwrite answers and job-scoped all/rename policies, original
  bytes, modification time and NTFS ADS, source recursion prevention, overlapping
  selections, real 96 MiB copy cancellation, pause/resume, sharing denial, a real
  source beyond MAX_PATH, permanent deletion and pre-delete cancellation.
  `build/stage4-file-checks.log`; committed [filesystem evidence](validation/stage4-filesystem.json).
- **99 archive adapter checks passed** with ABI 3, including nested edit/recovery,
  password/conflict/cancel, real RAR5 memory callbacks and the 100,000-entry TAR.
  `build/stage4-archive-regression.log`; committed [archive evidence](validation/stage4-archive.json).
- WinUI self-contained Release publish: zero warnings/errors.
  `build/stage4-publish.log`. All **553 payload hashes** matched after running the client.
- **23 application-owned WinUI checks passed**, including inline editing and
  conflict retention in actual native TextBoxes, all four visible editor slots,
  mark/current restoration, real DataPackage copy/move to the original provider,
  deferred outbound drag data, rejection of text paste, explicit plain-file
  recognition, light/dark control layouts and disabled archive-context mutations.
  `build/stage4-client-checks.log`; committed [client evidence](validation/stage4-client.json).
  The system clipboard was never changed and no external associated application
  was launched by the diagnostic.
- The earlier **33 client layout/lifetime checks passed again** on the final
  published client. `build/stage4-client-regression.log`; committed
  [client regression evidence](validation/stage4-client-regression.json).

Fresh-runner CI also passed for code commit `cdb38b9`:
[Native archive services, run 44236979795](https://github.com/IzusX/arkvello-windows/actions/runs/44236979795).
In 2m27s it built the service closure, passed 99 archive and 47 filesystem checks,
published the self-contained client and uploaded evidence. It did not run a
desktop GUI, change the clipboard or certify Explorer/Recycle Bin behavior.

Two RenderTargetBitmap captures were visually inspected in light/dark. They
omit DWM/Mica and have the previously recorded composition limitations (including
transparent/incorrect-looking address/caption pixels); they do not certify actual
desktop rendering. The existing opaque list/command surfaces remain intact.

## Findings and limitations

The initial deletion check caught the Shell STA requirement. A dedicated STA
route fixed it; real permanent deletion and sharing denial then passed. A test
timestamp assertion initially set the timestamp before writing ADS; writing ADS
changed it. Correcting fixture order proved both source and copied timestamps.
Windows.Storage returned access denied in a restricted execution sandbox. The
same published client and fixtures passed under the normal desktop token.

The Windows Computer Use runtime still fails before JS executes:
`failed to write kernel assets` / OS error 3, including after reset. No fallback
UI automation, injected window messages or forced termination was used. The
above application-owned tests are not mouse/keyboard or screen-reader acceptance.

Desktop acceptance still needs actual keyboard and pointer input, toolbar and
selection geometry, divider dragging, shell icon rendering, real Explorer
clipboard and drag in both directions, cut/partial/repeated paste, closing during
Shell deletion, cross-volume/UNC, contrast and DPI/text-scale transitions, and a
screen reader. The desktop automation runtime returned OS error 3 after a fresh
session reset. No second ready local volume was available for the transfer check.

Remaining parity items stay in the capability inventory, including the full
upstream virtual roots, bookmarks, advanced filesystem properties, reparse and
cloud placeholder copying, and explicit alternate-stream sources. The drive
picker provides access to Windows drive letters and the address field accepts
UNC share paths. Copy replacement leaves completed work in place on cancellation.

### 2026-11-19 follow-up

- Launch accepts a positional file path, including spaces and non-ASCII characters.
  The WinUI diagnostic verifies that it opens the archive on startup using the
  same argument form that Explorer supplies.
- Root browsing keeps a read lease and leaves content hashing to edit-back.
  Archive edit tests verify the lease, deferred hashing and same-length
  modification detection at commit. On a local 256 MiB stored 7z with one item,
  service open/list median was 324.39 ms before and 1.66 ms after across seven
  warm runs per build. This excludes app startup, layout and cold-cache I/O.
- Desktop drag-and-drop remains open. A real pointer gesture to a folder raised
  WinUI's drag start and then completion with no effect. No target handler ran.
  The same gesture moved a disposable file in Explorer. A separate WinUI row
  source and eager StorageItems payload gave the same result in the test and
  were left out of the fix. This is independent of the StorageItems checks.

### File gestures follow-up (2026-11-19)

- Mouse dragging from empty list space or the leading gutter selects items
  with a rectangle. The list captures the pointer and scrolls near its vertical
  edges. Ctrl toggles intersecting items, Shift extends selection, and mark mode
  preserves existing marks. A desktop gesture selected four fixture rows.
- Filesystem drags use OLE and a Shell data object. The window routes each drop
  to the Shell drop target for the folder under the pointer, or for the current
  directory over the list background. The Shell performs the copy or move.
  The app-owned verification routes a copy and a move into a folder row and
  checks the resulting data and originals.
- A physical drag between panes and in both directions with Explorer still
  needs acceptance. The automated pointer tool released the button before Shell
  drag initialization in the observed run. The app skips that expired gesture.

## Reproduce

PowerShell 7, repository root; native source changes require a fresh native build:

```powershell
./scripts/publish-winui.ps1
$env:DOTNET_CLI_HOME = "$PWD/build/dotnet-home"
$env:NUGET_PACKAGES = "$PWD/build/nuget"
dotnet run --project tests/Arkvello.FileChecks -- "$PWD"
dotnet run --project tests/Arkvello.BridgeChecks -- "$PWD"
./scripts/test-stage4-client.ps1
./scripts/test-stage3-client.ps1
./scripts/test-window-preferences.ps1
```

Run client diagnostics in a normal desktop session. Fixture operations stay in
new build/ subdirectories. Scripts retain evidence and close their own windows;
they do not delete user files or change personalization/clipboard contents.
