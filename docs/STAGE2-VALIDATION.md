# Stage 2 study validation

2026-11-16. Scope: [HTML design study](../design/stage2/README.md) and
[interface proposal](INTERFACE-PROPOSAL.md), not native application acceptance.
Composition is awaiting user review. The selected UI stack is recorded in
[decision 0001](decisions/0001-ui-stack.md).

## Executed checks

The local Edge/CDP script completed **27 of 27 checks**, including pointer
selection, toolbar hover/leave geometry and foreground, two-panel Size-column
visibility and retained inactive selection, Add focus, time override/value,
password-mismatch focus, Escape restoration, pause/resume, conflict response,
archive-level memory skip/save-enable behavior, a persistent footer and scrolling
at 200% CSS scale, constrained 960×640 footer, denied/retry, alternative marking,
Insert and menu keyboard navigation. No runtime exception was reported.

See [checks.json](../design/stage2/captures/checks.json) for the precise assertions,
timestamp and browser version, and
[check-stage2-study.mjs](../scripts/check-stage2-study.mjs) for their scope.
The runner required access to the browser's local debugging socket beyond the
default sandbox; the approved run used local files/an isolated browser profile.
No site, user browser profile, real file operation or registry mutation is used.

Twenty-four renders cover main Light/Dark/two-panel/long-name contrast, all five
Add sections, Extract, progress, three prompts, partial result, all seven settings
sections, 200% CSS scale and a constrained form. Captures are reviewed as actual
renders, not generated pictures. Metadata lists are abbreviated examples; the
full control destinations and dependencies are specified in the proposal.

The stage 2 placement block was compared with every stage 1 group: **49 unique
groups, none missing or extra**. The existing source audit again mapped all
**74 distinct static commands**, with no missing/extra IDs or invalid group/link.
Together these establish placement coverage, not implementation or behavior.
Active desktop Add/Options/Extract/settings controls were reviewed against
their RC source, including separate override/value and archive-level memory skip.

JavaScript syntax checks, local Markdown-link resolution for nine new/updated
documents and `git diff --check` passed. Line-ending warnings reflect the existing
Windows Git conversion policy. Application source did not change, so a native
build or old native harness run would not validate this deliverable.

## Review tooling and limits

The Impeccable detector could not run: engine 0.1.18 is not installed and its
launcher cannot create the user cache with available permissions. No detector
pass is claimed. Manual rendered review and a fresh independent reviewer were
used. The [independent review](../design/stage2/REVIEW.md) returned **ship** with
no material fixes at the declared proposal scope, after checking all 24 captures.
User composition approval remains outstanding.

The study does not test native WinUI controls, codecs, graceful cancellation,
password transport, item/session validity, edit-back, OLE, shell registration,
large-list performance, Narrator, actual Windows contrast themes, native popup
rendering, mixed-monitor DPI or text scaling. Stage 3 must prove the accepted
WinUI/C++ working slice. No native regression case is marked passed here, and
the old prototype's reported defects remain open.
