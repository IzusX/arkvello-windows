using Arkvello.Models;
using Microsoft.UI.Xaml.Data;

namespace Arkvello.Controls;

public sealed class FileTimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not FileEntry entry || parameter is not string { Length: 3 } options) return "";
        DateTime? timestamp = options[0] switch
        {
            'C' => entry.Created,
            'A' => entry.Accessed,
            _ => entry.Modified
        };
        if (timestamp is not { } date) return "";
        bool utc = options[2] == 'U';
        return (utc ? date.ToUniversalTime() : date.ToLocalTime()).ToString(options[1] switch
        {
            'D' => "dd.MM.yyyy",
            'S' => "dd.MM.yyyy HH:mm:ss",
            'F' => "dd.MM.yyyy HH:mm:ss.fffffff",
            _ => "dd.MM.yyyy HH:mm"
        }) + (utc ? " UTC" : "");
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
