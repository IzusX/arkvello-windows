# 0002 — Initial WinUI list and native boundary

2026-11-16. Implementation decision for the authorized stage 3 technical slice,
under the user-accepted [C# + WinUI 3 stack](0001-ui-stack.md). This is not a
claim that stage 3 or release validation is complete.

## Selected for the slice

- .NET SDK 10.0.401, `net10.0-windows10.0.26100.0`, x64. Stable Windows App SDK
  **2.6.5**, pinned by `packages.lock.json`; no experimental TableView dependency.
  [Microsoft's release notes](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-2-0)
  identify 2.6.5 as the November 2 stable release. Versions were checked against
  [the official NuGet feed](https://api.nuget.org/v3-flatcontainer/microsoft.windowsappsdk/index.json).
- Stable WinUI ListView for Details/List and GridView for Small/Large Icons.
  A shared column description generates header and row templates. Details
  retains horizontal overflow, 28 DIP rectangular rows, sorting and width/order
  controls. Native virtualization is retained; no per-cell native calls.
  Complete dynamic archive properties and original selection semantics remain
  stage 3/4 work, rather than a claim that these four columns finish the list.
- A small versioned C ABI (`av_abi() == 1`) around existing C++ Common services.
  Opaque session/job handles, UTF-16 borrowed callback strings, explicit
  ownership, native item indices and typed events. No STL objects, exceptions,
  HWNDs or console text cross into the C# presentation model.
- Direct `CArchiveLink`/`OpenArchive` for detection/open/volumes/passwords;
  `CArchiveExtractCallback` for output paths, file handling, metadata and
  conflict policies. This avoids duplicating the extraction engine or reducing
  interactive behavior to console parsing. The original CLI remains a valid
  candidate for other operations, not a second implementation of this path.
- Synchronous native work on a managed worker, with callbacks marshalled to
  the XAML dispatcher. Prompts block the worker until an explicit reply;
  the UI thread never waits for that worker. Progress/current-path updates
  coalesce at 100 ms instead of flooding the dispatcher.
- Cooperative job cancellation and pause. Cancel wakes a paused job; completion
  waits for native extraction/file close. No process termination, suspension,
  rollback guarantee or fake completion percentage.
- In-process for this bounded slice. Sessions are serialized and non-reentrant;
  cancel/pause may be concurrent. A separate native worker remains open if
  codec fault containment, priority semantics or session shutdown justify it.
  `Background` priority has deliberately not been implemented as a change to
  the whole UI process or a detached daemon.
- An unpackaged development executable with the WinAppSDK runtime beside it;
  .NET remains framework-dependent on the development machine. It does not
  register a package, install runtime frameworks or touch file associations.
  Clean-machine/self-contained .NET distribution remains a gate.

## Ownership and limits

The C# SafeHandles own native jobs/sessions. A native call holds the SafeHandle
reference for its duration. Delegates stay rooted by the job. Native callback
pointers expire on callback return; the adapter copies strings/records. Passwords
are not put in argv, logs or persistent settings; temporary native/managed
character buffers are cleared. UI/API strings are managed immutable strings,
so this is not a secure-erasure guarantee. Do not cache password state across jobs.

Only the `7z.dll` adjacent to the bridge is loaded, by absolute module-relative
path. No lookup through the user's installed 7-Zip, registry or current working
directory. The codec DLL and bridge derive from the pinned upstream source.
Optional plugin loading is not implemented by this slice.

For a first build, the bridge links a static library of existing FM-build objects;
the linker selects referenced Common/Windows/stream code. It does not instantiate
FM windows or use the old presentation adapter. This is a temporary **build**
coupling: a dedicated upstream source target must replace the broad object
archive before regular application development/upstream updates. Upstream
headers require a clean rebuild because the inherited nmake dependency tracking
is incomplete. No upstream source was edited for this slice.

The C# archive tree is a bounded projection of typed archive entries, retaining
native indices and duplicate file identities. It does not establish the final
nested/virtual-folder/edit-back model. Reuse Agent folder/proxy services where
they preserve those semantics more cheaply; do not expand string-prefix
navigation into an unreviewed replacement for all Agent behavior.

## Evidence and consequence

See [WINUI-SLICE.md](../WINUI-SLICE.md) for executed bridge, large-list and UI
checks, failures corrected and remaining gates. Current results support
continuing the selected stack and initial direct route. They do not prove
runtime parity, assistive-technology completeness or clean-machine deployment.
No stack change, broad feature migration, merge or publication follows from
this decision alone.
