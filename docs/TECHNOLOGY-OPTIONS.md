# Technology and boundary options after stage 1

2026-11-16. Comparison recorded before the user's decision. **Subsequent decision:**
the user accepted option A, C# + WinUI 3 with existing C++ services, and stage 2.
[Decision 0001](decisions/0001-ui-stack.md) supersedes statements below that no
language/UI stack is selected. ABI, process placement and exact controls remain
open. The comparison is retained as the rationale, not an invitation to select again.

Original assessment:
Based on [the capability assessment](CAPABILITY-ASSESSMENT.md),
[PRODUCT.md](../PRODUCT.md) and official documentation checked on this date.
Framework versions/features can change: pin a supported stable toolchain and
verify it in stage 3. No performance measurements of competing frameworks have
been made. Advantages/cost rankings below are project-specific judgments.

## What the assessed workload requires

The selection criteria, in priority order, are:

1. A responsive file list with dynamic/reorderable columns, four view modes,
   separate selection/current item, alternate selection, inline rename,
   keyboard commands, shell icons, drag/drop and two panels. Framework-provided
   virtualization does not establish data-loading/sorting performance.
2. A coherent Windows 11 interface across main/secondary surfaces, system
   personalization, light/dark/contrast, DPI/text scaling and accessibility.
3. Reuse of C++ Agent/Common/filesystem logic and typed archive properties,
   plus correct callbacks, cancellation, pause, editor and nested-archive lifetime.
4. Native Windows integration: STA/OLE, shell menus, quick actions, launching,
   paths, settings, clean packaging and coexistence with original 7-Zip.
5. A maintainable product for this Windows-only repository. Cross-platform UI
   sharing is currently not a requirement and must not change the macOS project.

Compression speed alone does not choose the UI language: the retained engine
performs that work in all these proposals. UI allocation, object/interop calls,
enumeration, sorting, icon loading and rendering still need separate measurement.
Neither C# nor Rust automatically makes the retained C++ backend memory-safe.

## Recommended shortlist

### A. C# + WinUI 3, with retained C++ services

**First candidate for the stated visual priority.** Use C#/XAML for new windows,
UI state/commands and forms; keep archive/folder and Windows-specific services
in C++, exposing a small owned boundary. This is a two-language application,
not an engine rewrite. Microsoft supports both C# and C++ for WinUI and positions
it for new Fluent Windows desktop UI. [WinUI documentation](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/).

Advantages: Fluent control baseline close to the target; C# is convenient for
observable UI state, forms, asynchronous coordination and binding; less manual
Win32 painting. These convenience judgments do not imply measured faster delivery.

Costs: author and maintain a native bridge, ownership/callback/thread rules and
two build toolchains; recreate FM input/state behavior; deploy .NET plus the
chosen Windows App SDK runtime model. XAML does not supply a finished file manager.

**Principal gate: the file table.** Do not assume stable WinUI has a ready-made
equivalent of the original details list. Current official `TableView` documentation
marks it experimental and lists single-row selection and no column reordering;
that control is not an acceptable foundation for this product as documented.
A stable ListView/ItemsView column template or a reviewed third-party control
needs actual proof of the required behavior. A custom header/column/selection
implementation may consume the apparent UI-development advantage.
[TableView status and alternatives](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/table-view).

Try A first only as a bounded list/operation slice. Reject the candidate if it
needs pervasive unsupported workarounds or fails input/AT/large-list gates.
An ordinary WinUI `GridView` is an item/tile layout, not WPF's columnar GridView.

### B. C++ + WinUI 3 (C++/WinRT)

**Strong alternative if native reuse dominates.** New XAML UI and C++ application
services in one language; keep deliberate service boundaries even without a DLL.
This has the same WinUI controls and the same table risk as A.

Advantages: direct use of upstream types/interfaces; fewer cross-language data
and callback conversions; no .NET runtime needed for this C++ application.
Existing MSVC/archive tooling remains useful.

Costs: implement XAML-facing observable objects, coroutines/lifetime and WinRT
projection in C++; new XAML/resource/MSBuild integration still required rather
than merely adding source files to the present nmake UI target. Existing HWND/
global/dialog coupling still needs adaptation. Single-language code does not
make that separation free. Windows App SDK deployment still applies.

Choose B over A if the slice shows that the C# bridge costs more than its UI
convenience saves, or if retaining one implementation language becomes a product
priority. Do not build two complete WinUI clients to discover this.

### C. C# + WPF, with retained C++ services

**Pragmatic alternative if the table is the decisive WinUI obstacle.** WPF's
ListView/GridView provides columnar display, resizing and reordering; sorting
and editing/selection semantics still require application work.
[GridView documentation](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/gridview).
It also has UI virtualization/recycling, which can be accidentally disabled and
is distinct from data virtualization. [Performance guidance](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-controls).

Advantages: relevant desktop controls, flexible templates and mature keyboard/
binding model; same C# application/native-service division as A; no need to adopt
WinUI just to use supported Windows APIs.

Costs: more deliberate work to match Explorer/WinUI spacing, states and chrome;
keep templates coherent across all forms, list modes and prompts. WPF has a
Fluent theme, so it should not be dismissed as inevitably old-looking; theme
coverage still needs review. .NET 10 documentation lists Fluent improvements
and describes that support as ongoing. [WPF Fluent changes](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100).
It retains the C++ bridge and .NET distribution costs, and has no guaranteed
startup/memory/performance advantage over A without measurement.

Choose C if it meets the approved visual study and materially simplifies the
hard list/input behaviors. Do not promise that applying one theme fully matches
Explorer or that switching frameworks removes selection/state work.

## Viable options outside the first shortlist

### D. C++ + Qt

Qt Widgets offers model/view classes well suited to item-based desktop tools
and direct C++ reuse. [Model/view documentation](https://doc.qt.io/qt-6/model-view-programming.html).
Qt Quick provides a separate UI approach. Its FluentWinUI3 style is explicitly
native-looking rather than native; current documentation lists unsupported
controls, including header/split-view types, which fall back to another style.
[FluentWinUI3 status](https://doc.qt.io/qt-6/qtquickcontrols-fluentwinui3.html).

Advantages: capable C++ toolkit and model/view foundation; no .NET/native bridge.
Costs: its own runtime/tooling and Windows shell integration; Windows 11 fidelity
requires visual work. Widgets and Quick are distinct choices, not a magical
combination of Widgets' table with Quick's appearance at no integration cost.
Consider D if C++ plus table control is prioritized and a consistent platform-
inspired appearance is acceptable. Current requirements give it less reason to
displace A/B/C; cross-platform reuse is not needed here.

### E. C# + Avalonia

Advantages: .NET/XAML model, flexible styling and table controls. Avalonia draws
its own controls rather than wrapping each platform's native control set.
[Official overview](https://docs.avaloniaui.net/docs/welcome).
Costs: reproduce the desired Windows feel and integrate Windows-only services;
still bridge to C++. Fluent styling does not establish Explorer behavior.
Useful if shared cross-platform UI later becomes a real objective. It currently
offers limited scope benefit for an independent Windows product with a native-
Windows design priority. This is not a claim that Avalonia cannot meet the design.

### F. Rust + a UI toolkit, with retained C++ backend

Rust is possible without rewriting compression or every existing component.
A concrete candidate is Rust + Slint; Slint supports Rust and C++ application
integration. [Slint](https://slint.dev/). A Rust application could instead call
a native facade or structured worker, and use a CLI adapter where it fits.

Advantages: ownership/memory-safety checks in newly written safe Rust, explicit
state and concurrency discipline, and an opportunity to build Rust expertise.
Costs: another language/build/FFI boundary around the same C++ services; new UI
and Windows shell integration; the actual toolkit must prove the advanced list,
IME, OLE, Narrator, focus, themes and menus. These are unverified requirements,
not an assertion that the toolkit lacks them. FFI calls need carefully designed
safe wrappers. [Rust FFI documentation](https://doc.rust-lang.org/stable/rust-by-example/std_misc/ffi.html).

Choosing Rust does not choose a UI toolkit, make WinUI/XAML integration equal
to its supported C#/C++ path, replace the backend, or make it safer retroactively.
An all-Rust engine rewrite would be a separate, much larger project with no
current need. Keep F as a deliberate learning/technology preference if desired;
it is not the efficiency recommendation for these requirements.

### G. continue custom Win32, or mix many UI islands

Win32 remains technically capable and maximizes existing control/handler reuse.
Its cost here is continuing to own rendering, layout, DPI, states and visual
integration across many surfaces. Existing defects demonstrate prototype gaps;
they do not prove Win32 can never work. Under the revised design freedom, it
has lost its main advantage of preserving window structure by requirement.

XAML Islands are supported for incremental replacement, but add host HWND/UI-
thread/input/focus/deployment responsibilities. Microsoft suggests considering
whole-UI migration when most screens change or two frameworks add complexity.
[Hosting guidance](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/host-controls-existing-desktop-apps).
Retain a legacy surface temporarily only for a specific workflow with an exit
plan. Avoid making a patchwork of modern forms plus permanently old prompts.

## Proposed logical boundaries

Keep these responsibilities separate regardless of the chosen language. These
are design responsibilities, not a commitment to four projects or public APIs.

1. **Presentation:** windows, controls, commands, focus, selection/current item,
   row layout, menus and localized labels. It does not parse console text or
   retain codec pointers. Only the UI thread touches UI objects.
2. **Application workflows:** panel/session state, navigation, item-handle
   validity, edit-back/temp ownership, validated operation requests, prompt
   decisions and settings policy. This layer defines what a user operation means.
3. **Native services:** adapted Agent/Common/filesystem behavior, typed archive
   properties and callbacks, Windows launch/OLE/shell helpers. Own archive/stream
   lifetime, resource cleanup and version-matched codec resolution here.
4. **Execution adapter where needed:** an operation invokes either native service
   code or a bundled CLI, with route-specific progress/input/error mapping hidden
   from presentation. Do not build a generic plugin/backend platform in advance.

An opened archive needs a session and stable identity independent of row order.
Use session/generation-aware handles and batch property reads as required by the
slice; do not marshal one cross-language call per cell during painting. Provider
support is explicit: unsupported rename/update on a format stays unsupported,
not a silent fallback to another operation with different semantics.

## Language boundary choices

**Small C-compatible facade + C# P/Invoke:** first boundary candidate for A/C.
Own sessions/jobs natively; pass explicitly sized/encoded values and opaque
handles, batch results, bounded callbacks or events. Define allocation/free,
callback lifetime, cancellation, shutdown and error translation; no upstream
`UString`, STL containers or C++ exceptions across the ABI. P/Invoke supports
unmanaged functions and callbacks. [Interop documentation](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke).

**C++/WinRT component:** credible alternative for A if WinRT-shaped async types/
events materially simplify integration. C#/WinRT provides .NET projections of
WinRT components. [C#/WinRT documentation](https://learn.microsoft.com/en-us/windows/apps/develop/platform/csharp-winrt/).
Cost: IDL, projections, packaging/type constraints and another generated surface.
Do not project all upstream private folder interfaces just because both are
COM-like. Keep this as a compare-only alternative until the slice needs it.

**Ordinary C++ boundary:** simplest language integration in B/D/G, with the same
session/lifetime/worker discipline. Lower language friction does not permit
calling slow operations synchronously from the UI or retaining global HWNDs in
the operation model.

**Rust facade:** F can consume the C-compatible boundary or use a reviewed C++
bridge. It still needs ownership/error/callback rules. Choosing it does not
remove the native adaptation work already identified.

## Process boundary choices

**P1. In-process services, off the UI thread:** first prototype candidate. Least
IPC/setup cost; useful for repeated archive access and direct callbacks. Native
failures affect the UI process; cancellation remains cooperative; no assumption
of codec/provider thread safety. Background currently changes process priority,
so an in-process port must assess its effect on UI responsiveness.

**P2. Application-owned native worker with a structured protocol:** credible if
operation lifetime, independent priority or fault containment justifies it.
Worker can load the same native services; it is not necessarily the stock CLI.
Costs: protocol/version handshake, session/job IDs, batched data, prompts/replies,
progress throttling, cancel/pause acknowledgement, disconnect/crash/cleanup and
ownership on UI shutdown. Separate process is not a security sandbox by itself.
Do not introduce a permanent service, RPC framework or one worker per item.

**P3. Bundled stock CLI for bounded operations:** low initial coupling for batch
Test/hash/create/extract. Stage 1 confirms basic stdin conflicts/password and
exit distinctions. It has no structured session protocol; GUI memory requests
and pause behavior are not supplied by stock console plumbing. Listing text
is not equivalent to indexed Agent sessions. Full interactive Add/Extract has
enough gaps that direct adapted services are the provisional first route.
CLI remains a genuine candidate where its complete behavior is sufficient;
there is no obligation to use it or maintain duplicate implementations.

For every process model, preserve success/warning/failure/cancel distinctions,
password/overwrite/memory decisions and partial outputs. UI close does not imply
forced termination is safe; nor does a worker automatically survive UI exit.
Define lifecycle from original workflow results and explicit product decisions.
Explorer integration should launch the application/operation host; it should
not load a managed UI runtime or run compression inside Explorer for convenience.

## Distribution must influence the experiment

WinUI is a desktop UI framework, not a requirement to publish through Store.
Windows App SDK offers runtime deployment choices; framework-dependent unpackaged
apps need initialization and runtime deployment planned explicitly.
[Deployment guide](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-unpackaged-apps).
Framework-dependent vs self-contained deployment, .NET (for A/C/E), VC/runtime
requirements, clean-machine startup, portable behavior and extension registration
must be tested for the actual selected stable versions. Do not infer that a
self-contained option guarantees a single dependency-free EXE or shell identity.

The current machine has MSVC tooling and .NET 10 SDKs/Desktop runtimes; this
does not establish WinUI templates/packages, a successful new UI build or clean
distribution readiness. `dotnet --info` partially failed on a sandbox-denied
Service Control Manager query; the listed installations alone are not a blocker
or proof of candidate feasibility. No toolkit/runtime installation was performed.

## Recommendation and decision sequence

**Propose A as the first candidate; B as the reuse-focused alternative; C as the
table-focused alternative.** Keep codecs and useful application services in C++.
Prefer one coherent new UI toolkit across application-owned surfaces, rather
than preserving every old window or introducing islands by default.

This is not an irrevocable ranking of framework quality. The assessment makes
the table and native application boundary the two decisive costs. If A's table
cost is excessive, compare C; switching to B cannot solve the same WinUI control
gap. If the table is satisfactory but the managed/native boundary is excessive,
compare B. Rust/Qt/Avalonia are reasonable only with a positive product reason,
not because modern appearance or an engine wrapper requires them.

1. Stage 2: agree on composition and map advanced options/input states. Use
   the inventory, especially two panels, four modes and all prompt types.
2. Stage 3, before broad UI work: prove A's table and a minimal native bridge
   with the required archive browse/extract/conflict/password/cancel path.
   Include a targeted memory/pause test; extend the slice enough to expose
   session identity/edit-back risk rather than building a universal backend.
3. Only if evidence warrants it, compare B's boundary or C's list against the
   same fixtures. Record supported versions, actual input/AT/DPI behavior,
   performance, interop calls and deployment results; choose the smallest
   architecture satisfying the product. A visual mockup cannot settle this.
4. Record the selected stack, operation routes and process placement in an ADR.
   P1 is the initial hypothesis, P2 needs evidence, P3 is chosen per task.
   No language/UI/process decision is marked accepted by this document.

Stage 1 implementation changes consist only of assessment scripts and records.
The original UI defects remain open. No replacement UI has been implemented.
