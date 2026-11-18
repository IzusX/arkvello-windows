using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Arkvello;

public sealed partial class MainWindow
{
    private async Task VerifyWindowPreferences(string output, string mode)
    {
        var checks = new List<object>();
        void Check(string name, bool passed)
        {
            checks.Add(new { name, passed });
            if (!passed) Environment.ExitCode = 1;
        }

        try
        {
            var left = panels[0];
            var right = panels[1];
            if (mode is "write" or "corrupt")
            {
                Check($"{mode} settings start with the standard theme, view and panel count",
                    WindowHost.RequestedTheme == ElementTheme.Default && !twoPanels && left.View == "Details" &&
                    left.Columns[0] == new Column("Имя", "Name", 420) && splitRatio == .5);
            }
            if (mode == "write")
            {
                OnTheme(ThemeMenu.Items[2], new RoutedEventArgs());
                await SetTwoPanels(true);
                ResizePanels((ListBand.ActualWidth - 8) * .13);
                OnView(new MenuFlyoutItem { Tag = "SmallIcons" }, new RoutedEventArgs());
                left.Columns[0] = left.Columns[0] with { Width = 330 };
                (left.Columns[0], left.Columns[1]) = (left.Columns[1], left.Columns[0]);
                left.Sort = "ModifiedText";
                left.Descending = true;
                right.Columns.Add(new("CRC", "Properties[19]", 178));
                right.MarkMode = true;
                ActivatePanel(right);
                OnView(new MenuFlyoutItem { Tag = "List" }, new RoutedEventArgs());
                Check("normal window close receives the requested pane and theme preferences",
                    twoPanels && activePanel == right && WindowHost.RequestedTheme == ElementTheme.Dark && Math.Abs(splitRatio - .63) < .001);
            }
            else if (mode == "read")
            {
                Check("new process restores dark theme and the checked theme command",
                    WindowHost.RequestedTheme == ElementTheme.Dark && ((ToggleMenuFlyoutItem)ThemeMenu.Items[2]).IsChecked);
                Check("new process restores visible panes, divider and split ratio",
                    twoPanels && TwoPanelsMenu.IsChecked && right.ListHost.Visibility == Visibility.Visible &&
                    panelDivider.Visibility == Visibility.Visible && Math.Abs(splitRatio - .63) < .001);
                Check("new process restores independent view, sort, selection mode and columns",
                    left.View == "SmallIcons" && left.List is GridView && left.Sort == "ModifiedText" && left.Descending &&
                    left.Columns[0].Property == "ModifiedText" && left.Columns[1].Width == 330 &&
                    right.View == "List" && right.List is ListView && right.MarkMode &&
                    right.List.SelectionMode == ListViewSelectionMode.Multiple && right.Columns.Contains(new("CRC", "Properties[19]", 178)));
                Check("no navigation or selection from the previous window is restored",
                    right.Directory == left.Directory && left.History.Count == 0 && right.History.Count == 0 &&
                    left.ArchiveSession == null && right.ArchiveSession == null && left.List.SelectedItems.Count == 0 && right.List.SelectedItems.Count == 0);
            }
        }
        catch (Exception error)
        {
            checks.Add(new { name = "preferences diagnostic", passed = false, error = error.ToString() });
            Environment.ExitCode = 1;
        }
        finally
        {
            File.WriteAllText(Path.Combine(output, $"preferences-{mode}.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
            RequestClose();
        }
    }
}
