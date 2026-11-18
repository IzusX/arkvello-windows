# 0004 — Filesystem operations and Windows Shell

2026-11-18. The user authorized starting stage 4 after receiving the stage 3
report with its remaining acceptance gates. Those gates remain open.
Stack stays C# / WinUI 3 plus the pinned C++ services. Native ABI is now **3**;
both archive and filesystem jobs reject an older adjacent bridge. Distribution
must update the entire development folder, not one executable or DLL.

## Copy and move

Reuse `NFsFolder::CopyFileSystemItems` from the pinned `FSFolderCopy.cpp`. It
already provides recursive traversal, CopyFileEx/MoveFileWithProgress, byte
progress and the original filesystem behavior. Do not replace it with a new
managed recursive copier or archive CLI commands.

The service build defines ARKVELLO_FS_SERVICE to exclude CFSFolder's panel/provider
entry methods from that translation unit. Ordinary upstream builds are unchanged.
The small CFsFolderStat sizing routine is extracted from FSFolder.cpp into the
bridge, with an explicit reparse-point rejection. This avoids pulling the drive,
network, comments and icon provider graph into an operation that needs only
explicit paths. Recheck that extraction and the guards on upstream updates.
The dedicated source closure is now 61 translation units. It still compiles from
source with W4/WX/MT and consumes no old FM/GUI objects or window controls.

Adapt the original AskWrite policy to the existing versioned job events:
replace, replace all, skip, skip all, upstream AutoRenamePath and cancel. Choices
are scoped to one job. WinUI uses the existing overwrite/progress/pause/cancel
surfaces, now with source-neutral wording. Copy replacement retains the original
delete-before-copy behavior: it is not an atomic replacement or rollback promise.
Cancellation can leave partial destination data and already moved source items.

Managed preflight normalizes exact absolute paths, collapses overlapping source
selections, rejects drive roots and source-inside-destination recursion, and
checks resolved directory paths so an existing destination junction alias cannot
bypass that check. It observes pause/cancel throughout traversal. The native
route also rejects encountered reparse points. Link/cloud-placeholder copying
and ADS-as-explicit-source operations are not implemented; they fail explicitly.
Copying an ordinary NTFS file's streams remains the upstream CopyFileEx behavior.
These checks do not claim protection against every concurrent path/reparse race.

## Rename, creation and deletion

Creation and rename use exact Windows filesystem primitives, off the UI thread.
Creation uses CreateNew/CreateDirectoryW so an existing item is a conflict.
Rename does not overwrite. The inline editor selects the basename, retains
input on invalid/conflicting names, accepts Enter and cancels Escape/lost focus.
Its key release is consumed at the content root when the focused editor is
removed, preserving the existing navigation gesture boundary.

Deletion has an explicit WinUI confirmation with Cancel as the default. Delete
requests recycling; Shift+Delete requests permanent deletion. The native adapter
uses Windows Shell IFileOperation with its own progress/safety/error surfaces,
the manager HWND as owner and a job-backed progress sink. It records individual
failures and aborted status rather than claiming success from PerformOperations
alone. Recycling requests include a warning if Windows would destroy an item.
There is no automatic permanent-delete fallback on a recycle failure.

[IFileOperation requires STA](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ifileoperation).
The initial console/MTA check failed; the adapter now owns an STA thread and
waits for completion before releasing the job or closing the manager. Archive
workers remain unchanged. Shell UI owns its pause/cancel controls; application
close still requests cooperative cancellation and waits for the native call.
[Operation flags](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifileoperation-setoperationflags)
and [per-item progress/results](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ifileoperationprogresssink)
remain Windows-owned behavior. Full original long-path/delete/link parity needs
the remaining execution cases; recycle behavior is not certified by the
permanent-delete fixture test.

## Clipboard and drag/drop

Use Windows DataPackage/StorageItems rather than filename text or an internal
clipboard. Outbound drag supplies storage items through a deferred provider.
Incoming items need real filesystem paths; virtual/archive items are rejected
until their extraction lifetime is implemented. Main-window controls are disabled
for filesystem mutation while an archive session or another operation is active.

Drops copy by default; Shift requests move. Ctrl+Shift link drops are refused.
The target is the hovered filesystem folder or the current directory. The
accepted effect must be supported by the source. Report move completion only
when the operation succeeds and all input paths have left their source location;
a skipped/partial move retains source state and does not report a completed move.
No synthetic system clipboard content is written during diagnostics. Actual
Explorer cut/copy/drag interoperability and repeated-paste UX remain unaccepted.

[Windows clipboard guidance](https://learn.microsoft.com/en-us/windows/apps/develop/communication/copy-and-paste)
and [ListView drag data](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.listviewbase.dragitemsstarting)
describe this platform boundary. In this environment, Windows.Storage failed
with E_ACCESSDENIED under the execution sandbox but passed with the normal
desktop token. No OS settings, ACLs, privileges or permissions were changed to
work around that restriction.

## Scope still open

Two panels and their session state, full selection/shortcut parity, shell file
icons/overlays, filesystem columns/preferences, path/destination histories,
archive clipboard/drag and broader provider behavior remain stage 4–6 work.
The validation report is [STAGE4-VALIDATION.md](../STAGE4-VALIDATION.md). A working
bounded increment does not mark the whole stage or functional parity complete.
