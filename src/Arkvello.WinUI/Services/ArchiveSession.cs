namespace Arkvello.Services;

// A level owns its native session and (for a child) exactly one temporary file.
// Parent lifetime is controlled by the workspace/UI, never by a child finalizer.
// All methods run serially on the operation worker.
public sealed class ArchiveSession : IDisposable
{
    private readonly ArchiveTemporaryFile? temporary;
    private FileStream? rootSource;
    private ArchiveSourceStamp? rootSourceStamp;
    public Guid Identity { get; } = Guid.NewGuid();
    public NativeArchive Archive { get; }
    public IReadOnlyList<ArchiveEntry> Entries { get; }
    public ArchiveSession? Parent { get; }
    public uint? ParentIndex { get; }
    public string ParentFolder { get; }
    public string PhysicalPath { get; }
    public string DisplayPath { get; }
    public string RootPath => Parent?.RootPath ?? PhysicalPath;
    public ArchiveSourceStamp? RootSourceStamp => Parent == null ? rootSourceStamp : Parent.RootSourceStamp;
    public bool IsClosed => Archive.IsClosed;
    private ArchiveSession(NativeArchive archive, IReadOnlyList<ArchiveEntry> entries,
        string path, string display, ArchiveSession? parent, uint? parentIndex,
        string parentFolder, ArchiveTemporaryFile? temporary)
    {
        Archive = archive; Entries = entries; PhysicalPath = path; DisplayPath = display;
        Parent = parent; ParentIndex = parentIndex; ParentFolder = parentFolder; this.temporary = temporary;
    }
    public static (ArchiveSession? Session, ArchiveResult Result) Open(string path, ArchiveJob job, bool captureSource = false)
    {
        path = Path.GetFullPath(path);
        // Keep the root bytes stable for this session, even if the codec
        // releases its input stream after reading the archive headers.
        FileStream? source = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var opened = OpenLevel(path, path, null, null, "", null, job);
            if (opened.Session == null) return opened;
            try
            {
                opened.Session.rootSource = source;
                source = null;
                if (captureSource) opened.Session.CaptureRootSourceStamp(job);
                return opened;
            }
            catch { opened.Session.Dispose(); throw; }
        }
        finally { source?.Dispose(); }
    }

    internal ArchiveSourceStamp CaptureRootSourceStamp(ArchiveJob job) =>
        Parent != null ? Parent.CaptureRootSourceStamp(job)
            : rootSourceStamp ??= ArchiveSourceStamp.Capture(
                rootSource ?? throw new ObjectDisposedException(nameof(ArchiveSession)), job);

    private static (ArchiveSession? Session, ArchiveResult Result) OpenLevel(string path, string display,
        ArchiveSession? parent, uint? index, string folder, ArchiveTemporaryFile? temporary, ArchiveJob job)
    {
        NativeArchive? candidate = null;
        try
        {
            var opened = NativeArchive.Open(path, job); candidate = opened.Archive;
            if (candidate == null) return (null, opened.Result);
            var entries = candidate.ReadEntries(job);
            var session = new ArchiveSession(candidate, entries, path, display, parent, index, folder, temporary);
            candidate = null;
            return (session, new(0, job.Issues));
        }
        catch (System.Runtime.InteropServices.COMException error) { return (null, new(error.HResult, job.Issues)); }
        finally { candidate?.Dispose(); }
    }

    public (ArchiveSession? Session, ArchiveResult Result) OpenChild(uint index, ArchiveJob job,
        string parentFolder = "", string? temporaryRoot = null)
    {
        ObjectDisposedException.ThrowIf(IsClosed, this);
        var item = Entries.Single(e => e.Index == index);
        var file = new ArchiveTemporaryFile(item.Path, temporaryRoot);
        bool transferred = false;
        try
        {
            var exported = Archive.ExportItem(index, file.Path, job);
            if (!exported.Succeeded) return (null, exported);
            var opened = OpenLevel(file.Path, DisplayPath + " › " + item.Path,
                this, index, parentFolder, file, job);
            transferred = opened.Session != null;
            return opened;
        }
        finally { if (!transferred) file.Dispose(); }
    }

    // Proof boundary for edit-back: construct the whole nested update chain as
    // new artifacts. This API deliberately cannot overwrite the root archive.
    // A later UI commit needs conflict/recovery and editor lifetime decisions.
    public (ArchiveTemporaryFile? File, ArchiveResult Result) StageReplacement(uint index,
        string replacement, ArchiveJob job, string? temporaryRoot = null)
    {
        ObjectDisposedException.ThrowIf(IsClosed, this);
        ArchiveTemporaryFile? staged = null;
        try
        {
            for (ArchiveSession? level = this; level != null; level = level.Parent)
            {
                var output = new ArchiveTemporaryFile(level.PhysicalPath, temporaryRoot);
                bool transferred = false;
                try
                {
                    var result = level.Archive.ReplaceItem(index, replacement, output.Path, job);
                    if (!result.Succeeded) return (null, result);
                    // A successful writer HRESULT alone is not proof of an archive.
                    var opened = NativeArchive.Open(output.Path, job);
                    using var check = opened.Archive;
                    if (check == null || !opened.Result.Succeeded) return (null, opened.Result);
                    var names = check.ReadEntries(job);
                    if (!names.Select(e => e.Path).SequenceEqual(level.Entries.Select(e => e.Path)))
                        throw new InvalidDataException("Имена элементов изменились при обновлении архива.");
                    result = check.Extract(names.Select(e => e.Index), "", job, test: true);
                    if (!result.Succeeded) return (null, result);
                    staged?.Dispose(); staged = output; transferred = true;
                    replacement = output.Path;
                    if (level.ParentIndex is { } parentIndex) index = parentIndex;
                }
                finally { if (!transferred) output.Dispose(); }
            }
            var complete = staged; staged = null;
            return (complete, new(0, job.Issues));
        }
        finally { staged?.Dispose(); }
    }
    public void Dispose()
    {
        Archive.Dispose();
        rootSource?.Dispose(); rootSource = null;
        temporary?.Dispose();
    }
}

public sealed class ArchiveTemporaryFile : IDisposable
{
    public string Path { get; }
    public string DirectoryPath { get; }
    public ArchiveTemporaryFile(string name, string? root = null)
    {
        DirectoryPath = System.IO.Path.Combine(root ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Arkvello"), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        // Keep the extension for codec detection, never the path supplied by an archive.
        string extension = System.IO.Path.GetExtension(name.Replace('\\', '/'));
        if (extension.Length > 32 || extension.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0) extension = "";
        Path = System.IO.Path.Combine(DirectoryPath, "item" + extension);
    }
    public void Dispose()
    {
        // No recursive cleanup: touch only the owned file. Unexpected files or
        // externally held handles leave a recoverable directory instead.
        try { File.Delete(Path); Directory.Delete(DirectoryPath, recursive: false); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
