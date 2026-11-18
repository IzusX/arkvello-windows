using System.Text.Json;

namespace Arkvello.Services;

public static class MemoryPreference
{
    private static string Path => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Arkvello", "memory-limit.json");
    public static ulong Load()
    {
        try { return JsonSerializer.Deserialize<ulong>(File.ReadAllText(Path)) is > 0 and var bytes ? bytes : ulong.MaxValue; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return ulong.MaxValue; }
    }
    public static void Save(ulong bytes)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, JsonSerializer.Serialize(bytes));
    }
}
