# 0003 — Operation ownership, virtual input and edit recovery

2026-11-18. This supplements decision 0002 for the stage 3 working slice.
Stack: C# / WinUI 3, pinned native 7-Zip services, C ABI 2. The validation record
is [STAGE3-VALIDATION.md](../STAGE3-VALIDATION.md). Outstanding acceptance is
explicit; this decision does not declare stage 3 complete.

## Execution and lifetime

Keep the current native services in process, with one serialized operation on
a dedicated managed thread. The UI remains on its normal dispatcher. SafeHandles
own sessions/jobs; callback delegates remain rooted until the synchronous call
returns. A native callback can block for a dispatcher-owned prompt. Cancel wakes
pause and dismisses the prompt, including the transition between progress and
prompt. Never dispose sessions/codecs or exit while the call is active.

The system caption's AppWindow.Closing is cancellable. Window.Close() bypasses
it: application exit commands therefore use RequestClose, cancel, await native
completion, and only then call Close. An application-owned test caught and
verified this distinction with an actual encrypted-archive password callback.
This test does not inject a caption click or Alt+F4.

The priority toggle changes the dedicated operation thread, under a lock shared
with its creation/exit. It never lowers the UI process priority. Its tooltip
states that additional codec threads may retain normal priority. This is a
bounded control, not original 7zG's whole-operation/background parity.

No additional process or console protocol is introduced merely to wrap the same
interfaces. In-process native faults can still terminate the client. Full
operation-wide CPU/I/O priority and codec fault containment would justify a
separate worker before those capabilities are claimed in stage 5; the session,
index, callback and ownership contracts must survive that move. This is an
explicit limitation, rather than a fallback through CLI with different prompts.

## File-backed and virtual sessions

Use file-backed child sessions for the integrated nested/edit workflow. Each
child retains its live parent and exact native index, owns one regular decoded
file, and closes before its file is removed. Archive paths are display values;
duplicate names never identify a replacement. Agent's update route needs this
physical lifecycle; splitting/multi-volume and editor handling must not be
assumed equivalent for a memory stream.

The production adapter also exercises bounded virtual input: at most 64 MiB,
copied into a session-owned CBufferInStream. Listing/export remain usable after
the managed input changes and is collected. Updates explicitly return E_NOTIMPL
for this route. It proves the underlying stream interfaces without selecting an
unbounded RAM-first UI policy or adding a second full editing implementation.

## Editor and root replacement

The integrated bounded editor is Windows Notepad, launched by its System32 path
with the owned working filename as one argument. No archive entry is executed.
The user explicitly confirms that its file/tab was saved and closed; Notepad can
reuse a process, so process exit cannot establish editor completion. General
editor configuration, binary viewers, diff and automatic watcher UX remain
stage 4–6 functionality, not part of this bounded proof.

Capture a streamed SHA-256 identity of the root before/after opening. Editor
handoff rejects a changed source. Take a write-denying snapshot of the editor
file; stage the exact-index replacement through every ancestor with Agent and
reopen/test every result. Detect editor saves during staging and before commit.
Keep a read lease on the editor file during commit, so it cannot be changed or
renamed while the prepared output is installed.

Close the native source chain, copy the verified output beside the destination,
flush it, compare the source again and use File.Replace with a unique backup.
No delete/move fallback is used when replacement is unsupported or denied.
Cancellation is checked during hashing/copy and before replacement; once the
atomic filesystem replacement starts it is allowed to finish. Reparse-point
targets are rejected by this bounded route.

File.Replace is not a pathname compare-and-swap. A rename race between final
comparison and replacement can still occur. The backup contains the actual
replaced target and is checked afterward; a mismatch reports a conflict and
retains both versions instead of attempting a second unsafe automatic overwrite.
This does not provide transactional semantics on every filesystem/network share.

The owned working file, flushed recovery journal and original backup survive
success/failure/close. Backups are not silently deleted. Known temporary stage
artifacts are cleaned nonrecursively. Recovery records contain paths, item index
and hashes, never passwords. Automatic recovery discovery/retention controls
are later product work. Power-loss durability of filesystem metadata is not
certified by managed Flush or the local tests.

## Prompts and presentation

RAR5's original IArchiveRequestMemoryUseCallback reaches the WinUI prompt with
required/allowed size and upstream flags. Allow, skip and cancel are distinct;
skips/reports become issues and cannot produce clean success. Explicitly saving
a rounded limit writes Arkvello's own preference, never original 7-Zip settings.
This is not a hard memory allocator limit for all codecs. The real fixture uses
a stored RAR5 item declaring a dictionary, without allocating that dictionary.

Format-provided column schema/property IDs cross the ABI separately from rows.
Native numeric/filetime values retain typed sort keys alongside display text;
numeric ordering does not compare formatted strings.
Only selected columns are loaded, on the operation worker; cache keys include
session identity and native index. Refresh reloads them and restores marks and
current item only for an unchanged source. Runtime XAML dictionary indexing did
not render reliably; a managed value converter performs that lookup, verified
against real rendered LZMA2 text. Native list peers retain Selection support.
These tests do not replace pointer/keyboard and screen-reader acceptance.

## Deployment

Select an unpackaged, self-contained x64 development folder with both .NET and
WinAppSDK app-local. Preserve generated app PRI/XBF resources explicitly: the
default .NET publish list omitted them and caused a real startup failure.
Include upstream, Fluent, .NET and NuGet dependency notices and a hashed payload
manifest. The local diagnostic verifies that coreclr.dll loads from this folder
with an invalid DOTNET_ROOT. Clean VM, Windows 10, signing, installer and shell
entry acceptance remain distinct checks; no installation/registration occurs.
