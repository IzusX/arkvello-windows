using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security;
using Arkvello.Models;
using Arkvello.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI.ViewManagement;

namespace Arkvello;

public sealed partial class MainWindow : Window
{
    private ArchiveSession? archiveSession { get => activePanel.ArchiveSession; set => activePanel.ArchiveSession = value; }
    private NativeArchive? archive => archiveSession?.Archive;
    private IReadOnlyList<ArchiveEntry> archiveEntries => archiveSession?.Entries ?? [];
    private string directory => activePanel.Directory;
    private string archivePrefix { get => activePanel.ArchivePrefix; set => activePanel.ArchivePrefix = value; }
    private string archivePath => archiveSession?.DisplayPath ?? "";
    private sealed record NavigationLocation(string Path, string Prefix, ArchiveSession? Session);
    private NavigationLocation CurrentLocation => activePanel.Location;
    private Stack<NavigationLocation> history => activePanel.History;
    private List<FileEntry> entries { get => activePanel.Entries; set => activePanel.Entries = value; }
    private ListViewBase list => activePanel.List;
    private List<Column> columns => activePanel.Columns;
    private string view { get => activePanel.View; set => activePanel.View = value; }
    private int scalePercent = InterfaceScale.Load();
    private bool busy, closing;
    private bool configuring;
    private bool operationCancelled;
    private Thread? operationThread;
    private readonly object workerSync = new();
    private bool backgroundRequested;
    private ContentDialog? currentDialog;
    private readonly ToggleButton backgroundButton = new() { Content = "Снизить приоритет" };
    private ArchiveJob? activeJob;
    private ContentDialog? progressDialog, promptDialog;
    private Task<ContentDialogResult>? progressShown;
    private bool allowProgressClose;
    private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 100, IsIndeterminate = true };
    private readonly TextBlock progressPath = new() { TextWrapping = TextWrapping.Wrap, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock progressState = new() { Text = "Подготовка…", TextWrapping = TextWrapping.Wrap };
    private readonly Button pauseButton = new() { Content = "Пауза" };
    private readonly Button cancelButton = new() { Content = "Отмена" };
    private readonly object progressSync = new();
    private ArchiveEvent? latestProgress, latestPath;
    private readonly DispatcherTimer progressTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private bool pauseRequested;
    private readonly UISettings uiSettings = new();
    private readonly AccessibilitySettings accessibility = new();
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, uint attribute, ref int value, uint size);
    private sealed record Column(string Label, string Property, double Width);

    public MainWindow()
    {
        InitializeComponent();
        InitializePanels();
        windowPreferences = ReadWindowPreferences();
        RestoreWindowPreferences(windowPreferences);
        foreach (int percent in InterfaceScale.Percentages)
        {
            var item = new ToggleMenuFlyoutItem { Text = percent + "%" + (percent == 100 ? " (по умолчанию)" : ""), Tag = percent, IsChecked = percent == scalePercent };
            item.Click += OnScale;
            ScaleMenu.Items.Add(item);
        }
        Title = "Arkvello — техническая сборка";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1180, 780));
        Root.ActualThemeChanged += (_, _) => RefreshColors();
        uiSettings.ColorValuesChanged += (_, _) => DispatcherQueue.TryEnqueue(RefreshColors);
        uiSettings.TextScaleFactorChanged += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            foreach (var pane in panels) BuildList(pane);
            ApplyInterfaceScale();
        });
        // XAML theme resources and UISettings already track system colors.
        // AccessibilitySettings.HighContrastChanged cannot be subscribed by
        // every unpackaged host (ERROR_NOT_FOUND on this machine).
        AppWindow.Closing += (_, e) =>
        {
            if (fileDragInProgress || shellDrag?.IsDropping == true) e.Cancel = true;
            else if (busy) { e.Cancel = true; closing = true; CancelOperation(); }
            else if (configuring && !closing) { e.Cancel = true; closing = true; currentDialog?.Hide(); }
        };
        Closed += (_, _) =>
        {
            closing = true;
            shellDrag?.Dispose();
            SaveWindowPreferences();
            folderRefreshTimer.Stop();
            foreach (var pane in panels) pane.Watch?.Dispose();
            shellIcons.Dispose();
            foreach (var pane in panels) CloseArchives(pane);
        };
        backgroundButton.Click += (_, _) =>
        {
            lock (workerSync)
            {
                backgroundRequested = backgroundButton.IsChecked == true;
                if (operationThread is { } worker)
                    worker.Priority = backgroundRequested ? ThreadPriority.BelowNormal : ThreadPriority.Normal;
            }
        };
        ToolTipService.SetToolTip(backgroundButton, "Снижает приоритет основного потока операции. Дополнительные потоки кодека могут сохранить обычный приоритет.");
        pauseButton.Click += (_, _) =>
        {
            pauseRequested = !pauseRequested;
            activeJob?.Pause(pauseRequested);
            pauseButton.Content = pauseRequested ? "Продолжить" : "Пауза";
            progressState.Text = pauseRequested ? "Ожидание паузы…" : "Выполняется…";
        };
        cancelButton.Click += (_, _) => CancelOperation();
        progressTimer.Tick += (_, _) => FlushProgress();
        folderRefreshTimer.Tick += OnFolderRefreshTick;
        foreach (var pane in panels) BuildList(pane);
        RefreshColors();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyInterfaceScale();
        string[] args = Environment.GetCommandLineArgs();
        allowFolderWatches = !args.Any(arg => arg is "--verify-stage3" or "--verify-stage4" or "--measure-list" or "--verify-preferences");
        var xamlRoot = Root.XamlRoot;
        double scale = xamlRoot.RasterizationScale;
        RestoreWindowBounds(scale);
        initialWindowPreferences = System.Text.Json.JsonSerializer.Serialize(CaptureWindowPreferences(windowPreferences.TwoPanels));
        xamlRoot.Changed += (_, _) =>
        {
            if (scale == xamlRoot.RasterizationScale) return;
            scale = xamlRoot.RasterizationScale;
            foreach (var pane in panels) pane.RefreshIcons?.Invoke();
        };
        int open = Array.IndexOf(args, "--open");
        // Explorer launches an unpackaged app with the selected path as argv[1].
        string? startupPath = open >= 0 && args.Length > open + 1 ? args[open + 1] :
            args.Length > 1 && !args[1].StartsWith('-') ? args[1] : null;
        await Navigate(startupPath ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), remember: false);
        if (closing) return;
        if (windowPreferences.TwoPanels) await SetTwoPanels(true);
        windowReady = !closing;
        if (NativeDependencies.Check(AppContext.BaseDirectory) is { } dependencyError) ShowError(dependencyError);
        // Explicit diagnostic mode never runs in ordinary application sessions.
        InitializeShellDrag();
        if (args.Contains("--measure-list")) await MeasureList();
        int verify = Array.IndexOf(args, "--verify-stage3");
        if (verify >= 0 && args.Length > verify + 1) await VerifyStage3(args[verify + 1]);
        int files = Array.IndexOf(args, "--verify-stage4");
        if (files >= 0 && args.Length > files + 1) await VerifyStage4(args[files + 1]);
        int preferences = Array.IndexOf(args, "--verify-preferences");
        if (preferences >= 0 && args.Length > preferences + 2) await VerifyWindowPreferences(args[preferences + 1], args[preferences + 2]);
    }

    private void RefreshColors()
    {
        bool dark = Root.ActualTheme == ElementTheme.Dark;
        int darkCaption = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(WinRT.Interop.WindowNative.GetWindowHandle(this), 20, ref darkCaption, 4);
        if (accessibility.HighContrast)
        {
            Root.Background = new SolidColorBrush(uiSettings.GetColorValue(UIColorType.Background));
        }
        else Root.Background = new SolidColorBrush(Colors.Transparent);
        // The menu and navigation reveal the same window Mica backdrop. Let
        // Windows own wallpaper sampling, activation and transparency fallback.
        // File content remains opaque for stable text/selection contrast.
        var listBrush = accessibility.HighContrast ? Root.Background : new SolidColorBrush(dark
            ? Windows.UI.Color.FromArgb(255, 25, 25, 25) : Colors.White);
        foreach (var pane in panels) pane.ListHost.Background = listBrush;
    }

    private static string Escape(string value) => SecurityElement.Escape(value) ?? "";
    private static string ItemIcon(int size, bool useShell)
    {
        string folder = size > 24 ? Controls.FluentItemPaths.FolderLarge : Controls.FluentItemPaths.Folder;
        string file = size > 24 ? Controls.FluentItemPaths.FileLarge : Controls.FluentItemPaths.File;
        int units = size > 24 ? 48 : 20;
        return "<Grid Width='" + size + "' Height='" + size + "'><Grid x:Name='FallbackIcon'>" +
            "<Viewbox Visibility='{Binding FolderVisibility}'><PathIcon Data='" + folder + "' Width='" + units + "' Height='" + units + "' Foreground='{ThemeResource FolderIconBrush}' AutomationProperties.AccessibilityView='Raw'/></Viewbox>" +
            "<Viewbox Visibility='{Binding FileVisibility}'><PathIcon Data='" + file + "' Width='" + units + "' Height='" + units + "' AutomationProperties.AccessibilityView='Raw'/></Viewbox></Grid>" +
            (useShell ? "<Image x:Name='ShellIcon' Visibility='Collapsed' IsHitTestVisible='False' AutomationProperties.AccessibilityView='Raw'/>" : "") + "</Grid>";
    }
    private void BuildList(PanelState? targetPanel = null, bool preserveViewport = false)
    {
        var pane = targetPanel ?? activePanel;
        var oldScroll = preserveViewport && pane.List != null ? VisualChild<ScrollViewer>(pane.List) : null;
        double? oldLeft = oldScroll?.HorizontalOffset, oldTop = oldScroll?.VerticalOffset;
        bool restoreFocus = pane == activePanel && pane.List != null && FocusInActiveList();
        string? focused = restoreFocus ? CurrentItem()?.Identity : pane.Current?.Identity;
        if (pane == activePanel) CancelRename();
        var selected = pane.List?.SelectedItems.Cast<FileEntry>().Select(e => e.Identity).ToHashSet() ?? [];
        pane.ListHost.Children.Clear();
        var columns = pane.Columns.Where(column => ColumnInView(pane, column)).ToList();
        string view = pane.View;
        var entries = pane.Entries;
        var layout = new Grid();
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        double width = columns.Sum(c => c.Width);
        bool details = view == "Details";
        ScrollViewer? headerScroll = null;
        if (details)
        {
            var header = new StackPanel { Orientation = Orientation.Horizontal, Height = Math.Max(32, 20 * uiSettings.TextScaleFactor + 12), Width = width, HorizontalAlignment = HorizontalAlignment.Left };
            for (int i = 0; i < columns.Count; i++)
            {
                int position = i; var col = columns[i];
                int backing = pane.Columns.IndexOf(col);
                void ResizeColumn(double width) => pane.Columns[backing] = col with { Width = width };
                void SwapColumn(int index)
                {
                    int other = pane.Columns.IndexOf(columns[index]);
                    (pane.Columns[backing], pane.Columns[other]) = (pane.Columns[other], pane.Columns[backing]);
                }
                var cell = new Grid { Width = col.Width, AllowDrop = true, CanDrag = true, Tag = col.Property };
                var button = new Button { Content = col.Label + (pane.Sort == col.Property ? pane.Descending ? " ↓" : " ↑" : ""), Padding = new(16, 0, 8, 0), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new(0), CornerRadius = new(0) };
                button.Click += (_, _) =>
                {
                    if (!ActivatePanel(pane)) return;
                    pane.Descending = pane.Sort == col.Property && !pane.Descending;
                    pane.Sort = col.Property;
                    ApplySort(pane); BuildList(pane); FocusHeader(col.Property);
                };
                var columnMenu = new MenuFlyout();
                var resize = new MenuFlyoutItem { Text = "Ширина столбца…" };
                resize.Click += async (_, _) =>
                {
                    if (!ActivatePanel(pane)) return;
                    var value = new NumberBox { Value = pane.Columns[backing].Width, Minimum = 90, Maximum = 4096, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
                    AutomationProperties.SetName(value, "Ширина в единицах интерфейса");
                    configuring = true;
                    try { if (await NewDialog("Ширина: " + col.Label, value, "Применить", "Отмена").ShowAsync() == ContentDialogResult.Primary && double.IsFinite(value.Value)) { ResizeColumn(Math.Clamp(value.Value, 90, 4096)); BuildList(pane); } }
                    finally { configuring = false; }
                };
                var left = new MenuFlyoutItem { Text = "Переместить левее", IsEnabled = position > 0 };
                left.Click += (_, _) => { if (ActivatePanel(pane)) { SwapColumn(position - 1); BuildList(pane); } };
                var right = new MenuFlyoutItem { Text = "Переместить правее", IsEnabled = position < columns.Count - 1 };
                right.Click += (_, _) => { if (ActivatePanel(pane)) { SwapColumn(position + 1); BuildList(pane); } };
                columnMenu.Items.Add(resize); columnMenu.Items.Add(left); columnMenu.Items.Add(right); button.ContextFlyout = columnMenu;
                var configure = new MenuFlyoutItem { Text = "Столбцы…" };
                configure.Click += (sender, e) => { if (ActivatePanel(pane)) OnColumns(sender, e); };
                columnMenu.Items.Add(configure);
                button.Tag = col.Property;
                AutomationProperties.SetAutomationId(button, "Column-" + col.Property);
                button.PreviewKeyDown += (_, e) =>
                {
                    bool control = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
                    if (!control || e.Key is not (VirtualKey.Left or VirtualKey.Right) || !ActivatePanel(pane)) return;
                    e.Handled = true;
                    bool shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
                    int delta = e.Key == VirtualKey.Left ? -1 : 1;
                    if (shift && position + delta >= 0 && position + delta < columns.Count)
                        SwapColumn(position + delta);
                    else if (!shift) ResizeColumn(Math.Clamp(col.Width + delta * 16, 90, 4096));
                    BuildList(pane); FocusHeader(col.Property);
                };
                AutomationProperties.SetHelpText(button, "Enter — сортировка. Ctrl и стрелки — ширина; Ctrl+Shift и стрелки — порядок. Контекстное меню — точная ширина.");
                var thumb = new Thumb { Width = 6, HorizontalAlignment = HorizontalAlignment.Right, Background = new SolidColorBrush(Colors.Transparent) };
                AutomationProperties.SetName(thumb, "Ширина столбца «" + col.Label + "»");
                thumb.DragDelta += (_, e) =>
                {
                    if (!ActivatePanel(pane)) return;
                    double w = Math.Clamp(pane.Columns[backing].Width + e.HorizontalChange, 90, 4096);
                    ResizeColumn(w);
                    cell.Width = w;
                };
                thumb.DragCompleted += (_, _) => { if (ActivatePanel(pane)) BuildList(pane); };
                cell.DragStarting += (_, e) => { e.Data.SetText(col.Property); e.AllowedOperations = DataPackageOperation.Move; };
                cell.DragOver += (_, e) => { e.AcceptedOperation = DataPackageOperation.Move; e.Handled = true; };
                cell.Drop += async (_, e) =>
                {
                    if (!ActivatePanel(pane) || !e.DataView.Contains(StandardDataFormats.Text)) return;
                    string key = await e.DataView.GetTextAsync();
                    int old = pane.Columns.FindIndex(c => c.Property == key && ColumnInView(pane, c));
                    if (!ActivatePanel(pane) || old < 0 || old == backing || backing >= pane.Columns.Count) return;
                    var moving = pane.Columns[old]; pane.Columns.RemoveAt(old); pane.Columns.Insert(backing, moving); BuildList(pane);
                };
                cell.Children.Add(button); cell.Children.Add(thumb); header.Children.Add(cell);
            }
            headerScroll = new ScrollViewer { Content = header, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollMode = ScrollMode.Disabled, ZoomMode = ZoomMode.Disabled };
            layout.Children.Add(headerScroll);
        }
        ListViewBase list = view is "SmallIcons" or "LargeIcons" ? new GridView() : new ListView();
        pane.List = list;
        pane.Current = focused == null ? null : entries.FirstOrDefault(e => e.Identity == focused);
        list.SelectionMode = pane.MarkMode ? ListViewSelectionMode.Multiple : ListViewSelectionMode.Extended;
        list.IsMultiSelectCheckBoxEnabled = false;
        list.IsItemClickEnabled = false;
        list.ItemsSource = entries;
        list.Background = new SolidColorBrush(Colors.Transparent);
        list.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        double rowHeight = Math.Max(28, 20 * uiSettings.TextScaleFactor + 8);
        if (list is ListView)
        {
            var style = new Style { TargetType = typeof(ListViewItem) };
            style.Setters.Add(new Setter(FrameworkElement.HeightProperty, rowHeight));
            style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, rowHeight));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(0)));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            list.ItemContainerStyle = style;
        }
        ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollMode(list, ScrollMode.Enabled);
        ScrollViewer.SetHorizontalScrollBarVisibility(list, details ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled);
        ScrollViewer.SetHorizontalScrollMode(list, details ? ScrollMode.Enabled : ScrollMode.Disabled);
        ScrollViewer.SetZoomMode(list, ZoomMode.Disabled);
        // The native list owns both axes, so its vertical bar stays at the
        // viewport edge even when columns extend beyond the window width.
        list.Loaded += (_, _) =>
        {
            if (VisualChild<ScrollViewer>(list) is not { } scroll) return;
            ReserveScrollBarGutters(scroll);
            if (headerScroll == null) return;
            void SyncHeader()
            {
                double gutter = VisualChild<ScrollContentPresenter>(scroll) is { } presenter ? Math.Max(0, scroll.ActualWidth - presenter.ActualWidth) : 0;
                headerScroll.Margin = new(0, 0, gutter, 0);
                headerScroll.ChangeView(scroll.HorizontalOffset, null, null, true);
            }
            scroll.ViewChanged += (_, _) => SyncHeader();
            scroll.SizeChanged += (_, _) => SyncHeader();
            headerScroll.ViewChanged += (_, _) => { if (Math.Abs(scroll.HorizontalOffset - headerScroll.HorizontalOffset) > .5) scroll.ChangeView(headerScroll.HorizontalOffset, null, null, true); };
            SyncHeader();
        };
        list.DoubleTapped += async (_, e) =>
        {
            if (!ReferenceEquals(pane.List, list) || !ActivatePanel(pane)) return;
            for (var source = e.OriginalSource as DependencyObject; source != null && !ReferenceEquals(source, list); source = VisualTreeHelper.GetParent(source))
                if (source is SelectorItem container && list.ItemFromContainer(container) is FileEntry item)
                { await OpenItem(item); return; }
        };
        // ListViewItem consumes Enter in its native KeyDown handling. Handle
        // navigation in the preview phase so one Enter opens the focused item.
        FileEntry? pendingOpen = null;
        list.PreviewKeyDown += async (_, e) =>
        {
            if (this.list != list || busy || configuring || renameBox != null) return;
            if (e.Key == VirtualKey.Enter)
            {
                FileEntry? focused = null;
                // A focused container need not be selected or have the item as
                // its DataContext. Resolve the routed source through its owner.
                for (var source = e.OriginalSource as DependencyObject; source != null && !ReferenceEquals(source, list); source = VisualTreeHelper.GetParent(source))
                    if (source is SelectorItem container && list.ItemFromContainer(container) is FileEntry entry) { focused = entry; break; }
                if ((focused ?? list.SelectedItem as FileEntry) is { } item) { e.Handled = true; pendingOpen = item; }
            }
            if (e.Key == VirtualKey.Back) { e.Handled = true; await Parent(); }
        };
        list.PreviewKeyUp += async (_, e) =>
        {
            if (e.Key != VirtualKey.Enter || pendingOpen == null) return;
            e.Handled = true;
            var item = pendingOpen; pendingOpen = null;
            // Finish the key gesture before replacing its focused control or
            // opening a dialog; the release must not activate a new surface.
            if (!busy && !configuring && ReferenceEquals(this.list, list)) await OpenItem(item);
        };
        AutomationProperties.SetName(list, pane.Label + ": файлы");
        AutomationProperties.SetAutomationId(list, pane.Index == 0 ? "FileList" : "RightFileList");
        ConfigureContextMenu(pane, list);
        ConfigureSelectionInput(pane, list);
        ConfigureFileInput(list);
        string template;
        bool useShell = pane.ArchiveSession == null;
        int iconSize = view switch { "LargeIcons" => 36, "SmallIcons" => 20, _ => 18 };
        string icon = ItemIcon(iconSize, useShell);
        if (details)
        {
            template = "<Grid Height='" + rowHeight.ToString(CultureInfo.InvariantCulture) + "' MinWidth='" + width.ToString(CultureInfo.InvariantCulture) + "' AutomationProperties.Name='{Binding AccessibleName}'><Grid.ColumnDefinitions>" + string.Join("", columns.Select(c => "<ColumnDefinition Width='" + c.Width.ToString(CultureInfo.InvariantCulture) + "'/>")) + "</Grid.ColumnDefinitions>";
            for (int i = 0; i < columns.Count; i++)
            {
                var c = columns[i];
                string binding = c.Property.StartsWith("Properties[", StringComparison.Ordinal)
                    ? "Converter={StaticResource ArchivePropertyConverter}, ConverterParameter=" + c.Property[11..^1] : Escape(c.Property);
                char time = c.Property switch { "ModifiedText" => 'M', "Created" => 'C', "Accessed" => 'A', _ => '\0' };
                if (time != '\0') binding = $"Converter={{StaticResource FileTimeConverter}}, ConverterParameter={time}{pane.TimePrecision}{(pane.TimeUtc ? 'U' : 'L')}";
                string text = "<TextBlock Text='{Binding " + binding + "}' FontSize='14' VerticalAlignment='Center' TextTrimming='CharacterEllipsis' Margin='16,0,12,0'" + (c.Property == "SizeText" ? " TextAlignment='Right'" : "") + "/>";
                template += c.Property == "Name"
                    ? "<Grid Grid.Column='" + i + "' Margin='16,0,0,0'><Grid.ColumnDefinitions><ColumnDefinition Width='26'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>" + icon + "<TextBlock Tag='FileName' Grid.Column='1' Text='{Binding Name}' FontSize='14' VerticalAlignment='Center' TextTrimming='CharacterEllipsis' Margin='0,0,12,0'/></Grid>"
                    : "<Grid Grid.Column='" + i + "'>" + text + "</Grid>";
            }
            template += "</Grid>";
        }
        else if (view == "List") template = "<StackPanel Orientation='Horizontal' Height='" + rowHeight.ToString(CultureInfo.InvariantCulture) + "' Spacing='8' Margin='16,0'>" + icon + "<TextBlock Tag='FileName' Text='{Binding Name}' VerticalAlignment='Center'/></StackPanel>";
        else
        {
            bool large = view == "LargeIcons";
            double lineHeight = 20 * uiSettings.TextScaleFactor;
            string line = lineHeight.ToString(CultureInfo.InvariantCulture), textHeight = (2 * lineHeight).ToString(CultureInfo.InvariantCulture);
            double tileHeight = large ? 64 + 2 * lineHeight : Math.Max(64, 16 + 2 * lineHeight);
            // Uniform tile dimensions must include two lines even when the first
            // realized filename fits on one. A finite Grid slot also constrains
            // horizontal measurement in the small-icon view.
            var tileStyle = new Style { TargetType = typeof(GridViewItem) };
            tileStyle.Setters.Add(new Setter(FrameworkElement.WidthProperty, large ? 140d : 220d));
            tileStyle.Setters.Add(new Setter(FrameworkElement.HeightProperty, tileHeight));
            tileStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            tileStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            tileStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Stretch));
            list.ItemContainerStyle = tileStyle;
            string label = "<TextBlock Tag='FileName' Text='{Binding Name}' TextTrimming='CharacterEllipsis' MaxLines='2' TextWrapping='Wrap' FontSize='14' LineHeight='" + line + "' LineStackingStrategy='BlockLineHeight' MaxHeight='" + textHeight + "' VerticalAlignment='Center'";
            template = large
                ? "<Grid Width='140' Height='" + tileHeight.ToString(CultureInfo.InvariantCulture) + "' Padding='8' RowSpacing='8' ToolTipService.ToolTip='{Binding Name}' AutomationProperties.Name='{Binding AccessibleName}'><Grid.RowDefinitions><RowDefinition Height='40'/><RowDefinition Height='" + textHeight + "'/></Grid.RowDefinitions>" + icon + "<Grid Grid.Row='1'>" + label + " TextAlignment='Center'/></Grid></Grid>"
                : "<Grid Width='220' Height='" + tileHeight.ToString(CultureInfo.InvariantCulture) + "' Padding='8' ColumnSpacing='8' ToolTipService.ToolTip='{Binding Name}' AutomationProperties.Name='{Binding AccessibleName}'><Grid.ColumnDefinitions><ColumnDefinition Width='24'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>" + icon + "<Grid Grid.Column='1'>" + label + "/></Grid></Grid>";
        }
        list.ItemTemplate = (DataTemplate)XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>" + template + "</DataTemplate>");
        pane.RefreshIcons = null;
        if (useShell) ConfigureShellIcons(pane, list, iconSize);
        Grid.SetRow(list, 1); layout.Children.Add(list);
        ConfigureFileGestures(pane, list, layout);
        pane.ListHost.Children.Add(layout);
        if (selected.Count > 0) ApplySelectionRanges(list, entries, item => selected.Contains(item.Identity) ? true : null);
        list.SelectionChanged += (_, _) =>
        {
            if (!ReferenceEquals(pane.List, list) || pane.UpdatingSelection) return;
            UpdatePanelStatus(pane);
            if (pane == activePanel) UpdateFileCommands();
        };
        if (restoreFocus && pane.Current is { } restore)
        {
            var target = list;
            target.Loaded += (_, _) => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (!ReferenceEquals(target, this.list) || busy || closing) return;
                target.ScrollIntoView(restore); target.UpdateLayout();
                if (target.ContainerFromItem(restore) is Control container) container.Focus(FocusState.Programmatic);
            });
        }
        if (oldTop != null)
        {
            list.Loaded += (_, _) => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (ReferenceEquals(pane.List, list) && VisualChild<ScrollViewer>(list) is { } scroll)
                    scroll.ChangeView(oldLeft, oldTop, null, true);
            });
        }
        UpdateStatus();
    }

    private void FocusHeader(string property)
    {
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            void Find(DependencyObject parent)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                {
                    var child = VisualTreeHelper.GetChild(parent, i);
                    if (child is Button button && Equals(button.Tag, property)) { button.Focus(FocusState.Keyboard); return; }
                    Find(child);
                }
            }
            Find(ListHost);
        });
    }

    private static T? VisualChild<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (VisualChild<T>(child) is { } descendant) return descendant;
        }
        return null;
    }

    private static void ReserveScrollBarGutters(ScrollViewer scroll)
    {
        // WinUI 2.6.5's presenter spans the scrollbar cells by default. Retain
        // the native scrolling template, but reserve its opaque gutter cells.
        // Revalidate template parts alongside FileCommandBar on SDK upgrades.
        if (VisualChild<ScrollContentPresenter>(scroll) is { } presenter)
        {
            Grid.SetRowSpan(presenter, 1);
            Grid.SetColumnSpan(presenter, 1);
        }
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e) => ApplyInterfaceScale();
    private void ApplyInterfaceScale()
    {
        if (Root == null || WindowHost.ActualWidth <= 0 || WindowHost.ActualHeight <= 0) return;
        // Give virtualizing lists a finite logical viewport before Viewbox
        // scales it. Resizing never changes the user's chosen percentage.
        Root.Width = WindowHost.ActualWidth * 100 / scalePercent;
        Root.Height = WindowHost.ActualHeight * 100 / scalePercent;
    }
    private void OnScale(object sender, RoutedEventArgs e)
    {
        if (!busy && !configuring)
        {
            scalePercent = (int)((ToggleMenuFlyoutItem)sender).Tag;
            ApplyInterfaceScale();
            foreach (var pane in panels) pane.RefreshIcons?.Invoke();
            if (!InterfaceScale.TrySave(scalePercent)) ShowError("Масштаб применён, но сохранить его не удалось. Проверьте доступ к папке настроек Arkvello.");
        }
        foreach (ToggleMenuFlyoutItem item in ScaleMenu.Items) item.IsChecked = (int)item.Tag == scalePercent;
    }

    private void ApplySort(PanelState? target = null)
    {
        var pane = target ?? activePanel;
        string sort = pane.Sort;
        if (sort == "None")
        {
            pane.Entries = pane.Entries.OrderBy(entry => entry.SourceOrder).ToList();
            return;
        }
        if (!pane.Columns.Any(column => column.Property == sort && ColumnInView(pane, column))) sort = "Name";
        Func<FileEntry, object?> key = sort switch
        {
            "SizeText" => e => e.Size,
            "ModifiedText" => e => e.Modified,
            "Created" => e => e.Created,
            "Accessed" => e => e.Accessed,
            "AttributesText" => e => (int)e.Attributes,
            "TypeText" => e => e.TypeText,
            _ => e => e.Name
        };
        if (sort.StartsWith("Properties[", StringComparison.Ordinal)) key = e => e.Properties?.GetValueOrDefault(sort[11..^1])?.SortKey;
        var ordered = pane.Entries.OrderByDescending(e => e.IsDirectory);
        pane.Entries = (pane.Descending ? ordered.ThenByDescending(key) : ordered.ThenBy(key)).ToList();
    }
    private void UpdateStatus()
    {
        foreach (var pane in panels) UpdatePanelStatus(pane);
        ExtractButton.IsEnabled = TestButton.IsEnabled = !busy && archive != null;
        MarkModeMenu.IsChecked = markMode;
        UpdateTimeMenu();
        UpdateFileCommands();
    }
    private string Location => archive != null ? archivePath + " | " + archivePrefix : directory;

    private async Task Navigate(string path, bool remember = true)
    {
        if (busy || configuring || renameBox != null) return;
        path = path.Trim().Trim('"');
        string archiveAddress = archivePath + " | ";
        if (archiveSession != null && (path.StartsWith(archiveAddress, StringComparison.Ordinal) || path == archiveAddress.TrimEnd()))
        {
            string prefix = path.Length < archiveAddress.Length ? "" : path[archiveAddress.Length..].Replace('\\', '/').Trim('/');
            if (prefix.Length > 0) prefix += "/";
            if (prefix.Length == 0 || archiveEntries.Any(entry =>
            {
                string item = entry.Path.Replace('\\', '/');
                return item.StartsWith(prefix, StringComparison.Ordinal) ||
                    (entry.IsDirectory && item.AsSpan().SequenceEqual(prefix.AsSpan(0, prefix.Length - 1)));
            }))
            {
                if (remember && archivePrefix != prefix) history.Push(CurrentLocation);
                archivePrefix = prefix;
                Notice.IsOpen = false;
                ShowArchiveFolder();
                return;
            }
            ShowError("Папка внутри открытого архива не найдена.");
            return;
        }
        string basePath = archiveSession is { } session ? Path.GetDirectoryName(session.RootPath)! : directory;
        try
        {
            if (path.Length > 0 && basePath.Length > 0 && !Path.IsPathFullyQualified(path)) path = Path.GetFullPath(path, basePath);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException) { ShowError(error.Message); return; }
        var pane = activePanel;
        busy = true; UpdateStatus();
        var kind = await Task.Run(() => Directory.Exists(path) ? 1 : File.Exists(path) ? 2 : 0);
        busy = false;
        if (closing) { Close(); return; }
        if (kind == 1) await NavigateDirectory(pane, path, remember);
        else if (kind == 2) await OpenArchive(path, remember);
        else ShowError("Путь не найден. Проверьте путь и повторите переход.");
        if (!closing) UpdateStatus();
    }

    private async Task NavigateDirectory(PanelState pane, string path, bool remember, bool refresh = false)
    {
        busy = true; UpdateStatus(); pane.Status.Text = "Загрузка…";
        try
        {
            string fullPath = Path.GetFullPath(path);
            var loaded = await Task.Run(() => new DirectoryInfo(fullPath).EnumerateFileSystemInfos().Select((info, index) =>
            {
                FileAttributes attributes = info.Attributes;
                bool dir = (attributes & FileAttributes.Directory) != 0;
                return new FileEntry(info.FullName, info.Name, info.FullName, dir, dir ? null : (ulong)((FileInfo)info).Length,
                    info.LastWriteTimeUtc, Attributes: attributes, Created: info.CreationTimeUtc, Accessed: info.LastAccessTimeUtc, SourceOrder: index);
            }).ToList());
            if (closing) return;
            if (remember && pane.Location.Path.Length > 0 && (pane.ArchiveSession != null || pane.Directory != fullPath))
                pane.History.Push(pane.Location);
            if (remember) RememberPath(recentPaths, fullPath);
            CloseArchives(pane); pane.ArchivePrefix = "";
            ClearShellIconCache();
            bool wasUnavailable = !pane.DirectoryAvailable;
            pane.Directory = fullPath; pane.DirectoryAvailable = true;
            pane.Entries = loaded; ApplySort(pane); BuildList(pane, preserveViewport: refresh);
            UpdateFolderWatch(pane);
            if (!refresh)
            {
                pane.Address.Text = fullPath;
                Notice.IsOpen = false;
            }
            else if (wasUnavailable) Notice.IsOpen = false;
        }
        catch (Exception error)
        {
            if (refresh)
            {
                pane.DirectoryAvailable = false;
                UpdateFolderWatch(pane);
            }
            ShowError(twoPanels ? $"{pane.Label}. {error.Message}" : error.Message);
            if (!refresh) pane.Address.Text = pane == activePanel ? Location : pane.Directory;
        }
        finally { busy = false; UpdateStatus(); if (closing) Close(); }
    }

    private async Task<ArchiveResult?> OpenArchive(string path, bool remember, bool quietNotArchive = false, bool captureSource = false)
    {
        ArchiveSession? candidate = null;
        var selectedProperties = SelectedProperties;
        var loadedProperties = new Dictionary<uint, IReadOnlyDictionary<uint, ArchiveProperty>>();
        var result = await RunOperation("Открытие архива", job =>
        {
            var opened = ArchiveSession.Open(path, job, captureSource);
            candidate = opened.Session;
            return candidate != null && opened.Result.Succeeded ? LoadSessionColumns(candidate, selectedProperties, job, loadedProperties) : opened.Result;
        }, showSuccess: false, quietNotArchive: quietNotArchive, delayProgress: true);
        if (!closing && result?.HResult == 0 && candidate != null)
        {
            if (remember && Location.Length > 0 &&
                (archiveSession?.Parent != null || archiveSession?.RootPath != candidate.RootPath || archivePrefix.Length > 0))
                history.Push(CurrentLocation);
            if (remember) RememberPath(recentPaths, candidate.RootPath);
            CloseArchives(); archiveSession = candidate; archivePrefix = "";
            UpdateFolderWatch(activePanel);
            RetainSessionColumns(candidate, loadedProperties);
            ShowArchiveFolder();
        }
        else candidate?.Dispose();
        UpdateStatus();
        return result;
    }

    private void CloseArchives(PanelState? target = null)
    {
        var pane = target ?? activePanel;
        while (pane.ArchiveSession != null)
        {
            var parent = pane.ArchiveSession.Parent;
            pane.ArchiveSession.Dispose(); pane.ArchiveSession = parent;
        }
        pane.Properties.Clear();
    }

    private async Task OpenNestedArchive(FileEntry item)
    {
        if (archiveSession == null || item.ArchiveIndex is not { } index) return;
        var parent = archiveSession;
        string parentFolder = archivePrefix;
        ArchiveSession? candidate = null;
        var selectedProperties = SelectedProperties;
        var loadedProperties = new Dictionary<uint, IReadOnlyDictionary<uint, ArchiveProperty>>();
        var result = await RunOperation("Открытие вложенного архива", job =>
        {
            var opened = parent.OpenChild(index, job, parentFolder);
            candidate = opened.Session;
            return candidate != null && opened.Result.Succeeded ? LoadSessionColumns(candidate, selectedProperties, job, loadedProperties) : opened.Result;
        }, showSuccess: false, quietNotArchive: true, delayProgress: true);
        if (!closing && result?.HResult == 0 && candidate != null)
        {
            history.Push(CurrentLocation);
            archiveSession = candidate; archivePrefix = "";
            RetainSessionColumns(candidate, loadedProperties); ShowArchiveFolder();
        }
        else
        {
            candidate?.Dispose();
            // Keep the slice's existing ordinary-file action until the editor
            // lifecycle/commit UI is implemented. Only S_FALSE means not archive.
            if (!closing && result?.HResult == 1) await Extract(false, [item]);
        }
        UpdateStatus();
    }

    private void ShowArchiveFolder()
    {
        // Session-local indices remain authoritative, including duplicate paths.
        var children = new List<FileEntry>();
        var folders = new Dictionary<string, List<ArchiveEntry>>(StringComparer.Ordinal);
        foreach (var entry in archiveEntries)
        {
            string path = entry.Path.Replace('\\', '/');
            if (!path.StartsWith(archivePrefix, StringComparison.Ordinal)) continue;
            string tail = path[archivePrefix.Length..]; if (tail.Length == 0) continue;
            int slash = tail.IndexOf('/');
            if (slash >= 0 || entry.IsDirectory)
            {
                string folder = slash >= 0 ? tail[..slash] : tail;
                if (folder.Length == 0) continue;
                if (!folders.TryGetValue(folder, out var values)) folders.Add(folder, values = []);
                values.Add(entry);
            }
            else children.Add(new("arc:" + archiveSession!.Identity + ":" + entry.Index, tail, path, false, entry.Size, entry.Modified, entry.Index, Properties: EntryProperties(entry.Index), SourceOrder: entry.Index));
        }
        foreach (var pair in folders) children.Add(new("folder:" + archiveSession!.Identity + ":" + archivePrefix + pair.Key, pair.Key, archivePrefix + pair.Key + "/", true, null, null, DescendantIndices: pair.Value.Select(e => e.Index).ToArray(), SourceOrder: pair.Value[0].Index));
        entries = children; ApplySort(); BuildList(); Address.Text = Location;
        FocusFileList();
    }

    private void FocusFileList()
    {
        var target = list;
        void FocusLoadedList()
        {
            if (!ReferenceEquals(list, target)) return;
            target.UpdateLayout();
            var item = entries.FirstOrDefault(e => e.Identity == lastCurrentItem?.Identity) ?? target.SelectedItem ?? target.Items.FirstOrDefault();
            if (item != null) { target.ScrollIntoView(item); target.UpdateLayout(); }
            if (item != null && target.ContainerFromItem(item) is Control container) container.Focus(FocusState.Programmatic);
            else if (!target.Focus(FocusState.Programmatic)) Address.Focus(FocusState.Programmatic);
        }
        // Removing the old focused ListView can schedule WinUI's automatic
        // fallback to the menu. Restore focus after that layout/focus work.
        void QueueFocus() => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, FocusLoadedList);
        if (target.IsLoaded) QueueFocus();
        else target.Loaded += (_, _) => QueueFocus();
    }
    private async Task OpenItem(FileEntry item)
    {
        if (archive != null)
        {
            if (item.IsDirectory) { history.Push(CurrentLocation); archivePrefix = item.FullPath; ShowArchiveFolder(); }
            else await OpenNestedArchive(item);
        }
        else if (item.IsDirectory) await Navigate(item.FullPath);
        else
        {
            var result = await OpenArchive(item.FullPath, true, quietNotArchive: true);
            // Only the engine's explicit "not an archive" outcome permits the
            // normal shell open. Password/data failures must stay in Arkvello.
            if (!closing && result?.HResult == 1)
                try { using var opened = Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true, WorkingDirectory = directory }); }
                catch (Exception error) { ShowError(error.Message); }
        }
    }
    private async Task Parent()
    {
        if (archive != null && archivePrefix.Length > 0)
        {
            history.Push(CurrentLocation); string prefix = archivePrefix.TrimEnd('/'); int i = prefix.LastIndexOf('/'); archivePrefix = i < 0 ? "" : prefix[..(i + 1)]; ShowArchiveFolder();
        }
        else if (archiveSession?.Parent is { } parent)
        {
            uint? selected = archiveSession.ParentIndex;
            archivePrefix = archiveSession.ParentFolder;
            DropSessionColumns(archiveSession);
            archiveSession.Dispose(); archiveSession = parent; ShowArchiveFolder();
            while (history.TryPeek(out var previous) && ReferenceEquals(previous.Session, parent) && previous.Prefix == archivePrefix) history.Pop();
            if (selected is { } index)
            {
                var item = entries.FirstOrDefault(e => e.ArchiveIndex == index);
                if (item != null) { list.SelectedItem = item; list.ScrollIntoView(item); }
            }
            list.Focus(FocusState.Programmatic);
            UpdateStatus();
        }
        else if (archiveSession != null) await Navigate(Path.GetDirectoryName(archiveSession.RootPath)!);
        else if (directory.Length > 0 && Directory.GetParent(directory) is { } folderParent) await Navigate(folderParent.FullName);
    }

    private async Task Extract(bool test, IEnumerable<FileEntry>? explicitItems = null)
    {
        if (busy || configuring || archive == null) return;
        var items = (explicitItems ?? list.SelectedItems.Cast<FileEntry>()).ToArray();
        // Original behavior: no marked items means the current visible folder.
        if (items.Length == 0) items = entries.ToArray();
        uint[] indices = items.SelectMany(e => e.DescendantIndices ?? (e.ArchiveIndex is { } index ? [index] : Array.Empty<uint>())).Distinct().Order().ToArray();
        if (indices.Length == 0) { ShowError("В этой папке нет элементов для операции."); return; }
        string destination = "";
        if (!test)
        {
            string rootPath = archiveSession!.RootPath;
            string suggestedDestination = OtherFilesystemDirectory.Length > 0 ? OtherFilesystemDirectory :
                Path.Combine(Path.GetDirectoryName(rootPath)!, Path.GetFileNameWithoutExtension(rootPath));
            var field = new TextBox { Text = suggestedDestination };
            AutomationProperties.SetName(field, "Папка назначения");
            var browse = new Button { Content = "Обзор…" };
            browse.Click += async (_, _) => { var picker = new FolderPicker(); picker.FileTypeFilter.Add("*"); WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this)); var folder = await picker.PickSingleFolderAsync(); if (folder != null) field.Text = folder.Path; };
            var content = new StackPanel { Spacing = 12 }; content.Children.Add(new TextBlock { Text = "Извлечь в папку" }); content.Children.Add(field); content.Children.Add(DestinationActions(browse, field, extractDestinations)); content.Children.Add(new TextBlock { Text = $"Выбрано файлов и папок в архиве: {indices.Length:N0}.\nПолные пути. При совпадении имён — запрос.", TextWrapping = TextWrapping.Wrap });
            var dialog = NewDialog("Извлечение", content, "Извлечь", "Отмена");
            dialog.Opened += (_, _) => { field.Focus(FocusState.Programmatic); field.SelectAll(); };
            configuring = true;
            ContentDialogResult answer;
            try { answer = await dialog.ShowAsync(); }
            finally { configuring = false; }
            if (closing || answer != ContentDialogResult.Primary) return;
            if (!Path.IsPathFullyQualified(field.Text)) { ShowError("Укажите абсолютный путь к папке назначения."); return; }
            destination = Path.GetFullPath(field.Text);
        }
        var target = archive;
        var result = await RunOperation(test ? "Проверка архива" : "Извлечение", job => target.Extract(indices, destination, job, test));
        if (!test && result?.Succeeded == true) RememberPath(extractDestinations, destination);
        if (!test && !closing && OtherFilesystemDirectory.Length > 0)
            await NavigateDirectory(OtherPanel, OtherFilesystemDirectory, remember: false, refresh: true);
    }

    private async Task<ArchiveResult?> RunOperation(string title, Func<ArchiveJob, ArchiveResult> action, bool showSuccess = true, bool quietNotArchive = false, bool showResult = true, bool shellProgress = false, bool delayProgress = false)
    {
        if (busy) return null;
        ArchiveJob? created = null;
        try { created = new ArchiveJob(HandleEvent); created.SetMemoryLimit(MemoryPreference.Load()); }
        catch (Exception error) { created?.Dispose(); ShowError(error.Message); return null; }
        using var job = created;
        busy = true; operationCancelled = false; UpdateStatus(); Notice.IsOpen = false;
        var invoker = FocusManager.GetFocusedElement(Root.XamlRoot) as Control;
        progress.IsIndeterminate = true; progress.Value = 0; progressPath.Text = ""; progressState.Text = "Подготовка…";
        pauseRequested = false; pauseButton.Content = "Пауза"; pauseButton.IsEnabled = cancelButton.IsEnabled = true;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(pauseButton); buttons.Children.Add(cancelButton);
        backgroundButton.IsChecked = false;
        lock (workerSync) backgroundRequested = false;
        var body = new StackPanel { Spacing = 16, MaxWidth = 520 }; body.Children.Add(progressPath); body.Children.Add(progress); body.Children.Add(progressState); body.Children.Add(backgroundButton); body.Children.Add(buttons);
        progressDialog = NewDialog(title, body);
        progressDialog.Closing += (_, e) => { if (!allowProgressClose) { e.Cancel = true; CancelOperation(); } };
        activeJob = job;
        lock (progressSync) { latestProgress = latestPath = null; }
        progressTimer.Start();
        allowProgressClose = false;
        DispatcherTimer? progressDelay = null;
        if (shellProgress || delayProgress) Status.Text = title + "…";
        if (!shellProgress)
        {
            if (!delayProgress) progressShown = progressDialog.ShowAsync().AsTask();
            else
            {
                progressDelay = new() { Interval = TimeSpan.FromMilliseconds(250) };
                progressDelay.Tick += (_, _) =>
                {
                    progressDelay.Stop();
                    if (!closing && !operationCancelled && progressShown == null && promptDialog == null)
                        progressShown = progressDialog.ShowAsync().AsTask();
                };
                progressDelay.Start();
            }
        }
        ArchiveResult? result = null;
        try
        {
            result = await Task.Factory.StartNew(() =>
            {
                lock (workerSync)
                {
                    operationThread = Thread.CurrentThread;
                    operationThread.Priority = backgroundRequested ? ThreadPriority.BelowNormal : ThreadPriority.Normal;
                }
                try { return action(job); }
                finally { lock (workerSync) operationThread = null; }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            if (job.CallbackError is { } callbackError) throw new InvalidOperationException("Ошибка обработки запроса операции.", callbackError);
        }
        catch (OperationCanceledException) { result = new(unchecked((int)0x80004004), job.Issues); }
        catch (Exception error) { ShowError(error.Message); }
        finally
        {
            activeJob = null; allowProgressClose = true;
            progressDelay?.Stop();
            progressTimer.Stop(); FlushProgress();
            if (progressShown != null)
            {
                progressDialog.Hide();
                await progressShown;
            }
            progressDialog = null; progressShown = null;
            // Detach reusable controls before the next operation's content is built.
            body.Children.Clear(); buttons.Children.Clear(); busy = false; UpdateStatus();
        }
        if (closing) { Close(); return result; }
        if (showResult && result != null && (showSuccess || !result.Succeeded) && !(quietNotArchive && result.HResult == 1))
        {
            string heading = result.Cancelled ? "Операция отменена" : result.Succeeded ? "Операция завершена" : result.HResult == 0 ? "Завершено с ошибками" : "Не удалось завершить операцию";
            var lines = new List<string>();
            if (result.Cancelled) lines.Add(shellProgress ? "Часть элементов уже могла быть удалена. Проверьте исходную папку и корзину." : "Уже созданные файлы и частично записанные данные могут остаться в папке назначения. Перемещённые элементы могли покинуть исходную папку.");
            else if (result.Succeeded) lines.Add("Ошибок не обнаружено.");
            else if (result.HResult != 0) lines.Add($"Код: 0x{result.HResult:X8}. Проверьте пути, доступ и сообщения операции перед повтором.");
            foreach (var issue in result.Issues) lines.Add(IssueText(issue));
            var text = new TextBox { Text = string.Join("\n", lines), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new(0), Background = new SolidColorBrush(Colors.Transparent), MaxHeight = 320, MinWidth = 420 };
            configuring = true;
            try { await NewDialog(heading, text, close: "Закрыть").ShowAsync(); }
            finally { configuring = false; }
        }
        if (closing) { Close(); return result; }
        invoker?.Focus(FocusState.Programmatic);
        return result;
    }

    private PromptReply HandleEvent(ArchiveEvent e)
    {
        if (e.Kind is ArchiveEventKind.Item or ArchiveEventKind.Issue or ArchiveEventKind.Column or ArchiveEventKind.Property) return new();
        if (e.Kind is ArchiveEventKind.Password or ArchiveEventKind.Overwrite or ArchiveEventKind.Memory)
        {
            var completion = new TaskCompletionSource<PromptReply>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!DispatcherQueue.TryEnqueue(async () =>
            {
                try { completion.TrySetResult(await Prompt(e)); }
                catch (Exception error) { completion.TrySetException(error); }
            })) return new(-1);
            return completion.Task.GetAwaiter().GetResult();
        }
        if (e.Kind is ArchiveEventKind.Progress or ArchiveEventKind.CurrentPath)
        {
            lock (progressSync) { if (e.Kind == ArchiveEventKind.Progress) latestProgress = e; else latestPath = e; }
            return new();
        }
        DispatcherQueue.TryEnqueue(() =>
        {
            if (e.Kind == ArchiveEventKind.Paused && pauseRequested) progressState.Text = "Приостановлено";
        });
        return new();
    }

    private void FlushProgress()
    {
        ArchiveEvent? p, path;
        lock (progressSync) { p = latestProgress; path = latestPath; latestProgress = latestPath = null; }
        if (path != null) progressPath.Text = path.Path1;
        if (p == null) return;
        progress.IsIndeterminate = p.Total == 0;
        if (p.Total > 0) progress.Value = Math.Min(100, (double)p.Completed / p.Total * 100);
        if (!pauseRequested && cancelButton.IsEnabled) progressState.Text = $"Обработано: {p.Completed:N0} Б" + (p.Total > 0 ? $" из {p.Total:N0} Б" : "");
    }

    private async Task<PromptReply> Prompt(ArchiveEvent e)
    {
        if (operationCancelled || closing || activeJob == null) return new(-1);
        allowProgressClose = true; progressDialog?.Hide();
        // ContentDialog permits only one open dialog per XamlRoot.
        if (progressShown != null) await progressShown;
        if (operationCancelled || closing || activeJob == null) return new(-1);
        PromptReply response = new(-1);
        if (e.Kind == ArchiveEventKind.Password)
        {
            var password = new PasswordBox { MinWidth = 380, PasswordRevealMode = PasswordRevealMode.Peek };
            AutomationProperties.SetName(password, "Пароль архива");
            var content = new StackPanel { Spacing = 12 }; content.Children.Add(new TextBlock { Text = "Введите пароль архива.", TextWrapping = TextWrapping.Wrap }); content.Children.Add(password);
            promptDialog = NewDialog("Пароль", content, "Продолжить", "Отмена");
            promptDialog.Opened += (_, _) => password.Focus(FocusState.Programmatic);
            if (await promptDialog.ShowAsync() == ContentDialogResult.Primary) response = new(0, password.Password);
            password.Password = "";
        }
        else if (e.Kind == ArchiveEventKind.Memory)
        {
            var text = new TextBlock { Text = $"Для распаковки требуется {e.Size1:N0} Б памяти. Текущий предел: {e.Size2:N0} Б.\nРазрешение действует для этого запроса.", TextWrapping = TextWrapping.Wrap, MaxWidth = 480 };
            ulong savedLimit = e.Size1 > ulong.MaxValue - ((1UL << 30) - 1) ? e.Size1 : (e.Size1 + ((1UL << 30) - 1)) / (1UL << 30) * (1UL << 30);
            var remember = new CheckBox { Content = $"Сохранить предел {savedLimit:N0} Б для следующих операций" };
            var content = new StackPanel { Spacing = 12 }; content.Children.Add(text); content.Children.Add(remember);
            promptDialog = NewDialog("Предел памяти", content, "Разрешить", "Отмена");
            bool canSkip = (e.Flags & (1u << 12)) != 0;
            promptDialog.SecondaryButtonText = canSkip ? "Пропустить архив" : "";
            var answer = await promptDialog.ShowAsync();
            response = answer == ContentDialogResult.Primary ? new(1) : answer == ContentDialogResult.Secondary ? new(4) : new(-1);
            if (answer == ContentDialogResult.Primary && remember.IsChecked == true)
                try { MemoryPreference.Save(savedLimit); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { ShowError("Разрешение применено, но сохранить предел не удалось: " + error.Message); }
        }
        else
        {
            var body = new StackPanel { Spacing = 12, MinWidth = 420 };
            string ExistingSize() => (e.Flags & 1) != 0 ? $"{e.Size1:N0} Б" : "Размер неизвестен";
            string IncomingSize() => (e.Flags & 2) != 0 ? $"{e.Size2:N0} Б" : "Размер неизвестен";
            body.Children.Add(new TextBlock { Text = "В папке назначения уже есть файл:", TextWrapping = TextWrapping.Wrap });
            body.Children.Add(new TextBlock { Text = e.Path1 + "\n" + ExistingSize() + FormatTime(e.Time1, (e.Flags & 4) != 0), TextWrapping = TextWrapping.Wrap });
            body.Children.Add(new TextBlock { Text = "Исходный файл:\n" + e.Path2 + "\n" + IncomingSize() + FormatTime(e.Time2, (e.Flags & 8) != 0), TextWrapping = TextWrapping.Wrap });
            var answers = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, SelectedIndex = 0 };
            foreach (string answer in new[] { "Пропустить", "Заменить", "Заменить все", "Пропустить все", "Переименовать входящий файл" }) answers.Items.Add(answer);
            AutomationProperties.SetName(answers, "Действие при совпадении имён"); body.Children.Add(answers);
            promptDialog = NewDialog("Файл уже существует", body, "Применить", "Отмена");
            promptDialog.DefaultButton = ContentDialogButton.Close;
            if (await promptDialog.ShowAsync() == ContentDialogResult.Primary) response = new(new[] { 2, 0, 1, 3, 4 }[answers.SelectedIndex]);
        }
        promptDialog = null;
        if (response.Value < 0) activeJob?.Cancel();
        allowProgressClose = false;
        if (busy && !closing && !operationCancelled && progressDialog != null) progressShown = progressDialog.ShowAsync().AsTask();
        return response;
    }

    private static string FormatTime(ulong time, bool defined) => defined ? "   ·   " + DateTime.FromFileTimeUtc(checked((long)time)).ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss") : "";
    private static string IssueText(ArchiveEvent e) => e.Path1.Length > 0 && e.Code == 0 && (e.Flags & 0x10000) == 0 ? e.Path1 : (e.Path1.Length > 0 ? e.Path1 + ": " : "") + (e.Code switch
    {
        1 => "Метод сжатия не поддерживается.", 2 => "Ошибка данных.", 3 => "Ошибка CRC: данные повреждены или указан неверный пароль.",
        4 => "Данные недоступны.", 5 => "Неожиданный конец данных.", 6 => "Данные после конца архива.", 7 => "Файл не является архивом.",
        8 => "Ошибка заголовков.", 9 => "Неверный пароль.", 0x101 => $"Превышен предел памяти: требуется {e.Size1:N0} Б, разрешено {e.Size2:N0} Б. Архив пропущен.", 0x100 => $"Архив сообщает об ошибках заголовков (0x{e.Size1:X}) или предупреждениях (0x{e.Size2:X}).", _ => $"Ошибка элемента: {e.Code}."
    });
    private ContentDialog NewDialog(string title, object content, string primary = "", string close = "")
    {
        var dialog = new ContentDialog { Title = title, Content = content, PrimaryButtonText = primary, CloseButtonText = close, DefaultButton = primary.Length > 0 ? ContentDialogButton.Primary : ContentDialogButton.Close, XamlRoot = Root.XamlRoot, RequestedTheme = WindowHost.RequestedTheme };
        dialog.Opened += (_, _) => currentDialog = dialog;
        dialog.Closed += (_, _) => { if (ReferenceEquals(currentDialog, dialog)) currentDialog = null; if (closing && !busy) DispatcherQueue.TryEnqueue(Close); };
        return dialog;
    }
    private void CancelOperation()
    {
        operationCancelled = true;
        activeJob?.Cancel(); pauseRequested = false; pauseButton.IsEnabled = cancelButton.IsEnabled = false;
        progressState.Text = "Отмена… Ожидание завершения записи и закрытия файлов.";
        promptDialog?.Hide();
    }
    private void ShowError(string message) { Notice.Severity = InfoBarSeverity.Error; Notice.Title = "Не удалось выполнить действие"; Notice.Message = message; Notice.IsOpen = true; }
    private async void OnOpenArchive(object sender, RoutedEventArgs e)
    {
        if (busy || configuring) return;
        configuring = true;
        var picker = new FileOpenPicker(); picker.FileTypeFilter.Add("*");
        Windows.Storage.StorageFile? file = null;
        try
        {
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            file = await picker.PickSingleFileAsync();
        }
        catch (Exception error) { ShowError(error.Message); }
        finally { configuring = false; UpdateStatus(); }
        if (closing) Close();
        else if (file != null) await Navigate(file.Path);
    }
    private async void OnParent(object sender, RoutedEventArgs e) { if (!busy) await Parent(); }
    private async void OnBack(object sender, RoutedEventArgs e) => await GoBack();
    private async Task GoBack()
    {
        if (busy || configuring) return;
        while (history.TryPop(out var target))
        {
            if (target.Session is { IsClosed: false } session)
            {
                for (var level = archiveSession; level != null; level = level.Parent)
                    if (ReferenceEquals(level, session))
                    {
                        while (!ReferenceEquals(archiveSession, session))
                        {
                            var parent = archiveSession!.Parent;
                            DropSessionColumns(archiveSession);
                            archiveSession.Dispose(); archiveSession = parent;
                        }
                        archivePrefix = target.Prefix; ShowArchiveFolder(); list.Focus(FocusState.Programmatic); return;
                    }
            }
            // Closed nested indices cannot be reinterpreted in a new root session.
            if (target.Session?.Parent != null) continue;
            if (target.Session != null)
            {
                var reopened = await OpenArchive(target.Path, false);
                if (!closing && reopened?.HResult == 0 && archiveSession?.RootPath == target.Path)
                { archivePrefix = target.Prefix; ShowArchiveFolder(); }
            }
            else await Navigate(target.Path, false);
            UpdateStatus(); return;
        }
        UpdateStatus();
    }
    private async void OnRefresh(object sender, RoutedEventArgs e) => await RefreshView();
    private async Task RefreshView()
    {
        if (busy || configuring) return;
        var selected = list.SelectedItems.Cast<FileEntry>().Select(item => (item.FullPath, item.ArchiveIndex)).ToHashSet();
        var focused = CurrentItem();
        string prefix = archivePrefix;
        // A child is an owned snapshot; refreshing its view keeps its native
        // identity and parent chain. Root refresh opens a new source session.
        if (archiveSession?.Parent != null) ShowArchiveFolder();
        else if (archiveSession != null) await OpenArchive(archiveSession.RootPath, false);
        else if (directory.Length > 0) await NavigateDirectory(activePanel, directory, remember: false, refresh: true);
        if (closing) return;
        if (archiveSession != null && archivePrefix != prefix &&
            archiveEntries.Any(item => item.Path.Replace('\\', '/').StartsWith(prefix, StringComparison.Ordinal)))
        { archivePrefix = prefix; ShowArchiveFolder(); }
        // Restore rows that still identify the same entry in the refreshed view.
        foreach (var item in entries.Where(item => selected.Contains((item.FullPath, item.ArchiveIndex)))) if (!list.SelectedItems.Contains(item)) list.SelectedItems.Add(item);
        lastCurrentItem = entries.FirstOrDefault(item => item.FullPath == focused?.FullPath && item.ArchiveIndex == focused?.ArchiveIndex);
        FocusFileList();
    }
    private async void OnExtract(object sender, RoutedEventArgs e) => await Extract(false);
    private async void OnTest(object sender, RoutedEventArgs e) => await Extract(true);
    private void OnView(object sender, RoutedEventArgs e)
    {
        if (busy || configuring) return;
        view = (string)((FrameworkElement)sender).Tag;
        BuildList(); FocusFileList();
    }
    private void OnTheme(object sender, RoutedEventArgs e)
    {
        WindowHost.RequestedTheme = Enum.Parse<ElementTheme>((string)((FrameworkElement)sender).Tag);
        UpdateThemeMenu();
        RefreshColors();
    }
    private void RequestClose()
    {
        // Window.Close() bypasses AppWindow.Closing. Menu/diagnostic close paths
        // must use the same cancellation/await policy as the system caption.
        if (fileDragInProgress || shellDrag?.IsDropping == true) return;
        if (busy) { closing = true; CancelOperation(); return; }
        if (configuring) { closing = true; currentDialog?.Hide(); return; }
        Close();
    }
    private void OnExit(object sender, RoutedEventArgs e) => RequestClose();
    private async void OnAbout(object sender, RoutedEventArgs e) => await NewDialog("Arkvello", new TextBlock { Text = "Технический срез C# + WinUI 3 / C++.\nПросмотр папок и архивов, извлечение, проверка и интерактивные запросы.\nОстальные команды и полный набор параметров будут перенесены на следующих этапах.\nОсновано на 7-Zip 26.06. Лицензии находятся рядом с программой.", TextWrapping = TextWrapping.Wrap, MaxWidth = 480 }, close: "Закрыть").ShowAsync();
    private async void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (suppressRenameKeyRelease && e.Key is VirtualKey.Enter or VirtualKey.Escape) { e.Handled = true; return; }
        if (busy || configuring || renameBox != null) return;
        bool alt = KeyDown(VirtualKey.Menu), ctrl = KeyDown(VirtualKey.Control), shift = KeyDown(VirtualKey.Shift);
        if (!alt && !ctrl && !shift && e.Key == VirtualKey.F9)
        {
            e.Handled = true;
            if (!e.KeyStatus.WasKeyDown) await SetTwoPanels(!twoPanels);
            return;
        }
        if (alt && !ctrl && !shift && e.Key is VirtualKey.F1 or VirtualKey.F2 or VirtualKey.F12)
        {
            e.Handled = true;
            return;
        }
        if (FocusInActiveList())
        {
            if (!alt && !ctrl && !shift && e.Key is VirtualKey.Divide or (VirtualKey)0xDC)
            {
                e.Handled = true;
                return;
            }
            if (ctrl && !alt && !shift && e.Key is >= VirtualKey.Number1 and <= VirtualKey.Number4)
            {
                e.Handled = true;
                if (e.KeyStatus.WasKeyDown) return;
                view = e.Key switch
                {
                    VirtualKey.Number1 => "LargeIcons", VirtualKey.Number2 => "SmallIcons",
                    VirtualKey.Number3 => "List", _ => "Details"
                };
                BuildList(); FocusFileList();
                return;
            }
        }
        if (twoPanels && FocusInActiveList())
        {
            if (!alt && !ctrl && !shift && e.Key == VirtualKey.Tab)
            {
                e.Handled = true;
                if (ActivatePanel(OtherPanel)) FocusFileList();
                return;
            }
            if (alt && !ctrl && !shift && archiveSession == null && directory.Length > 0)
            {
                string? path = e.Key == VirtualKey.Up ? directory :
                    e.Key is VirtualKey.Left or VirtualKey.Right && CurrentItem() is { IsDirectory: true } folder ? folder.FullPath : null;
                if (path != null)
                {
                    e.Handled = true;
                    await NavigateDirectory(OtherPanel, path, remember: true);
                    return;
                }
            }
        }
        if (alt && e.Key == VirtualKey.Left) { e.Handled = true; OnBack(sender, e); return; }
        if (ctrl && e.Key == VirtualKey.L) { Address.Focus(FocusState.Keyboard); Address.SelectAll(); e.Handled = true; }
        if (ctrl && e.Key == VirtualKey.R && !busy) { OnRefresh(sender, e); e.Handled = true; }
    }

    private async void OnRootKeyUp(object sender, KeyRoutedEventArgs e)
    {
        // Rename replaces/removes its focused editor before key release. Keep
        // that release from activating a new row or WinUI's fallback menu focus.
        if (suppressRenameKeyRelease && e.Key is VirtualKey.Enter or VirtualKey.Escape)
        { suppressRenameKeyRelease = false; e.Handled = true; return; }
        if (!SelectionReady || KeyDown(VirtualKey.Control) || KeyDown(VirtualKey.Shift)) return;
        if (KeyDown(VirtualKey.Menu))
        {
            if (e.Key == VirtualKey.F12) { e.Handled = true; OnPathHistory(sender, e); }
            else if (e.Key is VirtualKey.F1 or VirtualKey.F2)
            {
                e.Handled = true;
                var target = panels[e.Key == VirtualKey.F1 ? 0 : 1];
                if (!twoPanels && activePanel != target) await SetTwoPanels(true);
                await ChooseDrive(target);
            }
        }
        else if (FocusInActiveList() && e.Key is VirtualKey.Divide or (VirtualKey)0xDC)
        {
            e.Handled = true;
            await ChooseDrive(activePanel);
        }
    }

    private async Task MeasureList()
    {
        // Synthetic data isolates XAML realization from disk/decoder throughput.
        var results = new List<object>();
        foreach (int count in new[] { 10_000, 100_000 })
        foreach (string mode in new[] { "Details", "List", "SmallIcons", "LargeIcons" })
        {
            view = mode;
            var sw = Stopwatch.StartNew();
            entries = Enumerable.Range(0, count).Select(i => new FileEntry("measure:" + i, $"Файл {i:D6} — Unicode Ω.txt", "", false, (ulong)i, DateTime.UnixEpoch)).ToList();
            BuildList(); await Task.Delay(300); list.UpdateLayout();
            double initialMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart(); list.ScrollIntoView(entries[^1]); await Task.Delay(300); list.UpdateLayout();
            int realized = 0; for (int i = 0; i < count; i++) if (list.ContainerFromIndex(i) != null) realized++;
            results.Add(new { count, initialMsIncluding300msSettle = initialMs, scrollMsIncluding300msSettle = sw.Elapsed.TotalMilliseconds, realized, workingSet = Process.GetCurrentProcess().WorkingSet64, dpi = Root.XamlRoot.RasterizationScale, scalePercent, viewportWidth = list.ActualWidth, viewportHeight = list.ActualHeight, view });
        }
        string output = Path.GetFullPath("build/winui-list-measure.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, System.Text.Json.JsonSerializer.Serialize(results, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Status.Text = "Диагностика списка завершена: " + output;
    }
}
