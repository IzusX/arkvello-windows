# Stage 4 — filesystem workflow increment

2026-11-18. Status: authorized and in progress. The user approved moving to the
next stage after receiving the stage 3 report. Its outstanding real-input,
accessibility, DPI and clean-machine gates remain open. This record describes
the first complete filesystem operation path, not completion of all stage 4.

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

Remaining stage 4 work includes two independent panels and transfer/focus state,
complete selection/mask/incremental typing/shortcut behavior, shell file icons and
overlays, filesystem column options, persistent introduced preferences and path/
destination histories. Stage 3 outstanding acceptance and Windows 10/clean-machine
checks also remain. No full stage completion, parity, installer or release is claimed.

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
```

Run client diagnostics in a normal desktop session. Fixture operations stay in
new build/ subdirectories. Scripts retain evidence and close their own windows;
they do not delete user files or change personalization/clipboard contents.
