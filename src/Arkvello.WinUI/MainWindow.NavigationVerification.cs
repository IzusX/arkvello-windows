using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Arkvello;

public sealed partial class MainWindow
{
    private async Task VerifyFileNavigationAndSelection(string fixture, Action<string, bool> record)
    {
        string root = Directory.CreateDirectory(Path.Combine(fixture, "navigation")).FullName;
        string subfolder = Directory.CreateDirectory(Path.Combine(root, "папка")).FullName;
        string empty = Directory.CreateDirectory(Path.Combine(root, "empty")).FullName;
        string a = Path.Combine(root, "Alpha.txt");
        string b = Path.Combine(root, "alphabet.TXT");
        string other = Path.Combine(root, "альфа.bin");
        foreach (string path in new[] { a, b, other }) File.WriteAllText(path, "check");
        DateTime earlier = new(2025, 10, 15, 12, 34, 56, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(a, earlier);
        File.SetCreationTimeUtc(a, earlier);
        File.SetCreationTimeUtc(b, earlier.AddHours(2));

        await SetTwoPanels(false);
        view = "Details";
        activePanel.Sort = "Name";
        activePanel.Descending = false;
        await Navigate(root);
        await Navigate("папка");
        record("typed relative path resolves against the current filesystem pane", directory == subfolder && Address.Text == subfolder);
        int beforeDuplicate = history.Count;
        await Navigate(subfolder);
        record("revisiting the same directory deduplicates recents and back history",
            history.Count == beforeDuplicate && recentPaths[0] == subfolder && recentPaths.Count(path => path == subfolder) == 1);
        await Navigate(Path.Combine(root, "missing"));
        record("invalid path preserves location and stays out of recents", directory == subfolder && Notice.IsOpen &&
            !recentPaths.Any(path => path.EndsWith("missing", StringComparison.Ordinal)));
        await GoBack();
        record("back restores the previous directory without adding navigation entries", directory == root && history.Count == beforeDuplicate - 1);

        foreach (string mode in new[] { "Details", "List", "SmallIcons", "LargeIcons" })
        {
            view = mode;
            BuildList();
            list.SelectedItems.Clear();
            list.SelectedItems.Add(entries.Single(item => item.FullPath == other));
            ApplySelectionMask("a*.t?t", true);
            record("mask adds case-insensitive matches and preserves unrelated marks in " + mode, list.SelectedItems.Count == 3);
            ApplySelectionMask("*.txt", false);
            record("mask deselects matches without changing the other mark in " + mode,
                list.SelectedItems.Count == 1 && ((Models.FileEntry)list.SelectedItems[0]).FullPath == other);
            OnInvertSelection(this, new RoutedEventArgs());
            record("invert includes files and folders in " + mode, list.SelectedItems.Count == entries.Count - 1 &&
                list.SelectedItems.Cast<Models.FileEntry>().All(item => item.FullPath != other));
        }

        view = "Details"; markMode = false; BuildList();
        list.SelectedItems.Clear();
        lastCurrentItem = entries.Single(item => item.FullPath == a);
        FocusFileList();
        await Task.Delay(120);
        SelectByType(true);
        record("extension selection includes both filename casings and excludes folders",
            list.SelectedItems.Cast<Models.FileEntry>().Select(item => item.FullPath).ToHashSet().SetEquals([a, b]));
        OnMarkMode(MarkModeMenu, new RoutedEventArgs());
        await Task.Delay(120);
        record("changing selection mode retains existing marks",
            markMode && list.SelectionMode == ListViewSelectionMode.Multiple && list.SelectedItems.Count == 2);
        string prefix = FindByTyping("", 'а');
        await Task.Delay(120);
        record("Cyrillic incremental navigation moves the caret without changing independent marks",
            prefix == "а" && CurrentItem()?.FullPath == other && list.SelectedItems.Count == 2);
        OnMarkMode(MarkModeMenu, new RoutedEventArgs());
        await Task.Delay(120);
        prefix = FindByTyping("", 'a');
        await Task.Delay(120);
        prefix = FindByTyping(prefix, 'l');
        await Task.Delay(120);
        record("incremental prefix retains the matching filename in standard selection",
            prefix == "al" && CurrentItem()?.FullPath == a && list.SelectedItems.Count == 1);

        ApplyFilesystemColumns(OptionalFileColumns);
        activePanel.Sort = "Created";
        ApplySort();
        activePanel.TimePrecision = "S";
        activePanel.TimeUtc = true;
        BuildList();
        await Task.Delay(120);
        list.UpdateLayout();
        var texts = DescendantTexts(list).ToArray();
        record("filesystem metadata columns render an actual UTC timestamp and sort by creation time",
            entries.Where(item => !item.IsDirectory && (item.FullPath == a || item.FullPath == b)).First().FullPath == a &&
            texts.Contains("15.10.2025 12:34:56 UTC"));
        record("filesystem-only column contributes a header in Details",
            FindByAutomationId(activePanel.ListHost, "Column-Created"));
        activePanel.Sort = "None";
        ApplySort();
        record("unsorted view restores the provider enumeration order",
            entries.Select(item => item.SourceOrder).SequenceEqual(Enumerable.Range(0, entries.Count).Select(index => (long)index)));

        allowFolderWatches = true;
        autoRefresh = true;
        lastCurrentItem = entries.Single(item => item.FullPath == a);
        list.SelectedItems.Clear();
        list.SelectedItems.Add(lastCurrentItem);
        var pane = activePanel;
        UpdateFolderWatch(pane);
        string external = Path.Combine(root, "added-externally.txt");
        await Task.Run(() => File.WriteAllText(external, "external"));
        for (int i = 0; i < 40 && !entries.Any(item => item.FullPath == external); i++) await Task.Delay(100);
        record("real filesystem notification updates the folder and preserves marks",
            entries.Any(item => item.FullPath == external) && list.SelectedItems.Count == 1 &&
            ((Models.FileEntry)list.SelectedItems[0]).FullPath == a && Address.Text == root);
        OnAutoRefresh(AutoRefreshMenu, new RoutedEventArgs());
        record("turning auto-refresh off releases the directory watcher", pane.Watch == null && !AutoRefreshMenu.IsChecked);
        allowFolderWatches = false;
        await Navigate(empty);
        string temporarilyMoved = Path.Combine(root, "temporarily-moved");
        Directory.Move(empty, temporarilyMoved);
        try
        {
            await RefreshView();
            record("refresh detects loss of the current directory and disables mutations",
                directory == empty && !FilesystemReady && Notice.IsOpen);
        }
        finally { Directory.Move(temporarilyMoved, empty); }
        await RefreshView();
        record("manual retry restores the same directory after it returns", directory == empty && FilesystemReady && !Notice.IsOpen);

        string fileArchive = Path.Combine(fixture, "typed-folders.tar");
        using (var tar = new System.Formats.Tar.TarWriter(File.Create(fileArchive)))
        {
            tar.WriteEntry(a, "inside/Alpha.txt");
            tar.WriteEntry(empty, "empty");
        }
        await Navigate(fileArchive);
        var session = archiveSession;
        record("recent locations store the real archive path and hide filesystem columns",
            session != null && recentPaths[0] == fileArchive && !FindByAutomationId(activePanel.ListHost, "Column-Created"));
        await Navigate(fileArchive + " | inside");
        record("typed archive subfolder uses the existing live session and exact directory",
            archiveSession == session && archivePrefix == "inside/" && entries.Count == 1 && entries[0].Name == "Alpha.txt");
        ApplySelectionMask("*.txt", true);
        record("archive mask selects the real session member", list.SelectedItems.Count == 1 &&
            ((Models.FileEntry)list.SelectedItems[0]).ArchiveIndex.HasValue);
        await Navigate(fileArchive + " | empty");
        record("typed archive path can open an explicit empty directory", archiveSession == session && archivePrefix == "empty/" && entries.Count == 0);
        await Navigate(fileArchive + " | unknown");
        record("invalid archive subfolder leaves the valid session and directory in place", archiveSession == session && archivePrefix == "empty/" && Notice.IsOpen);
    }

    private static bool FindByAutomationId(DependencyObject parent, string id)
    {
        if (AutomationProperties.GetAutomationId(parent) == id) return true;
        for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            if (FindByAutomationId(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i), id)) return true;
        return false;
    }

    private static IEnumerable<string> DescendantTexts(DependencyObject parent)
    {
        if (parent is TextBlock text) yield return text.Text;
        for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (string child in DescendantTexts(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
}
