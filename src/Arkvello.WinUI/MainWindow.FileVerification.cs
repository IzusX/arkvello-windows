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
            var drag = new DataPackage(); SetDraggedFiles(drag, [renamedPath]);
            var dragItems = await drag.GetView().GetStorageItemsAsync();
            Record("outbound delayed drag provider supplies the exact storage item", dragItems.Count == 1 && dragItems[0].Path == renamedPath && drag.RequestedOperation == (DataPackageOperation.Copy | DataPackageOperation.Move));
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
        }
        catch (Exception error) { results.Add(new { name = "unexpected failure", passed = false, error = error.ToString() }); Environment.ExitCode = 1; }
        finally
        {
            File.WriteAllText(Path.Combine(output, "client-checks.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            RequestClose();
        }
    }
}
