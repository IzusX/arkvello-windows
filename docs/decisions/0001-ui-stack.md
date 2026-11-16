# 0001 — C# + WinUI 3 with existing C++ services

Accepted by the user on 2026-11-16, after the
[stage 1 assessment](../CAPABILITY-ASSESSMENT.md) and
[technology comparison](../TECHNOLOGY-OPTIONS.md).

Application-owned presentation will use C# + WinUI 3. Retain the original C++
archive engine and adapt useful native file/archive services. Design one coherent
interface; the Win32 painting prototype does not define the new composition.

The choice favors Windows 11 presentation and convenient application UI/state
development while accepting the cost of a managed/native boundary. It does not
establish that the list, interop or distribution already works. In particular,
the experimental WinUI TableView is not an accepted production dependency.

Stage 3 must prove a stable file-list implementation, real input/accessibility,
large-list behavior, archive sessions and extract/prompt/cancel integration. A
failed gate requires a concrete remedy and, if the stack must change, a new
decision with the user; it does not silently undo this accepted choice.

Still open: supported stable SDK/runtime versions, exact list control, C ABI vs
WinRT projection, in-process vs justified native worker, per-operation execution
routes, deployment model and shell launch/registration details. The initial
small C-compatible boundary and off-UI-thread in-process service remain
experiment hypotheses, not accepted ABI/process decisions.

No Rust rewrite, macOS changes, installation, shell registration, publication or
merge is authorized by this decision. Stage 2 interface design is authorized.

Subsequent user approval: stage 2 composition and stage 3 implementation were
authorized on 2026-11-16. Initial implementation choices and remaining proof
gates are recorded in [decision 0002](0002-working-slice-boundary.md).
