# Stage 1: capability and reuse assessment

2026-11-16. Source assessment complete for the known Windows x64 surface.
This is a planning deliverable, not implementation or runtime parity approval.
Baseline: 7-Zip 26.06, `d3296e39e1f1f92c7b41c0c3848e65640428d42d`.
Working prototype HEAD: `0c94232db418510ffabe7c1f59ce40518e217905`;
the existing documentation corrections are present locally.

Read with [PRODUCT.md](../PRODUCT.md), [source map](UI-MAP.md),
[regression cases](UI-REGRESSION.md) and [technology options](TECHNOLOGY-OPTIONS.md).
No new UI framework, language, ABI or helper process is selected by this record.

## Findings that determine the size of the work

The reusable foundation has several distinct layers, not just `7z.dll`:

- Archive handlers/codecs and archive interfaces in `CPP/7zip/Archive`,
  `Compress`, `Common` and the C sources. Keep the engine; no rewrite is needed.
- `UI/Common` contains enumeration, extraction/update orchestration, codec
  loading, path/overwrite policies and hash calculation. These are valuable
  application services, even though the directory name contains UI.
- `UI/Agent` supplies archive folders, proxy trees, typed properties and
  operations by item index. It is compiled into FM, rather than being the
  complete file-manager API exported by `7z.dll`. It also depends on settings,
  Windows helpers and upstream C++ types; extraction is an adaptation task.
- `FSFolder`, `FSFolderCopy`, `FSDrives`, `NetFolder`, `RootFolder` and
  `AltStreamsFolder` supply filesystem/virtual-root behavior. These are not
  archive CLI features. OS and shell integration still require Windows code.
- `Panel*`, `App`, GUI dialog classes and callback implementations mix decisions,
  presentation, globals, thread lifetime, registry state and window handles.
  Replacing their windows requires preserving their behavior deliberately.

Consequently, a new shell is a substantial desktop application project: panel
state/input, a capable list, archive sessions and external-edit lifecycle,
complete option models, operation supervision, settings/localization, Windows
integration and distribution. Reusing codecs saves the archive algorithms;
it does not implement these workflows. Counting source lines would not give a
credible schedule. The highest-risk units below govern the first working slice.

## Reading the inventory

Each entry records required behavior/options, source ownership and UI coupling,
a provisional execution/reuse route and verification/unknowns. All entries have
**source-inspected** evidence only unless the CLI probes are explicitly cited.
Existing partial GUI evidence remains in [NATIVE-PROTOTYPE.md](NATIVE-PROTOTYPE.md).
Tests listed below are required future checks, not passes.

Routes are recommendations for evaluation:

- **Keep**: reuse the existing lower-level implementation and known codec build.
- **Adapt**: retain logic behind an application-facing boundary, replace HWND,
  global-settings or dialog dependencies where necessary.
- **New UI**: implement presentation/state/input with the chosen toolkit.
- **CLI candidate**: compare for a bounded task; not approved for full parity.

These labels are independent of language and process location. An Adapt service
can live in a DLL or a worker. No percentages of reusable code are claimed.

## Navigation and presentation

### NAV-01: filesystem, virtual roots, drives and network

Behavior: local folders, Computer/Documents/network roots, UNC and the available
drive/volume/device contexts, with original read-only/unsupported/denied results.
Sources: [RootFolder.cpp](../CPP/7zip/UI/FileManager/RootFolder.cpp),
[FSFolder.cpp](../CPP/7zip/UI/FileManager/FSFolder.cpp),
[FSDrives.cpp](../CPP/7zip/UI/FileManager/FSDrives.cpp),
[NetFolder.cpp](../CPP/7zip/UI/FileManager/NetFolder.cpp).
Coupling: Windows APIs, localized names, shell icons and folder interfaces.
Route: Adapt providers; New UI navigation. CLI is not the filesystem model.
Verify NAV-01/02/04; privileged device contexts need an isolated baseline, not
real disks used as test outputs. UNC/device behavior remains runtime-unverified.

### NAV-02: open archive sessions and flat view

Behavior: directory-like archive navigation, virtual folders, typed/dynamic
properties, item indices, flat mode and supported alternate-stream views.
Sources: [Agent.h](../CPP/7zip/UI/Agent/Agent.h),
[ArchiveFolder.cpp](../CPP/7zip/UI/Agent/ArchiveFolder.cpp),
[AgentProxy.cpp](../CPP/7zip/UI/Agent/AgentProxy.cpp),
[IFolder.h](../CPP/7zip/UI/FileManager/IFolder.h).
Coupling: C++ COM-style interfaces and archive lifetime; presentation need not
own archive pointers. Route: Adapt session service; retain engine/proxies.
`7z l -slt` is formatted text without an equivalent live session/item identity.
Verify NAV-01, LIST-01, UTIL-01; duplicate paths, unnamed/raw properties and
indices invalidated by update must be tested before designing public handles.

### NAV-03: nested archives and parent restoration

Behavior: open a member as an archive through an available seekable stream or
temporary/memory extraction; return through the correct parent, retaining
password/Zone.Id/temp-file lifetime and prompting for changed nested archives.
Source: [PanelItemOpen.cpp](../CPP/7zip/UI/FileManager/PanelItemOpen.cpp),
especially `OpenItemInArchive` and `OpenParentArchiveFolder`.
Coupling: panels, `CFolderLink`, callbacks, memory limits, prompts and cleanup.
Route: Adapt session/lifecycle logic, New UI prompts. CLI-only recreation loses
the existing orchestration and is not the first candidate. Verify NAV-01 and
FILE-01 with nested encrypted/solid/read-only fixtures; update-back is high risk.

### NAV-04: Open Inside variants and open policy

Behavior: ordinary Open, explicitly Open Inside, Inside `*`, Inside `#`, and
Open Outside; recognition/fallback must not accidentally execute a member.
Sources: [MyLoadMenu.cpp](../CPP/7zip/UI/FileManager/MyLoadMenu.cpp),
[PanelItemOpen.cpp](../CPP/7zip/UI/FileManager/PanelItemOpen.cpp),
[OpenArchive.cpp](../CPP/7zip/UI/Common/OpenArchive.cpp).
Route: Adapt explicit open policy/format selection, New UI commands.
Verify NAV-01, UTIL-01, FILE-01 on recognized/unrecognized/multi-layer formats;
preserve suspicious-name warnings and external-launch failure reporting.

### NAV-05: editable paths, history and ten bookmarks

Behavior: typed/pasted paths, history, root/parent, invalid-path recovery,
Favorites slots 0..9 and keyboard open/set combinations. Breadcrumbs must retain
editable-path access if introduced. Sources:
[PanelFolderChange.cpp](../CPP/7zip/UI/FileManager/PanelFolderChange.cpp),
[PanelKey.cpp](../CPP/7zip/UI/FileManager/PanelKey.cpp),
[MyLoadMenu.cpp](../CPP/7zip/UI/FileManager/MyLoadMenu.cpp).
Coupling: combobox/list HWNDs and persisted state. Route: New UI state/input;
Adapt path binding and persist in Arkvello settings. Verify NAV-02, UTIL-01,
including format errors, focus return and both panels. No new search is implied.

### NAV-06: one/two panels and cross-panel navigation

Behavior: F9, Tab, Alt+F1/F2, splitter/geometry persistence, active panel and
copy destination, plus Alt+arrow same/subfolder navigation handled in PanelKey.
Sources: [App.cpp](../CPP/7zip/UI/FileManager/App.cpp),
[FM.cpp](../CPP/7zip/UI/FileManager/FM.cpp), [PanelKey.cpp](../CPP/7zip/UI/FileManager/PanelKey.cpp).
Route: New UI panel state/layout; share adapted providers without assuming
thread safety or sharing a mutable archive session between panels. Verify
NAV-03, FILE-02 and new CAP-01; two independently busy panels remain a slice risk.

### VIEW-01: four list modes and inline rename

Behavior: large/small icons, list, details; keyboard incremental navigation,
inline rename Enter/Escape, shell icons, scrollbars and context actions.
Sources: [Panel.cpp](../CPP/7zip/UI/FileManager/Panel.cpp),
[PanelItems.cpp](../CPP/7zip/UI/FileManager/PanelItems.cpp),
[PanelListNotify.cpp](../CPP/7zip/UI/FileManager/PanelListNotify.cpp).
Coupling: native ListView notifications and stored item data; it is not currently
`LVS_OWNERDATA`. Route: New UI list, Adapt property/icon provider. A toolkit's
generic ListView is not evidence of four-mode parity. Verify LIST-01/03,
PERF-01 and CAP-02; selection must survive sorting/mode changes without stale IDs.

### VIEW-02: properties, columns, sort and timestamp display

Behavior: format-dependent columns, resize/reorder/saved widths, name/type/date/
size/no-sort and reverse sort, dynamic timestamp precision and UTC display.
Sources: [PanelSort.cpp](../CPP/7zip/UI/FileManager/PanelSort.cpp),
[PanelItems.cpp](../CPP/7zip/UI/FileManager/PanelItems.cpp),
[MyLoadMenu.cpp](../CPP/7zip/UI/FileManager/MyLoadMenu.cpp),
[PropertyName.cpp](../CPP/7zip/UI/FileManager/PropertyName.cpp).
Route: Adapt typed property/comparison/formatting, New UI columns and state.
Verify LIST-01, UTIL-01, PERF-01 with missing values, UInt64 sizes, times and
multiple formats. Do not sort formatted size/time strings lexically.

### VIEW-03: selection, current item and list preferences

Behavior: normal/alternative selection, focused vs selected/inactive/hovered,
Ctrl/Shift ranges, Insert, masks, select/deselect all/by type/invert, counts,
single-click opening, full-row select and grid preferences.
Sources: [PanelSelect.cpp](../CPP/7zip/UI/FileManager/PanelSelect.cpp),
[PanelKey.cpp](../CPP/7zip/UI/FileManager/PanelKey.cpp),
[PanelListNotify.cpp](../CPP/7zip/UI/FileManager/PanelListNotify.cpp).
Route: New UI selection model/input; adapt rules, not old drawing rectangles.
Verify LIST-02, A11Y-01/03, PERF-01 and CAP-02 using actual input. Existing
prototype selection geometry is a known failure, not acceptable evidence.

### VIEW-04: commands, toolbar, status and focus

Behavior: Add/Extract/Test/Copy/Move/Delete/Info, visibility/size/label settings,
enabled/check states, menus/mnemonics/tooltips, selection totals and focus return.
Sources: [App.cpp](../CPP/7zip/UI/FileManager/App.cpp),
[MyLoadMenu.cpp](../CPP/7zip/UI/FileManager/MyLoadMenu.cpp),
[PanelListNotify.cpp](../CPP/7zip/UI/FileManager/PanelListNotify.cpp).
Route: New UI command/state model and status; existing bitmap/hover adapter
is not the foundation by default. Verify CMD-01/02, VIS-01/04, A11Y-01/03.
Known toolbar color/repaint/icon-shift defects require replacement verification.

### VIEW-05: refresh and external changes

Behavior: manual refresh, auto-refresh preference, valid current item/selection
after filesystem or archive changes. Sources:
[Panel.cpp](../CPP/7zip/UI/FileManager/Panel.cpp),
[PanelFolderChange.cpp](../CPP/7zip/UI/FileManager/PanelFolderChange.cpp),
[IFolder.h](../CPP/7zip/UI/FileManager/IFolder.h) (`IFolderWasChanged`).
Route: Adapt change detection; New UI reconciliation and invalidation.
Verify UTIL-01 and CAP-03 during sort, rename, external editing and slow UNC;
threading and stale-result rejection must be specified in the working slice.

## File and member workflows

### FILE-01: external open, viewer and editor

Behavior: system Open Outside, configured F3 viewer/F4 editor, quoted commands,
launch errors and archive-member extraction before launch. Source:
[PanelItemOpen.cpp](../CPP/7zip/UI/FileManager/PanelItemOpen.cpp).
Coupling: shell/process APIs, prompts, panel HWND, temp storage and exit tracking.
Route: Adapt Windows launcher and lifecycle, New UI errors. Verify FILE-01,
including applications returning no process handle and paths with spaces/Unicode;
neither C# nor Rust removes these Windows-specific requirements.

### FILE-02: edit member and update it back

Behavior: detect external modifications, ask whether to update the original
member, retain archive context and password, handle nested/read-only/closed
manager cases and clean only owned temporary data. Source:
[PanelItemOpen.cpp](../CPP/7zip/UI/FileManager/PanelItemOpen.cpp)
(`MyThreadFunction`, `OnOpenItemChanged`, `CExitEventLauncher`).
Route: Adapt lifecycle as a service; retain Agent update-by-index where possible.
CLI update-by-name is not automatically equivalent. Verify FILE-01 and CAP-04;
editor lifetime, stale indices, reopening/replacing archives and app shutdown
make this one of the highest-risk work packages.

### FILE-03: copy and move

Behavior: filesystem and open-archive Copy/Move, destinations/history, same-folder
variants, names, metadata, streams, errors and conflicts; unsupported contexts
must remain explicit. Sources: [App.cpp](../CPP/7zip/UI/FileManager/App.cpp),
[PanelCopy.cpp](../CPP/7zip/UI/FileManager/PanelCopy.cpp),
[FSFolderCopy.cpp](../CPP/7zip/UI/FileManager/FSFolderCopy.cpp).
Route: Adapt provider operations and replace GUI callback/progress ownership.
CLI is an extract/update candidate, not a substitute for filesystem Move.
Verify FILE-02, OP-01/03 with hashes, source/destination state and applicable
metadata; cross-volume and archive-to-archive semantics need baseline execution.

### FILE-04: clipboard and drag/drop

Behavior: cut/copy/paste, Ctrl/Shift+Insert, filesystem paths and archive items,
drop targets and copy/move effects. Sources:
[PanelDrag.cpp](../CPP/7zip/UI/FileManager/PanelDrag.cpp),
[EnumFormatEtc.cpp](../CPP/7zip/UI/FileManager/EnumFormatEtc.cpp),
[PanelKey.cpp](../CPP/7zip/UI/FileManager/PanelKey.cpp).
Coupling: OLE data objects, HWND, temp extraction and active-panel state.
Route: Adapt Windows data-object layer; New UI input/drop/focus integration.
Verify FILE-02 and CAP-05 with Explorer, canceled drags and large selections;
toolkit-local drag/drop does not establish Explorer interoperability.

### FILE-05: deletion and Recycle Bin

Behavior: Delete vs Shift+Delete on filesystem; archive deletion updates the
container, without promising Recycle Bin for archive members; confirmation,
denied/long-path failures and partial results. Source:
[PanelOperations.cpp](../CPP/7zip/UI/FileManager/PanelOperations.cpp).
Route: Adapt filesystem/Agent operations, New UI confirmation/progress.
Verify FILE-03, OP-02 and CAP-08 on disposable inputs. Shell deletion and direct folder
deletion have different policies; do not silently replace one with the other.

### FILE-06: create, rename and comment

Behavior: create folder/file, inline rename and comment property where supported;
validate names/paths and preserve unsupported results per provider/format.
Sources: [PanelOperations.cpp](../CPP/7zip/UI/FileManager/PanelOperations.cpp),
[ArchiveFolderOut.cpp](../CPP/7zip/UI/Agent/ArchiveFolderOut.cpp).
Route: Adapt `IFolderOperations`, New UI input. Agent `CreateFile` is E_NOTIMPL;
having a command does not mean all providers support it. Verify LIST-03,
FILE-01, UTIL-01 and archive results after reopening; invalid/duplicate names
must retain useful input/focus.

### FILE-07: properties and raw archive information

Behavior: complete properties for selections/folders/archive levels and members,
long values, copying, dynamic property names and non-string types.
Sources: [PanelMenu.cpp](../CPP/7zip/UI/FileManager/PanelMenu.cpp),
[IFolder.h](../CPP/7zip/UI/FileManager/IFolder.h),
[PropertyName.cpp](../CPP/7zip/UI/FileManager/PropertyName.cpp).
Route: Adapt typed properties and aggregation, New UI properties surface.
Verify UTIL-01/05, including multiple levels, empty values and large values;
do not flatten every property to a filename-list column or omit unknown fields.

### FILE-08: links

Behavior: hard link, file/directory symbolic link, junction and WSL link;
from/to history, validation and privilege errors. Sources:
[LinkDialog.cpp](../CPP/7zip/UI/FileManager/LinkDialog.cpp),
[FileLink.cpp](../CPP/Windows/FileLink.cpp).
Route: Keep Windows helpers, Adapt validation/settings, New UI. CLI is not this
feature's backend. Verify UTIL-03 on isolated fixtures, checking targets and
metadata; supported filesystem/privilege combinations remain unexecuted.

### FILE-09: alternate streams

Behavior: browse and operate on supported NTFS streams, with item identity,
names and correct unsupported behavior. Sources:
[AltStreamsFolder.cpp](../CPP/7zip/UI/FileManager/AltStreamsFolder.cpp),
[IFolder.h](../CPP/7zip/UI/FileManager/IFolder.h).
Route: Adapt provider and stream/zone policy; New UI navigation/properties.
Verify UTIL-03 and archive metadata round trips; a `-sns` switch alone does not
recreate interactive stream browsing or guarantee stream preservation.

## Archive options and execution

### ARC-01: Add and complete parameter model

Behavior: output path/name/history/browse and extension rules; format, level,
method, dictionary, word size/order, solid size, threads, RAM limit/estimates,
raw parameters, update and path modes, shared files and delete-after-compression.
Sources: [CompressDialog.cpp](../CPP/7zip/UI/GUI/CompressDialog.cpp),
[UpdateGUI.cpp](../CPP/7zip/UI/GUI/UpdateGUI.cpp),
[Update.cpp](../CPP/7zip/UI/Common/Update.cpp).
Coupling: dependent controls, validation, per-format settings and GUI callbacks.
Route: Keep Common update; Adapt option/validation model and estimates; New UI
form. CLI is credible for fixed batch options but full interactive route must
also meet OP-01..06. Verify ADD-01/02/03/06; preserve unknown raw properties
and explicit/default distinctions, not only visible presets.

### ARC-02: compression metadata, links and time

Behavior: store symlinks/hard links/ADS/security; precision plus explicit override
and value separately for mtime/ctime/atime/latest-file archive time; preserve
source atime. Format-specific availability and defaults are required.
Sources: [CompressOptionsDialog.rc](../CPP/7zip/UI/GUI/CompressOptionsDialog.rc),
[CompressDialog.cpp](../CPP/7zip/UI/GUI/CompressDialog.cpp),
[UpdateGUI.cpp](../CPP/7zip/UI/GUI/UpdateGUI.cpp).
Route: Adapt value/dependency model; Keep update/format behavior. CLI properties
can represent many values; complete mapping is unproven. Verify ADD-05 with
actual metadata and Cancel vs OK; a single Boolean per checkbox is insufficient.

### ARC-03: encryption when creating

Behavior: password/re-entry/show-hide, method choice, filename encryption,
format-specific validation and limits; never persist/log real passwords casually.
Sources: [CompressDialog.cpp](../CPP/7zip/UI/GUI/CompressDialog.cpp),
[UpdateGUI.cpp](../CPP/7zip/UI/GUI/UpdateGUI.cpp).
Route: Adapt option/validation model and password lifetime; Keep engine.
CLI candidate supports a header-encrypted fixture (probe), not complete password
UX. Verify ADD-04 with 7z/ZIP methods and Unicode/limit cases. Secret transport
and retry behavior must be part of the chosen boundary, not command-line glue.

### ARC-04: extract whole archives and selected members

Behavior: destination/history and optional output name; full/current/no-path
modes where offered, overwrite policy, duplicate-root elimination, password,
security and Zone.Id policy. Selected open-archive items use indexed extraction.
Sources: [ExtractDialog.cpp](../CPP/7zip/UI/GUI/ExtractDialog.cpp),
[ExtractGUI.cpp](../CPP/7zip/UI/GUI/ExtractGUI.cpp),
[Extract.cpp](../CPP/7zip/UI/Common/Extract.cpp),
[PanelCopy.cpp](../CPP/7zip/UI/FileManager/PanelCopy.cpp).
Route: Adapt Common/Agent callbacks; unify presentation while preserving entry-
point differences. CLI `x`/`e` is a candidate, not proof of the whole model.
Verify EXT-01, OP-01/03/04 and exact-item identity. Probe only confirms one
literal Unicode member extracted correctly with `-spd`.

### ARC-05: Test, update and member mutation

Behavior: whole selected archives vs open-archive selected-member test;
add/replace, update, freshen, synchronize, member delete/rename/comment and
edited-member replacement only where the format permits writing.
Sources: [CompressCall.cpp](../CPP/7zip/UI/Common/CompressCall.cpp),
[ArchiveFolderOut.cpp](../CPP/7zip/UI/Agent/ArchiveFolderOut.cpp),
[AgentOut.cpp](../CPP/7zip/UI/Agent/AgentOut.cpp),
[UpdateAction.cpp](../CPP/7zip/UI/Common/UpdateAction.cpp).
Route: Keep update actions; Adapt indexed Agent operations; CLI candidate for
whole-file Test/batch update. Verify ADD-06, OP-01/04 and CAP-06. Identical member
names and unsupported operations need explicit cases, not implicit name matching.

### ARC-06: volumes, SFX and format capabilities

Behavior: volume-size expressions/confirmation, multipart input, SFX when its
module is present, and all formats/methods exposed by the bundled engine. Read
support is not write support. Sources:
[CompressDialog.cpp](../CPP/7zip/UI/GUI/CompressDialog.cpp),
[LoadCodecs.cpp](../CPP/7zip/UI/Common/LoadCodecs.cpp),
[Update.cpp](../CPP/7zip/UI/Common/Update.cpp).
Route: Keep version-matched codecs/modules, Adapt capability enumeration and
validation; CLI candidate for fixed volumes/SFX. Verify ADD-03/06, OP-01 and
distribution fixtures. Current four-binary build does not establish SFX/help/
language/shell-extension packaging. Do not substitute a reduced engine binding.

### OP-01: progress, pause/resume and Background

Behavior: scan/compress/extract/test phases, files/bytes/timing/ratio/speed,
messages, pause/resume and priority toggle. Sources:
[ProgressDialog2.cpp](../CPP/7zip/UI/FileManager/ProgressDialog2.cpp),
[UpdateCallbackGUI2.cpp](../CPP/7zip/UI/GUI/UpdateCallbackGUI2.cpp).
The original uses cooperative `CProgressSync::CheckStop`/pause and changes
current-process priority for Background. Route: Adapt synchronization and
callbacks; New UI progress. Stock CLI has break handling but no corresponding
GUI pause protocol found in the inspected console code. Process suspension
is not presumed equivalent. Verify OP-02, including elapsed time and prompt races.

### OP-02: cancel, close and operation lifetime

Behavior: original confirmation/decline, cooperative abort, completion during a
prompt, temp/final archive cleanup and an unambiguous partial/canceled result.
Sources: [ProgressDialog2.cpp](../CPP/7zip/UI/FileManager/ProgressDialog2.cpp),
[ConsoleClose.cpp](../CPP/7zip/UI/Console/ConsoleClose.cpp),
[Update.cpp](../CPP/7zip/UI/Common/Update.cpp).
Route: Adapt explicit job lifetime; direct callbacks initially preferable for
interactive parity. CLI cancellation needs its own Windows console/signal setup
and real running tests. Probe `q`/EOF aborts are not evidence of that. Verify
OP-02 and CAP-08 with disposable large operations and no forced-kill-as-Cancel shortcut.

### OP-03: conflict decisions

Behavior: distinguish old/new name/size/time; Yes, Yes to All, No, No to All,
Auto Rename and Cancel, including subsequent conflicts. Sources:
[OverwriteDialog.cpp](../CPP/7zip/UI/FileManager/OverwriteDialog.cpp),
[ExtractCallback.cpp](../CPP/7zip/UI/FileManager/ExtractCallback.cpp),
[ExtractCallbackConsole.cpp](../CPP/7zip/UI/Console/ExtractCallbackConsole.cpp).
Route: Adapt typed request/reply callback; CLI text/input adapter is possible.
Probe y/a/n/s/u/q confirms single-conflict outcomes; it does not test All state,
live prompt parsing or old/new-details extraction. Verify OP-03 and completion/
disconnect races. Do not replace interactive choices with global overwrite-all.

### OP-04: password and archive-open prompts

Behavior: passwords while opening/enumerating/extracting/updating, wrong-password
and corrupt-data results, retries/cancel and correct ownership/focus.
Sources: [OpenCallback.cpp](../CPP/7zip/UI/FileManager/OpenCallback.cpp),
[ArchiveOpenCallback.cpp](../CPP/7zip/UI/Common/ArchiveOpenCallback.cpp),
[UserInputUtils.cpp](../CPP/7zip/UI/Console/UserInputUtils.cpp).
Route: Adapt direct callback/session state; CLI candidate must handle UTF input
and process lifetime. Probe confirms ASCII fixture password via stdin, wrong
password exit 2 and EOF exit 255. It does not establish automatic retry; new
attempts may need restarting/reopening. Verify OP-04, ADD-04, nested sessions.

### OP-05: memory limits and request decisions

Behavior: compression estimates/limit, extraction configured limit, request to
allow/skip archive, remember action and optionally save a limit for later work.
Sources: [MemDialog.cpp](../CPP/7zip/UI/FileManager/MemDialog.cpp),
[ExtractCallback.cpp](../CPP/7zip/UI/FileManager/ExtractCallback.cpp),
[ExtractCallbackConsole.cpp](../CPP/7zip/UI/Console/ExtractCallbackConsole.cpp).
Route: Adapt request callback and settings. Console implementation reports and
sets limit/skip answer flags; it does not reproduce the GUI allow/change-limit
dialog simply by piping stdin. Verify UTIL-05 and CAP-07 with deliberately
bounded fixtures; this is an important blocker for a universal stock-CLI route.

### OP-06: results, warnings and diagnostics

Behavior: success, warnings, per-file errors, unsupported methods, denied output,
corruption, user abort, partial results and complete copyable logs.
Sources: [Extract.cpp](../CPP/7zip/UI/Common/Extract.cpp),
[Update.cpp](../CPP/7zip/UI/Common/Update.cpp),
[Main.cpp](../CPP/7zip/UI/Console/Main.cpp),
[MessagesDialog.cpp](../CPP/7zip/UI/FileManager/MessagesDialog.cpp).
Route: Adapt result/error records; a CLI adapter owns exit-code and text mapping.
Probe produces warning exit 1 while the partial archive separately tests with
exit 0. Therefore a readable output archive is not proof the job fully succeeded.
Verify OP-04, CAP-08 and complete logs; no generic `exit != 0 = same error` policy.

## Utilities and configuration

### UTIL-01: Split and Combine

Behavior: destination, volume sizes, name/part validation, missing/existing parts,
progress/cancel and identical reconstructed content. Sources:
[PanelSplitFile.cpp](../CPP/7zip/UI/FileManager/PanelSplitFile.cpp),
[SplitUtils.cpp](../CPP/7zip/UI/FileManager/SplitUtils.cpp).
Route: Adapt I/O loop/validation/progress, New UI. Archive-volume creation via
CLI is not automatically equivalent to splitting arbitrary files. Verify UTIL-02
and OP-02 on disposable inputs; filesystem/part naming behavior stays explicit.

### UTIL-02: CRC/hash and checksum files

Behavior: CRC32/64, XXH64, MD5, SHA1/256/384/512, SHA3-256, BLAKE2sp and `*`,
file/archive-member routes, totals/data/names results and checksum-file actions
offered by the context menu. Sources:
[PanelCrc.cpp](../CPP/7zip/UI/FileManager/PanelCrc.cpp),
[HashGUI.cpp](../CPP/7zip/UI/GUI/HashGUI.cpp),
[HashCalc.cpp](../CPP/7zip/UI/Common/HashCalc.cpp),
[ContextMenu.cpp](../CPP/7zip/UI/Explorer/ContextMenu.cpp).
Route: Keep hash implementation; Adapt member streams/results. CLI `h` and
hash-format create/test are credible batch candidates; progress requirements
still apply. Verify UTIL-04 for every offered method and generated-file actions.

### UTIL-03: benchmark, normal and total

Behavior: both modes, dictionary/threads, results/log, Stop/Restart and resizing.
Sources: [BenchmarkDialog.cpp](../CPP/7zip/UI/GUI/BenchmarkDialog.cpp),
[Bench.cpp](../CPP/7zip/UI/Common/Bench.cpp),
[CompressCall.cpp](../CPP/7zip/UI/Common/CompressCall.cpp).
Route: Keep benchmark core; Adapt worker/results and New UI. CLI `b` is a batch
candidate but its output is not the GUI controller. Verify UTIL-04 on bounded
resources; restarting, cancellation and all-methods mode remain unexecuted.

### UTIL-04: temporary-file browser and cleanup

Behavior: browse temp entries, confirm cleanup, preserve files still needed by
external editors/nested sessions, full diagnostic text. Sources:
[BrowseDialog2.cpp](../CPP/7zip/UI/FileManager/BrowseDialog2.cpp),
[PanelItemOpen.cpp](../CPP/7zip/UI/FileManager/PanelItemOpen.cpp).
Route: Adapt ownership/cleanup rules, New UI browser. Verify UTIL-05, FILE-01
and CAP-04; never generalize cleanup to arbitrary user temp data.

### UTIL-05: external Diff

Behavior: configured diff tool, selection/panel combinations accepted by the
original, quoted paths and launch failure. Source:
[PanelItemOpen.cpp](../CPP/7zip/UI/FileManager/PanelItemOpen.cpp) (`DiffFiles`).
Route: Adapt Windows launch and validation, New UI command/settings. Verify
OPT-01 and new CAP-09 against the baseline; not an archive algorithm or CLI task.

### UTIL-06: conditional version-control commands

Behavior: Edit/Commit/Revert/Diff commands added dynamically when external diff
and registry `7vc` path/context enable them; read-only and writable states alter
availability. Sources: [MyLoadMenu.cpp](../CPP/7zip/UI/FileManager/MyLoadMenu.cpp),
[VerCtrl.cpp](../CPP/7zip/UI/FileManager/VerCtrl.cpp),
[RegistryUtils.cpp](../CPP/7zip/UI/FileManager/RegistryUtils.cpp).
Route: Adapt snapshot/numbering/file-time behavior and New UI commands/prompts.
This is an existing conditional feature, not a proposal to add Git integration.
Verify new CAP-10 in a disposable settings profile. Existing UI-MAP and the usual
visible menu did not fully describe it; runtime behavior remains an explicit risk.

### SET-01: list/application settings

Behavior: dots, real icons, full-row selection, grid, single-click, alternative
selection, system menu, large memory pages and extraction RAM limit; apply/
cancel/persist. Sources: [SettingsPage.cpp](../CPP/7zip/UI/FileManager/SettingsPage.cpp),
[RegistryUtils.cpp](../CPP/7zip/UI/FileManager/RegistryUtils.cpp),
[ViewSettings.cpp](../CPP/7zip/UI/FileManager/ViewSettings.cpp).
Route: Adapt value model and dependencies, New UI settings. Separate Arkvello
storage explicitly; no silent import/overwrite of original settings. Verify
OPT-01, SET-01 and NAV-03; new settings backend must cover all introduced state.

### SET-02: work/temp folder settings

Behavior: system/current/specified folder, path picker, removable-drives-only,
validation and actual use by archive work. Sources:
[FoldersPage.cpp](../CPP/7zip/UI/FileManager/FoldersPage.cpp),
[WorkDir.cpp](../CPP/7zip/UI/Common/WorkDir.cpp).
Route: Adapt policy/settings, New UI fields. Verify OPT-01 and CAP-04 on normal/
removable/denied destinations; don't reduce this to a generic temp path string.

### SET-03: external applications

Behavior: independent viewer/editor/diff commands and browse, apply/cancel,
quoting and use in FILE-01/02 and UTIL-05/06. Source:
[EditPage.cpp](../CPP/7zip/UI/FileManager/EditPage.cpp).
Route: Adapt settings/validation, New UI form. Verify OPT-01, CAP-09/10 with
harmless test programs; configured external programs remain user choices.

### SET-04: localization

Behavior: language choice, string resources/mnemonics, runtime command/toolbar
reload and translated operation surfaces. Sources:
[LangPage.cpp](../CPP/7zip/UI/FileManager/LangPage.cpp),
[LangUtils.cpp](../CPP/7zip/UI/FileManager/LangUtils.cpp).
Route: Adapt catalog/lookup or explicitly map to new resources; New UI layout
must support long text/CJK. Original IDs are reusable mapping keys, not fixed
window IDs. Verify LANG-01 and A11Y-01/03. Language assets are a packaging gap;
English-only development rendering is not localization parity.

### SET-05: file associations

Behavior: supported types/application choices and available per-user/system
registration context. Sources:
[SystemPage.cpp](../CPP/7zip/UI/FileManager/SystemPage.cpp),
[RegistryAssociations.cpp](../CPP/7zip/UI/FileManager/RegistryAssociations.cpp).
Route: Adapt registration/ownership and New UI; preserve original coexistence.
Verify OPT-01, DIST-02 in a VM. Windows default-app policy and privileges remain
integration constraints regardless of UI language; no registration was performed.

### SET-06: shell configuration

Behavior: integration including available 32-bit option, cascaded menu/icons,
individual context actions, eliminate duplicated root and Zone.Id propagation
choices. Sources: [MenuPage.cpp](../CPP/7zip/UI/FileManager/MenuPage.cpp),
[RegistryContextMenu.cpp](../CPP/7zip/UI/Explorer/RegistryContextMenu.cpp).
Route: Adapt independent product identifiers/settings, New UI. Verify OPT-01,
SHELL-01, DIST-02; x64 app evidence does not establish 32-bit extension shipping.

## Windows integration and product surfaces

### INT-01: Explorer quick actions

Behavior: Open/open-with-type, Extract dialog/Here/To, Test, Compress dialog,
7z/ZIP quick create, email variants when available, hash/calculate/create/test
checksum actions, multi-selection and no-main-window entry. Sources:
[ContextMenu.cpp](../CPP/7zip/UI/Explorer/ContextMenu.cpp),
[CompressCall.cpp](../CPP/7zip/UI/Common/CompressCall.cpp).
Coupling: shell COM lifetime, command verbs, selection handoff, sibling binaries
and MAPI/mail availability. Route: Adapt small native shell entry and shared
application operation requests. Keep compression/UI out of Explorer's process.
Verify SHELL-01, DIST-01/02 and CAP-11; email support is environment-dependent.
Exact Windows 11 menu placement and full shipping extension are not yet proven.

### INT-02: in-app system menus and native pickers

Behavior: user setting for system context menus, shell-extension message
handling and file/folder pickers with long/device-path fallback and cancellation.
Sources: [PanelMenu.cpp](../CPP/7zip/UI/FileManager/PanelMenu.cpp),
[BrowseDialog.cpp](../CPP/7zip/UI/FileManager/BrowseDialog.cpp).
Route: Adapt HWND/STA integration and fallback logic; use OS-owned surfaces
where appropriate. Verify SHELL-01, UTIL-05 and CAP-05. Toolkit menu styling
does not replace third-party shell extension ownership/input requirements.

### INT-03: launch arguments, command routing and app lifecycle

Behavior: FM startup paths and original supported arguments, GUI/helper command
entry points, bookmarks/accelerators, close/exit and outstanding editor/jobs.
Sources: [FM.cpp](../CPP/7zip/UI/FileManager/FM.cpp),
[GUI.cpp](../CPP/7zip/UI/GUI/GUI.cpp),
[CompressCall.cpp](../CPP/7zip/UI/Common/CompressCall.cpp).
Route: Adapt argument/selection handling and New UI lifecycle. `7zG.exe` is a
GUI helper, not the CLI; existing mapped UTF-16 selections illustrate the need
to avoid command-line length/quoting loss. Verify CMD-01, ADD-01, DIST-01 and
CAP-12. Replacing process boundaries requires an explicit compatibility policy.

### INT-04: distribution, help, About and independence

Behavior: version/About/help, matching helpers/codecs/SFX/languages, portable/
installer entry points, original notices, independently owned settings and
registrations. Sources: [BUILDING.md](BUILDING.md), [UPSTREAM.md](UPSTREAM.md),
[HelpUtils.cpp](../CPP/7zip/UI/FileManager/HelpUtils.cpp),
[AboutDialog.cpp](../CPP/7zip/UI/FileManager/AboutDialog.cpp),
[License.txt](../DOC/License.txt).
Route: Keep engine/notices and build evidence; implement product packaging and
assets deliberately. Verify CMD-01, LANG-01, SET-01, DIST-01..03 on a clean system.
UI-runtime deployment is additional to archive dependencies. No installer,
association change, publishing or macOS modification is part of this stage.

### NEW-01: Windows 11 visual and interaction quality

Requirements added by Arkvello: coherent Explorer-inspired composition across
surfaces, system accent, system/light/dark and contrast themes, per-monitor DPI,
text scaling, keyboard and assistive technology, stable real hover/selection.
Sources: [DESIGN.md](../DESIGN.md), [NATIVE-PROTOTYPE.md](NATIVE-PROTOTYPE.md).
Route: New UI with explicit platform integration; existing theme colors/policy
can inform it but the painting adapter is not a required dependency. Verify
VIS-01..04, A11Y-01..03, PERF-01 with actual input. Current system-aware-only
manifests, screenshots and message harness do not establish these requirements.

## Static command coverage ledger

The following maps concrete baseline menu/handler IDs to the capability groups
above. Run `python scripts/audit-capability-coverage.py` to compare with the
pinned source, not merely the prototype's modified menus. This textual audit
detects missing mapped entry points; it does not validate semantics, conditional
availability, dialog fields, keyboard-only behavior or runtime operation results.
Those are covered by the entries, source review and pending regression cases.

Recorded audit: 66 static menu IDs, 70 handler-case IDs and three archive-toolbar
IDs; their union is 74 distinct IDs, all mapped with no missing/extra entries.
These counts overlap and do not mean 74 independent user capabilities.

<!-- command-coverage-start -->
NAV-04: IDM_OPEN IDM_OPEN_INSIDE IDM_OPEN_INSIDE_ONE IDM_OPEN_INSIDE_PARSER IDM_OPEN_OUTSIDE
NAV-05: IDM_OPEN_ROOT_FOLDER IDM_OPEN_PARENT_FOLDER IDM_FOLDERS_HISTORY
NAV-06: IDM_VIEW_TWO_PANELS
VIEW-01: IDM_VIEW_LARGE_ICONS IDM_VIEW_SMALL_ICONS IDM_VIEW_LIST IDM_VIEW_DETAILS
VIEW-02: IDM_VIEW_ARANGE_BY_NAME IDM_VIEW_ARANGE_BY_TYPE IDM_VIEW_ARANGE_BY_DATE IDM_VIEW_ARANGE_BY_SIZE IDM_VIEW_ARANGE_NO_SORT IDM_VIEW_TIME IDM_VIEW_TIME_UTC
NAV-02: IDM_VIEW_FLAT_VIEW
VIEW-03: IDM_SELECT_ALL IDM_DESELECT_ALL IDM_INVERT_SELECTION IDM_SELECT IDM_DESELECT IDM_SELECT_BY_TYPE IDM_DESELECT_BY_TYPE
VIEW-04: IDM_VIEW_STANDARD_TOOLBAR IDM_VIEW_ARCHIVE_TOOLBAR IDM_VIEW_TOOLBARS_SHOW_BUTTONS_TEXT IDM_VIEW_TOOLBARS_LARGE_BUTTONS
VIEW-05: IDM_VIEW_REFRESH IDM_VIEW_AUTO_REFRESH
FILE-01: IDM_FILE_VIEW IDM_FILE_EDIT
FILE-03: IDM_COPY_TO IDM_MOVE_TO
FILE-05: IDM_DELETE
FILE-06: IDM_RENAME IDM_CREATE_FOLDER IDM_CREATE_FILE IDM_COMMENT
FILE-07: IDM_PROPERTIES
FILE-08: IDM_LINK
FILE-09: IDM_ALT_STREAMS
UTIL-01: IDM_SPLIT IDM_COMBINE
UTIL-02: IDM_HASH_ALL IDM_CRC32 IDM_CRC64 IDM_XXH64 IDM_MD5 IDM_SHA1 IDM_SHA256 IDM_SHA384 IDM_SHA512 IDM_SHA3_256 IDM_BLAKE2SP
UTIL-03: IDM_BENCHMARK IDM_BENCHMARK2
UTIL-04: IDM_TEMP_DIR
UTIL-05: IDM_DIFF
UTIL-06: IDM_VER_EDIT IDM_VER_COMMIT IDM_VER_REVERT IDM_VER_DIFF
SET-01: IDM_OPTIONS
INT-04: IDM_HELP_CONTENTS IDM_ABOUT IDCLOSE
ARC-01: kMenuCmdID_Toolbar_Add
ARC-04: kMenuCmdID_Toolbar_Extract
ARC-05: kMenuCmdID_Toolbar_Test
<!-- command-coverage-end -->

Dynamic entry points reviewed separately: ten Open/Set Bookmark IDs and
`IDM_ADD_TO_FAVORITES` popup (NAV-05), generated timestamp levels (VIEW-02),
archive/open-type and checksum submenus and shell-extension IDs (NAV-04,
UTIL-02, INT-01/02). Toolbar Copy/Move/Delete/Info reuse mapped file IDs.
`IDM_VIEW_SHOW_DELETED`/`IDM_VIEW_SHOW_STREAMS` are commented code, not active
commands to accidentally add; archive properties/deleted-item display semantics
remain part of VIEW-02/03. CE-only layouts are outside the initial x64 target.
The commented clipboard menu/handler IDs do not make clipboard unavailable:
keyboard and OLE routes in FILE-04 remain active and must be preserved.

## Bounded CLI execution evidence

Run: `python scripts/probe-reuse-cli.py`. Recorded Windows 11 build 26300,
2026-11-16, Python 3.15; 15 commands and file-content assertions passed using
local development binaries. Evidence directory:
`build/reuse-cli-nh099pdn/{commands,summary}.json` (ignored local artifacts).
CLI SHA-256 `3e1e7a277fca3533a5a8767b8a76d49af53559c1a66d13faf0235554e791c3b6`;
codec SHA-256 `ac43351fc48d258441fa9ef2c63f60c255d4ca04fb37c1e96d2d6ed6bba5aa44`.

- Create and technical list include spaced/Unicode/ampersand/bracket and
  leading-dash filenames. Exact extraction with `-spd` gives one matching file.
- Each y/a/n/s/u/q response gives the expected single-conflict result; q aborts
  with 255, and auto-rename preserves existing content plus extracted content.
- A disposable header-encrypted archive accepts its ASCII password via stdin;
  explicit wrong password returns 2 and password EOF aborts with 255.
- Valid plus missing input produces warning 1; the resulting partial archive
  independently tests successfully with 0.

Not tested: streaming prompt recognition, All persistence across conflicts,
Unicode passwords, asynchronous callbacks, cooperative cancellation of active
work, pause/resume, priority changes, NTFS metadata, large/UNC paths, duplicate
archive paths, nested edit-back or clean deployment. No comparison of UI
framework performance was run. Probe timeout is an infrastructure failure,
not application cancellation. No GUI regression case is promoted to Pass.

## Reuse disposition and risk-first order

**Reuse with minimal changes:** codecs/handlers, low-level streams/filesystem
helpers, update action rules, hash/benchmark algorithms, archive property types.
Keep upstream provenance and dependency versions explicit.

**Adapt behind boundaries:** Agent/archive and filesystem providers; Common
extract/update orchestration; compression dependency/estimate logic; callback
decisions, progress synchronization, external-edit/nested lifecycle; shell data
objects, launch/registration and settings. These are not drop-in UI-free DLLs.

**Replace presentation:** frame, command/menu composition, file-list rendering/
input, path/history controls, settings pages, operation forms and prompts.
Keep original implementations available as behavioral references. Unresolved
prototype bugs stay open until retained/replacement surfaces pass their cases.

Test first, in this order:

1. **List/input fidelity** (VIEW-01..03, NAV-06): details plus another view,
   dynamic reorderable columns, normal/alternative selection, rename, two
   panels, 10k/100k synthetic items and real archives. Record load/sort/scroll/
   selection latency, working set and resource use against original; these are
   proposed fixtures, not measured performance or promised hard limits.
2. **Archive session identity and edit-back** (NAV-02/03, FILE-02): duplicate
   names, sorting/refresh/update invalidation, nested and externally changed
   archives, password ownership and temp cleanup. Do not expose row positions
   as enduring item identifiers.
3. **Operation interaction** (OP-01..06): extract with conflict/password/memory
   request, pause/decline-cancel/cancel, completion while awaiting a decision,
   partial output and worker disconnect. Compare CLI and direct route only for
   a bounded path; full GUI requirements make the direct route the provisional
   default for interactive Add/Extract.
4. **Windows integration and deployment** (FILE-04, INT-01..04, NEW-01): real
   OLE/shell ownership, STA/focus/DPI/AT, no-window quick actions and runtime
   loading on a clean system. A framework's control gallery is not this test.

## Stage 1 completion and remaining decisions

The known static command families, keyboard-only workflows, dialog/options
families, conditional features and integration surfaces now have entries and
verification obligations. Newly identified regression cases are in the
[checklist](UI-REGRESSION.md#stage-1-additional-capability-cases).
Uncertainty is recorded rather than interpreted as permission to omit features.
This satisfies the source-assessment gate, not the later runtime-parity gate.

[Technology options](TECHNOLOGY-OPTIONS.md) proposes a shortlist based on these
findings. Stage 2 is the coherent composition/command map; stage 3 proves the
list, archive route and boundary with a runnable slice before a final stack
decision. No framework migration, UI implementation or system changes started.
