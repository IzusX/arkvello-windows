using Arkvello.Models;
using Arkvello.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

namespace Arkvello;

public sealed partial class MainWindow
{
    private TextBox? renameBox;
    private Action? restoreRename;
    private bool renameCommitting;
    private bool suppressRenameKeyRelease;
    private FileEntry[] OperatedFiles() => list.SelectedItems.Count > 0 ? list.SelectedItems.Cast<FileEntry>().ToArray() : CurrentItem() is { } item ? [item] : [];
    private bool FilesystemReady => archiveSession == null && activePanel.DirectoryAvailable && SelectionReady;
    private string OtherFilesystemDirectory => twoPanels && OtherPanel.ArchiveSession == null &&
        OtherPanel.DirectoryAvailable ? OtherPanel.Directory : "";
    private static bool KeyDown(VirtualKey key) => Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private void UpdateFileCommands()
    {
        bool selected = FilesystemReady && (list.SelectedItems.Count > 0 || CurrentItem() != null);
        CopyToButton.IsEnabled = CopyToMenu.IsEnabled = ClipboardCopyMenu.IsEnabled = ClipboardCutMenu.IsEnabled = selected;
        MoveToButton.IsEnabled = MoveToMenu.IsEnabled = DeleteButton.IsEnabled = DeleteMenu.IsEnabled = selected;
        RenameMenu.IsEnabled = FilesystemReady && CurrentItem() != null;
        NewFolderMenu.IsEnabled = NewFileMenu.IsEnabled = PasteMenu.IsEnabled = FilesystemReady;
    }
    private async void OnCopyTo(object sender, RoutedEventArgs e) => await TransferDialog(false);
    private async void OnMoveTo(object sender, RoutedEventArgs e) => await TransferDialog(true);
    private async void OnDelete(object sender, RoutedEventArgs e) => await DeleteFiles(true);
    private async void OnNewFolder(object sender, RoutedEventArgs e) => await CreateItem(true);
    private async void OnNewFile(object sender, RoutedEventArgs e) => await CreateItem(false);
    private void OnRename(object sender, RoutedEventArgs e) => BeginRename();
    private async void OnClipboardCopy(object sender, RoutedEventArgs e) => await CopyToClipboard(false);
    private async void OnClipboardCut(object sender, RoutedEventArgs e) => await CopyToClipboard(true);
    private async void OnPaste(object sender, RoutedEventArgs e)
    {
        if (!FilesystemReady) return;
        try { await PasteFiles(Clipboard.GetContent(), directory); }
        catch (Exception error) { ShowError(error.Message); }
    }

    private async Task TransferDialog(bool move, bool samePanel = false)
    {
        if (!FilesystemReady) return;
        string[] paths = OperatedFiles().Select(f => f.FullPath).ToArray(); if (paths.Length == 0) return;
        configuring = true; UpdateStatus();
        string suggested = !samePanel && OtherFilesystemDirectory.Length > 0 ? OtherFilesystemDirectory : directory;
        var destination = new TextBox { Text = suggested, Header = "Папка назначения", HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(destination, "Папка назначения");
        var errorText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var browse = new Button { Content = "Обзор…" };
        browse.Click += async (_, _) =>
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FolderPicker(); picker.FileTypeFilter.Add("*");
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                var folder = await picker.PickSingleFolderAsync(); if (folder != null) destination.Text = folder.Path;
            }
            catch (Exception error) { errorText.Text = error.Message; }
        };
        var body = new StackPanel { Spacing = 12, MaxWidth = 520 };
        body.Children.Add(new TextBlock { Text = $"Выбрано элементов: {paths.Length:N0}", TextWrapping = TextWrapping.Wrap }); body.Children.Add(destination); body.Children.Add(DestinationActions(browse, destination, transferDestinations)); body.Children.Add(errorText);
        var dialog = NewDialog(move ? "Переместить" : "Копировать", body, move ? "Переместить" : "Копировать", "Отмена");
        string? target = null;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try
            {
                target = Path.GetFullPath(destination.Text.Trim(), directory);
                if (!Directory.Exists(target)) throw new DirectoryNotFoundException("Папка назначения не найдена.");
            }
            catch (Exception error) { args.Cancel = true; target = null; errorText.Text = error.Message; destination.Focus(FocusState.Programmatic); }
        };
        ContentDialogResult answer;
        try { answer = await dialog.ShowAsync(); }
        finally { configuring = false; UpdateStatus(); }
        if (closing || answer != ContentDialogResult.Primary || target == null) { FocusFileList(); return; }
        var result = await TransferFiles(paths, target, move);
        if (result?.Succeeded == true) RememberPath(transferDestinations, target);
    }

    private async Task<ArchiveResult?> TransferFiles(string[] paths, string destination, bool move)
    {
        string currentDirectory = directory;
        var selected = OperatedFiles().Select(f => f.FullPath).ToArray();
        var result = await RunOperation(move ? "Перемещение файлов" : "Копирование файлов", job => FileSystemOperations.Transfer(paths, destination, move, job), showSuccess: false);
        if (!closing && archiveSession == null && directory == currentDirectory)
        {
            await ReloadFilesystem(selected.Where(p => File.Exists(p) || Directory.Exists(p)).ToArray());
            if (!closing && OtherFilesystemDirectory.Length > 0 && !SameDirectory(OtherFilesystemDirectory, currentDirectory))
                await NavigateDirectory(OtherPanel, OtherFilesystemDirectory, remember: false, refresh: true);
        }
        return result;
    }
    private async Task DeleteFiles(bool recycle)
    {
        if (!FilesystemReady) return;
        var selected = OperatedFiles(); if (selected.Length == 0) return;
        string[] paths = selected.Select(f => f.FullPath).ToArray();
        int oldIndex = Math.Max(0, entries.IndexOf(selected[0]));
        var message = new TextBlock { Text = (recycle ? "Переместить выбранные элементы в корзину?" : "Удалить выбранные элементы без корзины? Восстановление этой командой невозможно.") + "\n\n" + string.Join("\n", selected.Take(5).Select(f => f.Name)) + (selected.Length > 5 ? $"\nИ ещё: {selected.Length - 5:N0}" : ""), TextWrapping = TextWrapping.Wrap, MaxWidth = 480 };
        var dialog = NewDialog(recycle ? "Удаление в корзину" : "Удаление без корзины", message, recycle ? "В корзину" : "Удалить", "Отмена");
        dialog.DefaultButton = ContentDialogButton.Close;
        configuring = true; UpdateStatus();
        ContentDialogResult answer;
        try { answer = await dialog.ShowAsync(); }
        finally { configuring = false; UpdateStatus(); }
        if (closing || answer != ContentDialogResult.Primary) { FocusFileList(); return; }
        nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        // Shell's native progress owns its cancel/pause and recycle warnings.
        // The same job checkpoints keep application exit safe while it runs.
        await RunOperation(recycle ? "Удаление в корзину" : "Удаление файлов", job => FileSystemOperations.Delete(paths, recycle, hwnd, job), showSuccess: false, shellProgress: true);
        if (closing) return;
        await ReloadFilesystem(paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToArray());
        if (list.SelectedItems.Count == 0 && entries.Count > 0) { lastCurrentItem = entries[Math.Min(oldIndex, entries.Count - 1)]; list.SelectedItem = lastCurrentItem; FocusFileList(); }
    }

    private async Task CreateItem(bool folder)
    {
        if (!FilesystemReady) return;
        string parent = directory;
        var name = new TextBox { Text = folder ? "Новая папка" : "Новый файл.txt", Header = "Имя", HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(name, "Имя нового элемента");
        var errorText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var content = new StackPanel { Spacing = 8, MaxWidth = 480 }; content.Children.Add(name); content.Children.Add(errorText);
        var dialog = NewDialog(folder ? "Создать папку" : "Создать файл", content, "Создать", "Отмена");
        string? created = null;
        dialog.Opened += (_, _) => { name.Focus(FocusState.Programmatic); name.SelectAll(); };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral(); busy = true; UpdateStatus();
            try { string value = name.Text; created = await Task.Run(() => FileSystemOperations.Create(parent, value, folder)); }
            catch (Exception error) { args.Cancel = true; errorText.Text = error.Message; name.Focus(FocusState.Programmatic); }
            finally { busy = false; deferral.Complete(); UpdateStatus(); if (closing) dialog.Hide(); }
        };
        configuring = true; UpdateStatus();
        try { await dialog.ShowAsync(); }
        finally { configuring = false; UpdateStatus(); }
        if (closing) { Close(); return; }
        if (created != null) await ReloadFilesystem([created]); else FocusFileList();
    }

    private async Task ReloadFilesystem(string[] selected)
    {
        if (closing || archiveSession != null) return;
        await NavigateDirectory(activePanel, directory, remember: false, refresh: true);
        if (closing) return;
        var paths = selected.ToHashSet(StringComparer.OrdinalIgnoreCase);
        list.SelectedItems.Clear();
        foreach (var entry in entries.Where(f => paths.Contains(f.FullPath))) list.SelectedItems.Add(entry);
        lastCurrentItem = entries.FirstOrDefault(f => paths.Contains(f.FullPath));
        UpdateStatus(); FocusFileList();
        if (OtherFilesystemDirectory.Length > 0 && SameDirectory(OtherFilesystemDirectory, directory))
            await NavigateDirectory(OtherPanel, OtherFilesystemDirectory, remember: false, refresh: true);
    }

    private static bool SameDirectory(string first, string second) =>
        string.Equals(Path.TrimEndingDirectorySeparator(first), Path.TrimEndingDirectorySeparator(second), StringComparison.OrdinalIgnoreCase);

    private void CancelRename()
    {
        renameBox = null;
        var restore = restoreRename; restoreRename = null; restore?.Invoke();
    }
    private void BeginRename()
    {
        if (!FilesystemReady || CurrentItem() is not { } item) return;
        list.ScrollIntoView(item); list.UpdateLayout();
        if (list.ContainerFromItem(item) is not DependencyObject container) return;
        TextBlock? FindName(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is TextBlock text && Equals(text.Tag, "FileName")) return text;
                if (FindName(child) is { } found) return found;
            }
            return null;
        }
        if (FindName(container) is not { } label || VisualTreeHelper.GetParent(label) is not Panel parent) return;
        var box = new TextBox { Text = item.Name, FontSize = 14, MinHeight = 26, Padding = new(2, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = label.Margin };
        Grid.SetColumn(box, Grid.GetColumn(label));
        if (parent is StackPanel) box.Width = Math.Clamp(label.ActualWidth + 32, 140, 500);
        AutomationProperties.SetName(box, "Новое имя: " + item.Name);
        label.Visibility = Visibility.Collapsed; parent.Children.Add(box); renameBox = box;
        restoreRename = () => { parent.Children.Remove(box); label.Visibility = Visibility.Visible; };
        box.Unloaded += (_, _) => { if (ReferenceEquals(renameBox, box) && !renameCommitting) CancelRename(); };
        box.LostFocus += (_, _) => { if (ReferenceEquals(renameBox, box) && !renameCommitting) { CancelRename(); UpdateStatus(); } };
        box.KeyUp += (_, e) => { if (e.Key is VirtualKey.Enter or VirtualKey.Escape) e.Handled = true; };
        box.KeyDown += async (_, e) =>
        {
            if (e.Key == VirtualKey.Escape) { e.Handled = true; suppressRenameKeyRelease = true; CancelRename(); UpdateStatus(); FocusFileList(); return; }
            if (e.Key != VirtualKey.Enter || renameCommitting) return;
            e.Handled = true; suppressRenameKeyRelease = true; await CommitRename(item, box);
        };
        box.Focus(FocusState.Programmatic);
        box.Select(0, item.IsDirectory ? item.Name.Length : Path.GetFileNameWithoutExtension(item.Name).Length);
        UpdateStatus();
    }

    private async Task<bool> CommitRename(FileEntry item, TextBox box)
    {
        if (!ReferenceEquals(renameBox, box) || renameCommitting) return false;
        renameCommitting = true; busy = true; box.IsReadOnly = true; UpdateStatus();
        string? renamed = null;
        try { string name = box.Text; renamed = await Task.Run(() => FileSystemOperations.Rename(item.FullPath, name)); }
        catch (Exception error) { ShowError(error.Message); }
        finally { busy = false; renameCommitting = false; box.IsReadOnly = false; UpdateStatus(); }
        if (closing) { CancelRename(); Close(); return false; }
        if (renamed != null) { CancelRename(); await ReloadFilesystem([renamed]); return true; }
        box.Focus(FocusState.Programmatic); box.SelectAll(); return false;
    }

    private static async Task<IStorageItem[]> StorageItems(IEnumerable<string> paths)
    {
        var items = new List<IStorageItem>();
        foreach (string path in paths)
            items.Add(Directory.Exists(path) ? await StorageFolder.GetFolderFromPathAsync(path) : await StorageFile.GetFileFromPathAsync(path));
        return items.ToArray();
    }
    private async Task CopyToClipboard(bool cut)
    {
        if (!FilesystemReady) return;
        var selected = OperatedFiles().Select(f => f.FullPath).ToArray(); if (selected.Length == 0) return;
        configuring = true; UpdateStatus();
        try
        {
            var package = new DataPackage { RequestedOperation = cut ? DataPackageOperation.Move : DataPackageOperation.Copy };
            package.SetStorageItems(await StorageItems(selected));
            Clipboard.SetContent(package);
            Status.Text = cut ? $"Вырезано: {selected.Length:N0}. Выберите папку и вставьте (Ctrl+V)." : $"Скопировано в буфер: {selected.Length:N0}.";
        }
        catch (Exception error) { ShowError(error.Message); }
        finally { configuring = false; UpdateFileCommands(); if (closing) Close(); }
    }
    private async Task<ArchiveResult?> PasteFiles(DataPackageView data, string target, DataPackageOperation? requested = null)
    {
        if (!FilesystemReady) return null;
        string[] paths;
        configuring = true; UpdateStatus();
        try
        {
            if (!data.Contains(StandardDataFormats.StorageItems)) throw new NotSupportedException("В буфере или перетаскивании нет файлов и папок.");
            paths = (await data.GetStorageItemsAsync()).Select(item => item.Path).ToArray();
            if (paths.Length == 0 || paths.Any(string.IsNullOrWhiteSpace)) throw new NotSupportedException("Виртуальные элементы без файлового пути пока не поддерживаются.");
        }
        finally { configuring = false; UpdateStatus(); }
        if (closing) { Close(); return null; }
        bool move = (requested ?? data.RequestedOperation) == DataPackageOperation.Move;
        var result = await TransferFiles(paths, target, move);
        // A skipped move must not tell Explorer to discard its cut selection.
        if (result?.Succeeded == true)
        {
            if (move && paths.Any(p => File.Exists(p) || Directory.Exists(p)))
            {
                Notice.Severity = InfoBarSeverity.Informational; Notice.Title = "Перемещены не все элементы";
                Notice.Message = "Часть элементов осталась в исходной папке. Вырезанный набор не помечен как полностью перемещённый."; Notice.IsOpen = true;
                return new(1, result.Issues);
            }
            data.ReportOperationCompleted(move ? DataPackageOperation.Move : DataPackageOperation.Copy);
        }
        return result;
    }

    private void ConfigureFileInput(PanelState pane, ListViewBase target)
    {
        target.AllowDrop = true; target.CanDragItems = true;
        target.PreviewKeyDown += async (_, e) =>
        {
            if (e.Handled || !FilesystemReady || !ReferenceEquals(target, list)) return;
            bool ctrl = KeyDown(VirtualKey.Control), shift = KeyDown(VirtualKey.Shift);
            if (ctrl && e.Key == VirtualKey.C || ctrl && e.Key == VirtualKey.Insert) { e.Handled = true; await CopyToClipboard(false); }
            else if (ctrl && e.Key == VirtualKey.X || shift && e.Key == VirtualKey.Delete) { e.Handled = true; if (shift && e.Key == VirtualKey.Delete) await DeleteFiles(false); else await CopyToClipboard(true); }
            else if (ctrl && e.Key == VirtualKey.V || shift && e.Key == VirtualKey.Insert) { e.Handled = true; OnPaste(this, e); }
            else if (e.Key == VirtualKey.F2) { e.Handled = true; BeginRename(); }
            else if (e.Key == VirtualKey.F5 && !ctrl && !KeyDown(VirtualKey.Menu)) { e.Handled = true; await TransferDialog(false, shift); }
            else if (e.Key == VirtualKey.F6 && !ctrl && !KeyDown(VirtualKey.Menu)) { e.Handled = true; await TransferDialog(true, shift); }
            else if (e.Key == VirtualKey.F7 || ctrl && shift && e.Key == VirtualKey.N) { e.Handled = true; await CreateItem(true); }
            else if (ctrl && e.Key == VirtualKey.N && !KeyDown(VirtualKey.Menu)) { e.Handled = true; await CreateItem(false); }
            else if (e.Key == VirtualKey.Delete) { e.Handled = true; await DeleteFiles(true); }
        };
        target.DragItemsStarting += (_, e) =>
        {
            if (!ReferenceEquals(pane.List, target) || !ActivatePanel(pane) || !FilesystemReady) { e.Cancel = true; return; }
            string[] paths = e.Items.Cast<FileEntry>().Select(f => f.FullPath).ToArray();
            if (paths.Length == 0) { e.Cancel = true; return; }
            fileDragInProgress = true;
            SetDraggedFiles(e.Data, paths);
        };
        target.DragItemsCompleted += async (_, _) =>
        {
            fileDragInProgress = false;
            if (!busy && !configuring && !closing && pane.ArchiveSession == null && pane.Directory.Length > 0 && ReferenceEquals(pane.List, target))
                await NavigateDirectory(pane, pane.Directory, remember: false, refresh: true);
        };
        string DropTarget(DependencyObject? source)
        {
            for (; source != null && !ReferenceEquals(source, target); source = VisualTreeHelper.GetParent(source))
                if (source is Microsoft.UI.Xaml.Controls.Primitives.SelectorItem container && target.ItemFromContainer(container) is FileEntry { IsDirectory: true } folder) return folder.FullPath;
            return pane.Directory;
        }
        target.DragOver += (_, e) =>
        {
            e.Handled = true; e.AcceptedOperation = DataPackageOperation.None;
            if (busy || configuring || renameBox != null || pane.ArchiveSession != null ||
                !pane.DirectoryAvailable || !e.DataView.Contains(StandardDataFormats.StorageItems)) return;
            if (e.Modifiers.HasFlag(Windows.ApplicationModel.DataTransfer.DragDrop.DragDropModifiers.Control) &&
                e.Modifiers.HasFlag(Windows.ApplicationModel.DataTransfer.DragDrop.DragDropModifiers.Shift)) return; // Link effect is not implemented.
            bool move = e.Modifiers.HasFlag(Windows.ApplicationModel.DataTransfer.DragDrop.DragDropModifiers.Shift);
            var action = move ? DataPackageOperation.Move : DataPackageOperation.Copy;
            if ((e.AllowedOperations & action) == 0) return;
            e.AcceptedOperation = action;
            e.DragUIOverride.Caption = (move ? "Переместить в " : "Копировать в ") + DropTarget(e.OriginalSource as DependencyObject);
            e.DragUIOverride.IsCaptionVisible = true;
        };
        target.Drop += async (_, e) =>
        {
            e.Handled = true;
            if (e.AcceptedOperation == DataPackageOperation.None) return;
            if (!ReferenceEquals(pane.List, target) || !ActivatePanel(pane) || !FilesystemReady)
            { e.AcceptedOperation = DataPackageOperation.None; return; }
            string path = DropTarget(e.OriginalSource as DependencyObject);
            var deferral = e.GetDeferral();
            try
            {
                var result = await PasteFiles(e.DataView, path, e.AcceptedOperation);
                if (result?.Succeeded != true) e.AcceptedOperation = DataPackageOperation.None;
            }
            catch (Exception error) { e.AcceptedOperation = DataPackageOperation.None; ShowError(error.Message); }
            finally { deferral.Complete(); }
        };
    }

    private void SetDraggedFiles(DataPackage package, string[] paths)
    {
        package.RequestedOperation = DataPackageOperation.Copy | DataPackageOperation.Move;
        package.SetDataProvider(StandardDataFormats.StorageItems, async request =>
        {
            var deferral = request.GetDeferral();
            try { request.SetData(await StorageItems(paths)); }
            catch (Exception error) { DispatcherQueue.TryEnqueue(() => ShowError("Не удалось подготовить перетаскивание: " + error.Message)); }
            finally { deferral.Complete(); }
        });
    }
}
