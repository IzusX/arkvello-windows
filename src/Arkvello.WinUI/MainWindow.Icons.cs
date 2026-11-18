using System.Runtime.InteropServices.WindowsRuntime;
using Arkvello.Models;
using Arkvello.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Arkvello;

public sealed partial class MainWindow
{
    private readonly ShellFileIcons shellIcons = new();
    private readonly Dictionary<(string Path, int Pixels), WriteableBitmap> shellIconCache = [];
    private readonly Queue<(string Path, int Pixels)> shellIconOrder = [];

    private void ClearShellIconCache()
    {
        shellIconCache.Clear();
        shellIconOrder.Clear();
    }

    private void ConfigureShellIcons(PanelState pane, ListViewBase owner, int size)
    {
        var realized = new Dictionary<SelectorItem, (FileEntry Entry, Image Image, Grid Fallback)>();
        var pending = new Dictionary<SelectorItem, CancellationTokenSource>();

        void Cancel(SelectorItem container)
        {
            if (pending.Remove(container, out var request))
            {
                request.Cancel();
                request.Dispose();
            }
        }

        async void UpdateIcon(SelectorItem container)
        {
            if (closing || pane.List != owner) return;
            Cancel(container);
            var (item, image, fallback) = realized[container];
            var scale = owner.XamlRoot?.RasterizationScale ?? 1;
            int pixels = Math.Clamp((int)Math.Ceiling(size * scale * scalePercent / 100), 16, 128);
            var key = (item.FullPath, pixels);
            if (!shellIconCache.TryGetValue(key, out var bitmap))
            {
                using var request = new CancellationTokenSource();
                CancellationToken token = request.Token;
                pending.Add(container, request);
                var data = await shellIcons.GetAsync(item.FullPath, item.Attributes, pixels, token);
                if (token.IsCancellationRequested) return;
                pending.Remove(container);
                if (data == null || closing || pane.List != owner || !ReferenceEquals(owner.ItemFromContainer(container), item)) return;
                if (!shellIconCache.TryGetValue(key, out bitmap))
                {
                    bitmap = new WriteableBitmap(pixels, pixels);
                    using (var target = bitmap.PixelBuffer.AsStream()) target.Write(data.Pixels);
                    bitmap.Invalidate();
                    if (shellIconCache.Count >= 384) shellIconCache.Remove(shellIconOrder.Dequeue());
                    shellIconCache.Add(key, bitmap);
                    shellIconOrder.Enqueue(key);
                }
            }
            image.Source = bitmap;
            image.Visibility = Visibility.Visible;
            fallback.Visibility = Visibility.Collapsed;
        }

        owner.ContainerContentChanging += (_, args) =>
        {
            if (args.ItemContainer is not SelectorItem container) return;
            Cancel(container);
            if (realized.Remove(container, out var previous))
            {
                previous.Image.Source = null;
                previous.Image.Visibility = Visibility.Collapsed;
                previous.Fallback.Visibility = Visibility.Visible;
            }
            if (args.InRecycleQueue) return;
            args.RegisterUpdateCallback((_, next) =>
            {
                if (closing || pane.List != owner || next.InRecycleQueue || next.Item is not FileEntry item || !Path.IsPathFullyQualified(item.FullPath) ||
                    !ReferenceEquals(owner.ItemFromContainer(container), item) ||
                    container.ContentTemplateRoot is not FrameworkElement root ||
                    root.FindName("ShellIcon") is not Image image || root.FindName("FallbackIcon") is not Grid fallback) return;
                realized[container] = (item, image, fallback);
                UpdateIcon(container);
            });
        };
        owner.Unloaded += (_, _) =>
        {
            foreach (var container in pending.Keys.ToArray()) Cancel(container);
        };
        pane.RefreshIcons = () =>
        {
            foreach (var container in realized.Keys)
                if (ReferenceEquals(owner.ItemFromContainer(container), realized[container].Entry)) UpdateIcon(container);
        };
    }
}
