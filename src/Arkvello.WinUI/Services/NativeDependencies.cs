using System.Runtime.InteropServices;

namespace Arkvello.Services;

public static class NativeDependencies
{
    public static string? Check(string directory)
    {
        foreach (string name in new[] { "Arkvello.Native.dll", "7z.dll" })
        {
            string path = Path.Combine(directory, name);
            if (!File.Exists(path)) return "Не найден " + name + ". Восстановите все файлы сборки Arkvello в одной папке.";
            try { var module = NativeLibrary.Load(path); NativeLibrary.Free(module); }
            catch (Exception error) when (error is DllNotFoundException or BadImageFormatException)
            { return "Не удалось загрузить " + name + ". Нужна полная сборка для Windows x64. " + error.Message; }
        }
        return null;
    }
}
