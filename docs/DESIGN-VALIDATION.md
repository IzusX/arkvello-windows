# Stage 3 design validation

2026-11-15, revised against the user's File Explorer reference. Applies to [DESIGN.md](../DESIGN.md) and the
[HTML studies](../design/preview/index.html), not the Win32 binaries.

Stage 4 native implementation and its separate evidence are recorded in
[NATIVE-PROTOTYPE.md](NATIVE-PROTOTYPE.md). The HTML checks below do not establish
native DPI, keyboard or assistive-technology coverage.

## Performed

Rendered the real local HTML with installed Microsoft Edge
`157.0.4341.60` in headless mode, Windows x64, viewport 1280 x 920,
device scale factor 1. Inspected the four committed theme/surface captures,
plus 200% archive and nested-options renders. The in-app browser tool failed
with `failed to write kernel assets` / OS error 3; local headless rendering
provided visual and DOM checks without accessing external pages.

- JavaScript syntax check passed (`node --check`).
- Main-window measured geometry: 1180 x 740 CSS pixels; details rows: 28 pixels
  in light, dark and illustrative contrast themes.
- Both dialog action footers stayed inside the 920-pixel viewport at CSS scales
  100%, 125%, 150%, 200%. At 125% and above, the archive form scrolls; its
  content has no horizontal overflow within the allocated form width. Closed
  dropdown values may truncate at constrained widths and remain available in
  the dropdown. The viewer can scroll the study itself horizontally.
- Checked Add -> archive-path focus; Cancel -> Add focus; F9 -> two panels;
  selecting the right panel -> inactive left selection; empty and long-name
  lists; password visibility; Options modal; independent time override/value
  enabled state; Escape -> close modal and restore opener focus; submitting the
  archive form -> explanatory status without navigation or file operations.
- Checked System preference reacting to an emulated dark preference, explicit
  light override, and actual CSS forced-colors roles through browser emulation.
- Added a selectable password-mismatch illustration with text and an accessible
  error association; it is a sample state, not engine validation.

Palette contrast was calculated with WCAG relative luminance, from the CSS
semantic tokens. Light/dark ratios respectively:

- Primary text on panel: **17.40 / 15.84**.
- Secondary text on frame: **6.03 / 8.96**.
- Disabled foreground on frame: **4.66 / 6.23**.
- Primary-button foreground: **6.31 / 9.65**.
- Selected-row text: **13.82 / 9.20**.
- Inactive-selection text: **15.00 / 11.89**.
- Input edge against panel: **3.45 / 5.94**; against frame: **3.11 / 5.51**.

The revised address field uses a filled surface and surrounding focus outline,
not a permanent edit border. Dialog edits retain visible borders. The command
band uses horizontal icon/label pairs; the list now uses 14 DIP text and 28 DIP
rows. Dark navigation and address surfaces have a subtle blue-gray tint while
the file area is neutral `#191919`. File icons remain authored sample geometry;
production will use shell-provided icons. No Explorer functionality was added.
Available archive height is measured from the viewer's actual wrapped toolbar,
so its footer remains visible when viewer controls occupy a second line.

The initial disabled light foreground was corrected after this check. These
ratios certify the listed token pairs, not every browser-native control pixel
or a complete accessibility audit. The contrast example's checked pairs also
passed; it is not a substitute for a real Windows contrast-theme session.

## Still required in stage 4 and later

No new native GUI behavior is claimed or marked passed in
[UI-REGRESSION.md](UI-REGRESSION.md). In particular, actual font fallback, Windows
large-text preferences, mixed-monitor DPI, native dropdowns/menus/scrollbars,
assistive technology, localization/RTL, alternate selection, application theme
notifications and existing archive behavior need implementation and testing in
both `7zFM.exe` and `7zG.exe`.

The HTML's flexible layout and browser modal behavior do not prove the same
behavior for Win32. The most significant feasibility work remains shared
dark-control rendering and a constrained-size native archive form that retains
upstream notifications, dependencies and keyboard semantics.
