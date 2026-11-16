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
    private NativeArchive? archive;
    private IReadOnlyList<ArchiveEntry> archiveEntries = [];
    private string directory = "", archivePath = "", archivePrefix = "";
    private readonly Stack<string> history = new();
    private List<FileEntry> entries = [];
    private ListViewBase list = null!;
    private readonly List<Column> columns = [new("Имя", "Name", 420), new("Дата изменения", "ModifiedText", 210), new("Тип", "TypeText", 170), new("Размер", "SizeText", 150)];
    private string view = "Details";
    private int scalePercent = InterfaceScale.Load();
    private string sort = "Name";
    private bool descending, busy, closing;
    private bool configuring;
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
        // XAML theme resources and UISettings already track system colors.
        // AccessibilitySettings.HighContrastChanged cannot be subscribed by
        // every unpackaged host (ERROR_NOT_FOUND on this machine).
        AppWindow.Closing += (_, e) =>
        {
            if (busy) { e.Cancel = true; closing = true; CancelOperation(); }
        };
        Closed += (_, _) => { archive?.Dispose(); archive = null; };
        pauseButton.Click += (_, _) =>
        {
            pauseRequested = !pauseRequested;
            activeJob?.Pause(pauseRequested);
            pauseButton.Content = pauseRequested ? "Продолжить" : "Пауза";
            progressState.Text = pauseRequested ? "Ожидание паузы…" : "Выполняется…";
        };
        cancelButton.Click += (_, _) => CancelOperation();
        progressTimer.Tick += (_, _) => FlushProgress();
        BuildList(); RefreshColors();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyInterfaceScale();
        string[] args = Environment.GetCommandLineArgs();
        double scale = Root.XamlRoot.RasterizationScale;
        var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
        int width = Math.Min((int)(1180 * scale), area.Width - 48);
        int height = Math.Min((int)(780 * scale), area.Height - 48);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height));
        int open = Array.IndexOf(args, "--open");
        await Navigate(open >= 0 && args.Length > open + 1 ? args[open + 1] : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        // Explicit diagnostic mode never runs in ordinary application sessions.
        if (args.Contains("--measure-list")) await MeasureList();
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
        ListHost.Background = accessibility.HighContrast ? Root.Background : new SolidColorBrush(dark
            ? Windows.UI.Color.FromArgb(255, 25, 25, 25) : Colors.White);
    }

    private static string Escape(string value) => SecurityElement.Escape(value) ?? "";
    private static string ItemIcon(int size)
    {
        string folder = size > 24 ? Controls.FluentItemPaths.FolderLarge : Controls.FluentItemPaths.Folder;
        string file = size > 24 ? Controls.FluentItemPaths.FileLarge : Controls.FluentItemPaths.File;
        int units = size > 24 ? 48 : 20;
        return "<Grid><Viewbox Width='" + size + "' Height='" + size + "' Visibility='{Binding FolderVisibility}'><PathIcon Data='" + folder + "' Width='" + units + "' Height='" + units + "' Foreground='{ThemeResource FolderIconBrush}' AutomationProperties.AccessibilityView='Raw'/></Viewbox><Viewbox Width='" + size + "' Height='" + size + "' Visibility='{Binding FileVisibility}'><PathIcon Data='" + file + "' Width='" + units + "' Height='" + units + "' AutomationProperties.AccessibilityView='Raw'/></Viewbox></Grid>";
    }
    private void BuildList()
    {
        var selected = list?.SelectedItems.Cast<FileEntry>().Select(e => e.Identity).ToHashSet() ?? [];
        ListHost.Children.Clear();
        var layout = new Grid();
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        double width = columns.Sum(c => c.Width);
        bool details = view == "Details";
        ScrollViewer? headerScroll = null;
        if (details)
        {
            var header = new StackPanel { Orientation = Orientation.Horizontal, Height = 32, Width = width, HorizontalAlignment = HorizontalAlignment.Left };
            for (int i = 0; i < columns.Count; i++)
            {
                int position = i; var col = columns[i];
                var cell = new Grid { Width = col.Width, AllowDrop = true, CanDrag = true, Tag = col.Property };
                var button = new Button { Content = col.Label + (sort == col.Property ? descending ? " ↓" : " ↑" : ""), Padding = new(16, 0, 8, 0), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new(0), CornerRadius = new(0) };
                button.Click += (_, _) => { descending = sort == col.Property && !descending; sort = col.Property; ApplySort(); BuildList(); };
                var columnMenu = new MenuFlyout();
                var resize = new MenuFlyoutItem { Text = "Ширина столбца…" };
                resize.Click += async (_, _) =>
                {
                    if (busy || configuring) return;
                    var value = new NumberBox { Value = columns[position].Width, Minimum = 90, Maximum = 4096, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
                    AutomationProperties.SetName(value, "Ширина в единицах интерфейса");
                    configuring = true;
                    try { if (await NewDialog("Ширина: " + col.Label, value, "Применить", "Отмена").ShowAsync() == ContentDialogResult.Primary && !double.IsNaN(value.Value)) { columns[position] = columns[position] with { Width = value.Value }; BuildList(); } }
                    finally { configuring = false; }
                };
                var left = new MenuFlyoutItem { Text = "Переместить левее", IsEnabled = position > 0 };
                left.Click += (_, _) => { (columns[position - 1], columns[position]) = (columns[position], columns[position - 1]); BuildList(); };
                var right = new MenuFlyoutItem { Text = "Переместить правее", IsEnabled = position < columns.Count - 1 };
                right.Click += (_, _) => { (columns[position + 1], columns[position]) = (columns[position], columns[position + 1]); BuildList(); };
                columnMenu.Items.Add(resize); columnMenu.Items.Add(left); columnMenu.Items.Add(right); button.ContextFlyout = columnMenu;
                var thumb = new Thumb { Width = 6, HorizontalAlignment = HorizontalAlignment.Right, Background = new SolidColorBrush(Colors.Transparent) };
                AutomationProperties.SetName(thumb, "Ширина столбца «" + col.Label + "»");
                thumb.DragDelta += (_, e) => { double w = Math.Max(90, columns[position].Width + e.HorizontalChange); columns[position] = columns[position] with { Width = w }; cell.Width = w; };
                thumb.DragCompleted += (_, _) => BuildList();
                cell.DragStarting += (_, e) => { e.Data.SetText(col.Property); e.AllowedOperations = DataPackageOperation.Move; };
                cell.DragOver += (_, e) => { e.AcceptedOperation = DataPackageOperation.Move; e.Handled = true; };
                cell.Drop += async (_, e) =>
                {
                    if (!e.DataView.Contains(StandardDataFormats.Text)) return;
                    string key = await e.DataView.GetTextAsync(); int old = columns.FindIndex(c => c.Property == key);
                    if (old < 0 || old == position) return;
                    var moving = columns[old]; columns.RemoveAt(old); columns.Insert(position, moving); BuildList();
                };
                cell.Children.Add(button); cell.Children.Add(thumb); header.Children.Add(cell);
            }
            headerScroll = new ScrollViewer { Content = header, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollMode = ScrollMode.Disabled, ZoomMode = ZoomMode.Disabled };
            layout.Children.Add(headerScroll);
        }
        list = view is "SmallIcons" or "LargeIcons" ? new GridView() : new ListView();
        list.SelectionMode = ListViewSelectionMode.Extended;
        list.IsMultiSelectCheckBoxEnabled = false;
        list.IsItemClickEnabled = false;
        list.ItemsSource = entries;
        list.Background = new SolidColorBrush(Colors.Transparent);
        list.HorizontalContentAlignment = HorizontalAlignment.Stretch;
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
        list.SelectionChanged += (_, _) => UpdateStatus();
        list.DoubleTapped += async (_, _) => { if (!busy && list.SelectedItem is FileEntry item) await OpenItem(item); };
        list.KeyDown += async (_, e) =>
        {
            if (e.Key == VirtualKey.Enter && !busy && list.SelectedItem is FileEntry item) { e.Handled = true; await OpenItem(item); }
            if (e.Key == VirtualKey.Back && !busy) { e.Handled = true; await Parent(); }
        };
        AutomationProperties.SetName(list, "Файлы");
        AutomationProperties.SetAutomationId(list, "FileList");
        string template;
        if (details)
        {
            template = "<Grid Height='28' MinWidth='" + width.ToString(CultureInfo.InvariantCulture) + "' AutomationProperties.Name='{Binding AccessibleName}'><Grid.ColumnDefinitions>" + string.Join("", columns.Select(c => "<ColumnDefinition Width='" + c.Width.ToString(CultureInfo.InvariantCulture) + "'/>")) + "</Grid.ColumnDefinitions>";
            for (int i = 0; i < columns.Count; i++)
            {
                var c = columns[i];
                string text = "<TextBlock Text='{Binding " + Escape(c.Property) + "}' FontSize='14' VerticalAlignment='Center' TextTrimming='CharacterEllipsis' Margin='16,0,12,0'" + (c.Property == "SizeText" ? " TextAlignment='Right'" : "") + "/>";
                template += c.Property == "Name"
                    ? "<Grid Grid.Column='" + i + "' Margin='16,0,0,0'><Grid.ColumnDefinitions><ColumnDefinition Width='26'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>" + ItemIcon(18) + "<TextBlock Grid.Column='1' Text='{Binding Name}' FontSize='14' VerticalAlignment='Center' TextTrimming='CharacterEllipsis' Margin='0,0,12,0'/></Grid>"
                    : "<Grid Grid.Column='" + i + "'>" + text + "</Grid>";
            }
            template += "</Grid>";
        }
        else if (view == "List") template = "<StackPanel Orientation='Horizontal' Height='28' Spacing='8' Margin='16,0'>" + ItemIcon(18) + "<TextBlock Text='{Binding Name}' VerticalAlignment='Center'/></StackPanel>";
        else
        {
            bool large = view == "LargeIcons";
            // Uniform tile dimensions must include two lines even when the first
            // realized filename fits on one. A finite Grid slot also constrains
            // horizontal measurement in the small-icon view.
            var tileStyle = new Style { TargetType = typeof(GridViewItem) };
            tileStyle.Setters.Add(new Setter(FrameworkElement.WidthProperty, large ? 140d : 220d));
            tileStyle.Setters.Add(new Setter(FrameworkElement.HeightProperty, large ? 104d : 64d));
            tileStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            tileStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            tileStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Stretch));
            list.ItemContainerStyle = tileStyle;
            string label = "<TextBlock Text='{Binding Name}' TextTrimming='CharacterEllipsis' MaxLines='2' TextWrapping='Wrap' FontSize='14' LineHeight='20' LineStackingStrategy='BlockLineHeight' MaxHeight='40' VerticalAlignment='Center'";
            template = large
                ? "<Grid Width='140' Height='104' Padding='8' RowSpacing='8' ToolTipService.ToolTip='{Binding Name}' AutomationProperties.Name='{Binding AccessibleName}'><Grid.RowDefinitions><RowDefinition Height='40'/><RowDefinition Height='40'/></Grid.RowDefinitions>" + ItemIcon(36) + "<Grid Grid.Row='1'>" + label + " TextAlignment='Center'/></Grid></Grid>"
                : "<Grid Width='220' Height='64' Padding='8' ColumnSpacing='8' ToolTipService.ToolTip='{Binding Name}' AutomationProperties.Name='{Binding AccessibleName}'><Grid.ColumnDefinitions><ColumnDefinition Width='24'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>" + ItemIcon(20) + "<Grid Grid.Column='1'>" + label + "/></Grid></Grid>";
        }
        list.ItemTemplate = (DataTemplate)XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" + template + "</DataTemplate>");
        Grid.SetRow(list, 1); layout.Children.Add(list);
        ListHost.Children.Add(layout);
        foreach (var item in entries.Where(e => selected.Contains(e.Identity))) list.SelectedItems.Add(item);
        UpdateStatus();
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
            if (!InterfaceScale.TrySave(scalePercent)) ShowError("Масштаб применён, но сохранить его не удалось. Проверьте доступ к папке настроек Arkvello.");
        }
        foreach (ToggleMenuFlyoutItem item in ScaleMenu.Items) item.IsChecked = (int)item.Tag == scalePercent;
    }

    private void ApplySort()
    {
        Func<FileEntry, object?> key = sort switch { "SizeText" => e => e.Size, "ModifiedText" => e => e.Modified, "TypeText" => e => e.TypeText, _ => e => e.Name };
        entries = (descending ? entries.OrderByDescending(e => e.IsDirectory).ThenByDescending(key) : entries.OrderByDescending(e => e.IsDirectory).ThenBy(key)).ToList();
    }
    private void UpdateStatus()
    {
        Status.Text = $"Элементов: {entries.Count:N0}   |   Выбрано: {list.SelectedItems.Count:N0}" + (archive != null ? "   |   Архив" : "");
        ExtractButton.IsEnabled = TestButton.IsEnabled = !busy && archive != null;
        BackButton.IsEnabled = !busy && history.Count > 0;
    }
    private string Location => archive != null ? archivePath + " | " + archivePrefix : directory;

    private async Task Navigate(string path, bool remember = true)
    {
        if (busy || configuring) return;
        path = path.Trim();
        if (path.Contains(" | ", StringComparison.Ordinal)) { ShowError("Для перехода внутри архива используйте список и кнопку «Вверх»."); return; }
        if (Directory.Exists(path))
        {
            busy = true; UpdateStatus(); Status.Text = "Загрузка…";
            try
            {
                string fullPath = Path.GetFullPath(path);
                var loaded = await Task.Run(() => new DirectoryInfo(fullPath).EnumerateFileSystemInfos().Select(info =>
                {
                    bool dir = (info.Attributes & FileAttributes.Directory) != 0;
                    return new FileEntry(info.FullName, info.Name, info.FullName, dir, dir ? null : (ulong)((FileInfo)info).Length, info.LastWriteTime);
                }).ToList());
                if (remember && Location.Length > 0) history.Push(Location);
                archive?.Dispose(); archive = null; archiveEntries = []; archivePath = archivePrefix = "";
                directory = fullPath; entries = loaded; ApplySort(); BuildList(); Address.Text = directory; Notice.IsOpen = false;
            }
            catch (Exception error) { ShowError(error.Message); Address.Text = Location; }
            finally { busy = false; UpdateStatus(); }
        }
        else if (File.Exists(path)) await OpenArchive(path, remember);
        else ShowError("Путь не найден. Проверьте путь и повторите переход.");
    }

    private async Task OpenArchive(string path, bool remember)
    {
        NativeArchive? candidate = null;
        IReadOnlyList<ArchiveEntry> loaded = [];
        var result = await RunOperation("Открытие архива", job =>
        {
            var opened = NativeArchive.Open(Path.GetFullPath(path), job);
            candidate = opened.Archive;
            if (candidate != null)
            {
                try { loaded = candidate.ReadEntries(job); }
                catch (COMException error) { return new ArchiveResult(error.HResult, job.Issues); }
            }
            return opened.Result;
        }, showSuccess: false);
        if (result?.HResult == 0 && candidate != null)
        {
            if (remember && Location.Length > 0) history.Push(Location);
            archive?.Dispose(); archive = candidate; archiveEntries = loaded;
            archivePath = Path.GetFullPath(path); archivePrefix = "";
            ShowArchiveFolder(); Notice.IsOpen = false;
        }
        else candidate?.Dispose();
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
            else children.Add(new("arc:" + entry.Index, tail, path, false, entry.Size, entry.Modified, entry.Index));
        }
        foreach (var pair in folders) children.Add(new("folder:" + archivePrefix + pair.Key, pair.Key, archivePrefix + pair.Key + "/", true, null, null, DescendantIndices: pair.Value.Select(e => e.Index).ToArray()));
        entries = children; ApplySort(); BuildList(); Address.Text = Location;
    }
    private async Task OpenItem(FileEntry item)
    {
        if (archive != null)
        {
            if (item.IsDirectory) { history.Push(Location); archivePrefix = item.FullPath; ShowArchiveFolder(); }
            else await Extract(false, [item]);
        }
        else await Navigate(item.FullPath);
    }
    private async Task Parent()
    {
        if (archive != null && archivePrefix.Length > 0)
        {
            history.Push(Location); string prefix = archivePrefix.TrimEnd('/'); int i = prefix.LastIndexOf('/'); archivePrefix = i < 0 ? "" : prefix[..(i + 1)]; ShowArchiveFolder();
        }
        else if (archive != null) await Navigate(Path.GetDirectoryName(archivePath)!);
        else if (Directory.GetParent(directory) is { } parent) await Navigate(parent.FullName);
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
            var field = new TextBox { Text = Path.Combine(Path.GetDirectoryName(archivePath)!, Path.GetFileNameWithoutExtension(archivePath)), MinWidth = 380 };
            AutomationProperties.SetName(field, "Папка назначения");
            var browse = new Button { Content = "Обзор…" };
            browse.Click += async (_, _) => { var picker = new FolderPicker(); picker.FileTypeFilter.Add("*"); WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this)); var folder = await picker.PickSingleFolderAsync(); if (folder != null) field.Text = folder.Path; };
            var content = new StackPanel { Spacing = 12 }; content.Children.Add(new TextBlock { Text = "Извлечь в папку" }); content.Children.Add(field); content.Children.Add(browse); content.Children.Add(new TextBlock { Text = $"Выбрано файлов и папок в архиве: {indices.Length:N0}.\nПолные пути. При совпадении имён — запрос.", TextWrapping = TextWrapping.Wrap });
            var dialog = NewDialog("Извлечение", content, "Извлечь", "Отмена");
            dialog.Opened += (_, _) => { field.Focus(FocusState.Programmatic); field.SelectAll(); };
            configuring = true;
            ContentDialogResult answer;
            try { answer = await dialog.ShowAsync(); }
            finally { configuring = false; }
            if (answer != ContentDialogResult.Primary) return;
            if (!Path.IsPathFullyQualified(field.Text)) { ShowError("Укажите абсолютный путь к папке назначения."); return; }
            destination = Path.GetFullPath(field.Text);
        }
        var target = archive;
        await RunOperation(test ? "Проверка архива" : "Извлечение", job => target.Extract(indices, destination, job, test));
    }

    private async Task<ArchiveResult?> RunOperation(string title, Func<ArchiveJob, ArchiveResult> action, bool showSuccess = true)
    {
        if (busy) return null;
        busy = true; UpdateStatus(); Notice.IsOpen = false;
        var invoker = FocusManager.GetFocusedElement(Root.XamlRoot) as Control;
        progress.IsIndeterminate = true; progress.Value = 0; progressPath.Text = ""; progressState.Text = "Подготовка…";
        pauseRequested = false; pauseButton.Content = "Пауза"; pauseButton.IsEnabled = cancelButton.IsEnabled = true;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(pauseButton); buttons.Children.Add(cancelButton);
        var body = new StackPanel { Spacing = 16, MinWidth = 420 }; body.Children.Add(progressPath); body.Children.Add(progress); body.Children.Add(progressState); body.Children.Add(buttons);
        progressDialog = NewDialog(title, body);
        progressDialog.Closing += (_, e) => { if (!allowProgressClose) { e.Cancel = true; CancelOperation(); } };
        using var job = new ArchiveJob(HandleEvent); activeJob = job;
        lock (progressSync) { latestProgress = latestPath = null; }
        progressTimer.Start();
        allowProgressClose = false;
        progressShown = progressDialog.ShowAsync().AsTask();
        ArchiveResult? result = null;
        try
        {
            result = await Task.Run(() => action(job));
            if (job.CallbackError is { } callbackError) throw new InvalidOperationException("Ошибка обработки запроса операции.", callbackError);
        }
        catch (Exception error) { ShowError(error.Message); }
        finally
        {
            activeJob = null; busy = false; allowProgressClose = true;
            progressTimer.Stop(); FlushProgress();
            progressDialog.Hide();
            if (progressShown != null) await progressShown;
            progressDialog = null; progressShown = null;
            // Detach reusable controls before the next operation's content is built.
            body.Children.Clear(); buttons.Children.Clear(); UpdateStatus();
        }
        if (closing) { Close(); return result; }
        if (result != null && (showSuccess || !result.Succeeded))
        {
            string heading = result.Cancelled ? "Операция отменена" : result.Succeeded ? "Операция завершена" : result.HResult == 0 ? "Завершено с ошибками" : "Не удалось завершить операцию";
            var lines = new List<string>();
            if (result.Cancelled) lines.Add("Уже созданные файлы и частично записанные данные могут остаться в папке назначения.");
            else if (result.Succeeded) lines.Add("Ошибок не обнаружено.");
            else if (result.HResult != 0) lines.Add($"Код: 0x{result.HResult:X8}. Проверьте архив, путь и пароль; повторите операцию после исправления.");
            foreach (var issue in result.Issues) lines.Add(IssueText(issue));
            var text = new TextBox { Text = string.Join("\n", lines), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new(0), Background = new SolidColorBrush(Colors.Transparent), MaxHeight = 320, MinWidth = 420 };
            await NewDialog(heading, text, close: "Закрыть").ShowAsync();
        }
        invoker?.Focus(FocusState.Programmatic);
        return result;
    }

    private PromptReply HandleEvent(ArchiveEvent e)
    {
        if (e.Kind is ArchiveEventKind.Item or ArchiveEventKind.Issue) return new();
        if (e.Kind is ArchiveEventKind.Password or ArchiveEventKind.Overwrite)
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
        allowProgressClose = true; progressDialog?.Hide();
        // ContentDialog permits only one open dialog per XamlRoot.
        if (progressShown != null) await progressShown;
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
        else
        {
            var body = new StackPanel { Spacing = 12, MinWidth = 420 };
            string ExistingSize() => (e.Flags & 1) != 0 ? $"{e.Size1:N0} Б" : "Размер неизвестен";
            string IncomingSize() => (e.Flags & 2) != 0 ? $"{e.Size2:N0} Б" : "Размер неизвестен";
            body.Children.Add(new TextBlock { Text = "В папке назначения уже есть файл:", TextWrapping = TextWrapping.Wrap });
            body.Children.Add(new TextBlock { Text = e.Path1 + "\n" + ExistingSize() + FormatTime(e.Time1, (e.Flags & 4) != 0), TextWrapping = TextWrapping.Wrap });
            body.Children.Add(new TextBlock { Text = "Из архива:\n" + e.Path2 + "\n" + IncomingSize() + FormatTime(e.Time2, (e.Flags & 8) != 0), TextWrapping = TextWrapping.Wrap });
            var answers = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, SelectedIndex = 0 };
            foreach (string answer in new[] { "Пропустить", "Заменить", "Заменить все", "Пропустить все", "Переименовать извлекаемый файл" }) answers.Items.Add(answer);
            AutomationProperties.SetName(answers, "Действие при совпадении имён"); body.Children.Add(answers);
            promptDialog = NewDialog("Файл уже существует", body, "Применить", "Отмена");
            promptDialog.DefaultButton = ContentDialogButton.Close;
            if (await promptDialog.ShowAsync() == ContentDialogResult.Primary) response = new(new[] { 2, 0, 1, 3, 4 }[answers.SelectedIndex]);
        }
        promptDialog = null;
        if (response.Value < 0) activeJob?.Cancel();
        allowProgressClose = false;
        if (busy && !closing && progressDialog != null) progressShown = progressDialog.ShowAsync().AsTask();
        return response;
    }

    private static string FormatTime(ulong time, bool defined) => defined ? "   ·   " + DateTime.FromFileTimeUtc(checked((long)time)).ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss") : "";
    private static string IssueText(ArchiveEvent e) => e.Path1.Length > 0 && (e.Flags & 0x10000) == 0 ? e.Path1 : ((e.Flags & 0x10000) != 0 ? e.Path1 + ": " : "") + (e.Code switch
    {
        1 => "Метод сжатия не поддерживается.", 2 => "Ошибка данных.", 3 => "Ошибка CRC: данные повреждены или указан неверный пароль.",
        4 => "Данные недоступны.", 5 => "Неожиданный конец данных.", 6 => "Данные после конца архива.", 7 => "Файл не является архивом.",
        8 => "Ошибка заголовков.", 9 => "Неверный пароль.", 0x100 => $"Архив сообщает об ошибках заголовков (0x{e.Size1:X}) или предупреждениях (0x{e.Size2:X}).", _ => $"Ошибка элемента: {e.Code}."
    });
    private ContentDialog NewDialog(string title, object content, string primary = "", string close = "") => new()
    { Title = title, Content = content, PrimaryButtonText = primary, CloseButtonText = close, DefaultButton = primary.Length > 0 ? ContentDialogButton.Primary : ContentDialogButton.Close, XamlRoot = Root.XamlRoot, RequestedTheme = WindowHost.RequestedTheme };
    private void CancelOperation()
    {
        activeJob?.Cancel(); pauseRequested = false; pauseButton.IsEnabled = cancelButton.IsEnabled = false;
        progressState.Text = "Отмена… Ожидание завершения записи и закрытия файлов.";
        promptDialog?.Hide();
    }
    private void ShowError(string message) { Notice.Title = "Не удалось выполнить действие"; Notice.Message = message; Notice.IsOpen = true; }
    private async void OnOpenArchive(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var picker = new FileOpenPicker(); picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync(); if (file != null) await Navigate(file.Path);
    }
    private async void OnAddressKeyDown(object sender, KeyRoutedEventArgs e) { if (e.Key == VirtualKey.Enter) { e.Handled = true; await Navigate(Address.Text); } }
    private async void OnParent(object sender, RoutedEventArgs e) { if (!busy) await Parent(); }
    private async void OnBack(object sender, RoutedEventArgs e)
    {
        if (busy || history.Count == 0) return;
        string target = history.Pop(); int split = target.IndexOf(" | ", StringComparison.Ordinal);
        if (split >= 0 && archive != null && target[..split] == archivePath) { archivePrefix = target[(split + 3)..]; ShowArchiveFolder(); }
        else if (split >= 0) { await OpenArchive(target[..split], false); if (archive != null && archivePath == target[..split]) { archivePrefix = target[(split + 3)..]; ShowArchiveFolder(); } }
        else await Navigate(target, false);
    }
    private async void OnRefresh(object sender, RoutedEventArgs e) { if (!busy) { if (archive != null) await OpenArchive(archivePath, false); else await Navigate(directory, false); } }
    private async void OnExtract(object sender, RoutedEventArgs e) => await Extract(false);
    private async void OnTest(object sender, RoutedEventArgs e) => await Extract(true);
    private void OnSelectAll(object sender, RoutedEventArgs e) => list.SelectAll();
    private void OnClearSelection(object sender, RoutedEventArgs e) => list.SelectedItems.Clear();
    private void OnView(object sender, RoutedEventArgs e) { view = (string)((FrameworkElement)sender).Tag; BuildList(); }
    private void OnTheme(object sender, RoutedEventArgs e) { WindowHost.RequestedTheme = Enum.Parse<ElementTheme>((string)((FrameworkElement)sender).Tag); RefreshColors(); }
    private void OnExit(object sender, RoutedEventArgs e) => Close();
    private async void OnAbout(object sender, RoutedEventArgs e) => await NewDialog("Arkvello", new TextBlock { Text = "Технический срез C# + WinUI 3 / C++.\nПросмотр папок и архивов, извлечение, проверка и интерактивные запросы.\nОстальные команды и полный набор параметров будут перенесены на следующих этапах.\nОсновано на 7-Zip 26.06. Лицензии находятся рядом с программой.", TextWrapping = TextWrapping.Wrap, MaxWidth = 480 }, close: "Закрыть").ShowAsync();
    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (busy || configuring) return;
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (ctrl && e.Key == VirtualKey.L) { Address.Focus(FocusState.Keyboard); Address.SelectAll(); e.Handled = true; }
        if (ctrl && e.Key == VirtualKey.R && !busy) { OnRefresh(sender, e); e.Handled = true; }
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
