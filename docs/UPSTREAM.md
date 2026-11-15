# Upstream baseline

- Official repository: https://github.com/ip7z/7zip
- Baseline: **7-Zip 26.06**, dated 2026-11-06 in `C/7zVersion.h`.
- Commit: `d3296e39e1f1f92c7b41c0c3848e65640428d42d` (upstream `main` at project setup).
- Windows fork: https://github.com/IzusX/arkvello-windows

The first milestone adds build tooling and documentation only. Original C/C++,
assembly, resources, makefiles and license files remain unchanged. The initial
binaries therefore still use the original 7-Zip names, UI and settings locations.
Do not install or register this development build as a replacement for 7-Zip.

Configure a fresh clone:

```powershell
git clone https://github.com/IzusX/arkvello-windows.git
cd arkvello-windows
git remote add upstream https://github.com/ip7z/7zip.git
git fetch upstream
```

If `gh repo clone` already created `upstream`, skip `git remote add`.
Keep `origin` pointing to the Windows fork. The macOS repository
`IzusX/arkvello` is a separate project and is not part of this checkout.

Future upstream updates should be reviewed and merged on a dedicated branch,
followed by the Windows build, archive smoke tests and manual UI checks.
Update this baseline record when deliberately adopting a new upstream version.

Licensing details and original notices are preserved in `DOC/License.txt`,
`DOC/copying.txt` and `DOC/unRarLicense.txt`. Those files are also copied into the
development output. See `DOC/readme.txt` for the original build instructions.
