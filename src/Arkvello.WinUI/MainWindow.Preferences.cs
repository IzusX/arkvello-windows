using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Arkvello;

public sealed partial class MainWindow
{
    private sealed record PanePreferences(string View, string Sort, bool Descending, bool MarkMode, Column?[]? Columns);
    private sealed record WindowPreferences(string Theme = "Default", bool TwoPanels = false,
        double SplitRatio = .5, PanePreferences? Left = null, PanePreferences? Right = null);

    private readonly string? windowPreferencesPath = GetWindowPreferencesPath();
    private readonly WindowPreferences windowPreferences;
    private readonly string initialWindowPreferences;
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
    }

    private static void RestorePane(PanelState pane, PanePreferences? preferences)
    {
        if (preferences == null) return;
        if (preferences.View is "Details" or "List" or "SmallIcons" or "LargeIcons") pane.View = preferences.View;
        pane.MarkMode = preferences.MarkMode;
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
                else if (column.Label is { Length: > 0 and <= 128 } && property.StartsWith("Properties[", StringComparison.Ordinal) &&
                         property.EndsWith(']') && uint.TryParse(property.AsSpan(11, property.Length - 12), out uint id) && property == $"Properties[{id}]")
                    pane.Columns.Add(column);
                else continue;
                seen.Add(property);
            }
            pane.Columns.AddRange(defaults.Values.Where(column => !seen.Contains(column.Property)));
        }
        if (pane.Columns.Any(column => column.Property == preferences.Sort))
        {
            pane.Sort = preferences.Sort;
            pane.Descending = preferences.Descending;
        }
    }

    private WindowPreferences CaptureWindowPreferences(bool split)
    {
        static PanePreferences Capture(PanelState pane) => new(pane.View, pane.Sort, pane.Descending, pane.MarkMode, [.. pane.Columns]);
        return new(WindowHost.RequestedTheme.ToString(), split, splitRatio, Capture(panels[0]), Capture(panels[1]));
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
