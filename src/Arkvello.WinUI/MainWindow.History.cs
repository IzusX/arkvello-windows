using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Arkvello;

public sealed partial class MainWindow
{
    private const int HistoryLimit = 20;
    private readonly List<string> recentPaths = [];
    private readonly List<string> transferDestinations = [];
    private readonly List<string> extractDestinations = [];

    private static void RememberPath(List<string> paths, string path)
    {
        paths.RemoveAll(existing => string.Equals(existing, path, StringComparison.Ordinal));
        paths.Insert(0, path);
        if (paths.Count > HistoryLimit) paths.RemoveAt(HistoryLimit);
    }

    private static void RestoreHistory(List<string> paths, string?[]? saved)
    {
        if (saved == null) return;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string? path in saved.Take(HistoryLimit))
            if (path is { Length: > 0 and <= 32767 } && !path.Contains('\0') &&
                Path.IsPathFullyQualified(path) && seen.Add(path))
                paths.Add(path);
    }

    private async void OnPathHistory(object sender, RoutedEventArgs e)
    {
        if (!SelectionReady) return;
        string? selected = await ChoosePath("История путей", recentPaths, "Очистить");
        if (selected != null)
        {
            await Navigate(selected);
        }
        if (!closing) FocusFileList();
    }

    private async void OnDrives(object sender, RoutedEventArgs e) => await ChooseDrive(activePanel);

    private async Task ChooseDrive(PanelState pane)
    {
        if (!SelectionReady || !ActivatePanel(pane)) return;
        string[] drives;
        try { drives = Directory.GetLogicalDrives(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ShowError(error.Message);
            return;
        }
        string? selected = await ChoosePath("Диски", drives);
        if (selected != null) await Navigate(selected);
        if (!closing) FocusFileList();
    }

    private async Task<string?> ChoosePath(string title, IReadOnlyList<string> paths, string clear = "")
    {
        if (paths.Count == 0)
        {
            configuring = true;
            try { await NewDialog(title, new TextBlock { Text = "Список пуст." }, close: "Закрыть").ShowAsync(); }
            finally { configuring = false; }
            return null;
        }
        var choices = new ListView { MaxHeight = 340, SelectionMode = ListViewSelectionMode.Single };
        AutomationProperties.SetName(choices, title);
        foreach (string path in paths)
        {
            var text = new TextBlock { Text = path, TextTrimming = TextTrimming.CharacterEllipsis };
            ToolTipService.SetToolTip(text, path);
            choices.Items.Add(new ListViewItem { Content = text, Tag = path });
        }
        choices.SelectedIndex = 0;
        var dialog = NewDialog(title, choices, "Перейти", "Отмена");
        dialog.SecondaryButtonText = clear;
        dialog.Opened += (_, _) =>
        {
            if (choices.SelectedItem is Control item) item.Focus(FocusState.Programmatic);
        };
        bool openedByDoubleClick = false;
        choices.DoubleTapped += (_, e) =>
        {
            for (var element = e.OriginalSource as DependencyObject; element != null && element != choices;
                 element = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element))
                if (element is ListViewItem item && choices.Items.Contains(item))
                {
                    choices.SelectedItem = item;
                    openedByDoubleClick = true;
                    dialog.Hide();
                    break;
                }
        };
        configuring = true;
        ContentDialogResult answer;
        try { answer = await dialog.ShowAsync(); }
        finally { configuring = false; }
        if (closing) return null;
        if (answer == ContentDialogResult.Secondary && paths is List<string> history) history.Clear();
        return (answer == ContentDialogResult.Primary || openedByDoubleClick) &&
            choices.SelectedItem is ListViewItem { Tag: string pathChoice } ? pathChoice : null;
    }

    private static StackPanel DestinationActions(Button browse, TextBox field, IReadOnlyList<string> recent)
    {
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(browse);
        var pickRecent = new Button { Content = "История", IsEnabled = recent.Count > 0 };
        var menu = new MenuFlyout();
        foreach (string path in recent)
        {
            var item = new MenuFlyoutItem { Text = path.Length <= 70 ? path : path[..30] + "…" + path[^39..] };
            AutomationProperties.SetName(item, path);
            ToolTipService.SetToolTip(item, path);
            item.Click += (_, _) => { field.Text = path; field.Focus(FocusState.Programmatic); field.SelectAll(); };
            menu.Items.Add(item);
        }
        pickRecent.Flyout = menu;
        actions.Children.Add(pickRecent);
        return actions;
    }
}
