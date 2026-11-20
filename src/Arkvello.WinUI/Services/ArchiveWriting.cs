using System.Runtime.InteropServices;

namespace Arkvello.Services;

[Flags]
internal enum ArchiveWriteFlags : uint
{
    SharedRead = 1, SymbolicLinks = 2, HardLinks = 4, Streams = 8, Permissions = 16, ArchiveTime = 32, DeleteSources = 64
}

internal enum ArchiveUpdateAction : uint { Add, Update, Freshen, Synchronize }
internal enum ArchiveWritePath : uint { Relative, Full, Absolute }

internal sealed record ArchiveWriteFormat(string Name, string Extension, uint Flags, uint TimePrecisions, uint DefaultTimePrecision)
{
    public bool RequiresSingleFile => (Flags & 1) != 0;
    public ArchiveWriteFlags Metadata => (ArchiveWriteFlags)(Flags & 30);
    public bool StoresCreatedTime => (Flags & 32) != 0;
    public bool StoresModifiedTime => (Flags & 64) != 0;
    public bool StoresAccessedTime => (Flags & 128) != 0;
    public override string ToString() => Name;
}

internal sealed record ArchiveWriteRequest(string Destination, string Format, string[] Paths,
    IReadOnlyDictionary<string, string> Properties, ulong[] Volumes, string? Password = null,
    string? SfxModule = null, bool UpdateExisting = false, ArchiveUpdateAction Action = ArchiveUpdateAction.Add,
    ArchiveWritePath PathMode = ArchiveWritePath.Relative, ArchiveWriteFlags Flags = 0)
{
    public bool DeleteSources => Flags.HasFlag(ArchiveWriteFlags.DeleteSources);
}

internal static class ArchiveWriting
{
    internal static (ulong Compression, ulong Decompression, ulong Budget)? EstimateMemory(string format, string method,
        uint level, uint threads = 0, ulong dictionary = 0, ulong xzBlock = 0, ulong limit = 0, uint percent = 0)
    {
        int result = NativeApi.av_write_memory(format, method, level, threads, dictionary, xzBlock, limit, percent,
            out ulong compression, out ulong decompression, out ulong budget);
        return result == 0 ? (compression, decompression, budget) : null;
    }

    internal static ArchiveWriteFormat[] Formats()
    {
        var formats = new List<ArchiveWriteFormat>();
        using var job = new ArchiveJob(e =>
        {
            if (e.Kind == ArchiveEventKind.WriteFormat) formats.Add(new(e.Path1, e.Path2, e.Flags, (uint)e.Size1, (uint)e.Size2));
            return new();
        });
        int result = NativeApi.av_write_formats(job);
        if (job.CallbackError is { } error) throw new InvalidOperationException("Не удалось получить форматы архивов.", error);
        Marshal.ThrowExceptionForHR(result);
        return formats.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static ArchiveResult Run(ArchiveWriteRequest request, ArchiveJob job)
    {
        string target = Path.GetFullPath(request.Destination);
        string parent = Path.GetDirectoryName(target) ?? throw new IOException("Укажите имя архива.");
        string name = Path.GetFileName(target);
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new IOException("Укажите допустимое имя файла архива.");
        string[] paths = request.Paths.Select(source => Path.TrimEndingDirectorySeparator(Path.GetFullPath(source))).ToArray();
        foreach (string fullPath in paths)
        {
            if (target.Equals(fullPath, StringComparison.OrdinalIgnoreCase) ||
                (Directory.Exists(fullPath) &&
                 target.StartsWith(Path.EndsInDirectorySeparator(fullPath) ? fullPath : fullPath + Path.DirectorySeparatorChar,
                     StringComparison.OrdinalIgnoreCase)))
                throw new IOException("Выберите место для архива за пределами добавляемых файлов и папок.");
        }
        if (request.UpdateExisting && request.Volumes.Length != 0)
            throw new IOException("Обновление многотомного архива не поддерживается.");
        if (!request.UpdateExisting && File.Exists(target))
            throw new IOException("Файл назначения уже существует.");
        if (request.DeleteSources) CheckDeletionSources(paths, job);

        job.ThrowIfCancellationRequested();
        string staging = Path.Combine(parent, ".arkvello-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        string prepared = Path.Combine(staging, name);
        string backup = Path.Combine(staging, "previous-" + Guid.NewGuid().ToString("N") + ".tmp");
        bool preserve = false;
        bool published = false;
        Exception? publicationError = null;
        void PublishNew()
        {
            if (request.Volumes.Length == 0)
                File.Move(prepared, target);
            else
            {
                string[] parts = Directory.GetFiles(staging).Order(StringComparer.Ordinal).ToArray();
                if (parts.Length == 0) throw new IOException("7-Zip не записал ни одного тома.");
                try { PublishVolumes(parts, parent); }
                catch (AggregateException error)
                {
                    preserve = true;
                    throw new IOException($"Не удалось перенести все тома. Проверьте {parent} и {staging}.", error);
                }
            }
            published = true;
        }
        try
        {
            ArchiveSourceStamp? before = null;
            using (var source = request.UpdateExisting ? OpenUpdateSource(target) : null)
            {
                if (source != null) before = ArchiveSourceStamp.Capture(source, job);
                var properties = request.Properties.ToArray();
                var options = new NativeApi.WriteOptions
                {
                    Format = request.Format, Source = source == null ? null : target, Output = prepared,
                    Password = request.Password, SfxModule = request.SfxModule,
                    NumProperties = (uint)properties.Length, NumVolumes = (uint)request.Volumes.Length,
                    Action = (uint)request.Action, PathMode = (uint)request.PathMode, Flags = (uint)request.Flags
                };
                if (request.DeleteSources)
                    job.PublishBeforeDeleting = () =>
                    {
                        // UpdateArchive has closed all outputs before its source-cleanup
                        // callbacks. An unsuccessful publication leaves every input intact.
                        if (job.Issues.Count != 0 || job.CancellationRequested) return false;
                        try { PublishNew(); return true; }
                        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                        { publicationError = error; return false; }
                    };
                int result = NativeApi.av_write_archive(job, in options, paths, (uint)paths.Length,
                    properties.Select(p => p.Key).ToArray(), properties.Select(p => p.Value).ToArray(), request.Volumes);
                job.PublishBeforeDeleting = null;
                if (publicationError != null) throw publicationError;
                var status = new ArchiveResult(result, job.Issues);
                if (!status.Succeeded)
                {
                    if (!published || status.Cancelled) return status;
                    job.ReportIssue(0, $"Архив сохранён. Удаление исходников прервано (0x{result:X8}).");
                    return new(0, job.Issues);
                }
                if (!published) job.ThrowIfCancellationRequested();
            }
            if (before != null)
            {
                try
                {
                    File.Replace(prepared, target, backup, ignoreMetadataErrors: false);
                    if (ArchiveSourceStamp.Capture(backup) != before)
                        throw new IOException("Исходный архив изменился во время сохранения.");
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    preserve = true;
                    throw new IOException($"{error.Message} Файлы восстановления: {staging}", error);
                }
            }
            else if (!published)
                PublishNew();
            if (request.DeleteSources)
            {
                foreach (string path in paths.Where(path => File.Exists(path) || Directory.Exists(path)))
                    job.ReportIssue(0, $"Архив сохранён. Исходный элемент остался на диске: {path}.");
            }
            return new(0, job.Issues);
        }
        finally
        {
            job.PublishBeforeDeleting = null;
            if (!preserve) CleanStaging(staging, prepared, backup);
        }
    }

    private static void CheckDeletionSources(string[] paths, ArchiveJob job)
    {
        foreach (string path in paths)
        {
            if (Path.GetDirectoryName(path) == null)
                throw new IOException("Удаление корня диска или сетевой папки после сжатия недоступно.");
            for (string? ancestor = Path.GetDirectoryName(path); ancestor != null; ancestor = Path.GetDirectoryName(ancestor))
                CheckAttributes(ancestor);
        }
        var pending = new Stack<string>(paths);
        while (pending.TryPop(out string? path))
        {
            if ((CheckAttributes(path) & FileAttributes.Directory) == 0) continue;
            foreach (string child in Directory.EnumerateFileSystemEntries(path)) pending.Push(child);
        }

        FileAttributes CheckAttributes(string path)
        {
            job.ThrowIfCancellationRequested();
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Удаление исходников после сжатия недоступно для путей через ссылки: " + path);
            return attributes;
        }
    }

    private static FileStream OpenUpdateSource(string target)
    {
        if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Выберите исходный архив напрямую, без ссылки.");
        return new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    private static void PublishVolumes(string[] parts, string destination)
    {
        int published = 0;
        try
        {
            foreach (string part in parts)
            {
                File.Move(part, Path.Combine(destination, Path.GetFileName(part)));
                published++;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            var errors = new List<Exception> { error };
            while (published > 0)
            {
                string part = parts[--published];
                try { File.Move(Path.Combine(destination, Path.GetFileName(part)), part); }
                catch (Exception rollback) when (rollback is IOException or UnauthorizedAccessException) { errors.Add(rollback); }
            }
            if (errors.Count > 1) throw new AggregateException(errors);
            throw;
        }
    }

    private static void CleanStaging(string staging, string prepared, string backup)
    {
        try
        {
            File.Delete(prepared);
            File.Delete(backup);
            string prefix = Path.GetFileName(prepared) + ".";
            foreach (string path in Directory.EnumerateFiles(staging))
            {
                string name = Path.GetFileName(path);
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                ReadOnlySpan<char> suffix = name.AsSpan(prefix.Length);
                if (suffix.Length >= 3 && uint.TryParse(suffix, out _)) File.Delete(path);
            }
            Directory.Delete(staging);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
