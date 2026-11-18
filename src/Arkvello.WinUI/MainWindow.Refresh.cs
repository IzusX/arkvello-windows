using Microsoft.UI.Xaml;

namespace Arkvello;

public sealed partial class MainWindow
{
    private sealed class FolderWatch : IDisposable
    {
        private readonly FileSystemWatcher watcher;
        private long updatedAt;

        public string Path => watcher.Path;

        public FolderWatch(string path)
        {
            watcher = new FileSystemWatcher(path)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite |
                    NotifyFilters.Size | NotifyFilters.Attributes | NotifyFilters.CreationTime
            };
            watcher.Created += OnChange;
            watcher.Deleted += OnChange;
            watcher.Changed += OnChange;
            watcher.Renamed += OnChange;
            watcher.Error += (_, _) => MarkChanged();
        }

        public void Start() => watcher.EnableRaisingEvents = true;
        private void OnChange(object sender, FileSystemEventArgs args) => MarkChanged();
        private void MarkChanged() => Interlocked.Exchange(ref updatedAt, Environment.TickCount64);

        public bool ConsumeChanges()
        {
            long stamp = Interlocked.Read(ref updatedAt);
            return stamp != 0 && Environment.TickCount64 - stamp >= 350 &&
                Interlocked.CompareExchange(ref updatedAt, 0, stamp) == stamp;
        }

        public void Dispose() => watcher.Dispose();
    }

    private bool autoRefresh = true;
    private bool allowFolderWatches = true;
    private bool fileDragInProgress;
    private readonly DispatcherTimer folderRefreshTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };

    private void OnAutoRefresh(object sender, RoutedEventArgs e)
    {
        if (SelectionReady)
        {
            autoRefresh = !autoRefresh;
            foreach (var pane in panels) UpdateFolderWatch(pane);
        }
        AutoRefreshMenu.IsChecked = autoRefresh;
    }

    private void UpdateFolderWatch(PanelState pane)
    {
        bool visible = twoPanels || activePanel == pane;
        string path = allowFolderWatches && autoRefresh && visible && pane.ArchiveSession == null &&
            pane.DirectoryAvailable ? pane.Directory : "";
        if (pane.Watch?.Path == path) return;
        pane.Watch?.Dispose();
        pane.Watch = null;
        if (path.Length > 0)
        {
            FolderWatch? watch = null;
            try
            {
                watch = new(path);
                watch.Start();
                pane.Watch = watch;
            }
            catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
            {
                // Removable and network providers can reject notifications.
                // The existing manual Refresh command still works.
                watch?.Dispose();
            }
        }
        if (panels.Any(item => item.Watch != null)) folderRefreshTimer.Start();
        else folderRefreshTimer.Stop();
    }

    private async void OnFolderRefreshTick(object? sender, object e)
    {
        if (!SelectionReady || fileDragInProgress || currentDialog != null) return;
        foreach (var pane in panels)
        {
            if (pane.Watch?.ConsumeChanges() != true) continue;
            await NavigateDirectory(pane, pane.Directory, remember: false, refresh: true);
            if (!SelectionReady) break;
        }
    }
}
