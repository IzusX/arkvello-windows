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
    private bool markMode;
    private FileEntry? lastCurrentItem;
    private FileEntry? CurrentItem()
    {
        for (var source = FocusManager.GetFocusedElement(Root.XamlRoot) as DependencyObject;
             source != null && !ReferenceEquals(source, list); source = VisualTreeHelper.GetParent(source))
            if (source is Microsoft.UI.Xaml.Controls.Primitives.SelectorItem container && list.ItemFromContainer(container) is FileEntry item) return item;
        return entries.FirstOrDefault(e => e.Identity == lastCurrentItem?.Identity) ?? list.SelectedItem as FileEntry;
    }
    private void ConfigureContextMenu()
    {
        var menu = new MenuFlyout();
        var open = new MenuFlyoutItem { Text = "Открыть", KeyboardAcceleratorTextOverride = "Enter" };
        open.Click += async (_, _) => { if (CurrentItem() is { } item && !busy && !configuring) await OpenItem(item); };
        var edit = new MenuFlyoutItem { Text = "Редактировать в Блокноте…", KeyboardAcceleratorTextOverride = "F4" };
        edit.Click += async (_, _) => await EditCurrent();
        var extract = new MenuFlyoutItem { Text = "Извлечь…" }; extract.Click += OnExtract;
        var refresh = new MenuFlyoutItem { Text = "Обновить", KeyboardAcceleratorTextOverride = "Ctrl+R" }; refresh.Click += OnRefresh;
        var properties = new MenuFlyoutItem { Text = "Столбцы…" }; properties.Click += OnColumns;
        menu.Items.Add(open); menu.Items.Add(edit); menu.Items.Add(extract); menu.Items.Add(new MenuFlyoutSeparator()); menu.Items.Add(refresh); menu.Items.Add(properties);
        menu.Opening += (_, _) =>
        {
            var item = CurrentItem(); bool ready = !busy && !configuring;
            open.IsEnabled = ready && item != null;
            edit.IsEnabled = ready && archiveSession != null && item is { IsDirectory: false, ArchiveIndex: not null };
            extract.IsEnabled = ready && archiveSession != null;
            refresh.IsEnabled = ready; properties.IsEnabled = ready && archiveSession != null;
        };
        list.ContextFlyout = menu;
        list.GotFocus += (_, _) => { if (CurrentItem() is { } item) lastCurrentItem = item; };
        list.RightTapped += (_, e) =>
        {
            for (var source = e.OriginalSource as DependencyObject; source != null && !ReferenceEquals(source, list); source = VisualTreeHelper.GetParent(source))
                if (source is Microsoft.UI.Xaml.Controls.Primitives.SelectorItem container && list.ItemFromContainer(container) is FileEntry item)
                {
                    if (!list.SelectedItems.Contains(item)) { list.SelectedItems.Clear(); list.SelectedItems.Add(item); }
                    container.Focus(FocusState.Pointer); break;
                }
        };
        list.PreviewKeyDown += async (_, e) =>
        {
            if (busy || configuring) return;
            if (e.Key == VirtualKey.F4) { e.Handled = true; await EditCurrent(); }
            if (e.Key == VirtualKey.Insert && CurrentItem() is { } current)
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
        if (busy || configuring) return;
        markMode = ((ToggleMenuFlyoutItem)sender).IsChecked;
        list.SelectionMode = markMode ? ListViewSelectionMode.Multiple : ListViewSelectionMode.Extended;
        AutomationProperties.SetHelpText(list, markMode ? "Режим отметок. Стрелки перемещают фокус; пробел или Insert изменяет отметку." : "Выбор с Ctrl и Shift; Insert изменяет отметку и переходит к следующему элементу.");
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
                await OpenArchive(rootPath, false);
                if (saveError != null) ShowError(saveError + "\nРабочая копия: " + edit.RecoveryPath);
                if (saved?.Succeeded == true && archiveSession?.RootSourceStamp == expectedRoot)
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
