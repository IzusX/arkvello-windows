using Arkvello.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Arkvello;

public sealed partial class MainWindow
{
    private bool CanDeleteArchiveItems => SelectionReady && archive is { CanDeleteItems: true } &&
        (list.SelectedItems.Count > 0 || CurrentItem() != null);

    private async Task DeleteArchiveItems()
    {
        if (!CanDeleteArchiveItems || archiveSession is not { } session) return;
        var selected = OperatedFiles();
        uint[] indices = ArchiveIndices(selected);
        if (indices.Length == 0) return;
        string path = session.RootPath;
        string prefix = archivePrefix;
        var chain = new Stack<(uint Index, string Folder)>();
        for (var level = session; level.Parent != null; level = level.Parent)
            chain.Push((level.ParentIndex!.Value, level.ParentFolder));
        string description = string.Join("\n", selected.Take(5).Select(e => e.Name));
        if (selected.Length > 5) description += $"\nИ ещё: {selected.Length - 5:N0}";
        var dialog = NewDialog("Удалить из архива?", new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap },
            "Удалить", "Отмена");
        dialog.DefaultButton = ContentDialogButton.Close;
        configuring = true; UpdateStatus();
        ContentDialogResult answer;
        try { answer = await dialog.ShowAsync(); }
        finally { configuring = false; UpdateStatus(); }
        if (closing || answer != ContentDialogResult.Primary) { if (!closing) FocusFileList(); return; }

        ArchiveTemporaryFile? output = null;
        bool preserve = false;
        ArchiveSourceStamp? source = null;
        try
        {
            var staged = await RunOperation("Удаление элементов из архива", job =>
            {
                if ((File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.ReadOnly)) != 0)
                    throw new IOException("Изменение архива по ссылке или файла только для чтения недоступно.");
                source = session.CaptureRootSourceStamp(job);
                var result = session.StageDeletion(indices, job, Path.GetDirectoryName(path));
                output = result.File;
                return result.Result;
            }, showSuccess: false, cancellationMessage: "Изменения архива отменены до сохранения.");
            if (closing || staged?.Succeeded != true || output == null || source == null) return;

            CloseArchives(); archivePrefix = "";
            string backup = Path.Combine(Path.GetDirectoryName(path)!, ".arkvello-" + Guid.NewGuid().ToString("N") + ".backup");
            var saved = await RunOperation("Сохранение архива", job =>
            {
                if (ArchiveSourceStamp.Capture(path, job) != source)
                    throw new IOException("Другой процесс изменил архив во время операции.");
                try
                {
                    File.Replace(output.Path, path, backup, ignoreMetadataErrors: false);
                    if (ArchiveSourceStamp.Capture(backup) != source)
                        throw new IOException("Другой процесс изменил архив при сохранении.");
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    preserve = true;
                    throw new IOException($"{error.Message}\nПодготовленный архив: {output.Path}\nПуть резервной копии: {backup}", error);
                }
                return new(0, job.Issues);
            }, showSuccess: false, cancellationMessage: "Исходный архив сохранён.");
            if (closing) return;
            string message = Notice.Message;
            await OpenArchive(path, remember: false);
            bool restored = true;
            foreach (var next in chain)
            {
                if (closing || archiveSession == null) return;
                archivePrefix = next.Folder; ShowArchiveFolder();
                if (entries.FirstOrDefault(e => e.ArchiveIndex == next.Index) is not { } child) { restored = false; break; }
                var parent = archiveSession;
                await OpenNestedArchive(child);
                if (closing || ReferenceEquals(archiveSession, parent)) { restored = false; break; }
            }
            if (!closing && archiveSession != null && restored)
            {
                archivePrefix = prefix; ShowArchiveFolder(); FocusFileList();
            }
            if (saved?.Succeeded == true)
            {
                Notice.Title = "Архив сохранён";
                Notice.Message = "Резервная копия исходного архива: " + backup;
                Notice.Severity = InfoBarSeverity.Informational; Notice.IsOpen = true;
            }
            else if (!string.IsNullOrEmpty(message)) ShowError(message);
        }
        finally { if (!preserve) output?.Dispose(); }
    }
}
