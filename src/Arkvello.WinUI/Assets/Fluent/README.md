# Fluent System Icons subset

Official Microsoft Fluent System Icons, Regular style, vendored from the exact
commit recorded in `manifest.json`. Original SVG files are retained unchanged.
`Icons.xaml` contains their path geometry as native WinUI `PathIconSource`
resources. Native foreground inheritance supports theme, disabled state and
contrast without recolored image files or an installed icon font.

Dynamic file-row templates use generated `Controls/FluentItemPaths.cs` constants
to create `PathIcon` directly: XamlReader rejected assigning the shared source
resource to `IconSourceElement.IconSource`. Both representations are generated
from the same pinned SVG geometry.

20-unit sources are used for navigation, commands and compact file placeholders;
48-unit sources for large file placeholders. Placeholder icons are not a claim
of complete Windows shell file-type icon support. Stock control/caption icons
remain system-owned.

Run `python scripts/import-fluent-icons.py` from the repository to regenerate
this subset. Once a manifest exists, the importer reuses its pinned commit.
Normal builds are offline with respect to these icons and do not run the importer.

Upstream: https://github.com/microsoft/fluentui-system-icons

MIT `LICENSE` and upstream `NOTICE` are included here and copied beside the
executable under `Licenses/FluentSystemIcons/`.
