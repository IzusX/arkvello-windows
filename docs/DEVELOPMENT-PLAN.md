# Revised development plan

2026-11-16. Stage 1 source assessment is complete. The user accepted C# + WinUI 3
with existing C++ services, approved stage 2 composition and authorized stage 3.
Stage 3 implementation has a [runnable native slice](WINUI-SLICE.md), with final
acceptance still pending. After receiving that report, the user authorized the
next stage on 2026-11-18. Stage 4 is now in progress; stages 5-8 have not started.
This progression does not waive the recorded stage 3 acceptance gates.
Initial slice boundaries are recorded in
[decision 0002](decisions/0002-working-slice-boundary.md); release gates remain open.
The 2026-11-18 [stage 3 validation record](STAGE3-VALIDATION.md) and
[operation/recovery decision](decisions/0003-operation-ownership-and-recovery.md)
record the expanded implementation and acceptance that remains unexecuted.
See [decision 0001](decisions/0001-ui-stack.md).
This plan supersedes the earlier sequence of extending the Win32 styling
adapter to all dialogs. Product requirements are authoritative in
[PRODUCT.md](../PRODUCT.md); visual direction is recorded in
[DESIGN.md](../DESIGN.md).

## Working principle

Design Arkvello as a coherent Windows 11 application. Preserve all original
7zFM capabilities and equivalent user-visible results, with freedom to change
composition, navigation, controls and dialog organization. Retain the original
archive engine and reuse suitable code rather than requiring a rewrite.

For each operation, choose between the bundled CLI, direct interfaces and
adapted upstream code based on behavior and total implementation/maintenance
cost. Isolate execution details from presentation where the workflow needs it.
C# + WinUI 3 with retained C++ services is selected. Several competing backends,
a custom helper protocol and a particular ABI/process placement are not selected.

Windows File Explorer is the visual reference. System personalization supplies
restrained accents; light/dark/system and contrast themes remain requirements.
The macOS repository is independent and must remain untouched.

## Starting point

Reuse the pinned upstream source, build/CI tooling, archive round-trip checks,
source inventory and regression cases. Existing design studies, theme work and
the native prototype provide evidence and potentially reusable components.
The working tree already contains brief corrections from the preceding discussion.

The prototype has unresolved selection geometry, toolbar hover/repainting/icon
movement and approved-design fidelity issues. It has not passed acceptance.
Do not merge existing draft work merely because it builds, or extend its visual
adapter by default. If a component is retained, its defects must be resolved and
tested; a replacement must pass the same scenarios. Track any retired component
explicitly rather than marking its bugs fixed without a replacement check.

Recorded earlier build results do not constitute completion of a new stage.
Stage 1 completion evidence is recorded below. Stage 2 composition is user-approved.

## 1. Capabilities and reuse assessment

Status: complete as a source-assessment stage, 2026-11-16. See the
[capability/reuse record](CAPABILITY-ASSESSMENT.md) and
[language, UI and boundary proposals](TECHNOLOGY-OPTIONS.md).
The record covers 49 capability groups, with provisional routes and explicit
runtime uncertainties. Static command coverage and 15 bounded CLI commands
were checked; full runtime parity and framework feasibility remain later gates.

- Expand the existing source map and checklist into a capability record: user
  task, options and outcomes, upstream implementation, UI dependencies,
  candidate execution route, evidence and remaining uncertainty.
- Cover files/folders and archive contents, all main commands, list/selection
  modes, two panels, external viewer/editor/diff, nested archives, advanced
  compression/extraction, metadata, links/streams, utilities and Explorer actions.
- Compare CLI and direct reuse only where there is a meaningful choice. Inspect
  support for prompts, progress, cancellation/pause, warnings, item identity,
  Unicode/long paths and parameters; do not infer FM parity from CLI switches.
- Identify what can be reused intact, needs a boundary around it, or must be
  replaced. Record a provisional route per capability and verification cases.

Deliverable: capability/reuse record and a short list of risks to test first.
Completion: every known capability has an entry; unsupported or uncertain
behavior is visible and no feature has been silently removed.

## 2. Coherent interface proposal

Status: complete at design-study scope; user-approved 2026-11-16. See the
[interactive study](../design/stage2/README.md) and
[interface/capability placement](INTERFACE-PROPOSAL.md). HTML checks and
composition approval do not establish native functionality.

- Design the main window and the main operation surfaces together: navigation,
  command organization, file/archive lists, optional two panels, Add, Extract,
  progress, password/overwrite prompts and settings organization.
- Evaluate sidebar, breadcrumbs, tabs and search against useful workflows.
  Their appearance in reference screenshots does not add them to the scope.
- Map every capability to an accessible command or setting. Define keyboard
  behavior, selection/current-item semantics, editable paths, focus restoration,
  errors, busy/empty states and compact/constrained layouts.
- Show light/dark, accent behavior and representative scaling. Design advanced
  fields as well as the main window; do not postpone difficult options until
  after the composition is fixed.

Deliverable: revised interactive studies and a command/option coverage map.
Completion: the user reviews the proposed composition; all required options
have a place, with no implication that a mockup proves native behavior.

## 3. Technical working slice and architecture decision

Status: authorized and in progress. The first runnable slice and executed checks
are in [WINUI-SLICE.md](WINUI-SLICE.md). The remaining native gates are explicit;
do not treat this milestone as permission to skip them or start broad migration.

- Prove the accepted C# + WinUI 3 stack against the stage 1 risks and approved
  stage 2 composition. Do not build several full clients. Existing C++ services
  remain the reuse foundation; a stack change requires a new explicit decision.
- Build one end-to-end path: browse a directory and a real archive, select an
  item, extract it, handle a destination conflict and cancel during real work.
  Include a password-protected fixture and an error/warning result.
- Where useful, run the same bounded task through CLI and direct interfaces.
  Check real inputs/results, progress, prompt handling and graceful cancellation,
  not only whether the process exits. Record known parity gaps.
- Measure list responsiveness on representative large directories/archives;
  check theme states, actual pointer/keyboard use, focus, accessibility, target
  DPI changes, startup and deployment on a clean Windows environment.
- Define only the operation contracts exercised by the slice. Validate the UI
  stack and decide reuse boundaries and justified process boundaries from results.

Deliverable: runnable slice and an architecture decision with tradeoffs,
evidence, operation route choices and implications for packaging/upstream updates.
Completion: the slice works with the required interactions; unresolved failures
have a concrete resolution before broad implementation. A failed candidate
returns to evaluation rather than locking in a workaround for every surface.

## 4. Main window and filesystem workflows

Status: authorized and in progress, 2026-11-18. The first filesystem workflow
increment and its remaining work are recorded in [STAGE4-VALIDATION.md](STAGE4-VALIDATION.md).
This is not completion of stage 4 or retrospective acceptance of stage 3.

- Implement the selected composition, navigation, editable path/history,
  file icons/overlays, columns, sorting, list modes, status and one/two panels.
- Complete selection modes, keyboard commands, rename, create folder, copy,
  move, delete, clipboard and drag/drop, context menus and focus restoration.
- Add independent Arkvello settings for introduced preferences; plan any reuse
  or import of upstream settings explicitly. Preserve original 7-Zip coexistence.
- Compare real renders and pointer interactions against the approved study.
  In particular, selection/hover/focus geometry and icon placement must remain
  stable in normal use, not only in message-driven captures.

Deliverable: usable main window for filesystem tasks.
Completion: applicable navigation/list/file-operation cases pass and visual
review finds no unresolved counterpart to the reported prototype defects.

## 5. Archive workflows and full parameter support

- Complete archive browsing, nested archives, selected-item access/extraction,
  external opening/editing and update-back behavior, including temporary files.
- Implement Add/Extract/Test and update/delete operations through the selected
  routes, preserving supported formats, complete parameters, format/method
  dependencies, estimates/limits, volumes, SFX, encryption and metadata choices.
- Integrate progress, pause/background behavior where supported by the original,
  cancellation, password retry, overwrite policies and partial-success reporting.
  Choose another route if the initial CLI/direct candidate cannot preserve them.
- Keep shell quick-action use in view: the same operation behavior must work
  when invoked without navigating through the main window.

Deliverable: integrated archive workflows and real result verification.
Completion: applicable archive cases pass against original behavior; successful,
warning, failure and cancellation outcomes are distinguished accurately.

## 6. Remaining functionality and interface consistency

- Complete properties, CRC/hash tools, benchmark modes, links/alternate streams,
  temporary-file tools, favorites, settings, diagnostics and secondary prompts.
- Preserve all options and utilities identified in stage 1. The inventory, not
  this abbreviated list, controls completion. Reuse or replace each surface
  deliberately while keeping one interaction and visual language.
- Verify localization coverage, long labels/filenames, all appearance settings,
  assistive technology, DPI changes, large text and window resizing.

Deliverable: capability-complete development build.
Completion: every capability is implemented and checked or has a clearly
recorded blocker. Blocked/missing capabilities prevent claiming full parity.

## 7. Distribution and Explorer integration

- Complete product/executable branding, portable/installable packaging,
  required helpers/codecs/SFX/language/help assets and original notices.
- Implement associations and Explorer quick actions without requiring the main
  window; verify quoting, multi-selection, paths, operation results and cleanup.
- Validate installation, update and uninstall, independent settings, explicit
  import behavior if offered, and coexistence with original 7-Zip in a VM.
- Verify runtime loading on a clean system using the distribution selected in
  stage 3. Document the tested Windows versions and architectures; x64 evidence
  does not establish ARM64 support. Define signing/distribution expectations.

Deliverable: installable/portable prerelease candidate with tested integration.
Completion: distribution and Explorer entry points work without depending on
the developer machine or another installation of 7-Zip.

## 8. Release validation

- Run the full applicable regression record against the pinned upstream, with
  backend/version, build, environment and evidence recorded for each result.
- Exercise real mouse/keyboard workflows, representative large data, corrupt
  archives, denied access, conflicts, password failures and cancellation.
- Verify files and relevant metadata after operations; archive creation smoke
  tests alone do not establish file-manager or shell-integration parity.
- Review screenshots and actual interaction against the approved composition.
  Resolve regressions and acceptance blockers before proposing a release.

Deliverable: validation report, documented upstream limitations and release
candidate. Completion: required capabilities and quality gates pass; unexecuted
checks are never reported as passed.

## Sequencing and review

Stages 1-3 reduce uncertainty before broad UI implementation. Stage 2 may use
stage 1 findings as they become available; the technical slice may feed back
into the design. Stages 4-6 expand the proven foundation by complete workflows.
Testing, accessibility and visual review occur in each stage; stage 8 is the
final integration pass, not their first occurrence.

Stage 2 composition has been approved. Stage 3 now proves the accepted stack
and records ABI, operation-route, process and deployment decisions before
expanding the implementation. Its current evidence and remaining work are in
[WINUI-SLICE.md](WINUI-SLICE.md).
Keep changes reviewable and retain baseline comparison evidence. This document
does not authorize merges, installation/registration on the user's system,
publication or release. No calendar estimates are asserted before the main
integration risks have been examined.
