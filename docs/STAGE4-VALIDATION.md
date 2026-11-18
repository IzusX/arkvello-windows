# Stage 4 — filesystem workflow increment

2026-11-18. Status: authorized and in progress. The user approved moving to the
next stage after receiving the stage 3 report. Its outstanding real-input,
accessibility, DPI and clean-machine gates remain open. This record describes
the first complete filesystem operation path, not completion of all stage 4.

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
order and widths. Saved preferences contain no paths, selections, open sessions
or passwords. Existing scale and archive memory preferences keep their files.
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

Still required for this increment: actual keyboard gestures and key release,
create/destination/overwrite/delete dialog input, real Explorer clipboard and
drag in both directions, cut/partial/repeated paste behavior, actual Recycle Bin
execution/restoration, close during copy/deletion, cross-volume/UNC, actual contrast
and DPI/text-scale transitions. Copying reparse points/cloud placeholders and
explicit ADS sources fails with a clear unsupported result; those capabilities
are not silently removed from the inventory. Copy replacement is not atomic;
already performed work is not rolled back on cancellation. Plain-file recognition
currently inherits the slice's full source hashing cost before shell handoff.

Remaining stage 4 work includes desktop acceptance for both panes and Shell icon
rendering, full selection and shortcut parity, masks and incremental typing,
filesystem column options, window geometry and path/destination histories.
Pane contents and navigation history are retained within the current window session.
Stage 3 acceptance and Windows 10 and clean-machine checks also remain open.

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
