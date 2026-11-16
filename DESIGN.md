# Arkvello Windows visual system

Stage 3 design target, approved 2026-11-15; system-accent requirement and
composition freedom clarified 2026-11-16. This specification and the
[interactive studies](design/preview/index.html) record the approved visual
direction and the previous prototype composition. The layout-specific studies
below are references for reassessment, not a fixed structure for the next UI.
Native implementation and its limits are tracked in
[NATIVE-PROTOTYPE.md](docs/NATIVE-PROTOTYPE.md). Product requirements
come from [PRODUCT.md](PRODUCT.md), control ownership from
[UI-MAP.md](docs/UI-MAP.md), and acceptance scenarios from
[UI-REGRESSION.md](docs/UI-REGRESSION.md).

## Direction

Primary visual reference: the user's Windows 11 File Explorer screenshot,
reviewed 2026-11-15. Match its surface hierarchy, neutral row selection,
horizontal command treatment and softly filled address field. The screenshot
is a visual reference. Tabs, a sidebar, breadcrumbs and search may be considered
in a revised design; none is required solely because Explorer includes it.
Colors below are authored approximations, not extracted OS theme constants.
On 2026-11-16 the user replaced manual personalization-accent tint with the
Windows Mica material: menu and navigation share its wallpaper/theme-derived
backdrop. The old blue-gray studies remain historical palette examples.

Native Windows 11, compact desktop utility. Preserve all 7zFM capabilities and
equivalent operation results while allowing a new composition. Command placement,
navigation, panels and dialog grouping can change. The macOS example illustrates
platform-native design freedom; do not copy its Finder composition into Windows
as a requirement. Keep useful information density and all advanced parameters
accessible. Design complete surfaces together; the implementation may reuse or
replace existing UI without letting legacy control structure dictate the layout.
Detailed new layouts and their implementation technology remain undecided.
The [operation execution policy](PRODUCT.md#operation-execution-policy) allows
CLI, direct interfaces and adapted upstream code behind consistent application
behavior. Backend choice must not force a different visual language or omit
commands. Further work follows the [proposed plan](docs/DEVELOPMENT-PLAN.md).

Use Windows Mica Base for the common menu/navigation foundation, with native
system fallback. Keep file content, scrollbar gutters and the lower command
surface opaque and neutral. Keep Windows caption buttons, snapping, system
menu, resizing and window shadows under DWM control; custom chrome is not needed.

## Typography and dimensions

2026-11-16 icon choice: use the official Fluent System Icons Regular subset
vendored under `src/Arkvello.WinUI/Assets/Fluent`, rendered as native vector
geometry. Command/navigation and generic file/folder icons should not depend
on Segoe Fluent Icons being installed. Preserve theme/disabled/contrast
foreground inheritance. Windows-owned caption and control glyphs remain native.

All measurements below are design units at 96 DPI (DIP), not fixed device
pixels. Convert through the window DPI once; round shared edges consistently.
These are application choices, not a claim that every measurement is a Windows
default.

- Prefer Segoe UI Variable Text where the renderer supports it; Segoe UI is the
  GDI fallback. Do not bundle Microsoft fonts. Preserve Windows font fallback
  for Cyrillic, CJK and other scripts.
- Primary UI, file rows and form labels: 14 DIP regular, 20 DIP line box.
  Secondary status text: 12/16. Group headings: 14/20 semibold. Native caption
  text remains system-owned. No all-caps labels or decorative tracking.
- Base spacing: 4; related controls 8; group separation 16; dialog inset 20.
- Main window study: 1180 x 740; initial useful target 960 x 640. These do not
  replace existing saved geometry or impose a new main-window minimum yet.
- Main menu 28 high with 13 DIP text; command band 52; path band 48; list header
  32; details row 28; status 26. Native caption height is system-derived,
  illustrated at 32. Keep upstream density options available in production.
- Toolbar normal commands: 36 high, 20 DIP command glyph beside a 14 DIP label,
  9 DIP icon/text gap and 10 DIP horizontal padding; preserve existing
  large/small/text visibility options. Large-button
  mode must use a scaled icon source, not stretch the small bitmap.
- Address fields: 34 high, softly filled, 5 DIP corners; outline the whole field
  on focus. Retain directly editable paths and history. Other inputs, combos
  and dialog buttons: 28 high; labels have at least 4 DIP
  clearance. Check target row 26 high, glyph 16. Button minimum width 88; browse
  button 32. Text can expand widths; labels may wrap without overlap.
- Control corners 4; outer window corners belong to Windows. No rounded file
  rows, section cards or rounded selection pills. A list remains a continuous
  working surface.
- Archive study: 900 DIP wide, approximately 720 high including caption and
  footer. Keep related form rows on a 34 DIP rhythm. At constrained work areas,
  a scrollable form must retain a visible action footer and keyboard access to
  every field. The exact native layout/scroll strategy is a stage 4 checkpoint;
  increasing DPI alone is not an adequate layout strategy.

Microsoft's [Windows typography guidance](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/typography)
identifies Segoe UI Variable and a 12 DIP regular minimum. The slightly denser
14 DIP body here is deliberate for this file manager; do not substitute the
large typography of a touch-first settings application.

## Palette and theme resolution

The preview's [CSS tokens](design/preview/style.css) are the visual reference.
The native prototype must resolve the same semantic roles for both `7zFM.exe`
and `7zG.exe`, rather than introduce independent palettes.

- **Light**: frame `#F3F3F3`, navigation/caption `#F0F2F5`, address `#E5E8ED`
  (hover `#DCE0E6`), panel/input `#FFFFFF`, raised/hover `#EAEAEA`,
  pressed `#DFDFDF`, divider `#D5D5D5`, control edge `#8A8A8A`, text `#1A1A1A`,
  secondary `#5C5C5C`, disabled `#6D6D6D`, accent `#005FB8`, accent text
  `#FFFFFF`, focused selection `#E5E5E5`, selection text `#1A1A1A`, inactive
  selection `#EEEEEE`, current-item outline `#666666`, error `#B42318`, error
  surface `#FFF1EF`.
- **Dark**: frame `#202020`, navigation/caption `#1C202C`, address `#292D39`
  (hover `#333846`), panel/input `#191919`, raised/hover `#2D2D2D`,
  pressed `#3D3D3D`, divider `#383838`, control edge `#969696`, text `#F3F3F3`,
  secondary `#C0C0C0`, disabled `#A0A0A0`, accent `#75BFFF`, accent text
  `#101010`, focused selection `#414141`, selection text `#F3F3F3`, inactive
  selection `#303030`, current-item outline `#B0B0B0`, error `#FFB4AB`, error
  surface `#452521`.
- **System** is the initial preference: resolve Windows application light/dark
  preference, listen for changes, repaint existing windows, descendants and
  open popups. An explicit app theme overrides application light/dark only.
- **Windows contrast themes take precedence** over all app themes. Use system
  window, text, highlight, gray-text and focus colors; do not force the blue
  palette or bitmaps. The HTML's contrast selector is an illustrative palette;
  CSS forced-colors follows actual browser/system colors when active.
- Native menu/navigation use `Window.SystemBackdrop` with `MicaBackdrop`,
  `Kind=Base`, visible through transparent container backgrounds. Address fill
  uses the native neutral `ControlFillColorDefaultBrush`. Do not manually sample
  wallpaper, mix the system accent into these surfaces, or make the whole window
  translucent. Windows owns inactive/transparency-off/battery/unsupported-system
  fallback and contrast behavior. The theme preference is applied to the window
  content root so Mica and controls agree. Native control focus/default-action
  accents remain; file selection stays neutral. No global Windows settings are
  written. See Microsoft's [Mica guidance](https://learn.microsoft.com/en-us/windows/apps/design/style/mica).

Use neutral fills for selected items, accent for focus and the default action. Toolbar
commands stay neutral, including Delete; destructive confirmation retains its
original behavior. Avoid pure red selection fills. Alternative selection mode
must retain its separate marked-items/current-item semantics with a selection
fill plus focus outline; do not collapse it into ordinary Explorer selection.
Deleted-item information retains text/state meaning in contrast themes too.

The [Windows color guidance](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/color)
supports neutral surfaces, restrained accent and system preference. Win32
controls do not acquire these colors automatically by following WinUI advice.

## State vocabulary

- **Rest**: flat surface, legible text, outlined input boundary. Thin structural
  dividers need not meet the contrast of an interactive control edge.
- **Hover**: neutral surface change, no geometry movement or extra padding.
- **Pressed**: deeper neutral surface. Default action remains identifiable when
  another control owns focus; default and keyboard focus are different states.
- **Keyboard focus**: 2 DIP high-contrast outline, inset/offset so it is not
  clipped. Dialog edits also have a 2 DIP accent bottom rule; the address field
  uses a surrounding focus outline instead. Keep native caret,
  selection, default-button behavior and visible keyboard cues.
- **Selected list item**: rectangular fill across the user's configured full-row
  or item selection extent. Current/focused item has a distinct outline. An
  inactive panel's selection uses the neutral selection role. Selected items
  must not disappear when the panel loses focus.
- **Disabled**: distinct foreground and preserved geometry, actual disabled
  semantics. Never rely on opacity over unknown backgrounds. In normal themes
  the chosen disabled foreground remains readable, even though disabled text
  is exempt from normal contrast requirements.
- **Error**: original message plus error text and boundary, focus to the
  offending field when existing behavior does so. Never use only color. Do not
  add a new custom validation rule through the styling layer.
- **Busy**: original progress and cancel/pause semantics. No decorative loading
  animation or skeleton rows that impersonate real filesystem contents.
- **Empty**: retain columns, editable path and status count; no illustration,
  onboarding overlay or new action that displaces the file workspace.

Text and placeholder target >=4.5:1; essential input edges/focus target >=3:1
against adjacent surfaces. Selection keeps readable foregrounds in both themes.
The prototype must verify accessible names, checkbox/radio state, list selection,
default buttons and reading order with assistive technology; HTML accessibility
does not establish Win32 accessibility.

## Main-window study

This section records the approved stage 3 study. Its command coverage and
interaction requirements remain relevant; its exact composition may be revised.

Keep the native menu visible: File, Edit, View, Favorites, Tools, Help (localized
as today). Keep the archive toolbar group Add / Extract / Test, followed by
Copy / Move / Delete / Info. Preserve visibility and text options; no new
command destinations are introduced by the design.

Each panel owns its Up button, editable path with history, column header,
scrolling list and status. Single panel is the original default; two-panel mode
uses the same components with a narrow draggable divider, clear current-panel
focus and independent selections. No permanent decorative active-panel badge.

Numbers align right; dates and sizes use tabular figures where supported, with
the existing formatting retained. Name consumes remaining width. Ellipsis and
horizontal scrolling preserve long-name access; a preview tooltip is not a
replacement for copy/edit/full path behavior. File icons remain shell-provided
in production so file type, overlays and system associations remain accurate.
The preview contains authored generic folder/file shapes as sample data only.

## Add to Archive study

This section records the stage 3 form. Full parameter coverage, dependencies
and value semantics remain requirements; column grouping, control IDs and
dialog boundaries may change in a redesigned implementation.

Keep the original archive path/history and browse button first. Preserve the
two-column grouping and all fields: format, compression level/method,
dictionary, word size, solid block, CPU threads/hardware count, compression
memory estimate and limit, decompression memory estimate, volume sizes and raw
parameters on the left; update/path modes, SFX/shared/delete options and full
password/encryption controls on the right. The separate Options button and its
summary remain below the left column. OK, Cancel, Help retain order and meaning.

Section names and whitespace replace heavy etched group boxes. They do not
change grouping semantics, control IDs or resource-string mapping. Longer
localized captions may grow/wrap the row. Do not replace dropdowns with sliders,
hide raw parameters or merge password fields into a visibility-only control.

Format/method dependencies, unsupported options, password confirmation rules,
memory calculations, typed combo values, update modes and existing resource
localization remain controlled by upstream logic. The HTML uses illustrative
values; it does not emulate the archive engine or validate codec combinations.

The nested Options study preserves NTFS symlinks/hardlinks/ADS/security and time
precision, modification/creation/access time, latest-file archive time and
preserve-source-access time. Explicit-override checkboxes are separate from
stored values: disabled subordinate values do not mean false. Preserve each
original flag, enabled state, keyboard ordering and tri-state behavior if used.

## Icons, interaction and motion

Use a consistent 20 DIP line-icon grid, 1.25 DIP command strokes (1.5 for
navigation) with optical alignment. Sample folder icons use a restrained gold
fill and 1 DIP outline, instead of the previous outline-only placeholders.
The study contains original SVG geometry for the seven commands and navigation;
these are design references, not replacement product-logo assets. Production
may render vector geometry or approved multi-resolution assets; keep shell
file icons and overlays. Do not use font characters as command icons.

No entrance choreography, animated selection, blur or row transitions. Feedback
is immediate; optional hover color transitions stay <=100 ms and respect reduced
motion. Preserve useful shortcuts, equivalent mouse operations, focus restoration,
panel switching and correct modal ownership; existing event handlers and
accelerator tables may be replaced. Review against
the regression checklist rather than infer behavior from the HTML study.

## Scaling and implementation checkpoint

Test 100/125/150/200%, mixed-DPI monitors, large text, long translated captions,
RTL where supported, light/dark/system changes, contrast themes and reduced
motion. At 200% the physical work area may be smaller than the full dialog;
controls must remain reachable and footer actions visible. Do not declare
per-monitor awareness before fonts, images, non-client area, list metrics,
dialog geometry and popup placements respond correctly to DPI changes.

Stage 4 produced shared presentation services for the two GUI apps, the main
window and Add dialog. It remains an unaccepted prototype with user-reported
selection, toolbar hover/repainting and visual-fidelity problems. Do not extend
it to remaining dialogs by default or treat these bugs as planned behavior.
The revised approach is recorded in [PRODUCT.md](PRODUCT.md): design coherent
surfaces, evaluate reuse and replacement, and validate a real operation path
before selecting a UI framework. Native menus, dropdowns, tooltips, headers,
scrollbars and modal children all need coverage where retained. Windows-owned
pickers, message boxes and third-party shell menus may follow OS rendering;
their functionality and ownership take precedence over exact pixel matching.

Theme preference must use Arkvello-owned storage shared by whichever application
processes need it. Do not write new theme values into
`Software\7-Zip`. Existing settings migration, executable branding and installer
work remain separate planned tasks.

Feasibility gate: demonstrate the intended UI with real file/archive operations,
both themes, stable pointer/keyboard states, usable scaled layouts and accessible
controls. Evaluate large lists, operation prompts, cancellation and deployment.
Choose retained controls, replacement surfaces and operation adapters from that
evidence. The final UI need not preserve the current executables' window
structure; functional parity remains the acceptance criterion.
