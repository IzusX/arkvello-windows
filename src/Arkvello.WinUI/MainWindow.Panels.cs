using Arkvello.Models;
using Arkvello.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Arkvello;

public sealed partial class MainWindow
{
    private sealed class PanelState(int index)
    {
        public int Index { get; } = index;
        public string Label => Index == 0 ? "Левая панель" : "Правая панель";
        public Grid Navigation { get; } = new();
        public Grid ListHost { get; } = new();
        public Border StatusHost { get; } = new();
        public Border ActiveLine { get; } = new();
        public TextBox Address { get; } = new();
        public TextBlock Status { get; } = new();
        public Button BackButton { get; } = new();
        public ListViewBase List { get; set; } = null!;
        public List<FileEntry> Entries { get; set; } = [];
        public FileEntry? Current { get; set; }
        public string Directory { get; set; } = "";
        public string ArchivePrefix { get; set; } = "";
        public ArchiveSession? ArchiveSession { get; set; }
        public Stack<NavigationLocation> History { get; } = new();
        public List<Column> Columns { get; } =
            [new("Имя", "Name", 420), new("Дата изменения", "ModifiedText", 210), new("Тип", "TypeText", 170), new("Размер", "SizeText", 150)];
        public Dictionary<(Guid Session, uint Property), IReadOnlyDictionary<uint, ArchiveProperty>> Properties { get; } = [];
        public string View { get; set; } = "Details";
        public string Sort { get; set; } = "Name";
        public bool Descending { get; set; }
        public bool MarkMode { get; set; }
        public NavigationLocation Location => new(ArchiveSession?.RootPath ?? Directory, ArchivePrefix, ArchiveSession);
    }

    private readonly PanelState[] panels = [new(0), new(1)];
    private PanelState activePanel = null!;
    private PanelState OtherPanel => panels[1 - activePanel.Index];
    private bool twoPanels;
    private double splitRatio = .5;
    private readonly Thumb panelDivider = new();
    private TextBox Address => activePanel.Address;
    private TextBlock Status => activePanel.Status;
    private Grid ListHost => activePanel.ListHost;

    private void InitializePanels()
    {
        activePanel = panels[0];
        foreach (var band in new[] { NavigationBand, ListBand, StatusBand })
            for (int i = 0; i < 3; i++) band.ColumnDefinitions.Add(new());

        foreach (var pane in panels)
        {
            var navigation = pane.Navigation;
            var controls = new Grid { Padding = new(16, 8, 16, 8), ColumnSpacing = 8 };
            for (int i = 0; i < 3; i++) controls.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            controls.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            navigation.Children.Add(controls);

            void AddButton(Button button, int column, string icon, string label, string shortcut, RoutedEventHandler action)
            {
                button.Style = (Style)Root.Resources["NavigationButtonStyle"];
                // Create fresh geometry for each button. Sharing a PathIconSource
                // between the two panes can reject its already parented geometry.
                button.Content = XamlReader.Load("<PathIcon xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Width='20' Height='20' Data='" + icon + "'/>");
                button.Click += (sender, args) => { if (ActivatePanel(pane)) action(sender, args); };
                ToolTipService.SetToolTip(button, $"{label} ({shortcut})");
                AutomationProperties.SetName(button, label);
                AddToBand(controls, button, column);
            }

            AddButton(pane.BackButton, 0, Controls.FluentItemPaths.Back, "Назад", "Alt+←", OnBack);
            AddButton(new Button(), 1, Controls.FluentItemPaths.Parent, "На уровень вверх", "Backspace", OnParent);
            AddButton(new Button(), 2, Controls.FluentItemPaths.Refresh, "Обновить", "Ctrl+R", OnRefresh);
            pane.Address.Style = (Style)Root.Resources["PanelAddressStyle"];
            pane.Address.KeyDown += (sender, args) =>
            {
                if (ActivatePanel(pane)) OnAddressKeyDown(sender, args);
            };
            AutomationProperties.SetName(pane.Address, $"{pane.Label}: путь к папке или архиву");
            AddToBand(controls, pane.Address, 3);
            pane.ActiveLine.Style = (Style)Root.Resources["ActivePanelLineStyle"];
            pane.ActiveLine.IsHitTestVisible = false;
            navigation.Children.Add(pane.ActiveLine);

            pane.Status.FontSize = 12;
            pane.Status.TextTrimming = TextTrimming.CharacterEllipsis;
            pane.StatusHost.Style = (Style)Root.Resources["PanelStatusStyle"];
            pane.StatusHost.Child = pane.Status;
            AddToBand(NavigationBand, navigation, pane.Index * 2);
            AddToBand(ListBand, pane.ListHost, pane.Index * 2);
            AddToBand(StatusBand, pane.StatusHost, pane.Index * 2);
            foreach (var surface in new[] { navigation, pane.ListHost })
            {
                surface.GotFocus += (_, _) => ActivatePanel(pane);
                surface.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => ActivatePanel(pane)), true);
            }
        }

        foreach (var band in new[] { NavigationBand, ListBand, StatusBand })
            AddToBand(band, new Border { Style = (Style)Root.Resources["PanelDividerStyle"], Width = 1 }, 1);
        panelDivider.IsTabStop = true;
        panelDivider.Style = (Style)Root.Resources["PanelThumbStyle"];
        panelDivider.UseSystemFocusVisuals = true;
        AutomationProperties.SetName(panelDivider, "Ширина панелей");
        AutomationProperties.SetHelpText(panelDivider, "Стрелки влево и вправо меняют ширину панелей. Home выравнивает панели.");
        panelDivider.DragDelta += (_, args) => ResizePanels(args.HorizontalChange);
        panelDivider.DoubleTapped += (_, _) => { splitRatio = .5; ApplyPanelWidths(); };
        panelDivider.PreviewKeyDown += (_, args) =>
        {
            switch (args.Key)
            {
                case VirtualKey.Left: ResizePanels(-24); break;
                case VirtualKey.Right: ResizePanels(24); break;
                case VirtualKey.Home: splitRatio = .5; ApplyPanelWidths(); break;
                default: return;
            }
            args.Handled = true;
        };
        AddToBand(ListBand, panelDivider, 1);
        ListBand.SizeChanged += (_, _) => ApplyPanelWidths();
        panels[1].Navigation.Visibility = panels[1].ListHost.Visibility = panels[1].StatusHost.Visibility = Visibility.Collapsed;
        panelDivider.Visibility = Visibility.Collapsed;
        UpdatePanelIndicators();
        ApplyPanelWidths();
    }

    private static void AddToBand(Grid band, FrameworkElement element, int column)
    {
        Grid.SetColumn(element, column);
        band.Children.Add(element);
    }

    private void ResizePanels(double delta)
    {
        double width = ListBand.ActualWidth - 8;
        if (!twoPanels || width <= 0) return;
        splitRatio = Math.Clamp(splitRatio + delta / width, .25, .75);
        ApplyPanelWidths();
    }

    private void ApplyPanelWidths()
    {
        // Leave both paths usable at narrow window sizes. Expand the permitted
        // split as the viewport grows without changing the saved session ratio.
        double available = Math.Max(8, ListBand.ActualWidth - 8);
        double minimum = Math.Min(.5, 230 / available);
        double ratio = Math.Clamp(splitRatio, minimum, 1 - minimum);
        foreach (var band in new[] { NavigationBand, ListBand, StatusBand })
        {
            band.ColumnDefinitions[0].Width = new(twoPanels ? ratio : 1, GridUnitType.Star);
            band.ColumnDefinitions[1].Width = new(twoPanels ? 8 : 0);
            // A collapsed column must be pixel-zero: Star(0) can still be
            // measured with an unconstrained child during a scale change.
            band.ColumnDefinitions[2].Width = twoPanels ? new(1 - ratio, GridUnitType.Star) : new(0);
        }
    }

    private bool ActivatePanel(PanelState pane)
    {
        if (closing || busy || configuring) return false;
        if (pane == activePanel) return true;
        if (!twoPanels) return false;
        CancelRename();
        activePanel = pane;
        UpdatePanelIndicators();
        UpdateStatus();
        return true;
    }

    private void UpdatePanelIndicators()
    {
        foreach (var pane in panels)
        {
            pane.ActiveLine.Visibility = twoPanels && pane == activePanel ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetLiveSetting(pane.Status, pane == activePanel ? AutomationLiveSetting.Polite : AutomationLiveSetting.Off);
        }
    }

    private void UpdatePanelStatus(PanelState pane)
    {
        if (pane.List == null) return;
        decimal size = 0;
        foreach (FileEntry entry in pane.List.SelectedItems) size += entry.Size ?? 0;
        string selection = $"Выбрано: {pane.List.SelectedItems.Count:N0}";
        if (size > 0) selection += $" ({size:N0} Б)";
        pane.Status.Text = $"Элементов: {pane.Entries.Count:N0}   |   {selection}" + (pane.ArchiveSession != null ? "   |   Архив" : "");
        pane.BackButton.IsEnabled = !busy && !configuring && pane.History.Count > 0;
    }

    private async void OnTwoPanels(object sender, RoutedEventArgs e)
    {
        await SetTwoPanels(!twoPanels);
        TwoPanelsMenu.IsChecked = twoPanels;
    }

    private async Task SetTwoPanels(bool enabled)
    {
        if (enabled == twoPanels || busy || configuring || closing || renameBox != null) return;
        if (!enabled)
        {
            // Single-panel mode shows the active view. Swap the visual slots
            // without moving controls so hidden sessions and scroll offsets live on.
            SetSinglePanelSlot(activePanel);
        }
        else SetSinglePanelSlot(panels[0]);
        twoPanels = enabled;
        foreach (var pane in panels)
        {
            var visibility = enabled || pane == activePanel ? Visibility.Visible : Visibility.Collapsed;
            pane.Navigation.Visibility = pane.ListHost.Visibility = pane.StatusHost.Visibility = visibility;
        }
        panelDivider.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        ApplyPanelWidths();
        UpdatePanelIndicators();
        TwoPanelsMenu.IsChecked = twoPanels;
        if (enabled && panels[1].Directory.Length == 0 && panels[1].ArchiveSession == null)
        {
            string path = archiveSession is { } session ? Path.GetDirectoryName(session.RootPath)! : directory;
            if (path.Length > 0) await NavigateDirectory(panels[1], path, remember: false);
        }
        if (!closing) FocusFileList();
    }

    private void SetSinglePanelSlot(PanelState first)
    {
        foreach (var pane in panels)
        {
            int column = pane == first ? 0 : 2;
            Grid.SetColumn(pane.Navigation, column);
            Grid.SetColumn(pane.ListHost, column);
            Grid.SetColumn(pane.StatusHost, column);
        }
    }

    private bool FocusInActiveList()
    {
        for (var element = FocusManager.GetFocusedElement(Root.XamlRoot) as DependencyObject;
            element != null; element = VisualTreeHelper.GetParent(element))
            if (ReferenceEquals(element, list)) return true;
        return false;
    }
}
