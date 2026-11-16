# Arkvello for Windows

## Purpose

Modernize the appearance of 7-Zip File Manager for Windows 11 while preserving
the functionality and working habits that make the original useful. The primary
user already knows 7zFM: dense file lists, direct commands, keyboard shortcuts,
two panels when needed, and complete archive options are product requirements.

The project is a fork of the original C++/Win32 application. The working
direction, revised 2026-11-16, combines a coherently designed interface with
selective reuse of existing implementation. Preserve the archive engine and
reuse suitable file/archive logic. Existing windows, control classes, command
IDs and process boundaries are implementation choices that may be revised.

Functional parity means every original 7zFM capability remains available and
produces equivalent user-visible results. It does not require identical window
composition, control placement or sequences of dialogs. The macOS Arkvello
example demonstrates this freedom; Windows has its own platform-specific design.
On 2026-11-16 the user selected C# + WinUI 3 for application-owned UI, retaining
existing C++ services. See [decision 0001](docs/decisions/0001-ui-stack.md).
Exact ABI, process placement and deployment remain evidence gates for stage 3.

## Interface constraints

- Aim for a coherent Windows 11 appearance across the main window, settings,
  operation dialogs and secondary prompts across all application entry points.
- Keep commands, directly editable paths, list modes, columns, selection modes,
  useful shortcuts, optional two panels and complete advanced options accessible.
  Navigation, menu organization and dialog grouping may be redesigned.
- Design the interface as one product. Reuse an existing UI surface when it
  meets the target; replace it when adapting it would compromise that target
  or create excessive maintenance work. Migration need not mix frameworks.
- Sidebars, breadcrumbs, tabs and search are design candidates, not automatic
  additions. The Windows 11 Explorer reference sets the visual direction.
- The menu/navigation background uses the Windows Mica material, incorporating
  theme and desktop wallpaper. This user revision replaces the former manual
  personalization-accent tint. File content and scrollbar gutters stay opaque.
- Retain the information density of a desktop file manager. Decoration must not
  displace useful rows or conceal controls behind a simplified workflow.
- Design for light, dark and system themes, display scaling, keyboard use,
  contrast themes, long filenames and translated labels. Detailed visual rules
  belong to [DESIGN.md](DESIGN.md); native coverage is recorded per surface.
- Treat Windows-owned dialogs and shell extensions as integration surfaces;
  their exact rendering is not fully controlled by this application.
- Explorer quick actions without opening the main window remain in scope;
  their registration and full behavior need separate implementation/validation.

## Operation execution policy

Design UI and operation execution separately. For each capability, compare
the bundled CLI, direct archive/folder interfaces and reusable upstream source.
Choose the implementation with the lowest total complexity that preserves the
required behavior, responsiveness and maintainability. No single route is
mandatory, and supporting several routes for the same task is not a goal.

- CLI execution is a first-class option. A complete create, extract, test or
  hash task may be cheaper to integrate through it than to detach FM handlers.
  These are candidates, not decisions that every such task must use the CLI.
- Direct interfaces or adapted source may be preferable for interactive archive
  browsing, item identity, repeated access, external editing and richer prompts.
  A library can also run in a helper process if that boundary is justified.
- A separate process does not imply a console-text protocol. A custom helper
  with structured communication is an option to evaluate, not a required service
  or a commitment to replace the CLI.
- UI-facing operation contracts should expose typed inputs/results, progress,
  warnings/errors, user prompts and cancellation as needed by actual workflows.
  Raw console output must not become the UI's application model. A CLI adapter
  owns parsing, encoding, exit-code interpretation, input transport and graceful
  cancellation; force termination is not assumed equivalent to Cancel.
- Verify parity for every selected route, including options, metadata, password
  retries, overwrite choices and partial results. Do not silently fall back to a
  route with different semantics or remove a feature because one route lacks it.
- Ship and resolve known versions of required helpers/codecs with Arkvello.
  Do not depend on the user's installed 7-Zip or mix unverified CLI/DLL versions.

The existing FM, GUI helper and CLI are all implementation resources. Windows
offers additional reuse choices compared with the described macOS CLI-based
client; it does not impose a different rule for choosing an operation backend.

## Development approach

The user authorized committing and pushing completed development increments on
2026-11-17. Keep reviewable commits on the Windows development branch and report
their links; this does not authorize merging branches or publishing releases.

1. Map capabilities and representative workflows against the original. Distinguish
   required behavior from replaceable presentation and UI-coupled operation code.
2. Design the main window and operation dialogs together, including selection,
   keyboard use, progress, cancellation, errors and constrained layouts.
3. Prove the selected C# + WinUI 3 stack and reuse boundaries with a real working slice:
   browse an archive, extract an item, handle overwrite and cancel an operation.
   Evaluate CLI/direct integration where it matters. Check large lists, scaling,
   accessibility and deployment before expanding the rest of the interface.
   A need to change the selected stack requires a new explicit decision.
4. Expand by complete workflows, using explicit operation inputs, results and
   progress/prompt callbacks where separation is needed. Retain existing logic
   where practical; do not assume all FM functionality is supplied by 7z.dll.
5. Track functional coverage and real pointer/keyboard verification alongside
   visual fidelity. A retained dialog is an explicit migration decision, not
   an accepted permanent visual exception.

The [revised development plan](docs/DEVELOPMENT-PLAN.md) replaces the former
sequence for planning purposes. Extending the presentation adapter to all
remaining dialogs is no longer the automatic next step. The revised stage 1
assessment is complete. The user approved the stage 2
[coherent interface proposal](docs/INTERFACE-PROPOSAL.md) on 2026-11-16 and
authorized stage 3. A real C# + WinUI 3 / C++ technical slice now exists;
see [scope, evidence and remaining gates](docs/WINUI-SLICE.md). Stage 3 remains
in progress until its native integration, accessibility and deployment gates
are resolved; this does not establish full FM parity.

## Project boundaries

Windows 10 compatibility is a product goal accepted on 2026-11-16, alongside
the Windows 11 visual target. Retain the current Windows 10 2004 (19041), x64
minimum while dependencies allow it; prefer Windows 10 22H2 for validation.
Guard newer OS features and provide native fallbacks, including solid Mica
fallback. New dependencies/API choices must account for this goal. Actual
Windows 10 operation and deployment are unverified until tested there; runtime
vendor support/lifecycle is distinct from technical compatibility. See
[the compatibility checklist](docs/WINDOWS-COMPATIBILITY.md).

Application-authored command/navigation/file-placeholder icons use a vendored
Fluent System Icons Regular subset, independent of installed symbol fonts.
Keep the upstream MIT license with distribution. Windows-owned controls and
caption glyphs retain their native rendering.

Product name: **Arkvello for Windows**. Repository:
[IzusX/arkvello-windows](https://github.com/IzusX/arkvello-windows).
[Arkvello for macOS](https://github.com/IzusX/arkvello) is an independent project
and must not be changed as part of Windows work.

The pinned base is 7-Zip 26.06; see [upstream policy](docs/UPSTREAM.md).
Initial development targets x64. Installer, associations, Explorer registration,
independent settings and release branding require their own implementation and
validation before distribution.

## Current evidence

Stage 1 established local and CI builds with archive round-trip smoke tests.
The user confirmed that the development file manager launches and looks like
the original 7zFM. This confirms startup and the unchanged appearance, not a
complete manual regression pass.

Stage 2 records the [UI source map](docs/UI-MAP.md) and
[behavior checks](docs/UI-REGRESSION.md). Stage 3 defines the
[Windows visual system](DESIGN.md) and [interface studies](design/preview/README.md).
Stage 4 implements the main window, Add to Archive and nested Options as a
working Win32 prototype. The shared presentation adapter reads Windows app
theme and personalization accent through UISettings, with contrast-theme
priority. See [native evidence and remaining checks](docs/NATIVE-PROTOTYPE.md).
The user subsequently reported incorrect selection geometry, toolbar hover
colors/repainting and shifting icons, plus a mismatch with the approved second
Explorer-based study. These remain unresolved; static captures and message-based
tests did not establish ordinary pointer interaction or visual acceptance.
The prototype is implementation evidence, not a requirement for the final
architecture. Other dialogs, full validation and release packaging remain.

On 2026-11-16, the authorized revised stage 1 completed the
[capability/reuse assessment](docs/CAPABILITY-ASSESSMENT.md): 49 capability
groups, static command coverage and bounded CLI probes, with runtime parity
still explicitly unverified. [Technology options](docs/TECHNOLOGY-OPTIONS.md)
propose C# + WinUI 3 with retained C++ services as the first candidate, C++ +
WinUI 3 as the reuse-focused alternative and C# + WPF as the table-focused
alternative. The user subsequently accepted C# + WinUI 3 with existing C++
services. ABI and process placement remain undecided, and the technical slice
must prove the list, integration and deployment before broad implementation.
