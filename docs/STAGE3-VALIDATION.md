# Stage 3 validation — 2026-11-18

Status: implementation expanded and locally checked; final acceptance pending.
The user requested completing the whole stage. No requirement was removed to
make that status appear complete. Broad stages 4–8 have not been started.

## Runnable result

`build/portable-x64/Arkvello.exe`, with its entire adjacent folder. This is an
unpackaged, unsigned development build, self-contained for .NET and WinAppSDK.
It installs/registers nothing and does not touch the macOS repository.

Architecture and explicit limitations:
[decision 0003](decisions/0003-operation-ownership-and-recovery.md).

Implemented additions:

- Dedicated operation thread; scoped lower priority; cancellation across
  progress/prompt transitions; protected application exit after native completion.
- Original RAR5 memory requests: allow, skip, cancel, explicit Arkvello preference
  saving. Managed hashing/copy also observes cooperative pause/cancel checkpoints.
- Independent current item/marks, Insert marking, right-click/keyboard context
  access, selected format-property columns, keyboard column width/order controls,
  and mark/current/column restoration after an unchanged-root refresh.
- Stream-owned bounded virtual archive open/list/export proof, with explicit
  rejection of Agent update-back on that route. Integrated nested editing uses
  file-backed sessions and exact native item identity.
- Notepad handoff; editor/source fingerprint conflicts; validated nested staging;
  atomic same-volume root replacement with original backup and recovery journal.
  The explicit save confirmation handles editors that reuse an existing process.
- Main-window bands/rows/tiles account for Windows text-scale factor; full
  multi-DPI/large-text acceptance still needs the environments listed below.
- Self-contained publishing, generated XAML resource inclusion, dependency
  notices, actionable missing/invalid native DLL checks and payload hashing.

## Executed checks

1. Native service build: all 60 translation units, MSVC warnings as errors.
   `build/stage3-native-build.log`. No inherited File Manager/GUI object files.
2. **99 production-adapter/backend checks passed**, including the earlier 70,
   real RAR5 memory callback answers, virtual input ownership, property schema/
   typed numeric sort keys/index values/cancellation, paused managed hashing/cancel wake, nested editor
   commit, source/editor conflicts, sharing denial, cancellation and recovery.
   Log: `build/stage3-checks.log`; fixture:
   `build/bridge-checks-ba82dec978c64d90985cd30e66ff23cc/checks.json`.
   A committed copy is [stage3-backend.json](validation/stage3-backend.json).
3. WinUI Release/publish: zero compiler warnings/errors.
   `build/stage3-publish-final.log`. The complete payload manifest's **553** file
   hashes were checked after running the client.
4. **33 application-owned client checks passed**, using real WinUI lists,
   native peers and actual 10,000-file/encoded 7z I/O. The harness runs public
   application methods and native focus/selection, not injected mouse/keyboard.
   `build/stage3-client-checks.log`,
   `build/stage3-fixture/evidence/native-layout.json`;
   committed copy: [stage3-client.json](validation/stage3-client.json).
5. Client matrix: light/dark × application scale 75/100/150% × four views,
   **24 layout cases**, at actual Windows rasterization scale **1.5** and text
   factor **1.0**. Lists realized 21–101 containers for 10,000 real rows. Native
   selection peers were available. A dynamic LZMA2 property reached real text
   elements; current item stayed independent of two marks and survived root
   refresh with the selected dynamic column.
6. The final local I/O sample enumerated 10,000 physical files in about 51 ms,
   opened/listed their compressed 7z in about 215 ms, and tested its real payload.
   The backend's real 100,000-entry TAR list took about 532 ms. These are single
   warm local samples, not latency guarantees or coverage of every codec/disk.
   The whole diagnostic, including theme/layout/capture work, observed a maximum
   dispatcher interval of about 396 ms; this is not a per-operation percentile.
7. A real encrypted-header callback was cancelled through the UI lifetime code.
   A second operation requested application exit while its password prompt was
   present; the window closed after the worker callback returned and busy/job
   ownership was cleared. This verifies the application close route, not a
   physical caption/Alt+F4 gesture.
8. The published executable ran with an invalid DOTNET_ROOT and recorded
   `coreclr.dll` from its own deployment folder. No installed runtime was removed.
   This proves app-local selection on this machine, not a clean VM startup.

Fresh-runner CI for code commit `96e3a6c` also passed:
[Native archive services, run 44227991252](https://github.com/IzusX/arkvello-windows/actions/runs/44227991252).
It independently built the services without old FM/GUI outputs, passed all
99 backend checks and published the self-contained WinUI folder with zero
warnings/errors. This CI job did not run the desktop client diagnostic or a
screen reader. Its uploaded artifacts include the build/test logs and payload
manifest/runtime configuration.

Eight fixture-only RenderTargetBitmap images are under the ignored evidence
directory. They were visually inspected in light Details/dark Large Icons.
That API omits the system caption/backdrop and has composition limitations;
these images do not establish actual desktop Mica rendering or visual acceptance.

## Failures found and fixed

- Late cancellation could leave a password prompt waiting after progress hid.
  The UI now checks cancellation both before and after the dialog transition.
- Window.Close bypassed AppWindow.Closing. The exit command now shares the
  cancel/await policy instead of destroying the window during a callback.
- Numeric dictionary binding produced blank dynamic cells despite valid native
  values. A managed converter is now verified against actual LZMA2 text elements.
- Default .NET publish omitted the generated application PRI/XBF resources and
  the executable failed during XAML loading. Publishing explicitly builds/copies
  those resources and validates them as required payload files.
- Editor saves after staging are detected again before replacement; a write-
  denying editor lease covers the commit. The journal is flushed to a separate
  known file and renamed over the prior record, preserving the older record
  until its replacement is ready.

## Acceptance still required to close stage 3

The Windows Computer Use runtime failed three times, including after its kernel
reset, with `failed to write kernel assets` / OS error 3. No fallback UIA script,
message injection or forced process termination was used to manufacture a pass.
Application-owned diagnostics above remain distinct from real input acceptance.
No Windows Sandbox/VM launcher was available in this environment.

1. Real pointer/keyboard acceptance of the new current/mark/context/column and
   Notepad workflows, including password retry, memory save/reuse, cancelled
   prompts, system-caption/Alt+F4 close during actual decoding/writing and focus
   restoration. Prior navigation acceptance predates these changes.
2. Actual screen reader, contrast themes, Windows text enlargement, 100/125/150/
   200% Windows DPI and mixed-monitor transitions, constrained dialogs and live
   theme/material changes. Application scale cases are not Windows DPI cases.
   The user's display/personalization settings were not changed for the test.
3. A clean Windows end-user environment with no developer SDK/global runtime,
   full-folder deployment and missing dependency behavior. CI compilation and
   local app-root CLR selection are useful evidence but cannot substitute for it.

The process/priority/session ownership decision required by stage 3 is recorded
in decision 0003: retain serialized in-process services and a dedicated worker;
the current priority toggle explicitly covers that primary worker only.
Full operation-wide background/priority parity remains part of the original
stage 5 archive-workflow requirement, not a feature claimed by this technical
slice. Before claiming it, validate a complete worker boundary or another route
covering codec threads and I/O. Native faults remain in-process.

General editor configuration, automatic recovery discovery, complete archive
parameters, original selection/shortcut parity and broader capability migration
remain assigned to their original stages. This bounded proof does not claim
those features. Windows 10 also remains an unexecuted compatibility target.

The above acceptance items prevent declaring stage 3 fully complete. They have
concrete checks and boundaries; they are not erased by successful compilation.

## Reproduce

From PowerShell 7 at the repository root:

```powershell
./scripts/publish-winui.ps1
./scripts/prepare-stage3-fixtures.ps1
./scripts/test-stage3-client.ps1
$env:DOTNET_CLI_HOME = "$PWD/build/dotnet-home"
$env:NUGET_PACKAGES = "$PWD/build/nuget"
dotnet run --project tests/Arkvello.BridgeChecks -- "$PWD"
```

Client diagnostics create a real window and close it after completing their own
checks. They never run in ordinary sessions or save diagnostic scale/theme
choices as user preferences. Fixture/output paths stay under build/.
