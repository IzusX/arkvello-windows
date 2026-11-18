using System.Security.Cryptography;
using System.Text.Json;

namespace Arkvello.Services;

// Content identity, rather than timestamp/length alone (editors can preserve both).
public sealed record ArchiveSourceStamp(long Length, string Hash)
{
    public static ArchiveSourceStamp Capture(string path, ArchiveJob? job = null)
    {
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[128 * 1024]; int count;
        while ((count = source.Read(buffer)) != 0) { job?.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, count); }
        job?.ThrowIfCancellationRequested();
        return new(source.Length, Convert.ToHexString(hash.GetHashAndReset()));
    }
}

// One explicit editor hand-off. The user confirms when the editor has finished:
// shell activation can reuse an existing process, so process exit is not a lease.
// Recovery files are deliberately retained until an explicit discard or commit.
public sealed class ArchiveEdit
{
    public ArchiveSession Session { get; }
    public uint Index { get; }
    public string ItemName { get; }
    public ArchiveTemporaryFile WorkingFile { get; }
    public string RecoveryPath => WorkingFile.DirectoryPath;
    private readonly ArchiveSourceStamp source;
    private ArchiveSourceStamp? original;
    private ArchiveSourceStamp? stagedEdit;
    private ArchiveEdit(ArchiveSession session, uint index, ArchiveTemporaryFile file)
    {
        Session = session; Index = index; WorkingFile = file;
        ItemName = session.Entries.Single(e => e.Index == index).Path;
        source = session.RootSourceStamp;
    }
    public static (ArchiveEdit? Edit, ArchiveResult Result) Begin(ArchiveSession session, uint index,
        ArchiveJob job, string? temporaryRoot = null)
    {
        if (ArchiveSourceStamp.Capture(session.RootPath, job) != session.RootSourceStamp)
            throw new IOException("Исходный архив изменён. Обновите его перед редактированием.");
        var file = new ArchiveTemporaryFile(session.Entries.Single(e => e.Index == index).Path, temporaryRoot);
        bool retained = false;
        try
        {
            var result = session.Archive.ExportItem(index, file.Path, job);
            if (!result.Succeeded) return (null, result);
            var edit = new ArchiveEdit(session, index, file);
            edit.original = ArchiveSourceStamp.Capture(file.Path, job);
            edit.WriteJournal("editing", null);
            retained = true;
            return (edit, result);
        }
        finally { if (!retained) file.Dispose(); }
    }
    public bool HasChangesSinceExport(ArchiveJob? job = null) => ArchiveSourceStamp.Capture(WorkingFile.Path, job) != original;
    public (ArchiveTemporaryFile? File, ArchiveResult Result) Stage(ArchiveJob job)
    {
        if (ArchiveSourceStamp.Capture(Session.RootPath, job) != source)
            throw new IOException("Исходный архив изменён другой программой. Правки сохранены отдельно: " + RecoveryPath);
        // Copy while denying writes: never package an editor's half-written file.
        using var snapshot = new ArchiveTemporaryFile(ItemName, RecoveryPath);
        using (var input = new FileStream(WorkingFile.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var output = new FileStream(snapshot.Path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { Copy(input, output, job); }
        var stamp = ArchiveSourceStamp.Capture(snapshot.Path, job);
        var stage = Session.StageReplacement(Index, snapshot.Path, job, RecoveryPath);
        if (stage.File != null)
        {
            // An editor can save again while compression is running.
            if (ArchiveSourceStamp.Capture(WorkingFile.Path, job) != stamp)
            {
                stage.File.Dispose();
                throw new IOException("Файл снова изменён во время подготовки архива. Сохраните его и повторите.");
            }
            WriteJournal("staged", stage.File.Path);
            stagedEdit = stamp;
        }
        return stage;
    }
    // Call only after closing this root's entire native session chain. File.Replace
    // is same-volume and creates a backup of the actual replaced target. Retain it
    // even after success: there is no filesystem compare-and-swap for a pathname.
    public string Commit(ArchiveTemporaryFile staged, ArchiveJob? job = null)
    {
        if (!Session.IsClosed) throw new InvalidOperationException("Закройте сеансы архива перед сохранением.");
        using var editorLease = new FileStream(WorkingFile.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stagedEdit == null || ArchiveSourceStamp.Capture(WorkingFile.Path, job) != stagedEdit)
            throw new IOException("Редактор снова изменил файл после подготовки архива. Правки сохранены: " + RecoveryPath);
        string target = Session.RootPath;
        if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Сохранение через ссылку не поддерживается; сохраните архив отдельно.");
        string prepared = Path.Combine(Path.GetDirectoryName(target)!, ".arkvello-" + Guid.NewGuid().ToString("N") + ".tmp");
        string backup = prepared + ".backup";
        try
        {
            using (var input = File.OpenRead(staged.Path))
            using (var output = new FileStream(prepared, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { Copy(input, output, job); }
            WriteJournal("committing", staged.Path, backup);
            if (ArchiveSourceStamp.Capture(target, job) != source)
                throw new IOException("Исходный архив изменён. Сохранение отменено; правки доступны: " + RecoveryPath);
            File.Replace(prepared, target, backup, ignoreMetadataErrors: false);
            WriteJournal("committed", staged.Path, backup);
            if (ArchiveSourceStamp.Capture(backup) != source)
                throw new IOException("Обнаружено одновременное изменение исходного архива при сохранении. Обе версии сохранены. Резервная копия: " + backup);
            return backup;
        }
        finally { if (File.Exists(prepared)) File.Delete(prepared); }
    }
    private static void Copy(Stream input, FileStream output, ArchiveJob? job)
    {
        byte[] buffer = new byte[128 * 1024]; int count;
        while ((count = input.Read(buffer)) != 0) { job?.ThrowIfCancellationRequested(); output.Write(buffer, 0, count); }
        output.Flush(flushToDisk: true); job?.ThrowIfCancellationRequested();
    }
    private void WriteJournal(string state, string? staged, string? backup = null)
    {
        string path = Path.Combine(RecoveryPath, "recovery.json");
        string pending = path + ".new";
        using (var file = new FileStream(pending, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            JsonSerializer.Serialize(file, new { state, sourcePath = Session.RootPath, source, ItemName, Index, workingFile = WorkingFile.Path, staged, backup });
            file.Flush(flushToDisk: true);
        }
        File.Move(pending, path, overwrite: true);
    }
    public void Discard()
    {
        File.Delete(Path.Combine(RecoveryPath, "recovery.json"));
        File.Delete(Path.Combine(RecoveryPath, "recovery.json.new"));
        WorkingFile.Dispose();
    }
}
