using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.UI.Xaml;
using Windows.Foundation;

namespace Arkvello.Services;

// The folder's Shell drop target owns file operations and the optimized-move
// protocol. Arkvello never deletes originals based on a returned drag effect.
public sealed class ShellFileDrag : IDisposable
{
    private const uint CopyOrMove = 3;
    private static readonly Guid DataObjectId = typeof(IDataObject).GUID;
    private static readonly Guid DropTargetId = typeof(IDropTarget).GUID;
    private static readonly Guid ShellItemId = typeof(IShellItem).GUID;
    private static readonly Guid ShellUiObject = new("3981e225-f559-11d3-8e3a-00c04f6837d5");
    private readonly nint window;
    private readonly Target target;
    private bool registered;
    internal bool IsDropping => target.IsDropping;

    public ShellFileDrag(nint window, Func<Point, string?> resolveFolder, Action dropped)
    {
        this.window = window;
        Marshal.ThrowExceptionForHR(OleInitialize(0));
        try
        {
            target = new(window, resolveFolder, dropped);
            nint pointer = Marshal.GetComInterfaceForObject<IDropTarget, IDropTarget>(target);
            try { Marshal.ThrowExceptionForHR(RegisterDragDrop(window, pointer)); }
            finally { Marshal.Release(pointer); }
            registered = true;
        }
        catch
        {
            OleUninitialize();
            throw;
        }
    }

    public static IDataObject CreateData(string directory, IReadOnlyList<string> paths)
    {
        nint parent = ParsePath(directory);
        var items = new nint[paths.Count];
        try
        {
            for (int i = 0; i < items.Length; ++i) items[i] = ParsePath(paths[i]);
            nint[] children = items.Select(ILFindLastID).ToArray();
            Marshal.ThrowExceptionForHR(SHCreateDataObject(parent, (uint)children.Length, children,
                0, DataObjectId, out var data));
            return data;
        }
        finally
        {
            foreach (nint item in items) Marshal.FreeCoTaskMem(item);
            Marshal.FreeCoTaskMem(parent);
        }
    }

    private static nint ParsePath(string path)
    {
        Marshal.ThrowExceptionForHR(SHParseDisplayName(path, 0, out nint id, 0, out _));
        return id;
    }

    public void Start(IDataObject data)
    {
        int button = GetSystemMetrics(23) != 0 ? 2 : 1; // SM_SWAPBUTTON, VK_RBUTTON, VK_LBUTTON
        // Shell parses PIDLs before the loop starts. The release may already
        // have been delivered to XAML while that work was in progress.
        if (GetAsyncKeyState(button) >= 0) return;
        Marshal.ThrowExceptionForHR(SHDoDragDrop(window, data, 0, CopyOrMove, out _));
    }

    public static bool ContainsFiles(IDataObject data)
    {
        var format = new FORMATETC
        {
            cfFormat = 15, // CF_HDROP
            dwAspect = DVASPECT.DVASPECT_CONTENT,
            lindex = -1,
            tymed = TYMED.TYMED_HGLOBAL
        };
        return data.QueryGetData(ref format) == 0;
    }

    public static Size DragThreshold(XamlRoot root, double interfaceScale)
    {
        double factor = root.RasterizationScale * interfaceScale;
        uint dpi = (uint)Math.Round(root.RasterizationScale * 96);
        return new(Math.Max(1, GetSystemMetricsForDpi(68, dpi)) / factor,
            Math.Max(1, GetSystemMetricsForDpi(69, dpi)) / factor);
    }

    public void Dispose()
    {
        if (!registered) return;
        registered = false;
        RevokeDragDrop(window);
        target.DragLeave();
        OleUninitialize();
    }

    internal void DropForVerification(IDataObject data, Point clientPixels, bool move)
    {
        NativePoint screen = new() { X = (int)Math.Round(clientPixels.X), Y = (int)Math.Round(clientPixels.Y) };
        if (!ClientToScreen(window, ref screen)) throw new InvalidOperationException("Could not place the test drop.");
        uint effect = CopyOrMove, modifier = move ? 4u : 8u; // MK_SHIFT, MK_CONTROL
        try
        {
            Marshal.ThrowExceptionForHR(target.DragEnter(data, modifier | 1, screen, ref effect));
            if (effect == 0) throw new InvalidOperationException("Shell declined the test drop.");
            effect = CopyOrMove;
            Marshal.ThrowExceptionForHR(target.Drop(data, modifier, screen, ref effect));
        }
        finally { target.DragLeave(); }
    }

    private sealed class Target(nint window, Func<Point, string?> resolveFolder, Action dropped) : IDropTarget
    {
        private IDataObject? data;
        private IDropTarget? folderTarget;
        private string? folder;
        public bool IsDropping { get; private set; }

        public int DragEnter(IDataObject incoming, uint keys, NativePoint screen, ref uint effect)
        {
            DragLeave();
            if (ContainsFiles(incoming)) data = incoming;
            return DragOver(keys, screen, ref effect);
        }

        private bool SetFolder(uint keys, NativePoint screen, uint allowed)
        {
            var client = screen;
            string? path = data != null && ScreenToClient(window, ref client)
                ? resolveFolder(new(client.X, client.Y)) : null;
            if (string.Equals(path, folder, StringComparison.Ordinal)) return folderTarget != null;
            ReleaseFolder(leave: true);
            folder = path;
            if (path == null) return false;

            if (SHCreateItemFromParsingName(path, 0, ShellItemId, out var item) < 0) return false;
            try
            {
                int bind = item.BindToHandler(0, ShellUiObject, DropTargetId, out var next);
                if (bind < 0) return false;
                folderTarget = next;
                if (next.DragEnter(data!, keys, screen, ref allowed) >= 0) return true;
                ReleaseFolder(leave: false);
                return false;
            }
            finally { Marshal.ReleaseComObject(item); }
        }

        public int DragOver(uint keys, NativePoint screen, ref uint effect)
        {
            effect &= CopyOrMove;
            try
            {
                if (SetFolder(keys, screen, effect) && folderTarget!.DragOver(keys, screen, ref effect) >= 0)
                    return 0;
            }
            catch (COMException) { ReleaseFolder(leave: true); }
            effect = 0;
            return 0;
        }

        public int Drop(IDataObject incoming, uint keys, NativePoint screen, ref uint effect)
        {
            effect &= CopyOrMove;
            bool forwarded = false;
            try
            {
                if (!SetFolder(keys, screen, effect)) { effect = 0; return 0; }
                forwarded = true;
                IsDropping = true;
                int result = folderTarget!.Drop(incoming, keys, screen, ref effect);
                if (result >= 0) dropped();
                else effect = 0;
                return result;
            }
            finally
            {
                data = null;
                ReleaseFolder(leave: !forwarded);
                IsDropping = false;
            }
        }

        public int DragLeave()
        {
            data = null;
            ReleaseFolder(leave: true);
            return 0;
        }

        private void ReleaseFolder(bool leave)
        {
            var previous = folderTarget;
            folderTarget = null;
            folder = null;
            if (previous == null) return;
            try { if (leave) previous.DragLeave(); }
            finally { Marshal.ReleaseComObject(previous); }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativePoint { public int X, Y; }

    [ComImport, ComVisible(true), Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDropTarget
    {
        [PreserveSig] int DragEnter(IDataObject data, uint keys, NativePoint point, ref uint effect);
        [PreserveSig] int DragOver(uint keys, NativePoint point, ref uint effect);
        [PreserveSig] int DragLeave();
        [PreserveSig] int Drop(IDataObject data, uint keys, NativePoint point, ref uint effect);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [PreserveSig] int BindToHandler(nint context, in Guid handler, in Guid iid,
            [MarshalAs(UnmanagedType.Interface)] out IDropTarget target);
    }

    [DllImport("ole32.dll")] private static extern int OleInitialize(nint reserved);
    [DllImport("ole32.dll")] private static extern void OleUninitialize();
    [DllImport("ole32.dll")] private static extern int RegisterDragDrop(nint hwnd, nint target);
    [DllImport("ole32.dll")] private static extern int RevokeDragDrop(nint hwnd);
    [DllImport("shell32.dll")] private static extern int SHDoDragDrop(nint hwnd, IDataObject data, nint source, uint allowed, out uint effect);
    [DllImport("shell32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string path, nint context, out nint item, uint flags, out uint attributes);
    [DllImport("shell32.dll")] private static extern nint ILFindLastID(nint item);
    [DllImport("shell32.dll")]
    private static extern int SHCreateDataObject(nint folder, uint count, [In] nint[] items, nint inner,
        in Guid iid, [MarshalAs(UnmanagedType.Interface)] out IDataObject data);
    [DllImport("shell32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, nint context, in Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem item);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(nint window, ref NativePoint point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint window, ref NativePoint point);
    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int metric, uint dpi);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int metric);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
}
