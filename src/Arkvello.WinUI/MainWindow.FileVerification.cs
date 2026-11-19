using System.Text.Json;
using Arkvello.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;

namespace Arkvello;

public sealed partial class MainWindow
{
    // Application-owned integration checks, not injected user input. All file
    // mutations use a new fixture directory; the system clipboard is untouched.
    private async Task VerifyStage4(string evidenceDirectory)
    {
        var results = new List<object>();
        string output = Path.GetFullPath(evidenceDirectory);
        string fixture = Path.Combine(output, "files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        void Record(string name, bool passed)
        {
            results.Add(new { name, passed });
            File.WriteAllText(Path.Combine(output, "client-checks.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            if (!passed) throw new InvalidOperationException(name);
        }
        async Task FocusItem(string path)
        {
            await Task.Delay(120); list.UpdateLayout();
            var item = entries.Single(e => e.FullPath == path);
            list.SelectedItem = item; lastCurrentItem = item;
            list.ScrollIntoView(item); list.UpdateLayout();
            if (list.ContainerFromItem(item) is Control container) container.Focus(FocusState.Programmatic);
            UpdateStatus();
        }
        try
        {
            string original = FileSystemOperations.Create(fixture, "длинное имя Ω для переименования.txt", false);
            File.WriteAllText(original, "stage 4 real file");
            string occupied = FileSystemOperations.Create(fixture, "occupied.txt", false);
            File.WriteAllText(occupied, "untouched");
            await Navigate(fixture, false); await FocusItem(original);
            Record("filesystem commands enabled in directory", CopyToButton.IsEnabled && MoveToButton.IsEnabled && DeleteButton.IsEnabled && RenameMenu.IsEnabled && !ExtractButton.IsEnabled);
            BeginRename();
            var item = CurrentItem()!;
            Record("inline editor contains the real name and selects its stem", renameBox != null && renameBox.Text == item.Name && renameBox.SelectedText == Path.GetFileNameWithoutExtension(item.Name));
            var editor = renameBox!; editor.Text = "invalid:name.txt";
            bool invalid = await CommitRename(item, editor);
            Record("invalid inline rename keeps editor input and original", !invalid && ReferenceEquals(renameBox, editor) && editor.Text == "invalid:name.txt" && File.Exists(original));
            editor.Text = "occupied.txt";
            bool collision = await CommitRename(item, editor);
            Record("inline collision keeps both originals and editor", !collision && ReferenceEquals(renameBox, editor) && File.ReadAllText(occupied) == "untouched" && File.Exists(original));
            editor.Text = "переименован Ω.txt";
            bool renamed = await CommitRename(item, editor);
            string renamedPath = Path.Combine(fixture, "переименован Ω.txt");
            await Task.Delay(150); list.UpdateLayout();
            Record("inline commit refreshes selection and file bytes", renamed && renameBox == null && !File.Exists(original) && File.ReadAllText(renamedPath) == "stage 4 real file" && list.SelectedItem is Models.FileEntry selected && selected.FullPath == renamedPath);
            foreach (string mode in new[] { "Details", "List", "SmallIcons", "LargeIcons" })
            {
                view = mode; BuildList(); await FocusItem(renamedPath); BeginRename();
                await Task.Delay(100); list.UpdateLayout();
                Record("inline editor has a finite visible slot in " + mode, renameBox is { ActualWidth: > 0, ActualHeight: > 0 } box && double.IsFinite(box.ActualWidth));
                renameBox!.Text = "cancelled.txt"; CancelRename(); UpdateStatus();
                Record("inline cancel preserves file in " + mode, File.Exists(renamedPath) && !File.Exists(Path.Combine(fixture, "cancelled.txt")) && renameBox == null);
            }
            view = "Details"; BuildList(); await FocusItem(renamedPath);
            markMode = true; list.SelectionMode = ListViewSelectionMode.Multiple;
            list.SelectedItems.Clear(); list.SelectedItems.Add(entries.Single(e => e.FullPath == occupied));
            lastCurrentItem = entries.Single(e => e.FullPath == renamedPath);
            if (list.ContainerFromItem(lastCurrentItem) is Control caret) caret.Focus(FocusState.Programmatic);
            Record("filesystem operation selection uses marks before caret", OperatedFiles().Length == 1 && OperatedFiles()[0].FullPath == occupied && CurrentItem()?.FullPath == renamedPath);
            await RefreshView(); await Task.Delay(150);
            Record("filesystem refresh retains marks and independent caret", OperatedFiles().Length == 1 && OperatedFiles()[0].FullPath == occupied && CurrentItem()?.FullPath == renamedPath);
            markMode = false; list.SelectionMode = ListViewSelectionMode.Extended;
            string target = Directory.CreateDirectory(Path.Combine(fixture, "destination")).FullName;
            var copy = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            copy.SetStorageItems(await StorageItems([renamedPath]));
            var copied = await PasteFiles(copy.GetView(), target);
            Record("StorageItems paste uses real upstream copy without system clipboard", copied?.Succeeded == true && File.ReadAllText(Path.Combine(target, Path.GetFileName(renamedPath))) == "stage 4 real file" && File.Exists(renamedPath));
            var drag = ShellFileDrag.CreateData(fixture, [renamedPath, occupied]);
            try { Record("OLE drop target registers and outbound Shell object supplies the file list", shellDrag != null && ShellFileDrag.ContainsFiles(drag)); }
            finally { System.Runtime.InteropServices.Marshal.ReleaseComObject(drag); }
            string moveTarget = Directory.CreateDirectory(Path.Combine(fixture, "move-destination")).FullName;
            var move = new DataPackage { RequestedOperation = DataPackageOperation.Move };
            move.SetStorageItems(await StorageItems([Path.Combine(target, Path.GetFileName(renamedPath))]));
            var moved = await PasteFiles(move.GetView(), moveTarget);
            Record("StorageItems move paste retains bytes and removes only source", moved?.Succeeded == true && !File.Exists(Path.Combine(target, Path.GetFileName(renamedPath))) && File.ReadAllText(Path.Combine(moveTarget, Path.GetFileName(renamedPath))) == "stage 4 real file");
            var unsupported = new DataPackage(); unsupported.SetText("not a file list");
            try { await PasteFiles(unsupported.GetView(), target); Record("text paste cannot invoke a file operation", false); }
            catch (NotSupportedException) { Record("text paste cannot invoke a file operation", !busy && !configuring); }
            var notArchive = await OpenArchive(renamedPath, false, quietNotArchive: true);
            Record("plain file reaches explicit non-archive result without launching an app", notArchive?.HResult == 1 && archiveSession == null && directory == fixture && !busy);
            await VerifyShellIcons(fixture, renamedPath, target, Record);
            foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
            {
                WindowHost.RequestedTheme = theme; RefreshColors(); await FocusItem(renamedPath); Notice.IsOpen = false;
                await Task.Delay(120);
                var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(WindowHost);
                var pixels = await bitmap.GetPixelsAsync();
                using var file = new FileStream(Path.Combine(output, $"{theme}-files.png"), FileMode.Create, FileAccess.ReadWrite);
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, file.AsRandomAccessStream());
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray()); await encoder.FlushAsync();
                Record("filesystem controls render in " + theme, CopyToButton.ActualWidth > 0 && CopyToButton.IsEnabled && list.ActualHeight > 0);
            }
            await OpenArchive(Path.Combine(AppContext.BaseDirectory, "7z.dll"), false);
            Record("archive context cannot enable filesystem mutations", archiveSession != null && !FilesystemReady && !CopyToButton.IsEnabled && !MoveToButton.IsEnabled && !DeleteButton.IsEnabled && !RenameMenu.IsEnabled);
            Record("archive rows keep native Fluent fallback and make no shell requests", activePanel.RefreshIcons == null);
            await VerifyPanels(output, fixture, renamedPath, target, Record);
            await VerifyFileNavigationAndSelection(fixture, Record);
            await VerifyShellDrop(fixture, target, Record);
            await VerifyCloseDuringTransfer(fixture, Record);
        }
        catch (Exception error) { results.Add(new { name = "unexpected failure", passed = false, error = error.ToString() }); Environment.ExitCode = 1; }
        finally
        {
            File.WriteAllText(Path.Combine(output, "client-checks.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            if (!closing) RequestClose();
        }
    }

    private async Task VerifyShellDrop(string fixture, string destination, Action<string, bool> record)
    {
        string sources = Directory.CreateDirectory(Path.Combine(fixture, "ole-sources")).FullName;
        string copy = Path.Combine(sources, "copy Ω.txt");
        string move = Path.Combine(sources, "move.txt");
        File.WriteAllText(copy, "shell copy");
        File.WriteAllText(move, "shell move");
        await NavigateDirectory(activePanel, fixture, remember: false, refresh: true);
        var folder = entries.Single(item => item.FullPath == destination);
        list.ScrollIntoView(folder);
        list.UpdateLayout();
        if (list.ContainerFromItem(folder) is not FrameworkElement row || WindowHost.XamlRoot is not { } root || shellDrag == null)
            throw new InvalidOperationException("Could not realize the OLE drop test folder.");

        var point = row.TransformToVisual(WindowHost).TransformPoint(new(28, row.ActualHeight / 2));
        var pixels = new Windows.Foundation.Point(point.X * root.RasterizationScale, point.Y * root.RasterizationScale);
        fileDragInProgress = true;
        try
        {
            foreach (string source in new[] { copy, move })
            {
                bool moving = source == move;
                string expected = Path.Combine(destination, Path.GetFileName(source));
                var data = ShellFileDrag.CreateData(sources, [source]);
                try { shellDrag.DropForVerification(data, pixels, moving); }
                finally { System.Runtime.InteropServices.Marshal.ReleaseComObject(data); }
                for (int i = 0; i < 60 && (!File.Exists(expected) || moving && File.Exists(source)); ++i)
                    await Task.Delay(25);
                record(moving ? "OLE Shell move reaches the folder row and removes only its source"
                    : "OLE Shell copy reaches the folder row and preserves the Unicode source",
                    File.Exists(expected) && File.Exists(source) != moving &&
                    File.ReadAllText(expected) == (moving ? "shell move" : "shell copy"));
            }
        }
        finally { fileDragInProgress = false; }
    }

    private async Task VerifyCloseDuringTransfer(string fixture, Action<string, bool> record)
    {
        await Navigate(fixture, remember: false);
        string source = Path.Combine(fixture, "close-during-copy.txt");
        string target = Directory.CreateDirectory(Path.Combine(fixture, "close-during-copy")).FullName;
        string existing = Path.Combine(target, Path.GetFileName(source));
        File.WriteAllText(source, "source");
        File.WriteAllText(existing, "destination");
        bool prompted = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) =>
        {
            if (promptDialog == null) return;
            timer.Stop();
            prompted = true;
            RequestClose();
        };
        Closed += (_, _) => record("closing during a filesystem overwrite prompt cancels the worker and preserves both files",
            prompted && operationCancelled && activeJob == null && !busy &&
            File.ReadAllText(source) == "source" && File.ReadAllText(existing) == "destination");
        timer.Start();
        try
        {
            await RunOperation("Проверка закрытия при копировании", job => FileSystemOperations.Transfer([source], target, false, job),
                showResult: false);
            if (!prompted) record("close test opens the real filesystem overwrite prompt", false);
        }
        finally { timer.Stop(); }
    }

    private async Task VerifyPanels(string output, string fixture, string source, string destination, Action<string, bool> record)
    {
        var left = panels[0];
        var right = panels[1];
        // The inner archive is a real PE member of a temporary TAR. Closing the
        // panel would release its temporary file and break the retained session.
        string containerPath = Path.Combine(fixture, "nested.tar");
        using (var tar = new System.Formats.Tar.TarWriter(File.Create(containerPath)))
            tar.WriteEntry(left.ArchiveSession!.RootPath, "inner.dll");
        await OpenArchive(containerPath, false);
        var outer = left.ArchiveSession!;
        await OpenNestedArchive(left.Entries.Single(e => e.Name == "inner.dll"));
        var leftArchive = left.ArchiveSession!;
        record("left pane opens an actual nested archive with its own temporary file",
            leftArchive.Parent == outer && File.Exists(leftArchive.PhysicalPath));
        await SetTwoPanels(true);
        await Task.Delay(150);
        record("two independently laid out lists with first-use filesystem path",
            twoPanels && right.Directory == Path.GetDirectoryName(leftArchive.RootPath) &&
            left.List.ActualWidth > 0 && right.List.ActualWidth > 0 &&
            left.List != right.List && left.ActiveLine.Visibility == Visibility.Visible);

        ActivatePanel(right);
        await Navigate(destination);
        await GoBack();
        record("right navigation and back leave the left archive open",
            right.Directory == Path.GetDirectoryName(leftArchive.RootPath) && right.History.Count == 0 &&
            left.ArchiveSession == leftArchive && !leftArchive.IsClosed);
        await OpenArchive(leftArchive.RootPath, true);
        var rightArchive = right.ArchiveSession;
        record("opening the same archive twice creates separate sessions",
            rightArchive != null && rightArchive.Identity != leftArchive.Identity && !leftArchive.IsClosed);

        await SetTwoPanels(false);
        record("single view keeps the active right pane and suspends left in place",
            right.ListHost.Visibility == Visibility.Visible && Grid.GetColumn(right.ListHost) == 0 &&
            left.ListHost.Visibility == Visibility.Collapsed && !leftArchive.IsClosed && right.ArchiveSession == rightArchive);
        await SetTwoPanels(true);
        record("reopening split retains both sessions and restores visual order",
            Grid.GetColumn(left.ListHost) == 0 && Grid.GetColumn(right.ListHost) == 2 &&
            left.ArchiveSession == leftArchive && leftArchive.Parent == outer && File.Exists(leftArchive.PhysicalPath) &&
            right.ArchiveSession == rightArchive && rightArchive is { IsClosed: false });

        await Navigate(destination);
        record("leaving one archive releases only its own session",
            rightArchive is { IsClosed: true } && !leftArchive.IsClosed && right.Directory == destination);
        view = "SmallIcons";
        right.Columns[0] = right.Columns[0] with { Width = 240 };
        BuildList();
        ActivatePanel(left);
        record("views and column widths are owned by each pane",
            left.View == "Details" && left.Columns[0].Width != right.Columns[0].Width && right.List is GridView);
        await Navigate(fixture);
        record("navigating away closes the owned nested chain and its temporary file",
            leftArchive.IsClosed && outer.IsClosed && !File.Exists(leftArchive.PhysicalPath));
        var row = left.Entries.Single(e => e.FullPath == source);
        left.List.SelectedItem = row;
        left.Current = row;
        FocusFileList();
        await Task.Delay(150);
        record("filesystem transfer defaults to the other pane's directory",
            activePanel == left && OtherFilesystemDirectory == destination && CopyToButton.IsEnabled);

        var result = await TransferFiles([source], OtherFilesystemDirectory, move: false);
        string copied = Path.Combine(destination, Path.GetFileName(source));
        record("copy updates both lists and keeps source selection",
            result?.Succeeded == true && File.ReadAllText(copied) == File.ReadAllText(source) &&
            right.Entries.Any(e => e.FullPath == copied) && left.List.SelectedItem is Models.FileEntry marked && marked.FullPath == source);
        ActivatePanel(right);
        right.List.SelectedItem = right.Entries.Single(e => e.FullPath == copied);
        await NavigateDirectory(left, Path.Combine(fixture, "move-destination"), remember: true);
        record("passive navigation preserves active right selection and commands",
            activePanel == right && right.List.SelectedItems.Count == 1 && CopyToButton.IsEnabled);
        string moved = Path.Combine(OtherFilesystemDirectory, "cross-panel");
        Directory.CreateDirectory(moved);
        await NavigateDirectory(left, moved, remember: true);
        result = await TransferFiles([copied], OtherFilesystemDirectory, move: true);
        record("move removes source from right and appears in left",
            result?.Succeeded == true && !File.Exists(copied) && right.Entries.All(e => e.FullPath != copied) &&
            left.Entries.Any(e => e.FullPath == Path.Combine(moved, Path.GetFileName(source))));

        var previousSize = AppWindow.Size;
        AppWindow.Resize(new((int)(546 * Root.XamlRoot.RasterizationScale), previousSize.Height));
        await Task.Delay(180);
        record("narrow window keeps both path fields and both list viewports usable",
            left.Address.ActualWidth > 64 && right.Address.ActualWidth > 64 &&
            left.List.ActualWidth > 220 && right.List.ActualWidth > 220);
        AppWindow.Resize(previousSize);
        await Task.Delay(180);
        await NavigateDirectory(right, fixture, remember: false);
        FocusFileList();
        right.View = "Details";
        BuildList(right);
        foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
        {
            WindowHost.RequestedTheme = theme;
            RefreshColors();
            await Task.Delay(100);
            ResizePanels(-100);
            ListBand.UpdateLayout();
            record($"split widths align across path, list and status in {theme}",
                left.ListHost.ActualWidth > 0 && right.ListHost.ActualWidth > left.ListHost.ActualWidth &&
                Math.Abs(left.Navigation.ActualWidth - left.ListHost.ActualWidth) < 1 &&
                Math.Abs(right.StatusHost.ActualWidth - right.ListHost.ActualWidth) < 1);
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(WindowHost);
            var pixels = await bitmap.GetPixelsAsync();
            using var file = File.Open(Path.Combine(output, $"{theme}-panels.png"), FileMode.Create);
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, file.AsRandomAccessStream());
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray());
            await encoder.FlushAsync();
        }
    }
}
