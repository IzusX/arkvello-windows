using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Arkvello.Services;

// Filesystem commands never interpret a filename as a mask. Archive members use
// their separate session/index contract and cannot enter this adapter.
public static class FileSystemOperations
{
    public static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.') || name.EndsWith(' '))
            throw new ArgumentException("Введите имя без символов \\ / : * ? \" < > |, точки или пробела в конце.");
        string stem = name.Split('.')[0].ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
            (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && "123456789¹²³".Contains(stem[3])))
            throw new ArgumentException("Это имя зарезервировано Windows. Выберите другое имя.");
        return name;
    }

    public static string Create(string parent, string name, bool folder)
    {
        string path = Path.Combine(Absolute(parent), ValidateName(name));
        if (folder)
        {
            // Unlike Directory.CreateDirectory, an existing folder is a conflict.
            if (!CreateDirectoryW(Extended(path), IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        else using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        return path;
    }

    public static string Rename(string source, string name)
    {
        source = Absolute(source);
        string target = Path.Combine(Path.GetDirectoryName(source) ?? throw new ArgumentException("Нельзя переименовать корень диска."), ValidateName(name));
        if (source.Equals(target, StringComparison.Ordinal)) return target;
        if ((File.GetAttributes(source) & FileAttributes.Directory) != 0) Directory.Move(source, target);
        else File.Move(source, target); // No overwrite, including a concurrent collision.
        return target;
    }

    public static ArchiveResult Transfer(IEnumerable<string> selected, string destination, bool move, ArchiveJob job)
    {
        string[] paths = NormalizeSelection(selected);
        destination = Absolute(destination);
        if (!Directory.Exists(destination)) throw new DirectoryNotFoundException("Папка назначения не найдена: " + destination);
        string finalDestination = FinalDirectory(destination);
        foreach (string path in paths)
        {
            job.ThrowIfCancellationRequested();
            bool folder = (File.GetAttributes(path) & FileAttributes.Directory) != 0;
            string resolvedSource = folder ? FinalDirectory(path) : Path.Combine(FinalDirectory(Path.GetDirectoryName(path)!), Path.GetFileName(path));
            string resolvedTarget = Path.Combine(finalDestination, Path.GetFileName(path));
            if (resolvedSource.Equals(resolvedTarget, StringComparison.OrdinalIgnoreCase) || folder && Within(finalDestination, resolvedSource))
                throw new IOException("Нельзя копировать или перемещать элемент в него самого или в его вложенную папку: " + path);
            // The upstream provider follows directory junctions during sizing.
            // Reject that route explicitly until FILE-08 link semantics are implemented.
            if (folder) CheckDirectoryTree(path, job);
            else if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new NotSupportedException("Копирование ссылок и облачных файлов-заполнителей ещё не реализовано: " + path);
        }
        job.ThrowIfCancellationRequested();
        return new(NativeApi.av_fs_transfer(job, paths, (uint)paths.Length, destination, move ? 1 : 0), job.Issues);
    }

    public static ArchiveResult Delete(IEnumerable<string> selected, bool recycle, nint owner, ArchiveJob job)
    {
        string[] paths = NormalizeSelection(selected);
        job.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        // IFileOperation requires STA. CLR operation workers and console tests
        // use MTA; never change the apartment of an already running thread.
        var completion = new TaskCompletionSource<ArchiveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() =>
        {
            try { completion.SetResult(new(NativeApi.av_fs_delete(job, paths, (uint)paths.Length, (nuint)owner, recycle ? 1 : 0), job.Issues)); }
            catch (Exception error) { completion.SetException(error); }
        }) { IsBackground = true, Name = "Arkvello Shell operation" };
        worker.SetApartmentState(ApartmentState.STA); worker.Start();
        return completion.Task.GetAwaiter().GetResult();
    }

    public static string[] NormalizeSelection(IEnumerable<string> paths)
    {
        var selected = paths.Select(Absolute).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p.Length).ToArray();
        if (selected.Length == 0) throw new ArgumentException("Выберите файлы или папки.");
        var result = new List<string>();
        foreach (string path in selected)
        {
            if (Path.GetPathRoot(path)!.TrimEnd('\\').Equals(path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Операция над корнем диска не поддерживается.");
            _ = File.GetAttributes(path); // Missing/denied selections fail before mutation.
            if (!result.Any(parent => Within(path, parent))) result.Add(path);
        }
        return result.ToArray();
    }

    private static string Absolute(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.IndexOf('\0') >= 0) throw new ArgumentException("Нужен полный путь к файлу или папке.");
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (full.AsSpan(Path.GetPathRoot(full)!.Length).Contains(':')) throw new NotSupportedException("Операции с альтернативными потоками ещё не реализованы.");
        return full;
    }
    private static bool Within(string path, string parent) => path.Equals(parent, StringComparison.OrdinalIgnoreCase) || path.StartsWith(Path.TrimEndingDirectorySeparator(parent) + "\\", StringComparison.OrdinalIgnoreCase);
    private static void CheckDirectoryTree(string root, ArchiveJob job)
    {
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out var path))
        {
            job.ThrowIfCancellationRequested();
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new NotSupportedException("Копирование папок со ссылками и облачными файлами-заполнителями ещё не реализовано: " + path);
            if (Directory.Exists(path)) foreach (string child in Directory.EnumerateFileSystemEntries(path)) { job.ThrowIfCancellationRequested(); pending.Push(child); }
        }
    }
    private static string Extended(string path) => path.StartsWith("\\\\?\\", StringComparison.Ordinal) ? path : path.StartsWith("\\\\", StringComparison.Ordinal) ? "\\\\?\\UNC\\" + path[2..] : "\\\\?\\" + path;
    private static string FinalDirectory(string path)
    {
        using var handle = CreateFileW(Extended(path), 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var buffer = new char[32768];
        uint length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0);
        if (length == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (length >= buffer.Length) throw new PathTooLongException();
        return Path.TrimEndingDirectorySeparator(new string(buffer, 0, (int)length));
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateDirectoryW(string path, nint security);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, [Out] char[] path, uint capacity, uint flags);
}
