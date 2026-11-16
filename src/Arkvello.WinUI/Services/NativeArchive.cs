using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Arkvello.Services;

public enum ArchiveEventKind : uint { Item = 1, Progress, Password, Overwrite, Issue, CurrentPath, Paused }
public sealed record ArchiveEvent(ArchiveEventKind Kind, uint Code, uint Index, uint Flags,
    ulong Completed, ulong Total, ulong Size1, ulong Size2, ulong Time1, ulong Time2, string Path1, string Path2);
public sealed record PromptReply(int Value = 0, string? Password = null);
public sealed record ArchiveEntry(uint Index, string Path, bool IsDirectory, ulong? Size, ulong PackedSize, DateTime? Modified);
public sealed record ArchiveResult(int HResult, IReadOnlyList<ArchiveEvent> Issues)
{
    public bool Cancelled => HResult == unchecked((int)0x80004004);
    public bool Succeeded => HResult == 0 && Issues.Count == 0;
}

// The delegate and callback state live as long as the job. Callbacks are
// synchronous on native workers and may not throw across the C ABI.
public sealed class ArchiveJob : SafeHandleZeroOrMinusOneIsInvalid
{
    private readonly NativeApi.Callback callback;
    private readonly Func<ArchiveEvent, PromptReply> handler;
    private readonly List<ArchiveEvent> issues = [];
    private readonly List<ArchiveEntry> items = [];
    public Exception? CallbackError { get; private set; }
    public IReadOnlyList<ArchiveEvent> Issues { get { lock (issues) return issues.ToArray(); } }
    internal IReadOnlyList<ArchiveEntry> Items => items.ToArray();
    internal void ResetItems() => items.Clear();
    public ArchiveJob(Func<ArchiveEvent, PromptReply> handler) : base(true)
    {
        this.handler = handler;
        callback = OnEvent;
        SetHandle(NativeApi.av_job_create(callback, IntPtr.Zero));
        if (IsInvalid) throw new OutOfMemoryException("Cannot create archive job.");
    }
    private int OnEvent(IntPtr context, in NativeApi.Event value, IntPtr reply, uint capacity)
    {
        try
        {
            var e = new ArchiveEvent((ArchiveEventKind)value.Kind, value.Code, value.Index, value.Flags,
                value.Completed, value.Total, value.Size1, value.Size2, value.Time1, value.Time2,
                Marshal.PtrToStringUni(value.Path1) ?? "", Marshal.PtrToStringUni(value.Path2) ?? "");
            if (e.Kind == ArchiveEventKind.Issue) lock (issues) issues.Add(e);
            if (e.Kind == ArchiveEventKind.Item)
                items.Add(new(e.Index, e.Path1, (e.Flags & 1) != 0, (e.Flags & 2) != 0 ? e.Size1 : null, e.Size2,
                    (e.Flags & 4) != 0 ? DateTime.FromFileTimeUtc(checked((long)e.Time1)).ToLocalTime() : null));
            var response = handler(e);
            if (e.Kind == ArchiveEventKind.Password && response.Value >= 0)
            {
                var secret = (response.Password ?? "").ToCharArray();
                try
                {
                    if (secret.Length >= capacity) throw new ArgumentException("Password exceeds native transport capacity.");
                    Marshal.Copy(secret, 0, reply, secret.Length);
                    Marshal.WriteInt16(reply, secret.Length * 2, 0);
                }
                finally { Array.Clear(secret); }
            }
            return response.Value;
        }
        catch (Exception error) { CallbackError = error; return -1; }
    }
    public void Cancel() { if (!IsClosed) NativeApi.av_job_cancel(this); }
    public void Pause(bool pause) { if (!IsClosed) NativeApi.av_job_pause(this, pause ? 1 : 0); }
    protected override bool ReleaseHandle() { NativeApi.av_job_close(handle); return true; }
}

public sealed class NativeArchive : SafeHandleZeroOrMinusOneIsInvalid
{
    private NativeArchive(IntPtr value) : base(true) => SetHandle(value);
    public static (NativeArchive? Archive, ArchiveResult Result) Open(string path, ArchiveJob job)
    {
        if (NativeApi.av_abi() != 1) throw new InvalidOperationException("Unsupported Arkvello native ABI.");
        int result = NativeApi.av_open(path, job, out var value);
        return (result == 0 ? new NativeArchive(value) : null, new(result, job.Issues));
    }
    public IReadOnlyList<ArchiveEntry> ReadEntries(ArchiveJob? caller = null)
    {
        using var owned = caller == null ? new ArchiveJob(_ => new()) : null;
        var job = caller ?? owned!;
        job.ResetItems();
        int result = NativeApi.av_list(this, job);
        if (job.CallbackError is { } error) throw new InvalidOperationException("Archive listing callback failed.", error);
        if (result != 0) throw new COMException("Cannot list archive.", result);
        return job.Items;
    }
    public ArchiveResult Extract(IEnumerable<uint> indices, string destination, ArchiveJob job, bool test = false)
    {
        uint[] selected = indices.Distinct().Order().ToArray();
        if (selected.Length == 0) throw new ArgumentException("An explicit item selection is required.", nameof(indices));
        int result = NativeApi.av_extract(this, job, selected, (uint)selected.Length, destination, test ? 1 : 0);
        return new(result, job.Issues);
    }
    protected override bool ReleaseHandle() { NativeApi.av_close(handle); return true; }
}

internal static class NativeApi
{
    private const string Library = "Arkvello.Native.dll";
    [StructLayout(LayoutKind.Sequential)]
    internal struct Event
    {
        internal uint Kind, Code, Index, Flags;
        internal ulong Completed, Total, Size1, Size2, Time1, Time2;
        internal IntPtr Path1, Path2;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int Callback(IntPtr context, in Event value, IntPtr reply, uint capacity);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern uint av_abi();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr av_job_create(Callback callback, IntPtr context);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void av_job_close(IntPtr job);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void av_job_cancel(ArchiveJob job);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void av_job_pause(ArchiveJob job, int pause);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    internal static extern int av_open(string path, ArchiveJob job, out IntPtr session);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void av_close(IntPtr session);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int av_list(NativeArchive session, ArchiveJob job);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    internal static extern int av_extract(NativeArchive session, ArchiveJob job, uint[] indices, uint count, string destination, int test);
}
