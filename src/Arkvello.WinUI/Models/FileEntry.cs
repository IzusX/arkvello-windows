using Arkvello.Services;
namespace Arkvello.Models;

public sealed record FileEntry(string Identity, string Name, string FullPath, bool IsDirectory,
    ulong? Size, DateTime? Modified, uint? ArchiveIndex = null, uint[]? DescendantIndices = null,
    IReadOnlyDictionary<string, ArchiveProperty>? Properties = null)
{
    public string SizeText => Size is { } size ? size.ToString("N0") + " Б" : "";
    public string ModifiedText => Modified?.ToString("dd.MM.yyyy HH:mm") ?? "";
    public string TypeText => IsDirectory ? "Папка" : ArchiveIndex != null ? "Файл в архиве" : Path.GetExtension(Name).TrimStart('.').ToUpperInvariant() is { Length: > 0 } ext ? ext + " — файл" : "Файл";
    public Microsoft.UI.Xaml.Visibility FolderVisibility => IsDirectory ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility FileVisibility => IsDirectory ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
    public string AccessibleName => string.Join(", ", new[] { Name, TypeText, SizeText, ModifiedText }.Where(s => s.Length > 0));
}
