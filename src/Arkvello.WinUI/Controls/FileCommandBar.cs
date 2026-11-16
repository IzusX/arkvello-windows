using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Arkvello.Controls;

public sealed class FileCommandBar : CommandBar
{
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        // WinUI 2.6.5 aligns this template part to the right. Align the command
        // group itself, retaining native overflow, focus, icons and popup states.
        // Recheck this part when upgrading the pinned WinUI dependency.
        if (GetTemplateChild("PrimaryItemsControl") is FrameworkElement primary)
            primary.HorizontalAlignment = HorizontalAlignment.Left;
    }
}
