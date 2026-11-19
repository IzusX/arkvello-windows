using System.Diagnostics;
using Arkvello.Models;
using Arkvello.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Arkvello;

public sealed partial class MainWindow
{
    private bool markMode { get => activePanel.MarkMode; set => activePanel.MarkMode = value; }
    private FileEntry? lastCurrentItem { get => activePanel.Current; set => activePanel.Current = value; }
    private FileEntry? CurrentItem()
    {
        for (var source = FocusManager.GetFocusedElement(Root.XamlRoot) as DependencyObject;
             source != null && !ReferenceEquals(source, list); source = VisualTreeHelper.GetParent(source))
            if (source is Microsoft.UI.Xaml.Controls.Primitives.SelectorItem container && list.ItemFromContainer(container) is FileEntry item) return item;
        return entries.FirstOrDefault(e => e.Identity == lastCurrentItem?.Identity) ?? list.SelectedItem as FileEntry;
    }
    private void ConfigureContextMenu(PanelState pane, ListViewBase list)
    {
        var menu = new MenuFlyout();
        var open = new MenuFlyoutItem { Text = "Открыть", KeyboardAcceleratorTextOverride = "Enter" };
        open.Click += async (_, _) => { if (CurrentItem() is { } item && !busy && !configuring) await OpenItem(item); };
        var edit = new MenuFlyoutItem { Text = "Редактировать в Блокноте…", KeyboardAcceleratorTextOverride = "F4" };
        edit.Click += async (_, _) => await EditCurrent();
        var extract = new MenuFlyoutItem { Text = "Извлечь…" }; extract.Click += OnExtract;
        var test = new MenuFlyoutItem { Text = "Проверить архив" }; test.Click += OnTest;
        var add = new MenuFlyoutItem { Text = "Добавить в архив…" }; add.Click += OnAddToArchive;
        var refresh = new MenuFlyoutItem { Text = "Обновить", KeyboardAcceleratorTextOverride = "Ctrl+R" }; refresh.Click += OnRefresh;
        var properties = new MenuFlyoutItem { Text = "Столбцы…" }; properties.Click += OnColumns;
        var copy = new MenuFlyoutItem { Text = "Копировать", KeyboardAcceleratorTextOverride = "Ctrl+C" }; copy.Click += OnClipboardCopy;
        var cut = new MenuFlyoutItem { Text = "Вырезать", KeyboardAcceleratorTextOverride = "Ctrl+X" }; cut.Click += OnClipboardCut;
        var paste = new MenuFlyoutItem { Text = "Вставить", KeyboardAcceleratorTextOverride = "Ctrl+V" }; paste.Click += OnPaste;
        var rename = new MenuFlyoutItem { Text = "Переименовать", KeyboardAcceleratorTextOverride = "F2" }; rename.Click += OnRename;
        var delete = new MenuFlyoutItem { Text = "Удалить…", KeyboardAcceleratorTextOverride = "Delete" }; delete.Click += OnDelete;
        var folder = new MenuFlyoutItem { Text = "Создать папку…", KeyboardAcceleratorTextOverride = "F7" }; folder.Click += OnNewFolder;
        menu.Items.Add(open); menu.Items.Add(edit); menu.Items.Add(add); menu.Items.Add(extract); menu.Items.Add(test); menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(copy); menu.Items.Add(cut); menu.Items.Add(paste); menu.Items.Add(rename); menu.Items.Add(delete); menu.Items.Add(folder);
        menu.Items.Add(new MenuFlyoutSeparator()); menu.Items.Add(refresh); menu.Items.Add(properties);
        menu.Opening += (_, _) =>
        {
            if (!ActivatePanel(pane)) return;
            var item = CurrentItem(); bool ready = !busy && !configuring;
            open.IsEnabled = ready && item != null;
            edit.IsEnabled = ready && archiveSession != null && item is { IsDirectory: false, ArchiveIndex: not null };
            extract.IsEnabled = test.IsEnabled = CanExtractOrTest;
            add.IsEnabled = FilesystemReady && OperatedFiles().Length > 0;
            refresh.IsEnabled = ready; properties.IsEnabled = ready;
            copy.IsEnabled = cut.IsEnabled = FilesystemReady && OperatedFiles().Length > 0;
            delete.IsEnabled = copy.IsEnabled || CanDeleteArchiveItems;
            rename.IsEnabled = FilesystemReady && item != null; paste.IsEnabled = folder.IsEnabled = FilesystemReady;
        };
        AutomationProperties.SetHelpText(list, pane.MarkMode
            ? "Стрелки перемещают фокус. Пробел или Insert меняет отметку."
            : "Ctrl и Shift расширяют выделение. Insert меняет отметку и переходит к следующему элементу.");
        list.ContextFlyout = menu;
        list.GotFocus += (_, _) =>
        {
            if (ActivatePanel(pane) && CurrentItem() is { } item) { pane.Current = item; UpdateFileCommands(); }
        };
        list.RightTapped += (_, e) =>
        {
            if (!SelectionReady || !ActivatePanel(pane)) return;
            for (var source = e.OriginalSource as DependencyObject; source != null && !ReferenceEquals(source, list); source = VisualTreeHelper.GetParent(source))
                if (source is Microsoft.UI.Xaml.Controls.Primitives.SelectorItem container && list.ItemFromContainer(container) is FileEntry item)
                {
                    if (!pane.MarkMode && !list.SelectedItems.Contains(item)) { list.SelectedItems.Clear(); list.SelectedItems.Add(item); }
                    container.Focus(FocusState.Pointer); break;
                }
        };
        list.PreviewKeyDown += async (_, e) =>
        {
            if (pane != activePanel || busy || configuring || renameBox != null) return;
            if (e.Key == VirtualKey.F4) { e.Handled = true; await EditCurrent(); }
            if (e.Key == VirtualKey.Insert && !KeyDown(VirtualKey.Control) && !KeyDown(VirtualKey.Shift) && CurrentItem() is { } current)
            {
                e.Handled = true;
                if (list.SelectedItems.Contains(current)) list.SelectedItems.Remove(current); else list.SelectedItems.Add(current);
                int next = Math.Min(entries.Count - 1, entries.IndexOf(current) + 1);
                if (next >= 0)
                {
                    list.ScrollIntoView(entries[next]); list.UpdateLayout();
                    if (list.ContainerFromIndex(next) is Control container) container.Focus(FocusState.Keyboard);
                }
            }
        };
    }
    private void OnMarkMode(object sender, RoutedEventArgs e)
    {
        if (SelectionReady)
        {
            markMode = !markMode;
            BuildList();
            FocusFileList();
        }
        MarkModeMenu.IsChecked = markMode;
    }
    private async Task EditCurrent()
    {
        if (busy || configuring || archiveSession == null || CurrentItem() is not { IsDirectory: false, ArchiveIndex: { } index }) return;
        ArchiveEdit? edit = null;
        var exported = await RunOperation("Подготовка к редактированию", job =>
        {
            var opened = ArchiveEdit.Begin(archiveSession, index, job); edit = opened.Edit; return opened.Result;
        }, showSuccess: false);
        if (closing || edit == null || exported?.Succeeded != true) return;
        try
        {
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe")) { UseShellExecute = false };
            start.ArgumentList.Add(edit.WorkingFile.Path);
            using var editor = Process.Start(start); // Existing Notepad can reuse another process/tab.
            var text = new TextBlock { Text = "Сохраните файл в Блокноте и закройте его вкладку, затем нажмите «Сохранить в архив».\n\n" + edit.ItemName + "\n\nРабочая копия и журнал восстановления:\n" + edit.RecoveryPath, TextWrapping = TextWrapping.Wrap, MaxWidth = 480 };
            configuring = true;
            ContentDialogResult answer;
            try { answer = await NewDialog("Редактирование файла", text, "Сохранить в архив", "Оставить отдельно").ShowAsync(); }
            finally { configuring = false; }
            if (closing || answer != ContentDialogResult.Primary) return;
            bool changed = false;
            var checkedEdit = await RunOperation("Проверка правок", job => { changed = edit.HasChangesSinceExport(job); return new(0, []); }, showSuccess: false);
            if (closing || checkedEdit?.Succeeded != true) return;
            if (!changed) { edit.Discard(); return; }
            ArchiveTemporaryFile? staged = null;
            ArchiveSourceStamp? expectedRoot = null;
            try
            {
                var result = await RunOperation("Подготовка обновлённого архива", job =>
                {
                    var prepared = edit.Stage(job); staged = prepared.File;
                    if (staged != null) expectedRoot = ArchiveSourceStamp.Capture(staged.Path, job);
                    return prepared.Result;
                }, showSuccess: false);
                if (closing || staged == null || result?.Succeeded != true) return;
                string rootPath = edit.Session.RootPath;
                string leafPrefix = archivePrefix;
                var chain = new List<(uint Index, string Folder)>();
                for (var level = edit.Session; level.Parent != null; level = level.Parent) chain.Add((level.ParentIndex!.Value, level.ParentFolder));
                chain.Reverse();
                // Stage verified every level; release all handles before the atomic replacement.
                CloseArchives(); archivePrefix = "";
                string? backup = null;
                var saved = await RunOperation("Сохранение архива", job => { backup = edit.Commit(staged, job); return new(0, []); }, showSuccess: false);
                if (closing) return;
                string? saveError = saved?.Succeeded == true ? null : Notice.Message;
                await OpenArchive(rootPath, false, captureSource: saved?.Succeeded == true);
                if (saveError != null) ShowError(saveError + "\nРабочая копия: " + edit.RecoveryPath);
                if (saved?.Succeeded == true && expectedRoot != null && archiveSession?.RootSourceStamp == expectedRoot)
                {
                    bool restored = true;
                    foreach (var parentItem in chain)
                    {
                        var parent = archiveSession;
                        archivePrefix = parentItem.Folder; ShowArchiveFolder();
                        if (entries.FirstOrDefault(e => e.ArchiveIndex == parentItem.Index) is not { } child) { restored = false; break; }
                        await OpenNestedArchive(child);
                        if (closing || ReferenceEquals(archiveSession, parent)) { restored = false; break; }
                    }
                    if (closing) return;
                    if (restored)
                    {
                        archivePrefix = leafPrefix; ShowArchiveFolder();
                        list.SelectedItem = entries.FirstOrDefault(e => e.ArchiveIndex == edit.Index); FocusFileList();
                    }
                }
                if (backup != null)
                {
                    Notice.Title = "Архив сохранён";
                    Notice.Message = "Резервная копия исходного архива: " + backup + "\nРабочая копия: " + edit.RecoveryPath;
                    Notice.Severity = InfoBarSeverity.Informational; Notice.IsOpen = true;
                }
            }
            finally { staged?.Dispose(); }
        }
        catch (Exception error) { ShowError(error.Message + "\nРабочая копия сохранена: " + edit.RecoveryPath); }
        finally { configuring = false; }
    }
}
