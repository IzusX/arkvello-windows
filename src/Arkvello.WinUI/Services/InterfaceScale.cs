using System.Text.Json;

namespace Arkvello.Services;

internal static class InterfaceScale
{
    public static readonly int[] Percentages = [75, 80, 90, 100, 110, 125, 150];
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Arkvello", "ui-scale.json");

    public static int Load()
    {
        try
        {
            int percent = JsonSerializer.Deserialize<int>(File.ReadAllText(SettingsPath));
            if (Percentages.Contains(percent)) return percent;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        return 100;
    }

    public static bool TrySave(int percent)
    {
        if (!Percentages.Contains(percent)) return false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(percent));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }
}
