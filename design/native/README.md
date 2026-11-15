# Native prototype captures

Real `7zFM.exe` / `7zG.exe` client rendering, captured 2026-11-16 at 144 DPI
(150%) by the [private-desktop integration test](../../scripts/test-native-ui.cpp).
Fixture files are generated test data; column resizing is exercised without
saving it to the user's preferences. These are not HTML mockups.

- [Main, light](captures/main-light.png) / [dark](captures/main-dark.png)
- [Two panels, dark](captures/main-two-panels-dark.png)
- [Editable path focus, dark](captures/main-path-focus-dark.png)
- [Add to Archive, light](captures/archive-light.png) / [dark](captures/archive-dark.png)
- [Options, light](captures/options-light.png) / [dark](captures/options-dark.png)
- [Disabled checked value, light](captures/options-disabled-checked-light.png) /
  [dark](captures/options-disabled-checked-dark.png)
- [Narrow, scrolled archive form](captures/archive-constrained-dark.png)
- [Restored archive form](captures/archive-restored-dark.png)

Private-desktop captions use legacy non-client rendering. Verify DWM caption
appearance on the normal Windows 11 desktop. See [coverage and open checks](../../docs/NATIVE-PROTOTYPE.md).
