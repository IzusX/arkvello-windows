using System.Diagnostics;
using Arkvello.Services;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;

namespace Arkvello;

public sealed partial class MainWindow
{
    // Explicit test mode: real XAML layout/native peers and real fixture I/O.
    // It does not synthesize pointer/keyboard input or certify a screen reader.
    private async Task VerifyStage3(string fixture)
    {
        string output = Path.Combine(Path.GetFullPath(fixture), "evidence");
        Directory.CreateDirectory(output);
        var results = new List<object>();
        var watch = Stopwatch.StartNew();
        double previousTick = 0, maxDispatcherGap = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) => { double now = watch.Elapsed.TotalMilliseconds; if (previousTick > 0) maxDispatcherGap = Math.Max(maxDispatcherGap, now - previousTick); previousTick = now; };
        timer.Start();
        void Record(string name, bool passed, object? details = null)
        {
            results.Add(new { name, passed, details });
            File.WriteAllText(Path.Combine(output, "native-layout.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            if (!passed) throw new InvalidOperationException(name);
        }
        try
        {
            Record("positional archive argument opens the archive on startup",
                archiveSession?.RootPath == Path.GetFullPath(Path.Combine(fixture, "encoded launch Ω.7z")) && entries.Count == 10_000 &&
                archiveSession.RootSourceStamp == null);
            var io = Stopwatch.StartNew();
            await Navigate(Path.Combine(fixture, "files"), false);
            Record("real filesystem enumeration", entries.Count == 10_000, new { count = entries.Count, ms = io.Elapsed.TotalMilliseconds });
            io.Restart(); await OpenArchive(Path.Combine(fixture, "encoded.7z"), false);
            Record("encoded archive enumeration", entries.Count == 10_000, new { count = entries.Count, ms = io.Elapsed.TotalMilliseconds });
            var tested = await RunOperation("Проверка тестового архива", job => archive!.Extract(archiveEntries.Select(e => e.Index), "", job, test: true), showSuccess: false);
            Record("real encoded archive test", tested?.Succeeded == true);
            var loadedColumns = new Dictionary<uint, IReadOnlyDictionary<uint, ArchiveProperty>>();
            var columnOutcome = await RunOperation("Проверка столбцов", job => LoadSessionColumns(archiveSession!, [22], job, loadedColumns), showResult: false);
            RetainSessionColumns(archiveSession!, loadedColumns); columns.Add(new("Метод", "Properties[22]", 170));
            ShowArchiveFolder(); await Task.Delay(150); list.UpdateLayout();
            var texts = new List<string>();
            void ReadTexts(DependencyObject parent)
            {
                for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
                {
                    var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
                    if (child is TextBlock text) texts.Add(text.Text);
                    ReadTexts(child);
                }
            }
            ReadTexts(list);
            Record("dynamic property binding renders real method", columnOutcome?.Succeeded == true && texts.Any(t => t.Contains("LZMA2", StringComparison.Ordinal)), new { value = entries[0].Properties?.GetValueOrDefault("22"), text = texts.Take(12).ToArray() });
            markMode = true; list.SelectionMode = ListViewSelectionMode.Multiple;
            list.SelectedItems.Clear(); list.SelectedItems.Add(entries[4]); list.SelectedItems.Add(entries[8]);
            list.ScrollIntoView(entries[23]); list.UpdateLayout();
            if (list.ContainerFromIndex(23) is Control caret) caret.Focus(FocusState.Keyboard);
            string currentPath = entries[23].FullPath;
            Record("native current item is independent of marks", CurrentItem()?.FullPath == currentPath && list.SelectedItems.Count == 2 && !list.SelectedItems.Contains(entries[23]));
            await RefreshView(); await Task.Delay(150); list.UpdateLayout();
            Record("root refresh retains marks current item and dynamic columns", CurrentItem()?.FullPath == currentPath && list.SelectedItems.Count == 2 && entries[23].Properties?.GetValueOrDefault("22")?.Text.Contains("LZMA2", StringComparison.Ordinal) == true);
            markMode = false; list.SelectionMode = ListViewSelectionMode.Extended;
            foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
            foreach (int percent in new[] { 75, 100, 150 })
            foreach (string mode in new[] { "Details", "List", "SmallIcons", "LargeIcons" })
            {
                WindowHost.RequestedTheme = theme; RefreshColors(); scalePercent = percent;
                ApplyInterfaceScale(); view = mode; BuildList(); await Task.Delay(150); list.UpdateLayout();
                list.SelectedItem = entries[500]; list.ScrollIntoView(entries[500]); await Task.Delay(100); list.UpdateLayout();
                var peer = FrameworkElementAutomationPeer.CreatePeerForElement(list);
                var selection = peer?.GetPattern(PatternInterface.Selection) as ISelectionProvider;
                int realized = 0;
                for (int i = 0; i < entries.Count; i++) if (list.ContainerFromIndex(i) != null) realized++;
                Record($"layout/{theme}/{percent}/{mode}", list.ActualHeight > 0 && double.IsFinite(list.ActualWidth) && realized is > 0 and < 1000 && selection != null,
                    new { list.ActualWidth, list.ActualHeight, realized, selectionPattern = selection != null, dpi = Root.XamlRoot.RasterizationScale, textScale = uiSettings.TextScaleFactor });
                if (percent == 100)
                {
                    var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(WindowHost);
                    var pixels = await bitmap.GetPixelsAsync();
                    using var file = new FileStream(Path.Combine(output, $"{theme}-{mode}.png"), FileMode.Create, FileAccess.ReadWrite);
                    var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, file.AsRandomAccessStream());
                    encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray());
                    await encoder.FlushAsync();
                }
            }
            string core = Process.GetCurrentProcess().Modules.Cast<ProcessModule>().First(m => m.ModuleName.Equals("coreclr.dll", StringComparison.OrdinalIgnoreCase)).FileName;
            Record("runtime payload", true, new { core, app = AppContext.BaseDirectory, maxDispatcherGapMs = maxDispatcherGap, elapsedMs = watch.Elapsed.TotalMilliseconds });
            foreach (bool closeWindow in new[] { false, true })
            {
                bool promptWasVisible = false;
                if (closeWindow) Closed += (_, _) => Record("close window after native callback completes", promptWasVisible && !busy && activeJob == null, new { promptWasVisible, busy, jobDetached = activeJob == null });
                var cancelPrompt = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
                cancelPrompt.Tick += (_, _) =>
                {
                    if (promptDialog == null) return;
                    promptWasVisible = true; cancelPrompt.Stop();
                    if (closeWindow) RequestClose(); else CancelOperation();
                };
                cancelPrompt.Start();
                Arkvello.Services.ArchiveSession? candidate = null;
                var outcome = await RunOperation("Проверка отмены запроса пароля", job =>
                {
                    var opened = Arkvello.Services.ArchiveSession.Open(Path.Combine(fixture, "encrypted.7z"), job);
                    candidate = opened.Session; return opened.Result;
                }, showResult: false);
                cancelPrompt.Stop(); candidate?.Dispose();
                if (!closeWindow) Record("cancel native password callback", promptWasVisible && outcome?.Cancelled == true);
            }
        }
        catch (Exception error) { results.Add(new { name = "verification exception", passed = false, details = error.ToString() }); Environment.ExitCode = 1; }
        finally
        {
            timer.Stop();
            File.WriteAllText(Path.Combine(output, "native-layout.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            if (!closing) Close();
        }
    }
}
