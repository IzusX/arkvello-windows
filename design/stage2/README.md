# Current interface proposal — C# + WinUI 3

Open [index.html](index.html) in a desktop browser. It works locally without a
server, network, package installation or access to real files. This HTML is a
design review artifact; the application UI will be WinUI 3. The accepted stack
is recorded in [decision 0001](../../docs/decisions/0001-ui-stack.md).

The [complete proposal](../../docs/INTERFACE-PROPOSAL.md) defines behavior,
advanced options, constrained layout and placement of all 49 capability groups
and their 74 mapped static commands. The user approved composition on 2026-11-16.
The [native stage 3 slice](../../docs/WINUI-SLICE.md) is a separate implementation.
The earlier [preview](../preview/README.md) is retained for comparison.

## Review the study

The controls above the application frame belong to the viewer. They are not
proposed application chrome. Choose a surface directly, or open Add/Extract
from the toolbar/menu. The interface demonstrates:

- One/two panels (F9), independent focus and retained inactive selection;
  editable paths, long names, empty/loading/denied states and retry.
- Pointer selection, Ctrl toggle, Shift range, keyboard movement, Ctrl+A,
  Insert/Space marking and a bounded alternative-selection example. These are
  design interactions, not the complete 7zFM input implementation.
- Full command organization through menus. Most secondary commands explain
  their scope in the viewer status instead of opening an implemented dialog.
- Add sections with retained fields, separate metadata overrides/values,
  password visibility/mismatch and a persistent footer.
- Extract options; common progress/pause; overwrite, password and memory
  requests; partial-result and cancellation illustrations; settings organization.
- System/Light/Dark, illustrative contrast, manually chosen navigation accent,
  CSS 100/125/150/200% scaling and scrollable forms.

An operation never runs. Progress is fixed synthetic data. Clicking a decision
only changes the illustration. Cancel immediately shows a synthetic terminal
result; the production contract requires Cancelling and service acknowledgement.
Repeated opening resets illustrative configuration; the native implementation
will use explicit drafts/settings according to the proposal. Settings do not
change Windows or 7-Zip. Short option lists and drawn file icons are examples;
native services supply full format/method/property data and shell icons.

The viewer color picker only tints navigation/address fields. Native system
accent resolution and contrast-safe action/focus colors remain implementation
work. Browser contrast/scale checks do not prove native contrast themes,
per-monitor DPI, text scaling, Narrator or real file-list virtualization.

## Captures and verification

Rendered from the local HTML using installed Edge in headless mode. The
[verification script](../../scripts/check-stage2-study.mjs) uses Node's built-in
CDP/WebSocket support, an isolated profile under ignored `build/`, local file
URLs and only the local browser debugging endpoint. It performs real browser
pointer/key dispatch for its interaction checks. Run from the repository:

`node scripts/check-stage2-study.mjs`

The [machine-readable record](captures/checks.json) lists actual checks and
browser version. This is evidence for the study only; no native regression
checkbox is promoted to passed and no Windows app is built or installed.
The [validation record](../../docs/STAGE2-VALIDATION.md) describes executed
checks, placement coverage and tooling limits.
The [independent finish review](REVIEW.md) returned ship for the design proposal;
this does not replace user approval or native validation.

Representative captures (1280×900 unless named constrained):

- [Main, light](captures/main-light.png), [main, dark](captures/main-dark.png)
  and [two panels, dark](captures/main-two-dark.png).
- [Add, light](captures/add-light.png), [metadata, dark](captures/add-metadata-dark.png),
  [200% CSS scale](captures/add-200-dark.png) and [960×640 constrained](captures/add-constrained-light.png).
- [Extract, dark](captures/extract-dark.png) and [progress, light](captures/progress-light.png).
- [Overwrite](captures/overwrite-light.png), [password error](captures/password-error-light.png),
  [memory request](captures/memory-dark.png) and [partial result](captures/result-dark.png).
- [Integration settings](captures/settings-integration-light.png) and
  [long-name contrast example](captures/main-contrast-long.png).

The old prototype's selection, toolbar hover/repainting and approved-design
fidelity defects remain open. The replacement must pass actual native pointer,
keyboard and visual review before those issues can be closed.
