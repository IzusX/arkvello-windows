using Arkvello.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Arkvello;

public sealed partial class MainWindow
{
    private Dictionary<(Guid Session, uint Property), IReadOnlyDictionary<uint, ArchiveProperty>> propertyCache => activePanel.Properties;
    private uint[] SelectedProperties => columns.Where(c => c.Property.StartsWith("Properties[", StringComparison.Ordinal)).Select(c => uint.Parse(c.Property[11..^1], System.Globalization.CultureInfo.InvariantCulture)).ToArray();
    private static ArchiveResult LoadSessionColumns(ArchiveSession session, uint[] selected, ArchiveJob job,
        Dictionary<uint, IReadOnlyDictionary<uint, ArchiveProperty>> loaded)
    {
        if (selected.Length == 0) return new(0, job.Issues);
        var available = session.Archive.ReadColumns().Select(c => c.Id).ToHashSet();
        foreach (uint property in selected.Where(available.Contains))
        {
            var result = session.Archive.ReadProperty(property, job);
            if (!result.Result.Succeeded) return result.Result;
            loaded.Add(property, result.Values);
        }
        return new(0, job.Issues);
    }
    private void RetainSessionColumns(ArchiveSession session, Dictionary<uint, IReadOnlyDictionary<uint, ArchiveProperty>> loaded)
    {
        foreach (var pair in loaded) propertyCache[(session.Identity, pair.Key)] = pair.Value;
    }
    private void DropSessionColumns(ArchiveSession session)
    {
        foreach (var key in propertyCache.Keys.Where(k => k.Session == session.Identity).ToArray()) propertyCache.Remove(key);
    }
    private static string PropertyLabel(ArchiveColumn column) => column.Name.Length > 0 ? column.Name : column.Id switch
    {
        3 => "Путь", 4 => "Имя в архиве", 6 => "Папка", 7 => "Размер в архиве", 8 => "Сжатый размер",
        9 => "Атрибуты", 10 => "Создан", 11 => "Открыт", 12 => "Изменён", 13 => "Непрерывный",
        15 => "Зашифрован", 19 => "CRC", 22 => "Метод", 23 => "Исходная ОС", 25 => "Владелец", 26 => "Группа",
        27 => "Блок", 28 => "Комментарий", 53 => "POSIX-атрибуты", 54 => "Символьная ссылка",
        _ => "Свойство " + column.Id
    };
    private async void OnColumns(object sender, RoutedEventArgs e)
    {
        if (busy || configuring || archiveSession == null) return;
        var session = archiveSession;
        IReadOnlyList<ArchiveColumn> available = [];
        await RunOperation("Свойства архива", _ => { available = session.Archive.ReadColumns(); return new(0, []); }, showSuccess: false);
        if (closing || available.Count == 0) return;
        var choices = new ListView { SelectionMode = ListViewSelectionMode.Multiple, IsMultiSelectCheckBoxEnabled = true, MaxHeight = 320 };
        AutomationProperties.SetName(choices, "Дополнительные столбцы архива");
        foreach (var property in available)
        {
            var choice = new ListViewItem { Content = PropertyLabel(property), Tag = property };
            choices.Items.Add(choice);
            if (columns.Any(c => c.Property == "Properties[" + property.Id + "]")) choices.SelectedItems.Add(choice);
        }
        configuring = true;
        ContentDialogResult answer;
        try { answer = await NewDialog("Столбцы архива", choices, "Применить", "Отмена").ShowAsync(); }
        finally { configuring = false; }
        if (closing || answer != ContentDialogResult.Primary) return;
        var selected = choices.SelectedItems.Cast<ListViewItem>().Select(c => (ArchiveColumn)c.Tag).ToArray();
        var result = await RunOperation("Загрузка столбцов", job =>
        {
            foreach (var property in selected)
            {
                var key = (session.Identity, property.Id);
                if (propertyCache.ContainsKey(key)) continue;
                var loaded = session.Archive.ReadProperty(property.Id, job);
                if (!loaded.Result.Succeeded) return loaded.Result;
                propertyCache.Add(key, loaded.Values);
            }
            return new(0, job.Issues);
        }, showSuccess: false);
        if (closing || result?.Succeeded != true) return;
        columns.RemoveAll(c => c.Property.StartsWith("Properties[", StringComparison.Ordinal));
        columns.AddRange(selected.Select(p => new Column(PropertyLabel(p), "Properties[" + p.Id + "]", 170)));
        ShowArchiveFolder();
    }
    private IReadOnlyDictionary<string, ArchiveProperty> EntryProperties(uint index)
    {
        var result = new Dictionary<string, ArchiveProperty>();
        if (archiveSession == null) return result;
        foreach (var pair in propertyCache)
            if (pair.Key.Session == archiveSession.Identity && pair.Value.TryGetValue(index, out ArchiveProperty? value)) result[pair.Key.Property.ToString()] = value;
        // Bindings for columns retained from another archive resolve to blank.
        foreach (var column in columns.Where(c => c.Property.StartsWith("Properties[", StringComparison.Ordinal))) result.TryAdd(column.Property[11..^1], new(0, "", 0));
        return result;
    }
    private async void OnEdit(object sender, RoutedEventArgs e) => await EditCurrent();
}
