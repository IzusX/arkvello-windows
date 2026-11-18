using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Arkvello.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Arkvello;

public sealed partial class MainWindow
{
    private async Task VerifyShellIcons(string fixture, string textFile, string folder, Action<string, bool> record)
    {
        string shortcutPath = Path.Combine(fixture, "shortcut.lnk");
        var shellType = Type.GetTypeFromProgID("WScript.Shell", throwOnError: true)!;
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            try { shortcut.TargetPath = folder; shortcut.Save(); }
            finally { Marshal.FinalReleaseComObject(shortcut); }
        }
        finally { Marshal.FinalReleaseComObject(shell); }

        async Task<WriteableBitmap?> WaitForIcon(string path)
        {
            var entry = entries.Single(item => item.FullPath == path);
            list.ScrollIntoView(entry);
            for (int attempt = 0; attempt < 40; attempt++)
            {
                await Task.Delay(25);
                if (list.ContainerFromItem(entry) is ContentControl container &&
                    container.ContentTemplateRoot is FrameworkElement root &&
                    root.FindName("ShellIcon") is Image { Visibility: Visibility.Visible, Source: WriteableBitmap bitmap } &&
                    root.FindName("FallbackIcon") is Grid { Visibility: Visibility.Collapsed }) return bitmap;
            }
            return null;
        }

        await NavigateDirectory(activePanel, fixture, false);
        foreach (string mode in new[] { "Details", "List", "SmallIcons", "LargeIcons" })
        {
            view = mode;
            BuildList();
            var fileIcon = await WaitForIcon(textFile);
            var folderIcon = await WaitForIcon(folder);
            record("shell icons for ordinary files and folders in " + mode,
                fileIcon != null && folderIcon != null && !fileIcon.PixelBuffer.ToArray().SequenceEqual(folderIcon.PixelBuffer.ToArray()));
        }
        var linkIcon = await WaitForIcon(shortcutPath);
        var shortcutBitmap = await shellIcons.GetAsync(shortcutPath, FileAttributes.Normal, 36, CancellationToken.None);
        record("Shell returns a shortcut overlay for a local folder target",
            linkIcon != null && shortcutBitmap is { OverlayIndex: > 0 });
        record("offline files, reparse points, long paths and UNC use attributes only",
            ShellFileIcons.UseFileAttributes(textFile, FileAttributes.Offline) &&
            ShellFileIcons.UseFileAttributes(folder, FileAttributes.Directory | FileAttributes.ReparsePoint) &&
            ShellFileIcons.UseFileAttributes(textFile + new string('x', 260), FileAttributes.Normal) &&
            ShellFileIcons.UseFileAttributes(@"\\example\share\file.txt", FileAttributes.Normal) &&
            !ShellFileIcons.UseFileAttributes(textFile, FileAttributes.Normal));

        string many = Directory.CreateDirectory(Path.Combine(fixture, "icons-scroll")).FullName;
        for (int i = 0; i < 600; i++) File.WriteAllText(Path.Combine(many, $"{i:D4}.txt"), "");
        string tail = Path.Combine(many, "zzzz.lnk");
        File.Copy(shortcutPath, tail);
        view = "Details";
        await NavigateDirectory(activePanel, many, false);
        var first = await WaitForIcon(entries[0].FullPath);
        record("icon cache fills from realized rows rather than the whole directory",
            first != null && entries.Count == 601 && shellIconCache.Count < 300 && !shellIconCache.Keys.Any(key => key.Path == tail));
        var last = await WaitForIcon(tail);
        record("scrolling to a distant row replaces recycled file icons",
            first != null && last != null && !first.PixelBuffer.ToArray().SequenceEqual(last.PixelBuffer.ToArray()));
        await NavigateDirectory(activePanel, fixture, false);
        await WaitForIcon(shortcutPath);
    }
}
