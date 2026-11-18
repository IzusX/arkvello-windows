using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Arkvello.Services;

internal sealed class ShellFileIcons : IDisposable
{
    private const int QueueCapacity = 256;
    private readonly BlockingCollection<Request> requests = new(QueueCapacity);
    internal sealed record IconBitmap(byte[] Pixels, int OverlayIndex);
    private sealed record Request(string Path, FileAttributes Attributes, int Pixels,
        CancellationToken Cancel, TaskCompletionSource<IconBitmap?> Completion);

    public ShellFileIcons()
    {
        var worker = new Thread(ReadRequests) { Name = "Arkvello Shell icons", IsBackground = true };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
    }

    public Task<IconBitmap?> GetAsync(string path, FileAttributes attributes, int pixels, CancellationToken cancel)
    {
        var completion = new TaskCompletionSource<IconBitmap?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!requests.TryAdd(new(path, attributes, pixels, cancel, completion))) completion.SetResult(null);
        return completion.Task;
    }

    public void Dispose() => requests.CompleteAdding();

    private void ReadRequests()
    {
        int apartment = CoInitializeEx(0, 2); // COINIT_APARTMENTTHREADED
        try
        {
            foreach (var request in requests.GetConsumingEnumerable())
            {
                IconBitmap? bitmap = null;
                try
                {
                    if (apartment >= 0 && !requests.IsAddingCompleted && !request.Cancel.IsCancellationRequested)
                        bitmap = ReadIcon(request.Path, request.Attributes, request.Pixels);
                }
                catch (Exception error) when (error is ArgumentException or ExternalException or IOException or UnauthorizedAccessException)
                {
                    // A Shell provider failure leaves the standard fallback visible.
                }
                request.Completion.SetResult(bitmap);
            }
        }
        finally { if (apartment >= 0) CoUninitialize(); }
    }

    internal static bool UseFileAttributes(string path, FileAttributes attributes)
    {
        const FileAttributes recall = (FileAttributes)(0x00040000 | 0x00400000);
        return path.Length >= 260 || path.StartsWith(@"\\", StringComparison.Ordinal) ||
            (attributes & (FileAttributes.Offline | FileAttributes.ReparsePoint | recall)) != 0 ||
            GetDriveType(Path.GetPathRoot(path)) != 3; // DRIVE_FIXED
    }

    private static IconBitmap? ReadIcon(string path, FileAttributes attributes, int pixels)
    {
        const uint icon = 0x100, smallIcon = 0x1, shellSize = 0x4, useAttributes = 0x10, addOverlays = 0x20, overlayIndex = 0x40;
        uint flags = icon | shellSize | (pixels <= 24 ? smallIcon : 0);
        uint type = 0;
        if (UseFileAttributes(path, attributes))
        {
            bool directory = attributes.HasFlag(FileAttributes.Directory);
            path = directory ? "Folder" : "File" + Path.GetExtension(path);
            type = (uint)(directory ? FileAttributes.Directory : FileAttributes.Normal);
            flags |= useAttributes;
        }
        else flags |= addOverlays | overlayIndex;

        nint result = SHGetFileInfo(path, type, out var info, (uint)Marshal.SizeOf<ShellFileInfo>(), flags);
        if (info.Icon == 0) return null;
        try { return result != 0 && ReadPixels(info.Icon, pixels) is { } data ? new(data, (int)((uint)info.IconIndex >> 24)) : null; }
        finally { DestroyIcon(info.Icon); }
    }

    private static byte[]? ReadPixels(nint icon, int size)
    {
        var header = new BitmapHeader { Size = 40, Width = size, Height = -size, Planes = 1, BitsPerPixel = 32 };
        nint context = CreateCompatibleDC(0);
        if (context == 0) return null;
        nint bitmap = 0, previous = 0;
        try
        {
            bitmap = CreateDIBSection(context, in header, 0, out nint bits, 0, 0);
            if (bitmap == 0 || bits == 0) return null;
            previous = SelectObject(context, bitmap);
            if (previous == 0 || previous == -1) return null;

            // Drawing over black gives premultiplied RGB. Drawing over white
            // also reveals the coverage of legacy mask-based Shell icons.
            byte[] black = new byte[size * size * 4], white = new byte[black.Length];
            Marshal.Copy(black, 0, bits, black.Length);
            if (!DrawIconEx(context, 0, 0, icon, size, size, 0, 0, 3)) return null;
            GdiFlush();
            Marshal.Copy(bits, black, 0, black.Length);
            Array.Fill(white, (byte)255);
            Marshal.Copy(white, 0, bits, white.Length);
            if (!DrawIconEx(context, 0, 0, icon, size, size, 0, 0, 3)) return null;
            GdiFlush();
            Marshal.Copy(bits, white, 0, white.Length);
            for (int i = 0; i < black.Length; i += 4)
            {
                int uncovered = Math.Min(white[i] - black[i], Math.Min(white[i + 1] - black[i + 1], white[i + 2] - black[i + 2]));
                black[i + 3] = (byte)(255 - Math.Max(0, uncovered));
            }
            return black;
        }
        finally
        {
            if (previous != 0 && previous != -1) SelectObject(context, previous);
            if (bitmap != 0) DeleteObject(bitmap);
            DeleteDC(context);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public nint Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapHeader
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitsPerPixel;
        public uint Compression, ImageSize;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ColorsUsed, ColorsImportant;
    }

    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint SHGetFileInfo(string path, uint attributes, out ShellFileInfo info, uint size, uint flags);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("kernel32.dll", EntryPoint = "GetDriveTypeW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint GetDriveType(string? root);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")] private static extern bool DrawIconEx(nint dc, int x, int y, nint icon, int width, int height, uint step, nint brush, uint flags);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint dc, in BitmapHeader header, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint bitmap);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint bitmap);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
}
