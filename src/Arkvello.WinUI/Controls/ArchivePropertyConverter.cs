using Arkvello.Models;
using Microsoft.UI.Xaml.Data;

namespace Arkvello.Controls;

// Runtime XAML templates cannot reliably bind a numeric dictionary key through
// WinRT's indexed-property projection. Keep the lookup in the managed model.
public sealed class ArchivePropertyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is FileEntry entry ? entry.Properties?.GetValueOrDefault(parameter?.ToString() ?? "")?.Text ?? "" : "";
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
