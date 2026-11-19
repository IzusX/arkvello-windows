using System.Runtime.InteropServices;
using Arkvello.Models;
using Arkvello.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Rectangle = Microsoft.UI.Xaml.Shapes.Rectangle;
using Windows.Foundation;
using Windows.System;

namespace Arkvello;

public sealed partial class MainWindow
{
    private ShellFileDrag? shellDrag;
    private bool selectingWithMouse;

    private void InitializeShellDrag()
    {
        try
        {
            shellDrag = new(WinRT.Interop.WindowNative.GetWindowHandle(this), ResolveDropFolder,
                () => DispatcherQueue.TryEnqueue(async () => await RefreshAfterDrop()));
        }
        catch (COMException error) { ShowError("Не удалось включить перетаскивание файлов: " + error.Message); }
    }

    private static Rect BoundsIn(FrameworkElement element, UIElement relativeTo) =>
        element.TransformToVisual(relativeTo).TransformBounds(new(0, 0, element.ActualWidth, element.ActualHeight));

    private string? ResolveDropFolder(Point clientPixels)
    {
        if (!SelectionReady || currentDialog != null || WindowHost.XamlRoot is not { } root) return null;
        Point host = new(clientPixels.X / root.RasterizationScale, clientPixels.Y / root.RasterizationScale);
        foreach (var pane in panels)
        {
            if (pane.ListHost.Visibility != Visibility.Visible || !pane.DirectoryAvailable || pane.ArchiveSession != null ||
                pane.List is not { } target || VisualChild<ScrollContentPresenter>(target) is not { } viewport ||
                !BoundsIn(viewport, WindowHost).Contains(host)) continue;

            foreach (var element in VisualTreeHelper.FindElementsInHostCoordinates(host, target))
            {
                if (element is SelectorItem row && target.ItemFromContainer(row) is FileEntry { IsDirectory: true } folder)
                    return folder.FullPath;
            }
            return pane.Directory;
        }
        return null;
    }

    private async Task RefreshAfterDrop()
    {
        if (!SelectionReady || currentDialog != null || fileDragInProgress || selectingWithMouse) return;
        foreach (var pane in panels)
        {
            if (!SelectionReady) break;
            if (pane.ListHost.Visibility == Visibility.Visible && pane.ArchiveSession == null && pane.DirectoryAvailable)
                await NavigateDirectory(pane, pane.Directory, remember: false, refresh: true);
        }
    }

    private async void StartFileDrag(PanelState pane, FileEntry[] files)
    {
        if (shellDrag == null || fileDragInProgress || !FilesystemReady || pane != activePanel) return;
        fileDragInProgress = true;
        try
        {
            var data = ShellFileDrag.CreateData(pane.Directory, files.Select(item => item.FullPath).ToArray());
            try { shellDrag.Start(data); }
            finally { Marshal.ReleaseComObject(data); }
        }
        catch (COMException error) { ShowError("Не удалось перетащить файлы: " + error.Message); }
        finally { fileDragInProgress = false; }
        await RefreshAfterDrop();
    }

    private void ConfigureFileGestures(PanelState pane, ListViewBase target, Grid layout)
    {
        var overlay = new Canvas { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        var outline = new Rectangle { StrokeThickness = 1 };
        overlay.Children.Add(outline);
        Grid.SetRow(overlay, 1);
        layout.Children.Add(overlay);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        ScrollViewer? scroll = null;
        ScrollContentPresenter? viewport = null;
        Pointer? pointer = null;
        SelectorItem? pressedRow = null;
        FileEntry[] dragged = [];
        var initial = new HashSet<FileEntry>();
        var selected = new HashSet<FileEntry>();
        var bounds = new Dictionary<FileEntry, Rect>();
        Point start = default, anchor = default, cursor = default;
        Size threshold = default;
        bool started = false, marquee = false, toggle = false, extend = false;

        Point Offset(Point point) => new(point.X + scroll!.HorizontalOffset, point.Y + scroll.VerticalOffset);

        void UpdateMarquee()
        {
            if (!started || viewport == null || scroll == null) return;
            var end = Offset(new(Math.Clamp(cursor.X, 0, viewport.ActualWidth), Math.Clamp(cursor.Y, 0, viewport.ActualHeight)));
            var region = new Rect(Math.Min(anchor.X, end.X), Math.Min(anchor.Y, end.Y),
                Math.Abs(anchor.X - end.X), Math.Abs(anchor.Y - end.Y));
            var drawing = new Rect(region.X - scroll.HorizontalOffset, region.Y - scroll.VerticalOffset,
                region.Width, region.Height);
            drawing.Intersect(new Rect(0, 0, viewport.ActualWidth, viewport.ActualHeight));
            var origin = viewport.TransformToVisual(overlay).TransformPoint(new Point());
            if (drawing.IsEmpty) outline.Width = outline.Height = 0;
            else
            {
                Canvas.SetLeft(outline, origin.X + drawing.X);
                Canvas.SetTop(outline, origin.Y + drawing.Y);
                outline.Width = drawing.Width;
                outline.Height = drawing.Height;
            }

            if (target.ItemsPanelRoot is not { } panel) return;
            foreach (var child in panel.Children)
            {
                if (child is not SelectorItem { Visibility: Visibility.Visible } row ||
                    target.ItemFromContainer(row) is not FileEntry item) continue;
                var rect = BoundsIn(row, viewport);
                var position = Offset(new(rect.X, rect.Y));
                bounds[item] = new(position.X, position.Y, rect.Width, rect.Height);
            }
            var desired = new HashSet<FileEntry>(initial);
            foreach (var (item, rect) in bounds)
            {
                var overlap = rect;
                overlap.Intersect(region);
                if (overlap.Width <= 0 || overlap.Height <= 0) continue;
                if (!toggle || !desired.Remove(item)) desired.Add(item);
            }
            if (selected.SetEquals(desired)) return;

            pane.UpdatingSelection = true;
            try
            {
                foreach (var item in selected.Except(desired)) target.SelectedItems.Remove(item);
                foreach (var item in desired.Except(selected)) target.SelectedItems.Add(item);
                selected = desired;
            }
            finally { pane.UpdatingSelection = false; }
            UpdatePanelStatus(pane);
            if (pane == activePanel) UpdateFileCommands();
        }

        void EndGesture()
        {
            var captured = pointer;
            bool wasMarquee = marquee;
            pointer = null;
            pressedRow = null;
            dragged = [];
            marquee = started = false;
            if (!wasMarquee) return;
            selectingWithMouse = false;
            timer.Stop();
            overlay.Visibility = Visibility.Collapsed;
            bounds.Clear();
            initial.Clear();
            selected.Clear();
            if (captured != null) target.ReleasePointerCapture(captured);
        }

        timer.Tick += (_, _) =>
        {
            if (scroll == null || viewport == null || !SelectionReady) { EndGesture(); return; }
            double edge = Math.Min(24, viewport.ActualHeight / 4);
            double delta = cursor.Y < edge ? -Math.Min(64, edge - cursor.Y)
                : cursor.Y > viewport.ActualHeight - edge ? Math.Min(64, cursor.Y - viewport.ActualHeight + edge) : 0;
            if (delta != 0)
            {
                scroll.ChangeView(null, Math.Clamp(scroll.VerticalOffset + delta, 0, scroll.ScrollableHeight), null, true);
                UpdateMarquee();
            }
        };

        target.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            if (pointer != null || e.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse ||
                !SelectionReady || !ActivatePanel(pane) || target.XamlRoot == null) return;
            scroll = VisualChild<ScrollViewer>(target);
            viewport = scroll == null ? null : VisualChild<ScrollContentPresenter>(scroll);
            if (viewport == null) return;
            var point = e.GetCurrentPoint(viewport);
            if (!point.Properties.IsLeftButtonPressed || point.Properties.PointerUpdateKind != Microsoft.UI.Input.PointerUpdateKind.LeftButtonPressed ||
                !new Rect(0, 0, viewport.ActualWidth, viewport.ActualHeight).Contains(point.Position)) return;

            SelectorItem? row = null;
            for (var element = e.OriginalSource as DependencyObject; element != null && !ReferenceEquals(element, target); element = VisualTreeHelper.GetParent(element))
            {
                if (element is ScrollBar) return;
                if (element is SelectorItem item) { row = item; break; }
            }
            // File details and list templates reserve a left gutter before the
            // item icon. It also gives long lists room to start a marquee.
            bool gutter = target is ListView && row != null && e.GetCurrentPoint(row).Position.X < 12;
            threshold = ShellFileDrag.DragThreshold(target.XamlRoot, scalePercent / 100d);
            start = point.Position;
            if (!gutter && row != null)
            {
                if (shellDrag == null || !FilesystemReady || target.ItemFromContainer(row) is not FileEntry file) return;
                dragged = target.SelectedItems.Contains(file) ? target.SelectedItems.Cast<FileEntry>().ToArray() : [file];
                pressedRow = row;
                pointer = e.Pointer;
                return;
            }

            if (gutter) row!.ReleasePointerCapture(e.Pointer);
            if (!target.CapturePointer(e.Pointer)) return;
            pointer = e.Pointer;
            marquee = selectingWithMouse = true;
            anchor = Offset(start);
            toggle = KeyDown(VirtualKey.Control);
            extend = KeyDown(VirtualKey.Shift) || pane.MarkMode || toggle;
            selected = target.SelectedItems.Cast<FileEntry>().ToHashSet();
            initial = extend ? new(selected) : [];
            e.Handled = true;
        }), true);

        target.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((_, e) =>
        {
            if (pointer?.PointerId != e.Pointer.PointerId || viewport == null) return;
            var point = e.GetCurrentPoint(viewport);
            if (!SelectionReady || !point.Properties.IsLeftButtonPressed) { EndGesture(); return; }
            cursor = point.Position;
            if (!started && Math.Abs(cursor.X - start.X) < threshold.Width && Math.Abs(cursor.Y - start.Y) < threshold.Height) return;
            e.Handled = true;
            if (!marquee)
            {
                var files = dragged;
                var row = pressedRow;
                EndGesture();
                row?.ReleasePointerCapture(e.Pointer);
                // Return to the dispatcher before entering OLE's modal loop.
                DispatcherQueue.TryEnqueue(() => StartFileDrag(pane, files));
                return;
            }
            if (!started)
            {
                started = true;
                target.Focus(FocusState.Pointer);
                var color = uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
                if (accessibility.HighContrast) color = uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground);
                outline.Stroke = new SolidColorBrush(color);
                color.A = 48;
                outline.Fill = new SolidColorBrush(color);
                overlay.Visibility = Visibility.Visible;
                timer.Start();
            }
            UpdateMarquee();
        }), true);

        void FinishPointer(object sender, PointerRoutedEventArgs e)
        {
            if (pointer?.PointerId != e.Pointer.PointerId) return;
            if (marquee && !started && !extend && e.GetCurrentPoint(target).Properties.PointerUpdateKind == Microsoft.UI.Input.PointerUpdateKind.LeftButtonReleased)
                target.SelectedItems.Clear();
            EndGesture();
        }
        target.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(FinishPointer), true);
        target.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(FinishPointer), true);
        target.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(FinishPointer), true);
        target.Unloaded += (_, _) => EndGesture();
        target.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Escape && pointer != null) { EndGesture(); e.Handled = true; }
        };
    }
}
