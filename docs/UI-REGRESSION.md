# UI behavior and visual regression checklist

Use with the [source map](UI-MAP.md). This document defines cases to execute;
unchecked boxes are **not yet verified**, not known failures. Source inspection
establishes intended routing, not runtime success. Initial target: Windows 11
x64, original 7-Zip 26.06 baseline versus the Arkvello build under review.

Under the [revised product brief](../PRODUCT.md) and
[proposed plan](DEVELOPMENT-PLAN.md), this remains a behavioral baseline.
Adapt steps that name old menus, pages or helper processes to the redesigned
entry point while preserving the capability, option coverage and result.
Record the operation backend/version used. Layout or backend changes do not
waive cases, establish parity or turn existing partial evidence into a pass.

## Evidence already available (2026-11-15)

- [x] **BASE-01** Local x64 build and GitHub Actions build pass for all four
  binaries; see [build record](BUILDING.md#initial-baseline-verification-2026-11-15).
- [x] **BASE-02** Console smoke tests pass for 7z, ZIP and header-encrypted 7z,
  with a Unicode/spaced filename and SHA-256 equality after extraction.
  This checks archive operations, not GUI command wiring.
- [x] **BASE-03** User confirmed the built manager launches and looks identical
  to original 7zFM. No broader navigation/dialog coverage was claimed.

## How to record a run

For each executed case, record case ID, commit, Windows build, theme, scale,
result and evidence (screenshot, output hash or concise observation). Include
which entry point/process was used. Use Pass, Fail, Blocked or Not run; do not
tick a case because its code exists. Describe any upstream limitation observed
in the baseline separately from a regression introduced by the fork.

Use disposable input/output folders and copies of archives. Prepare: empty
folder, nested tree, text/binary files, duplicate names, Cyrillic/CJK names,
spaces and `&`, long names/paths, large directory, normal/encrypted/corrupt
archives, and a destination containing conflicts. Generate format fixtures with
the built CLI where possible. Use a harmless external editor/viewer for tests.
Do not change real file associations or delete real user files for this pass.
Because settings still use the original 7-Zip namespace, run settings-changing
cases in a disposable Windows profile/VM. Use an isolated environment for
access-denied, memory-limit, links and shell-integration cases as well.

P0 blocks acceptance of the visual prototype. P1 covers the remaining UI groups
as they change and must pass before prerelease. P2 covers distribution features
when implemented. CLI checks are repeated after executable changes; they do
not replace the manual cases below.

## P0: main-window working habits

- [ ] **NAV-01** Start at a local directory; use mouse and Enter to navigate
  into/out of folders and an archive nested inside another archive. Backspace
  and Ctrl+PgUp return to the correct parent; Ctrl+PgDn opens inside. The
  original path, focused item and selection return when leaving an archive.
- [ ] **NAV-02** Type/paste a path in each panel header; test Enter, Escape and
  the history dropdown. Include Unicode, spaces, an invalid path and a UNC
  share when available. No text loss or focus jump on errors.
- [ ] **NAV-03** Toggle F9 between one/two panels; Tab switches panels, Alt+F1/
  Alt+F2 focuses the corresponding path field. Resize and move the splitter;
  the active panel and commands remain correct. Close/reopen preserves the
  baseline's saved panel/window state.
- [ ] **NAV-04** Check empty folders, inaccessible paths and unavailable shares:
  errors are readable, navigation remains usable, and no stale file selection
  is operated on. Do not infer network coverage from local-folder checks.
- [ ] **LIST-01** Test all four Ctrl+1..4 view modes and return to Details.
  Sort by header and Ctrl+F3..F7, reverse sort, resize/reorder columns, reopen
  the folder/archive. Values and saved widths/order remain correct.
- [ ] **LIST-02** Compare normal and alternative selection modes: Ctrl/Shift,
  keyboard range, Ctrl+A, numpad +/− masks, Shift+numpad +/−, numpad * inversion,
  Alt+numpad +/− by type and Insert where applicable. Distinguish selected,
  focused, inactive and hovered items; status counts match actual operations.
- [ ] **LIST-03** F2 inline rename accepts/cancels with Enter/Escape; failed and
  duplicate-name attempts show the original validation and keep usable focus.
  Editing text must not trigger list shortcuts.
- [ ] **CMD-01** Use menu, toolbar and keyboard for equivalent commands. Check
  enabled/disabled/check states with no selection, folders, archives and items
  inside an archive. Menu mnemonics, Alt activation, F1 entry point and Esc work.
  Missing compiled help is a baseline packaging gap, not a passed Help test.
- [ ] **CMD-02** Toggle archive/standard toolbar groups, labels and large/small
  buttons. Commands/tooltips remain correct after each toolbar recreation;
  narrow widths do not cover the file list or lose actions.
- [ ] **FILE-01** F7 creates a folder; Ctrl+N/Shift+F4 create a file; F3/F4 open
  viewer/editor; Shift+Enter opens outside. Opening/editing a copied file from
  an archive retains the original modified-file update prompt and result.
- [ ] **FILE-02** F5/F6 copy/move files between panels and Shift+F5/F6 exercise
  the same-folder variant. Ctrl+C/X/V and supported drag/drop retain names,
  content and copy/move semantics. Verify hashes and destination/source state.
- [ ] **FILE-03** Delete sends disposable filesystem items to Recycle Bin;
  Shift+Delete follows permanent-delete confirmation. Cancel leaves data
  untouched. Inside an archive, check original update/delete semantics instead
  of assuming filesystem Recycle Bin behavior.

## P0: Add to Archive and real operations

- [ ] **ADD-01** Invoke Add from the manager and the helper directly, using its
  supported command line (`7zG.exe a -ad <archive> <input>`). The dialog has the
  intended selection/path and appears with consistent theme in either entry.
  Test 7z and ZIP, plus a supported single-stream format such as gzip on one
  file. A valid format is not assumed to support every option.
- [ ] **ADD-02** Change format → level → method → dictionary → solid size →
  threads/memory settings and back. Enabled fields, estimates, extension and
  remembered per-format choices match the baseline. Inspect dropdowns as well
  as closed controls. No hidden option remains accidentally active.
- [ ] **ADD-03** Enter archive names with spaces/Unicode, choose via Browse and
  history, enter raw parameters and volume sizes. Check invalid parameters,
  invalid/small volume sizes, invalid destination and cancellation. Validation
  retains typed input and focuses a useful control.
- [ ] **ADD-04** Test password confirmation/mismatch, show/hide, 7z filename
  encryption and ZIP encryption choices. Create, test and extract the result;
  expected password behavior and hashes match. Screenshots must not contain
  real secrets.
- [ ] **ADD-05** Open nested Options, change supported NTFS/time flags and
  explicit overrides, Cancel/reopen, then OK. Parent/child values and per-format
  availability match the baseline. Compare archive metadata, not only content.
- [ ] **ADD-06** Add/replace, update, freshen and synchronize a fixture with
  changed, new and absent files. Verify members/content according to each
  baseline mode. SFX is conditional on the required module being available;
  missing baseline packaging is recorded, not treated as functional coverage.
  Test delete-after-compression only on disposable inputs.
- [ ] **OP-01** Test ordinary/encrypted archives using Test and Extract on archive
  files, then copy selected members out of an open archive with F5. These cover
  both external-helper and internal-manager paths. Check files, paths and hashes.
- [ ] **OP-02** Observe a sufficiently long operation: progress, time, sizes,
  speed, filenames, Pause/Resume and Background behave as in the baseline.
  Cancel and closing the progress window preserve the original confirmation;
  declining cancellation continues. Completion during a prompt does not hang.
- [ ] **OP-03** Cause file conflicts, then separately test Yes, Yes to All, No,
  No to All, Auto Rename and Cancel. Old/new details remain distinguishable;
  final names/content and subsequent-conflict behavior match the chosen action.
- [ ] **OP-04** Open an encrypted archive, give wrong/correct passwords and
  cancel; use a corrupt archive and a blocked output destination. Prompts/logs
  remain readable and complete, no dialog is hidden behind its owner, and the
  manager remains usable after success, failure and cancellation.

## P0: appearance and accessibility of changed surfaces

These are target acceptance criteria for the prototype, not claims about the
original baseline. Capture baseline observations before changing these surfaces.

- [ ] **VIS-01** Check light, dark and follow-system modes across main frame,
  both panels, menus, toolbar/tooltips, address edit/dropdown, list/header/rename,
  scrollbars, status bars, Add and nested Options. Then inspect real progress,
  password, overwrite and validation prompts. Record remaining OS-owned gaps.
- [ ] **VIS-02** Change system theme with windows/dropdowns open and during a
  running operation. New helper processes use the same effective theme;
  no stale brushes/fonts, unreadable text, selection loss or operation restart.
- [ ] **VIS-03** Check 100%, 125%, 150% and 200% scale, including movement between
  monitors with different scales and an already open dialog. If a second
  display is unavailable, mark that subcase Not run. Text/icons stay sharp,
  hit targets align, layouts do not overlap and dialogs fit the work area.
- [ ] **VIS-04** Resize to a narrow practical window and maximize/restore;
  include long filenames/paths, large values and expanded dropdowns. All commands
  remain reachable, horizontal scrolling works and status text does not collide.
- [ ] **A11Y-01** Complete navigation and Add using the keyboard: Tab/Shift+Tab,
  arrow keys, Space, mnemonics, Enter/Escape. Focus remains visible; disabled
  controls are distinguishable; default/cancel actions do not change meaning.
- [ ] **A11Y-02** Check Windows contrast themes, including a runtime switch.
  Focus and selected/unselected text remain legible; meaning is not conveyed
  solely by a custom selection/deleted-item color.
- [ ] **A11Y-03** With Narrator or an inspection tool, check labels, names,
  roles and states for changed buttons, input, list selection, inline rename
  and dialog actions. Owner drawing must not remove accessible control behavior.
- [ ] **PERF-01** Compare the same large-directory fixture before/after for
  opening, scrolling, sort and selection. Repeat window/theme cycles and
  inspect handle/resource growth. Record observed times, fixture size and
  environment; no arbitrary performance claim follows from a successful build.

## P1: remaining interface groups

- [ ] **EXT-01** Extract dialog: destination/history, optional output folder,
  all available path/overwrite modes, duplicate-root handling, password and
  security options. Confirm final tree and overwrite outcomes, including cancel.
- [ ] **UTIL-01** Properties on files, folders and archive members preserves all
  values/copying/scrolling. Comments (Ctrl+Z), Open Inside `*`/`#`, flat view,
  time-display choices, refresh/auto-refresh, history and Favorites retain their
  original behavior. Include Alt/right-Ctrl+digit bookmark combinations.
- [ ] **UTIL-02** Split and Combine disposable files; compare reconstructed hash.
  Exercise invalid sizes, missing parts, existing output and cancellation.
- [ ] **UTIL-03** Create links and inspect alternate streams on a supported NTFS
  fixture with appropriate privileges. Unsupported contexts preserve original
  errors. Check metadata/targets, not just displayed labels.
- [ ] **UTIL-04** Run individual CRC/hash methods and all-methods mode, with
  internal/external entry points as available; compare results with the CLI.
  Benchmark normal/total modes retain controls, results/log, Stop/Restart/resize.
- [ ] **UTIL-05** Browse temporary files and remove only disposable test entries;
  Cancel changes nothing. Inspect long-path custom picker fallback, shared text/
  list dialogs, diagnostics, memory-limit request choices and About.
- [ ] **OPT-01** Visit all six Options pages; test Apply/OK/Cancel, page changes
  and reopen. Verify viewer/editor/diff, folders, list settings and memory limits
  keep original semantics. Association/shell settings run only in a disposable
  environment, with before/after registration observations.
- [ ] **LANG-01** When language/help assets are packaged, test English, Russian,
  a language with longer labels and CJK text. Check clipping/mnemonics and the
  menu/toolbar reload after a language change. Missing assets remain a gap.
- [ ] **SHELL-01** With system context-menu integration enabled in the test
  profile, test right-click and keyboard invocation, disabled items, submenus
  and a representative third-party extension. Native/custom file/folder pickers
  preserve cancellation, names and long-path fallback. Document external styling.
- [ ] **SET-01** When independent Arkvello settings are introduced, both GUI
  processes agree on appearance, original 7-Zip settings remain independent,
  and running both products does not redirect helper/codec loading.

## P2: release integration (later stage)

- [ ] **DIST-01** Install/update/uninstall and portable launch, signed/unsigned
  expectations, icons/version metadata, license notices, language/help/SFX
  packaging and links all match the distribution being tested.
- [ ] **DIST-02** Side-by-side original 7-Zip installation retains its own
  settings, binaries and registrations. Exercise Arkvello associations and
  Explorer commands, including uninstall cleanup, in a disposable VM.
- [ ] **DIST-03** Validate each advertised architecture/Windows version on that
  environment. The current x64 build does not establish ARM64 support.

## Stage 1 additional capability cases

Added 2026-11-16 from [the revised assessment](CAPABILITY-ASSESSMENT.md).
These are pending acceptance obligations, not findings of runtime failure.
Use the baseline before interpreting a provider/format limitation as a regression.

- [ ] **CAP-01** In two-panel mode, exercise Alt+arrow same/subfolder navigation,
  independent archive sessions and correct active-panel/destination state after
  a prompt. Resize/reopen and check persisted panel state.
- [ ] **CAP-02** On 10k/100k synthetic items and representative real archives,
  compare load, sort, scroll and bulk-selection behavior with the baseline.
  Include two panels, dynamic/reordered columns, mode changes, rename, normal/
  alternative selection and actual pointer/keyboard/assistive-technology input.
  Record latency, working set and fixture/environment; these sizes are proposed
  stress fixtures, not advertised performance guarantees.
- [ ] **CAP-03** Change files externally during refresh/sort/rename and replace
  an archive externally. Confirm stale results/handles cannot act on another
  item; preserve meaningful focus and display refresh errors. Include slow UNC
  only when an appropriate test share exists.
- [ ] **CAP-04** Edit a member and a nested-archive member with a harmless
  external editor. Test update accepted/declined, read-only formats, archive
  replacement, manager close and editor remaining open; check content, identity,
  password/Zone.Id policy and cleanup of only owned temporary files.
- [ ] **CAP-05** Clipboard/drag/drop between panels, Explorer and another test
  application: large multi-selection, archive items, canceled drop, cut vs copy,
  modifier effects, shell menu keyboard ownership and native picker fallback.
  Record supported formats/effects instead of assuming toolkit-local drag works.
- [ ] **CAP-06** Use an archive fixture with duplicate/ambiguous member paths,
  nested paths and special names. Select/test/extract/update a particular item;
  sort/refresh between actions. Compare indexed behavior with any name-based
  backend candidate; do not infer identity from the displayed row position.
- [ ] **CAP-07** In an isolated profile, trigger a bounded extraction-memory
  request. Test allow/skip, remember-for-job and save-for-future limit, Cancel,
  next archive and UI focus. Verify pause/resume, Background and cancellation
  while a prompt is active; no uncontrolled real-memory exhaustion is required.
- [ ] **CAP-08** Distinguish success, warning/partial output, failure and abort.
  A missing input plus valid input can create an archive that separately tests
  successfully: the creation warning must remain visible. Test corrupt/denied
  cases, completion during a decision and worker disconnect if applicable.
- [ ] **CAP-09** External Diff with the baseline's supported selection and panel
  combinations, quoted Unicode/spaced paths, invalid executable and Cancel.
- [ ] **CAP-10** In a disposable settings profile, configure harmless Diff and
  `7vc` paths to expose conditional version-control commands. Compare Edit,
  Commit, Revert and Diff, read-only/writable availability, snapshots, timestamps
  and confirmations. Do not alter the user's real registry or source files.
- [ ] **CAP-11** In a VM with required assets, test every offered Explorer
  operation, including open-with-type, checksum generate/test and available email
  variants, with no main window open and a large multi-selection. Verify results,
  entry-point identity, errors and coexistence; unavailable mail is recorded.
- [ ] **CAP-12** Compare original documented/supported FM startup arguments and
  helper invocations with redesigned entry points: path handoff, long selection,
  Unicode/quoting, invalid requests and outstanding job/editor lifetime on close.
  Record any intentional compatibility mapping before claiming equivalent entry
  point coverage.

## Acceptance at the stage 4 checkpoint

The 2026-11-16 [native prototype record](NATIVE-PROTOTYPE.md) lists partial
automated evidence against every P0 group and the unperformed manual subcases.
Checklist cases are not promoted to complete on message-based evidence alone.

All P0 cases must have recorded outcomes. Failures and unavailable environment/
packaging subcases must be listed explicitly; blocked coverage cannot be
reported as passed. Decide whether the observed presentation limitations are
acceptable before expanding the treatment to every remaining dialog. Prerelease
also requires applicable P1/P2 cases and the build/archive smoke checks.
