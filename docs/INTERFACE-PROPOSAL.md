# Stage 2 — coherent WinUI interface proposal

2026-11-16. **Composition approved by the user.** The user accepted
C# + WinUI 3 with retained C++ services; see [decision 0001](decisions/0001-ui-stack.md).
The [interactive study](../design/stage2/index.html) is HTML used to review
composition and states. It is not the application's frontend, a WinUI control
proof or an archive implementation. [Review instructions and evidence](../design/stage2/README.md).
Executed study checks and their limits are in [STAGE2-VALIDATION.md](STAGE2-VALIDATION.md).

## Surface brief

Operate mode. The primary user already knows 7zFM and wants its complete file
and archive workflows in a coherent Windows 11 interface. A successful session
allows direct navigation, precise selection, complete archive configuration and
understandable recovery without the old arrangement dictating presentation.

Keep the approved Explorer-inspired visual system: subtle personalization tint
in navigation, neutral list/command surfaces, rectangular selection, compact
horizontal commands, readable rows and native window behavior. Preserve
[DESIGN.md](../DESIGN.md)'s established world. This proposal changes composition,
not the visual identity. Its signature interaction is maintaining the user's
current panel/item context through commands, prompts and return to the list.

The first viewport devotes most height to the list. The path is editable without
switching modes. Add, Extract and Test stay visible. No welcome dashboard,
decorative cards or archive-engine terminology appear in the working chrome.
The risks are the WinUI file list, full option dependencies and exact operation
semantics; HTML cannot prove them. Stage 3 must test those risks before expansion.

## Main window

- System-owned caption behavior, snapping, resize and system menu. The preview
  caption buttons are drawings. The application must implement actual platform
  hit testing/drag regions if a custom title bar is used.
- A compact File / Edit / View / Favorites / Tools / Help menu retains complete
  discoverable commands and keyboard access. Secondary commands are grouped
  rather than removed. The toolbar is an additional entry, not the only one.
- Each panel owns navigation/history, editable path, command targets, selection,
  current item and view/columns. Back/forward are a proposed history convenience;
  root/parent, original history and ten bookmarks remain. The path can represent
  filesystem, virtual roots, archive levels and streams without pretending every
  context is a normal directory.
- One panel initially. F9 toggles two panels with independently retained state;
  Tab switches panel focus when the list owns focus, with normal Tab traversal
  available in forms. Original cross-panel Alt+arrow operations remain. A divider
  changes widths. Enter/leave two-panel mode must not discard a session.
- Add / Extract / Test, then Copy / Move / Delete; View and More on the right.
  Commands target the active panel, including menu invocation. In narrow panels,
  toolbar text can collapse while names remain in tooltips/accessibility names;
  user text/large-button preferences still exist. No glyph movement on hover.
- Four list modes remain. Details has dynamic property columns, resize/reorder,
  sorting including unsorted, timestamp precision and UTC. Default filesystem
  columns are Name / Modified / Type / Size; archive providers supply their own
  useful columns. Two panels never remove Size or another configured column
  merely to fit. Horizontal scrolling preserves access when columns cannot fit.
- Status reports active-panel totals and selected count/size. The inactive
  panel retains its selection; its path and focus identify command destination.
  Large lists use data-backed loading/batching, not thousands of per-cell calls.

Sidebar: defer; it duplicates roots/history/bookmarks and reduces useful width
in two panels. Breadcrumbs: defer visual segmentation until virtual/archive
boundaries are modeled; editable path is first-class now. Tabs: defer; independent
sessions, edit-back and two panels must be sound before another lifetime layer.
Search: defer; a box must not imply a new recursive search engine. Incremental
typing, masks, by-type selection and flat view preserve the original workflows.
These are proposals for scope discipline, not permanent prohibitions.

## Input and state contract

Normal selection: pointer single, Ctrl toggle, Shift range; keyboard movement,
Ctrl+A, Insert/Space and original useful accelerators. Alternative selection:
current item and marked set are separate; moving the current item must not erase
marks. Full-row vs item-only selection follows the preference. The current item
has a separate visible focus outline, including when not marked. Inline rename
commits with Enter, cancels with Escape, preserves useful input on validation
failure and restores row focus. Provider identity is not the displayed filename.

The study demonstrates a bounded subset, not a complete keyboard emulator.
The implementation must preserve original selection precedence for commands,
incremental typing, shortcuts, panel transfer and mouse behavior. No arbitrary
selection toggle may silently reinterpret operation inputs.

Dialog open captures the invoker and selection/session generation. Cancel returns
to a valid invoker/current row; refresh/reorder can require identity-based recovery.
Tab traverses accessible controls; arrow keys traverse menu/tab groups; Escape
closes a non-running editor or requests cancellation of a job as appropriate.
Native menus expose access keys and accelerators. A disabled command has real
disabled semantics and, when needed, an explanation tied to context/format.

List states: populated, empty, loading/reading, denied/unavailable with retry and
external-change/refresh. Keep the path while reporting a failure. Long Unicode
names truncate visually, with full text accessible through tooltip/properties/
editable path. Pending enumeration does not block the UI or allow stale rows to
be treated as current service handles.

## Add to Archive

One configuration surface, one draft and one action footer. Changing sections
must preserve entered values. Archive output path/history/browse remains above
the sections. Entry context identifies selected input and destination; shell
entry points use the same configuration model without requiring the manager.

- **Compression:** format, level, method, dictionary, word size/order, solid
  block, threads and hardware-thread information, compression memory limit and
  both compression/decompression memory estimates, editable volume sizes/presets.
- **Update:** add/replace, update/add, freshen, synchronize; path mode; SFX,
  shared-file access and delete-after-compression. Destructive behavior retains
  upstream confirmation/result semantics. Read support does not imply write or SFX.
- **Encryption:** password, confirmation, show/hide, encryption method and
  filename encryption. Format/method-specific constraints and validation remain.
  Show the error in the correct section and focus the field; never discard the
  rest of the draft. Do not persist real passwords with settings/history/logs.
- **Metadata:** symlinks, hard links, ADS and security; precision plus mtime,
  ctime, atime and latest-member archive time. Explicit override flags and values
  are separate. Include preserve-source-atime. Unsupported controls retain clear
  availability; changing format must not silently change the submitted meaning.
- **Advanced:** raw codec parameters; retain unknown properties for native
  validation. No simplified-only replacement of the complete parameter model.

All controls in the non-CE portion of `CompressDialog.rc` and
`CompressOptionsDialog.rc` have a destination above. Options moves into the same
draft rather than opening a second nested dialog. Native services supply the
full choices, defaults, dependency graph and estimates. The study deliberately
uses short example lists; they are not the supported format/parameter contract.

## Extract

Destination history/browse, optional output-name enable + name, full/current/no
paths where valid for the entry point, duplicate-root elimination, all overwrite
policies, password/show and restore-security. These cover the active controls
in `ExtractDialog.rc`; the commented-out ADS checkbox is not newly activated.
Zone.Id remains a shared integration policy. Selected members use stable
session/item identity; extracting an external archive and a current archive
selection are distinct requests with one visual language.

The example destination and optional name are illustrative; production combines
them with the original rules without doubling the suffix or creating a second
unintended directory. Invalid paths retain typed values and focus. Default action
names the task (Extract), rather than a generic OK.

## Operation and prompt surfaces

Use one operation-window design for Add, Extract, Test, copy/move, hashes and
other lengthy work. It can be owned by the manager or shown alone from Explorer.
HTML uses a single modal study container to review the appearance; it does not
decide that every job blocks all manager panels or add a multi-job queue.

Display task, input/output, current member, processed/total, completed bytes,
packed size/ratio where meaningful, elapsed/remaining time, speed, progress and
messages. Fields may be unknown during enumeration: use indeterminate progress
and an unknown value, not fabricated estimates. Show terminal results only after
the service confirms them. The state model is:

`Preparing → Running ↔ Paused / Awaiting decision → Cancelling → Cancelled`
or `Completed / Completed with issues / Failed`.

Pause is cooperative and acknowledged; show pending pause/resume if not immediate.
Background preserves upstream priority behavior, with the UI impact assessed in
stage 3. It is not an implied detached background daemon. Close/Cancel during
work enters cancellation, keeps the window until safe acknowledgement, and
explains any partial output. Do not force-kill or claim rollback.

- **Overwrite:** existing/incoming full paths, sizes and times; Replace,
  Replace all, Skip, Skip all, Auto rename, Cancel operation. A request ID and
  operation scope prevent stale replies. Auto-rename uses the original policy;
  do not substitute a new rename algorithm. Initial focus must not accidentally
  accept a destructive choice on Enter.
- **Password:** archive/context, masked entry/show, retry without clearing unrelated
  inputs and Cancel. Distinguish a known missing/wrong password from a diagnostic
  that can also indicate damaged data. Apply format-specific limits. Secrets
  do not enter public logs, stored drafts or process command lines by default.
- **Memory:** required/current limit and available RAM when known; Allow archive
  unpacking or Skip archive; Repeat chosen action for current operation; Save
  new limit for subsequent operations and numeric GB value; Continue/Cancel
  semantics. `MemDialog.rc` skips an **archive**, not a file; the commented skip-file
  control is not an active capability. New-limit editing is enabled only when
  saving. Availability/remember visibility/default action follow the real request,
  including when physical RAM makes allowing impractical. The study shows the
  allow/skip choices as direct buttons rather than radio + Continue.
- **Results:** distinguish success, warnings/partial completion, failure and
  cancellation with text, counts and complete diagnostics. Copy messages and
  open output where valid. The warning illustration must not show a full success
  label. Worker disconnect is a failure/unknown-outcome report until reconciled,
  not silent completion. Retain enough context to inspect partial outputs.

## Settings and remaining surfaces

One settings window with sections, scrollable content and persistent action
footer. Changes are a draft until Save; Cancel discards. Settings are owned by
Arkvello and do not write the original 7-Zip profile. Current-user/system scope,
privileges and errors must be explicit for integration. No registration is
performed as part of a design review.

- **Appearance:** System/Light/Dark; contrast-system priority, OS accent, toolbar
  text and large buttons. Existing independent toolbar visibility remains in View.
- **List and behavior:** `..` row, real icons, full row, grid lines, single click,
  alternative selection, system context menu, large memory pages, explicit
  unpack-memory override and numeric GB value. These cover `SettingsPage2.rc`.
- **Working folder:** system/current/specified + editable path/browse + removable
  only, covering `FoldersPage2.rc`. Enable path only for the specified choice.
- **External applications:** full viewer/editor/diff command strings and browse,
  covering `EditPage2.rc`; preserve command-template semantics.
- **Integration:** shell registration and conditional 32-bit component, cascade,
  icons, duplicate-root policy, exact supported Zone.Id policies and full dynamic
  per-command toggles from `MenuPage2.rc`/native capability data. Quick actions
  include email/checksum variants and no-main-window launches.
- **File types:** full format list with current-user/all-users states and bulk
  selection, preserving `SystemPage.rc` scope. Follow Windows' supported default
  application mechanisms rather than forcing an association preference.
- **Language:** full catalog, language details and runtime refresh as supported,
  from `LangPage.rc`; stable semantic property IDs do not depend on translated text.

Secondary windows use the same title/content/scroll/footer system: copy/move
destination/history, delete confirmation, create/name/comment/mask input,
properties with all typed/raw/multi-level values and copying, links with five
types/from/to, Split size/path, Combine order/output, hash method/checksum
generation/verification, Benchmark normal/total and stop/restart, temp-file
inspection/cleanup, edit-back confirmation and failure details, About/help.
They are specified here and in the capability map; not every secondary dialog
is implemented in the HTML. Native pickers/third-party shell menus remain
Windows-owned; their functional integration still needs testing.

## Complete capability placement

Every group in the stage 1 inventory has a destination below. This is a
**placement map**, not proof that the preview or application implements it.
The 74-entry command ledger in [CAPABILITY-ASSESSMENT.md](CAPABILITY-ASSESSMENT.md#static-command-coverage-ledger)
inherits these placements by group; dynamic bookmarks, columns, time formats,
shell verbs and conditional commands are handled explicitly in the relevant groups.

<!-- stage2-placement-start -->
- NAV-01: Editable path / root/history menu; list providers; native picker integration.
- NAV-02: Panel archive context, path, dynamic columns; View → Flat view.
- NAV-03: Panel archive-level navigation; edit-back/close prompts; temporary ownership.
- NAV-04: File → Open / Inside / Inside * / Inside # / Outside; item context menu.
- NAV-05: Editable path/history; View → Root / Parent; Favorites → ten bookmarks + assign.
- NAV-06: View → Two panels / F9, independent panel state, divider and cross-panel input.
- VIEW-01: View → four modes; File → Rename / inline F2; incremental keyboard input.
- VIEW-02: View → Sorting and columns / time precision / UTC; header context menu.
- VIEW-03: Edit → selection/masks/type/invert; Settings → List; separate current item.
- VIEW-04: Menubar, per-panel toolbar, status; View → Toolbars; Appearance preferences.
- VIEW-05: View → Refresh / Auto refresh; unavailable/retry and external-change states.
- FILE-01: File → Open outside / Viewer / Editor; Settings → External applications.
- FILE-02: Editor-return update-back prompt; pending editor/close state and failure detail.
- FILE-03: Toolbar + File → Copy / Move; destination/history; common progress/conflicts.
- FILE-04: Edit → Cut / Copy / Paste; native list drag/drop; shell effects/context policy.
- FILE-05: Toolbar + File → Delete; Recycle Bin/permanent/archive confirmation + result.
- FILE-06: File → Create folder / Create file / Rename / Comment; provider availability.
- FILE-07: File → Properties / More; typed/raw/multi-level values, copying and long text.
- FILE-08: File → Link; Hard / File symlink / Directory symlink / Junction / WSL; from/to.
- FILE-09: File → Alternate streams; panel context/path, supported operations/properties.
- ARC-01: Toolbar + Tools → Add; all five configuration sections, history and dependency data.
- ARC-02: Add → Metadata; separate override/value states; source-atime preservation.
- ARC-03: Add → Encryption; confirmation/error/method/name encryption and native limits.
- ARC-04: Toolbar + Tools → Extract; exact selection vs whole-archive context, full options.
- ARC-05: Toolbar → Test; Add → update modes; File → archive rename/delete/comment; edit-back.
- ARC-06: Add → format/volumes/SFX; format-aware availability and multipart open/results.
- OP-01: Common operation window; progress/metrics/log; Pause/Resume and Background.
- OP-02: Operation Cancel/Close → Cancelling → acknowledged terminal result and partial output.
- OP-03: Overwrite prompt with all six choices, plus upfront Extract overwrite policy.
- OP-04: Password prompt/retry; Add confirmation and Extract password; safe secret lifetime.
- OP-05: Memory request with allow/skip archive/remember/save numeric limit; settings override.
- OP-06: Common results/log; distinct success/issues/failure/cancel/disconnect states.
- UTIL-01: File → Split / Combine; own size/order/destination controls, common progress/results.
- UTIL-02: File → CRC/SHA; all runtime methods, create/test checksum files, archive streams.
- UTIL-03: Tools → Benchmark / full benchmark; mode, threads, dictionary, stop/restart, results.
- UTIL-04: Tools → Temporary files; inspect/select cleanup; occupied/failed cleanup feedback.
- UTIL-05: File → Compare; Settings → External applications → Diff command template.
- UTIL-06: File → configured version control → Edit/Commit/Revert/Diff under original conditions.
- SET-01: Settings → List and behavior / Appearance; independent persistence; View preferences.
- SET-02: Settings → Working folder; all location choices/path/removable-only.
- SET-03: Settings → External applications; viewer/editor/diff command strings/browse.
- SET-04: Settings → Language and language details; menus/help/messages/localized controls.
- SET-05: Settings → File types; all formats, user/system states and bulk operations.
- SET-06: Settings → Integration; registration/32-bit/cascade/icons/verbs/root/Zone.Id.
- INT-01: Explorer Arkvello verbs → standalone operation/config/result surface; same option model.
- INT-02: In-app context menu / configured Windows shell menu; Windows-owned pickers/fallback.
- INT-03: Launch routing to manager or operation host; argument compatibility and pending-work close.
- INT-04: Help / About / notices; independent settings, product packaging and runtime errors.
- NEW-01: Shared Windows 11 theme/state/layout/accessibility rules across all application surfaces.
<!-- stage2-placement-end -->

## Constrained layout and implementation handoff

Design coordinates are DIPs. Keep 14/20 body, 28-row baseline and established
4/8/16 spacing; WinUI styles may require explicit density templates rather than
their untouched defaults. Main study sizes 1280×900 and constrained 960×640;
form input must remain reachable at 200% scaling. These are review viewports,
not a final minimum window size. Dialog content scrolls; title and action footer
remain visible. At narrow effective widths, form columns stack and sections
scroll. The native implementation must respond to effective width, not just
CSS/browser viewport rules, and preserve large-text preferences.

OS accent is an input to semantic colors. The study's color picker changes
navigation/address tint; it does not read personalization. Default-action/focus
colors use contrast-safe theme samples. Native accent contrast adjustment,
color-change subscriptions and actual Windows contrast roles remain required.
No browser contrast preset establishes native contrast-theme support.

Use WinUI controls/templates across application-owned windows. Reuse C++ behavior
behind requests/results/prompt callbacks, not old HWND/control IDs as the view
model. Stable list implementation, ABI/process placement and deployment remain
stage 3 evidence gates. Do not duplicate archive algorithms or parse engine text
directly in a view. Native callbacks cannot synchronously mutate XAML controls.

The user approved composition, grouping and workflow direction on 2026-11-16
and authorized stage 3. Approval does not mark old selection/hover bugs fixed
or certify WinUI parity. The [native working slice](WINUI-SLICE.md) is the
first implementation milestone against this accepted target.
