# Arkvello for Windows

A fork of the official [7-Zip](https://www.7-zip.org/) File Manager, intended to
preserve its functionality and familiar workflow while modernizing the interface
for Windows 11. The Win32 prototype now styles the main window, Add to Archive
and its nested Options dialog, with system/light/dark themes and a subtle tint
from the Windows personalization accent. This prototype has unresolved selection
and toolbar interaction bugs and has not passed visual acceptance. The approach
is being replaced by a coherent C# + WinUI 3 interface with retained C++ services,
as selected by the user on 2026-11-16. The user approved the new composition;
a runnable native WinUI technical slice now browses directories/archives,
extracts selected entries and handles prompts. It is not a complete FM replacement.
See [running it and remaining gates](docs/WINUI-SLICE.md) and the
[revised product brief](PRODUCT.md).

This is the Windows project. [Arkvello for macOS](https://github.com/IzusX/arkvello)
is developed in a separate repository.

- [Product scope and constraints](PRODUCT.md)
- [Revised development plan and completion criteria](docs/DEVELOPMENT-PLAN.md)
- [Stage 1 capability and reuse assessment](docs/CAPABILITY-ASSESSMENT.md)
- [Language, UI framework and boundary options](docs/TECHNOLOGY-OPTIONS.md)
- [Accepted UI stack](docs/decisions/0001-ui-stack.md)
- [Runnable WinUI slice, validation and limitations](docs/WINUI-SLICE.md)
- [Initial native boundary and list decision](docs/decisions/0002-working-slice-boundary.md)
- [Stage 2 interface proposal and full capability placement](docs/INTERFACE-PROPOSAL.md)
- [Current interactive design study](design/stage2/README.md)
- [Build and validation](docs/BUILDING.md)
- [UI source map and modernization boundaries](docs/UI-MAP.md)
- [UI regression checklist](docs/UI-REGRESSION.md)
- [Windows visual system](DESIGN.md)
- [Interactive interface studies and captures](design/preview/README.md)
- [Design validation and remaining native checks](docs/DESIGN-VALIDATION.md)
- [Native prototype, evidence and remaining checks](docs/NATIVE-PROTOTYPE.md)
- [Pinned upstream baseline and update policy](docs/UPSTREAM.md)
- [Original source documentation](DOC/readme.txt)
- [Original license and notices](DOC/License.txt)

Based on 7-Zip 26.06 by Igor Pavlov. Arkvello is an independent fork.
