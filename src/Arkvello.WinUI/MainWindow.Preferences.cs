using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace Arkvello;

public sealed partial class MainWindow
{
    private sealed record PanePreferences(string View, string Sort, bool Descending, bool MarkMode, Column?[]? Columns,
        string TimePrecision = "M", bool TimeUtc = false);
    private sealed record WindowBounds(int X, int Y, int Width, int Height, bool Maximized);
    private sealed record WindowPreferences(string Theme = "Default", bool TwoPanels = false,
        double SplitRatio = .5, PanePreferences? Left = null, PanePreferences? Right = null,
        string?[]? RecentPaths = null, string?[]? TransferDestinations = null, string?[]? ExtractDestinations = null,
        WindowBounds? Bounds = null, bool AutoRefresh = true,
        Dictionary<string, CompressionPreset>? Compression = null, string ArchiveFormat = "7z");

    private readonly string? windowPreferencesPath = GetWindowPreferencesPath();
    private readonly WindowPreferences windowPreferences;
    private string initialWindowPreferences = "";
    private WindowBounds? windowBounds;
    private bool windowReady;

    private static string? GetWindowPreferencesPath()
    {
        var args = Environment.GetCommandLineArgs();
        int test = Array.IndexOf(args, "--verify-preferences");
        if (test >= 0 && args.Length > test + 2) return Path.Combine(args[test + 1], "window.json");
        if (args.Any(arg => arg is "--verify-stage3" or "--verify-stage4" or "--measure-list")) return null;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Arkvello", "window.json");
    }

    private WindowPreferences ReadWindowPreferences()
    {
        if (windowPreferencesPath == null) return new();
        try { return JsonSerializer.Deserialize<WindowPreferences>(File.ReadAllText(windowPreferencesPath)) ?? new(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    private void RestoreWindowPreferences(WindowPreferences preferences)
    {
        WindowHost.RequestedTheme = preferences.Theme switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        UpdateThemeMenu();
        splitRatio = preferences.SplitRatio is >= .25 and <= .75 ? preferences.SplitRatio : .5;
        RestorePane(panels[0], preferences.Left);
        RestorePane(panels[1], preferences.Right);
        RestoreHistory(recentPaths, preferences.RecentPaths);
        RestoreHistory(transferDestinations, preferences.TransferDestinations);
        RestoreHistory(extractDestinations, preferences.ExtractDestinations);
        if (preferences.Compression != null)
            compressionPresets = new(preferences.Compression, StringComparer.OrdinalIgnoreCase);
        lastArchiveFormat = preferences.ArchiveFormat;
        autoRefresh = preferences.AutoRefresh;
        AutoRefreshMenu.IsChecked = autoRefresh;
    }

    private void RestoreWindowBounds(double scale)
    {
        var saved = windowPreferences.Bounds;
        bool valid = saved is { Width: >= 200 and <= 32767, Height: >= 160 and <= 32767,
            X: >= -65536 and <= 65536, Y: >= -65536 and <= 65536 };
        var display = valid ? DisplayArea.GetFromRect(new(saved!.X, saved.Y, saved.Width, saved.Height), DisplayAreaFallback.Nearest)
            : DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var area = display.WorkArea;
        area.X += display.OuterBounds.X;
        area.Y += display.OuterBounds.Y;
        int width = Math.Min(valid ? saved!.Width : (int)(1180 * scale), Math.Max(1, area.Width - (valid ? 0 : 48)));
        int height = Math.Min(valid ? saved!.Height : (int)(780 * scale), Math.Max(1, area.Height - (valid ? 0 : 48)));
        int x = valid ? Math.Clamp(saved!.X, area.X, area.X + area.Width - width) : area.X + (area.Width - width) / 2;
        int y = valid ? Math.Clamp(saved!.Y, area.Y, area.Y + area.Height - height) : area.Y + (area.Height - height) / 2;
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
        bool maximize = valid && saved!.Maximized;
        windowBounds = new(x, y, width, height, maximize);
        if (maximize && AppWindow.Presenter is OverlappedPresenter presenter) presenter.Maximize();
        AppWindow.Changed += (_, e) =>
        {
            if (e.DidSizeChange || e.DidPositionChange || e.DidPresenterChange) CaptureWindowBounds();
        };
    }

    private void CaptureWindowBounds()
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter || windowBounds == null) return;
        switch (presenter.State)
        {
            case OverlappedPresenterState.Maximized:
                windowBounds = windowBounds with { Maximized = true };
                break;
            case OverlappedPresenterState.Restored:
                windowBounds = new(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height, false);
                break;
        }
    }

    private static void RestorePane(PanelState pane, PanePreferences? preferences)
    {
        if (preferences == null) return;
        if (preferences.View is "Details" or "List" or "SmallIcons" or "LargeIcons") pane.View = preferences.View;
        pane.MarkMode = preferences.MarkMode;
        if (preferences.TimePrecision is "D" or "M" or "S" or "F") pane.TimePrecision = preferences.TimePrecision;
        pane.TimeUtc = preferences.TimeUtc;
        if (preferences.Columns is { } saved)
        {
            var defaults = pane.Columns.ToDictionary(column => column.Property);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            pane.Columns.Clear();
            foreach (var column in saved.Take(132))
            {
                if (column?.Property is not { } property || column.Width is not (>= 90 and <= 4096) || seen.Contains(property)) continue;
                if (defaults.TryGetValue(property, out var original))
                    pane.Columns.Add(original with { Width = column.Width });
                else if (OptionalFileColumns.FirstOrDefault(item => item.Property == property) is { } optional)
                    pane.Columns.Add(optional with { Width = column.Width });
                else if (column.Label is { Length: > 0 and <= 128 } && property.StartsWith("Properties[", StringComparison.Ordinal) &&
                         property.EndsWith(']') && uint.TryParse(property.AsSpan(11, property.Length - 12), out uint id) && property == $"Properties[{id}]")
                    pane.Columns.Add(column);
                else continue;
                seen.Add(property);
            }
            pane.Columns.AddRange(defaults.Values.Where(column => !seen.Contains(column.Property)));
        }
        if (preferences.Sort == "None" || pane.Columns.Any(column => column.Property == preferences.Sort))
        {
            pane.Sort = preferences.Sort;
            pane.Descending = preferences.Descending;
        }
    }

    private WindowPreferences CaptureWindowPreferences(bool split)
    {
        static PanePreferences Capture(PanelState pane) => new(pane.View, pane.Sort, pane.Descending, pane.MarkMode, [.. pane.Columns],
            pane.TimePrecision, pane.TimeUtc);
        return new(WindowHost.RequestedTheme.ToString(), split, splitRatio, Capture(panels[0]), Capture(panels[1]),
            [.. recentPaths], [.. transferDestinations], [.. extractDestinations], windowBounds, autoRefresh,
            compressionPresets.Count == 0 ? null : compressionPresets, lastArchiveFormat);
    }

    private void SaveWindowPreferences()
    {
        if (!windowReady || windowPreferencesPath == null) return;
        string json = JsonSerializer.Serialize(CaptureWindowPreferences(twoPanels));
        if (json == initialWindowPreferences) return;
        string temporary = windowPreferencesPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(windowPreferencesPath)!);
            File.WriteAllText(temporary, json);
            File.Move(temporary, windowPreferencesPath, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temporary); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
        }
    }

    private void UpdateThemeMenu()
    {
        string theme = WindowHost.RequestedTheme.ToString();
        foreach (ToggleMenuFlyoutItem item in ThemeMenu.Items) item.IsChecked = (string)item.Tag == theme;
    }
}
