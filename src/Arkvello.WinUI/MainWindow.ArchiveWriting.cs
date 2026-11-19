using System.Globalization;
using Arkvello.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Arkvello;

public sealed partial class MainWindow
{
    private ArchiveWriteFormat[]? archiveWriteFormats;

    private async void OnAddToArchive(object sender, RoutedEventArgs e)
    {
        if (!FilesystemReady) return;
        var pane = activePanel;
        var selected = OperatedFiles();
        if (selected.Length == 0) return;
        string[] paths = selected.Select(item => item.FullPath).ToArray();
        var request = await ConfigureArchiveWrite(paths, selected.Length == 1 && !selected[0].IsDirectory,
            selected.All(item => !item.IsDirectory && (item.Attributes & FileAttributes.ReparsePoint) == 0));
        if (request == null || closing) { if (!closing) FocusFileList(); return; }
        var result = await RunOperation(request.UpdateExisting ? "Обновление архива" : "Создание архива",
            job => ArchiveWriting.Run(request, job), delayProgress: true,
            cancellationMessage: request.DeleteSources
                ? "Если архив уже сохранён, часть исходных файлов могла остаться."
                : "Запись архива отменена до сохранения.");
        string output = request.Destination + (request.Volumes.Length > 0 ? ".001" : "");
        if (!closing && (result?.HResult == 0 || request.DeleteSources && File.Exists(output)))
        {
            await ReloadFilesystem([output]);
            if (OtherFilesystemDirectory.Length > 0 && !SameDirectory(OtherFilesystemDirectory, pane.Directory))
                await NavigateDirectory(OtherPanel, OtherFilesystemDirectory, remember: false, refresh: true);
        }
    }

    private async Task<ArchiveWriteRequest?> ConfigureArchiveWrite(string[] paths, bool oneFile, bool canDeleteSources)
    {
        configuring = true; UpdateStatus();
        try
        {
            archiveWriteFormats ??= await Task.Run(ArchiveWriting.Formats);
            if (closing) { Close(); return null; }
            var formats = archiveWriteFormats.Where(f => (!f.RequiresSingleFile || oneFile) &&
                (!f.Name.Equals("swfc", StringComparison.OrdinalIgnoreCase) ||
                    oneFile && Path.GetExtension(paths[0]).Equals(".swf", StringComparison.OrdinalIgnoreCase))).ToArray();
            if (formats.Length == 0) throw new IOException("Для выбранных элементов нет доступных форматов.");
            string folder = OtherFilesystemDirectory.Length > 0 ? OtherFilesystemDirectory : directory;
            string name = paths.Length == 1
                ? oneFile ? Path.GetFileNameWithoutExtension(paths[0]) : Path.GetFileName(paths[0])
                : Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
            if (string.IsNullOrEmpty(name)) name = "Archive";
            string suggestedStem = Path.Combine(folder, name);
            var path = WriteField("Путь к архиву");
            var choose = new Button { Content = "Выбрать папку…" };
            var errorText = new TextBlock { TextWrapping = TextWrapping.Wrap };
            choose.Click += async (_, _) =>
            {
                try
                {
                    var picker = new FolderPicker(); picker.FileTypeFilter.Add("*");
                    WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                    var target = await picker.PickSingleFolderAsync();
                    if (target != null) path.Text = Path.Combine(target.Path, Path.GetFileName(path.Text));
                }
                catch (Exception error) { errorText.Text = error.Message; }
            };
            var format = WriteChoice("Формат", formats, Array.FindIndex(formats, f => f.Name == "7z"));
            var level = WriteChoice("Уровень сжатия", new[] { "0 · Без сжатия", "1 · Скоростной", "3 · Быстрый", "5 · Нормальный", "7 · Максимальный", "9 · Ультра" }, 3);
            var method = WriteChoice("Метод сжатия", new[] { "По умолчанию" });
            var action = WriteChoice("Режим обновления", new[] { "Добавить и заменить", "Обновить устаревшие и добавить", "Обновить только существующие", "Синхронизировать" });
            var pathMode = WriteChoice("Пути в архиве", new[] { "Относительные", "Полные", "Абсолютные" });
            var volumes = WriteField("Размеры томов", "Например, 100M  1G");
            var threads = WriteField("Потоки", "Авто");
            var dictionary = WriteField("Размер словаря или памяти метода", "По умолчанию");
            var order = WriteField("Порядок или число быстрых байтов", "По умолчанию");
            var solid = WriteField("Размер непрерывного блока 7z", "По умолчанию");
            var memory = WriteField("Предел памяти", "Например, 70% или 4G");
            var properties = WriteField("Дополнительные параметры 7-Zip", "name=value");
            var sfx = new CheckBox { Content = "Создать самораспаковывающийся архив (.exe)" };
            var shared = new CheckBox { Content = "Сжимать файлы, открытые для записи" };
            var deleteSources = new CheckBox { Content = "Удалить исходные файлы после сохранения архива", IsEnabled = canDeleteSources };
            var archiveTime = new CheckBox { Content = "Сохранить время последнего изменения файлов как время архива" };
            var timeCreated = WriteChoice("Дата создания", new[] { "По умолчанию", "Сохранять", "Пропускать" });
            var timeModified = WriteChoice("Дата изменения", new[] { "По умолчанию", "Сохранять", "Пропускать" });
            var timeAccessed = WriteChoice("Дата доступа", new[] { "По умолчанию", "Сохранять", "Пропускать" });
            var password = new PasswordBox { Header = "Пароль", MaxLength = 4095 };
            var confirm = new PasswordBox { Header = "Повторите пароль", MaxLength = 4095 };
            var zipEncryption = WriteChoice("Метод шифрования ZIP", new[] { "AES-256", "ZipCrypto" });
            var headers = new CheckBox { Content = "Шифровать имена файлов в 7z" };
            (CheckBox Input, ArchiveWriteFlags Flag)[] metadata =
            [
                (new() { Content = "Сохранять символические ссылки" }, ArchiveWriteFlags.SymbolicLinks),
                (new() { Content = "Сохранять жёсткие ссылки" }, ArchiveWriteFlags.HardLinks),
                (new() { Content = "Сохранять альтернативные потоки NTFS" }, ArchiveWriteFlags.Streams),
                (new() { Content = "Сохранять разрешения NTFS" }, ArchiveWriteFlags.Permissions)
            ];

            string lastSuggestion = "";
            void UpdateFormat()
            {
                var selected = (ArchiveWriteFormat)format.SelectedItem;
                string key = selected.Name.ToLowerInvariant();
                bool is7z = key == "7z", isZip = key == "zip", isSfx = is7z && sfx.IsChecked == true;
                bool compressing = key is not ("tar" or "wim");
                bool stored = level.SelectedIndex == 0;
                sfx.IsEnabled = is7z && File.Exists(Path.Combine(AppContext.BaseDirectory, "7z.sfx"));
                headers.IsEnabled = is7z;
                zipEncryption.IsEnabled = isZip;
                password.IsEnabled = confirm.IsEnabled = is7z || isZip;
                solid.IsEnabled = is7z && !stored;
                level.IsEnabled = compressing;
                threads.IsEnabled = memory.IsEnabled = (is7z || isZip || key == "xz") && !stored;
                string[] methods = key switch
                {
                    "7z" when isSfx => ["LZMA2", "LZMA", "PPMd"],
                    "7z" => ["LZMA2", "LZMA", "PPMd", "BZip2", "Deflate", "Deflate64"],
                    "zip" => ["Deflate", "Deflate64", "BZip2", "LZMA", "PPMd"],
                    "xz" => ["LZMA2"],
                    "gzip" => ["Deflate"],
                    "bzip2" => ["BZip2"],
                    "tar" => ["GNU", "POSIX"],
                    _ => []
                };
                string[] choices = ["По умолчанию", .. methods];
                if (!method.Items.Cast<string>().SequenceEqual(choices))
                {
                    string? previous = method.SelectedItem as string;
                    method.ItemsSource = choices;
                    method.SelectedIndex = Math.Max(0, Array.IndexOf(choices, previous));
                }
                method.IsEnabled = methods.Length > 0 && (!stored || !compressing);
                string? codec = method.SelectedIndex > 0 ? method.SelectedItem as string : methods.FirstOrDefault();
                dictionary.IsEnabled = !stored && codec is "LZMA" or "LZMA2" or "PPMd" or "BZip2";
                order.IsEnabled = !stored && codec is "LZMA" or "LZMA2" or "PPMd" or "Deflate" or "Deflate64";
                foreach (var (input, flag) in metadata) input.IsEnabled = selected.Metadata.HasFlag(flag);
                timeCreated.IsEnabled = selected.StoresCreatedTime && key != "tar";
                timeModified.IsEnabled = selected.StoresModifiedTime;
                timeAccessed.IsEnabled = selected.StoresAccessedTime && (key != "tar" || codec == "POSIX");
                string suggested = suggestedStem + "." + (isSfx ? "exe" : selected.Extension);
                if (path.Text.Length == 0 || path.Text == lastSuggestion) path.Text = suggested;
                else if (Path.GetExtension(path.Text) == Path.GetExtension(lastSuggestion))
                    path.Text = Path.ChangeExtension(path.Text, isSfx ? "exe" : selected.Extension);
                lastSuggestion = suggested;
            }
            format.SelectionChanged += (_, _) => UpdateFormat();
            level.SelectionChanged += (_, _) => UpdateFormat();
            method.SelectionChanged += (_, _) =>
            {
                if (method.SelectedIndex >= 0) UpdateFormat();
            };
            sfx.Checked += (_, _) => UpdateFormat();
            sfx.Unchecked += (_, _) => UpdateFormat();
            UpdateFormat();

            var extra = new StackPanel { Spacing = 10, Padding = new(0, 8, 0, 0) };
            foreach (var control in new FrameworkElement[] { method, dictionary, order, solid, threads, memory, volumes,
                action, pathMode, password, confirm, zipEncryption, headers, sfx, properties, shared, deleteSources, archiveTime,
                timeCreated, timeModified, timeAccessed })
                extra.Children.Add(control);
            foreach (var (input, _) in metadata) extra.Children.Add(input);
            var form = new StackPanel { Spacing = 10, MaxWidth = 540 };
            foreach (var control in new FrameworkElement[]
            {
                path, choose, format, level,
                new Expander { Header = "Дополнительные параметры", Content = extra, HorizontalAlignment = HorizontalAlignment.Stretch },
                errorText
            }) form.Children.Add(control);
            var dialog = NewDialog("Добавить в архив", new ScrollViewer { Content = form, MaxHeight = 540, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
                "Сжать", "Отмена");
            ArchiveWriteRequest? request = null;
            dialog.PrimaryButtonClick += (_, args) =>
            {
                try
                {
                    string target = Path.GetFullPath(path.Text.Trim().Trim('"'), directory);
                    if (Directory.Exists(target) || !Directory.Exists(Path.GetDirectoryName(target)))
                        throw new IOException("Укажите имя файла в существующей папке.");
                    var selected = (ArchiveWriteFormat)format.SelectedItem;
                    bool is7z = selected.Name.Equals("7z", StringComparison.OrdinalIgnoreCase);
                    string? sfxModule = sfx.IsEnabled && sfx.IsChecked == true ? Path.Combine(AppContext.BaseDirectory, "7z.sfx") : null;
                    if (sfxModule != null && !Path.GetExtension(target).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Укажите расширение .exe для самораспаковывающегося архива.");
                    string? secret = password.IsEnabled && password.Password.Length > 0 ? password.Password : null;
                    if (password.IsEnabled && password.Password != confirm.Password) throw new IOException("Пароли не совпадают.");
                    if (zipEncryption.IsEnabled && secret?.Any(c => c is < ' ' or > '~') == true)
                        throw new IOException("Для пароля ZIP используйте печатные символы ASCII.");
                    var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    if (level.IsEnabled) options["x"] = new[] { "0", "1", "3", "5", "7", "9" }[level.SelectedIndex];
                    if (method.IsEnabled && method.SelectedIndex > 0) options[is7z ? "0" : "m"] = (string)method.SelectedItem;
                    string? codec = method.SelectedIndex > 0 ? method.SelectedItem as string :
                        is7z || selected.Name.Equals("xz", StringComparison.OrdinalIgnoreCase) ? "LZMA2" : selected.Name;
                    string prefix = is7z ? "0" : "";
                    if (dictionary.IsEnabled && dictionary.Text.Trim().Length > 0)
                        options[prefix + (codec == "PPMd" ? "mem" : "d")] = ParseArchiveSize(dictionary.Text) + "b";
                    if (order.IsEnabled && order.Text.Trim().Length > 0)
                        options[prefix + (codec == "PPMd" ? "o" : "fb")] = ParsePositiveNumber(order.Text);
                    if (threads.IsEnabled && threads.Text.Trim().Length > 0) options["mt"] = ParsePositiveNumber(threads.Text);
                    if (solid.IsEnabled && solid.Text.Trim().Length > 0)
                    {
                        string block = solid.Text.Trim();
                        options["s"] = block is "on" or "off" ? block : ParseArchiveSize(block) + "b";
                    }
                    if (memory.IsEnabled && memory.Text.Trim().Length > 0)
                    {
                        string limit = memory.Text.Trim();
                        options["memuse"] = limit.EndsWith('%') ? ParsePositiveNumber(limit[..^1]) + "%" : ParseArchiveSize(limit) + "b";
                    }
                    if (secret != null && zipEncryption.IsEnabled) options["em"] = zipEncryption.SelectedIndex == 0 ? "AES256" : "ZipCrypto";
                    if (secret != null && headers.IsEnabled && headers.IsChecked == true) options["he"] = "on";
                    foreach (var (control, key) in new[] { (timeCreated, "tc"), (timeModified, "tm"), (timeAccessed, "ta") })
                        if (control.IsEnabled && control.SelectedIndex > 0) options[key] = control.SelectedIndex == 1 ? "on" : "off";
                    foreach (string option in properties.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    {
                        int equals = option.IndexOf('=');
                        if (equals == 0) throw new FormatException("Укажите имя параметра 7-Zip.");
                        options[equals < 0 ? option : option[..equals]] = equals < 0 ? "" : option[(equals + 1)..];
                    }
                    var flags = (shared.IsChecked == true ? ArchiveWriteFlags.SharedRead : 0) |
                        (archiveTime.IsChecked == true ? ArchiveWriteFlags.ArchiveTime : 0);
                    foreach (var (input, flag) in metadata)
                        if (input.IsEnabled && input.IsChecked == true) flags |= flag;
                    ulong[] sizes = volumes.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(ParseArchiveSize).ToArray();
                    bool exists = File.Exists(target);
                    if (exists && sizes.Length > 0) throw new IOException("Укажите новое имя для многотомного архива.");
                    if (deleteSources.IsEnabled && deleteSources.IsChecked == true)
                    {
                        if (exists || action.SelectedIndex != 0 || shared.IsChecked == true)
                            throw new IOException("Удаление исходников доступно при создании нового архива в режиме «Добавить и заменить», без совместной записи.");
                        flags |= ArchiveWriteFlags.DeleteSources;
                    }
                    request = new(target, selected.Name, paths, options, sizes, secret, sfxModule, exists,
                        (ArchiveUpdateAction)action.SelectedIndex, (ArchiveWritePath)pathMode.SelectedIndex, flags);
                }
                catch (Exception error) when (error is IOException or ArgumentException or FormatException or OverflowException or NotSupportedException)
                { args.Cancel = true; request = null; errorText.Text = error.Message; }
            };
            dialog.Opened += (_, _) => { path.Focus(FocusState.Programmatic); path.SelectAll(); };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || closing || request == null) return null;
            if (request.UpdateExisting)
            {
                var update = NewDialog("Обновить существующий архив?", new TextBlock { Text = request.Destination, TextWrapping = TextWrapping.Wrap },
                    "Обновить", "Отмена");
                update.DefaultButton = ContentDialogButton.Close;
                if (await update.ShowAsync() != ContentDialogResult.Primary) return null;
            }
            if (request.DeleteSources)
            {
                var remove = NewDialog("Удалить исходные файлы после сжатия?",
                    new TextBlock { Text = $"После сохранения архива будет удалено без корзины файлов: {paths.Length:N0}.",
                        TextWrapping = TextWrapping.Wrap, MaxWidth = 480 }, "Сжать и удалить", "Отмена");
                remove.DefaultButton = ContentDialogButton.Close;
                if (await remove.ShowAsync() != ContentDialogResult.Primary) return null;
            }
            return request;
        }
        catch (Exception error) { ShowError(error.Message); return null; }
        finally { configuring = false; UpdateStatus(); }
    }

    private static TextBox WriteField(string label, string placeholder = "") => new() { Header = label, PlaceholderText = placeholder };
    private static ComboBox WriteChoice<T>(string label, T[] options, int index = 0) =>
        new() { Header = label, ItemsSource = options, SelectedIndex = Math.Max(0, index), HorizontalAlignment = HorizontalAlignment.Stretch };

    private static string ParsePositiveNumber(string text)
    {
        if (!uint.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out uint number) || number == 0)
            throw new FormatException("Укажите положительное целое число: " + text);
        return number.ToString(CultureInfo.InvariantCulture);
    }

    private static ulong ParseArchiveSize(string text)
    {
        string value = text.Trim().ToLowerInvariant();
        if (value.EndsWith("ib")) value = value[..^2];
        else if (value.EndsWith('b')) value = value[..^1];
        int unit = value.Length > 0 ? "kmgt".IndexOf(value[^1]) : -1;
        if (unit >= 0) value = value[..^1];
        if (!ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ulong count) || count == 0)
            throw new FormatException("Укажите размер в байтах или с суффиксом K, M, G, T: " + text);
        return checked(count * (1UL << ((unit + 1) * 10)));
    }
}
