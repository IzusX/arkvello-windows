# Arkvello for Windows

A fork of the official [7-Zip](https://www.7-zip.org/) File Manager, intended to
preserve its functionality and familiar workflow while modernizing the interface
for Windows 11. The current client uses C# + WinUI 3 with retained C++ archive
services. Its technical slice browses real folders and nested archives, extracts
exact native items, handles prompts and demonstrates safe editor update-back.
A self-contained development folder is built with `scripts/publish-winui.ps1`.
Stage 4 has started with filesystem copy/move, inline rename, create/delete and
StorageItems clipboard/drag integration. See [its validation and remaining work](docs/STAGE4-VALIDATION.md).
Stage 3 acceptance remains pending; this is not a complete FM replacement.
The earlier Win32 styling prototype and its unresolved interaction defects are
documented separately and are not the current UI implementation.
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
- [Operation ownership, virtual input and edit recovery](docs/decisions/0003-operation-ownership-and-recovery.md)
- [Stage 3 validation and outstanding acceptance](docs/STAGE3-VALIDATION.md)
- [Stage 4 filesystem workflow implementation and validation](docs/STAGE4-VALIDATION.md)
- [Filesystem service and Windows Shell boundary](docs/decisions/0004-filesystem-operations.md)
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
