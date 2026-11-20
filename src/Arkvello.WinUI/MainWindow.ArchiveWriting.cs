using System.Globalization;
using Arkvello.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Arkvello;

public sealed partial class MainWindow
{
    private ArchiveWriteFormat[]? archiveWriteFormats;
    private sealed record CompressionPreset(int Level, string Method, string Dictionary, string Order,
        string Threads, string Block, string Memory, int Precision);
    private Dictionary<string, CompressionPreset> compressionPresets = new(StringComparer.OrdinalIgnoreCase);
    private string lastArchiveFormat = "7z";

    private async void OnAddToArchive(object sender, RoutedEventArgs e)
    {
        if (!FilesystemReady) return;
        var pane = activePanel;
        var selected = OperatedFiles();
        if (selected.Length == 0) return;
        string[] paths = selected.Select(item => item.FullPath).ToArray();
        var request = await ConfigureArchiveWrite(paths, selected.Length == 1 && !selected[0].IsDirectory,
            selected.All(item => (item.Attributes & FileAttributes.ReparsePoint) == 0));
        if (request == null || closing) { if (!closing) FocusFileList(); return; }
        var result = await RunOperation(request.UpdateExisting ? "Обновление архива" : "Создание архива",
            job => ArchiveWriting.Run(request, job), delayProgress: true,
            cancellationMessage: request.DeleteSources
                ? "Если архив уже сохранён, часть исходных файлов и папок могла остаться."
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
            int preferredFormat = Array.FindIndex(formats, f => f.Name.Equals(lastArchiveFormat, StringComparison.OrdinalIgnoreCase));
            var format = WriteChoice("Формат", formats, preferredFormat >= 0 ? preferredFormat : Array.FindIndex(formats, f => f.Name == "7z"));
            string[] levelLabels = ["0 · Без сжатия", "1 · Скоростной", "2", "3 · Быстрый", "4",
                "5 · Нормальный", "6", "7 · Максимальный", "8", "9 · Ультра"];
            int[] levels = [];
            var level = WriteChoice("Уровень сжатия", levelLabels, 5);
            var method = WriteChoice("Метод сжатия", new[] { "По умолчанию" });
            var action = WriteChoice("Режим обновления", new[] { "Добавить и заменить", "Обновить устаревшие и добавить", "Обновить только существующие", "Синхронизировать" });
            var pathMode = WriteChoice("Пути в архиве", new[] { "Относительные", "Полные", "Абсолютные" });
            var volumes = WriteField("Размеры томов", "Например, 100M  1G");
            var threads = WriteField("Потоки", "Авто");
            var dictionary = WriteField("Размер словаря или памяти метода", "По умолчанию");
            var order = WriteField("Порядок или число быстрых байтов", "По умолчанию");
            var solid = WriteField("Размер непрерывного блока 7z", "По умолчанию");
            var memory = WriteField("Предел памяти", "Например, 70% или 4G");
            var estimate = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var properties = WriteField("Дополнительные параметры 7-Zip", "name=value");
            var sfx = new CheckBox { Content = "Создать самораспаковывающийся архив (.exe)" };
            var shared = new CheckBox { Content = "Сжимать файлы, открытые для записи" };
            var deleteSources = new CheckBox { Content = "Удалить исходные файлы и папки после сохранения архива", IsEnabled = canDeleteSources };
            var archiveTime = new CheckBox { Content = "Сохранить время последнего изменения файлов как время архива" };
            var timeCreated = WriteChoice("Дата создания", new[] { "По умолчанию", "Сохранять", "Пропускать" });
            var timeModified = WriteChoice("Дата изменения", new[] { "По умолчанию", "Сохранять", "Пропускать" });
            var timeAccessed = WriteChoice("Дата доступа", new[] { "По умолчанию", "Сохранять", "Пропускать" });
            var precision = WriteChoice("Точность времени", new[] { "По умолчанию" });
            string[] precisionLabels = ["100 нс · Windows", "1 с · Unix", "2 с · DOS", "1 нс"];
            int[] precisions = [];
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
            bool updatingFormat = false;
            string[] methodsForFormat = [];
            string? currentFormat = null;
            string currentCodec = "";
            var presets = new Dictionary<string, CompressionPreset>(compressionPresets, StringComparer.OrdinalIgnoreCase);
            string Codec() => method.SelectedIndex > 0 ? (string)method.SelectedItem : methodsForFormat.FirstOrDefault() ?? "";
            CompressionPreset CapturePreset() => new(levels[level.SelectedIndex],
                method.SelectedIndex > 0 ? Codec() : "", dictionary.Text, order.Text, threads.Text, solid.Text,
                memory.Text, precisions[precision.SelectedIndex]);
            void UpdateMemory()
            {
                if (updatingFormat) return;
                var selected = (ArchiveWriteFormat)format.SelectedItem;
                if (level.SelectedIndex < 0 || !level.IsEnabled ||
                    (Codec().Length == 0 && levels[level.SelectedIndex] != 0))
                {
                    estimate.Visibility = Visibility.Collapsed;
                    return;
                }
                estimate.Visibility = Visibility.Visible;
                if (properties.Text.Trim().Length != 0)
                {
                    estimate.Text = "Оценка памяти недоступна с дополнительными параметрами 7-Zip.";
                    return;
                }
                try
                {
                    uint threadCount = threads.IsEnabled && threads.Text.Trim().Length > 0
                        ? uint.Parse(ParsePositiveNumber(threads.Text), CultureInfo.InvariantCulture) : 0;
                    ulong dict = dictionary.IsEnabled && dictionary.Text.Trim().Length > 0 ? ParseArchiveSize(dictionary.Text) : 0;
                    ulong xzBlock = selected.Name.Equals("xz", StringComparison.OrdinalIgnoreCase) &&
                        solid.IsEnabled && solid.Text.Trim().Length > 0 ? solid.Text.Trim().ToLowerInvariant() switch
                        {
                            "on" => 1UL, "off" => 0UL, var size => ParseArchiveSize(size)
                        } : 0;
                    var (bytes, percent) = MemoryLimit();
                    var usage = ArchiveWriting.EstimateMemory(selected.Name, Codec(), (uint)levels[level.SelectedIndex],
                        threadCount, dict, xzBlock, bytes, percent);
                    estimate.Text = usage is { } value
                        ? $"Память при сжатии ≈ {SizeInMiB(value.Compression)}. При распаковке ≈ {SizeInMiB(value.Decompression)}."
                          + (memory.IsEnabled ? $" Предел {SizeInMiB(value.Budget)}." : "")
                          + (memory.IsEnabled && value.Compression > value.Budget ? " Оценка выше предела." : "")
                        : "Оценка памяти недоступна для этих параметров.";
                }
                catch (Exception error) when (error is FormatException or OverflowException)
                { estimate.Text = "Проверьте словарь, потоки, размер блока и предел памяти."; }
            }
            (ulong Bytes, uint Percent) MemoryLimit()
            {
                string limit = memory.Text.Trim();
                if (!memory.IsEnabled || limit.Length == 0) return (0, 0);
                if (!limit.EndsWith('%')) return (ParseArchiveSize(limit), 0);
                return (0, uint.Parse(ParsePositiveNumber(limit[..^1]), CultureInfo.InvariantCulture));
            }
            void UpdateFormat()
            {
                if (updatingFormat) return;
                updatingFormat = true;
                var selected = (ArchiveWriteFormat)format.SelectedItem;
                string key = selected.Name.ToLowerInvariant();
                bool newFormat = key != currentFormat;
                CompressionPreset? preset = null;
                if (newFormat)
                {
                    if (currentFormat != null) presets[currentFormat] = CapturePreset();
                    currentFormat = key;
                    presets.TryGetValue(key, out preset);
                }
                bool is7z = key == "7z", isZip = key == "zip", isSfx = is7z && sfx.IsChecked == true;
                bool compressing = key is not ("tar" or "wim");
                int[] availableLevels = key switch
                {
                    "zip" => [0, 1, 3, 5, 7, 9],
                    "gzip" => [1, 5, 7, 9],
                    "bzip2" => [1, 3, 5, 7, 9],
                    "xz" => [1, 2, 3, 4, 5, 6, 7, 8, 9],
                    "tar" or "wim" => [0],
                    _ => [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
                };
                if (newFormat || !levels.SequenceEqual(availableLevels))
                {
                    int previous = preset?.Level ?? 5;
                    levels = availableLevels;
                    level.ItemsSource = levels.Select(i => levelLabels[i]).ToArray();
                    int index = Array.IndexOf(levels, previous);
                    level.SelectedIndex = index >= 0 ? index : Math.Max(0, Array.IndexOf(levels, 5));
                }
                bool stored = levels[level.SelectedIndex] == 0;
                sfx.IsEnabled = is7z && File.Exists(Path.Combine(AppContext.BaseDirectory, "7z.sfx"));
                headers.IsEnabled = is7z;
                zipEncryption.IsEnabled = isZip;
                password.IsEnabled = confirm.IsEnabled = is7z || isZip;
                solid.Header = key == "xz" ? "Размер блока XZ" : "Размер непрерывного блока 7z";
                solid.IsEnabled = (is7z || key == "xz") && !stored;
                level.IsEnabled = compressing;
                methodsForFormat = key switch
                {
                    "7z" when isSfx => ["LZMA2", "LZMA", "PPMd"],
                    "7z" => ["LZMA2", "LZMA", "PPMd", "BZip2", "Deflate", "Deflate64", "Copy"],
                    "zip" => ["Deflate", "Deflate64", "BZip2", "LZMA", "PPMd"],
                    "xz" => ["LZMA2"],
                    "gzip" => ["Deflate"],
                    "bzip2" => ["BZip2"],
                    "tar" => ["GNU", "POSIX"],
                    _ => []
                };
                string[] choices = ["По умолчанию", .. methodsForFormat];
                if (newFormat || !method.Items.Cast<string>().SequenceEqual(choices))
                {
                    string? previous = newFormat ? preset?.Method : method.SelectedItem as string;
                    method.ItemsSource = choices;
                    method.SelectedIndex = Math.Max(0, Array.IndexOf(choices, previous));
                }
                method.IsEnabled = methodsForFormat.Length > 0 && (!stored || !compressing);
                string codec = Codec();
                threads.IsEnabled = !stored && (isZip || codec is "LZMA" or "LZMA2" or "BZip2");
                memory.IsEnabled = compressing && !stored && codec != "Copy";
                dictionary.IsEnabled = !stored && codec is "LZMA" or "LZMA2" or "PPMd" or "BZip2";
                order.IsEnabled = !stored && codec is "LZMA" or "LZMA2" or "PPMd" or "Deflate" or "Deflate64";
                foreach (var (input, flag) in metadata) input.IsEnabled = selected.Metadata.HasFlag(flag);
                int[] formatsPrecisions = [-1, .. Enumerable.Range(0, precisionLabels.Length)
                    .Where(i => ((selected.TimePrecisions | 1u << (int)selected.DefaultTimePrecision) & (1u << i)) != 0 &&
                        (key != "tar" || codec == "POSIX" || i == 1))];
                if (newFormat || !precisions.SequenceEqual(formatsPrecisions))
                {
                    int previous = newFormat ? preset?.Precision ?? -1 : precisions[precision.SelectedIndex];
                    precisions = formatsPrecisions;
                    precision.ItemsSource = precisions.Select(i => i < 0 ? "По умолчанию" : precisionLabels[i]).ToArray();
                    precision.SelectedIndex = Math.Max(0, Array.IndexOf(precisions, previous));
                }
                precision.IsEnabled = precisions.Length > 2;
                uint selectedPrecision = precision.SelectedIndex > 0 ? (uint)precisions[precision.SelectedIndex] : selected.DefaultTimePrecision;
                bool zipNtfsTimes = !isZip || selectedPrecision == 0;
                timeCreated.IsEnabled = selected.StoresCreatedTime && key != "tar" && zipNtfsTimes;
                timeModified.IsEnabled = selected.StoresModifiedTime;
                timeAccessed.IsEnabled = zipNtfsTimes && (key == "tar" ? codec == "POSIX" : selected.StoresAccessedTime);
                string suggested = suggestedStem + "." + (isSfx ? "exe" : selected.Extension);
                if (path.Text.Length == 0 || path.Text == lastSuggestion) path.Text = suggested;
                else if (Path.GetExtension(path.Text) == Path.GetExtension(lastSuggestion))
                    path.Text = Path.ChangeExtension(path.Text, isSfx ? "exe" : selected.Extension);
                lastSuggestion = suggested;
                if (newFormat)
                {
                    dictionary.Text = preset?.Dictionary ?? "";
                    order.Text = preset?.Order ?? "";
                    threads.Text = preset?.Threads ?? "";
                    solid.Text = preset?.Block ?? "";
                    memory.Text = preset?.Memory ?? "";
                }
                else if (!string.Equals(currentCodec, codec, StringComparison.Ordinal))
                {
                    dictionary.Text = order.Text = threads.Text = "";
                }
                currentCodec = codec;
                updatingFormat = false;
                UpdateMemory();
            }
            format.SelectionChanged += (_, _) => UpdateFormat();
            level.SelectionChanged += (_, _) => UpdateFormat();
            method.SelectionChanged += (_, _) =>
            {
                if (method.SelectedIndex >= 0) UpdateFormat();
            };
            sfx.Checked += (_, _) => UpdateFormat();
            sfx.Unchecked += (_, _) => UpdateFormat();
            precision.SelectionChanged += (_, _) => UpdateFormat();
            foreach (var field in new[] { dictionary, threads, solid, memory, properties })
                field.TextChanged += (_, _) => UpdateMemory();
            UpdateFormat();

            var extra = new StackPanel { Spacing = 10, Padding = new(0, 8, 0, 0) };
            foreach (var control in new FrameworkElement[] { method, dictionary, order, solid, threads, memory, volumes,
                action, pathMode, password, confirm, zipEncryption, headers, sfx, properties, shared, deleteSources, archiveTime,
                precision, timeCreated, timeModified, timeAccessed })
                extra.Children.Add(control);
            foreach (var (input, _) in metadata) extra.Children.Add(input);
            var form = new StackPanel { Spacing = 10, MaxWidth = 540 };
            foreach (var control in new FrameworkElement[]
            {
                path, choose, format, level, estimate,
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
                    if (level.IsEnabled) options["x"] = levels[level.SelectedIndex].ToString(CultureInfo.InvariantCulture);
                    if (method.IsEnabled && method.SelectedIndex > 0) options[is7z ? "0" : "m"] = (string)method.SelectedItem;
                    string codec = Codec();
                    string prefix = is7z ? "0" : "";
                    if (dictionary.IsEnabled && dictionary.Text.Trim().Length > 0)
                        options[prefix + (codec == "PPMd" ? "mem" : "d")] = ParseArchiveSize(dictionary.Text) + "b";
                    if (order.IsEnabled && order.Text.Trim().Length > 0)
                        options[prefix + (codec == "PPMd" ? "o" : "fb")] = ParsePositiveNumber(order.Text);
                    if (threads.IsEnabled && threads.Text.Trim().Length > 0) options["mt"] = ParsePositiveNumber(threads.Text);
                    if (solid.IsEnabled && solid.Text.Trim().Length > 0)
                    {
                        string block = solid.Text.Trim().ToLowerInvariant();
                        options["s"] = block is "on" or "off" ? block : ParseArchiveSize(block) + "b";
                    }
                    var (bytes, percent) = MemoryLimit();
                    if (bytes != 0) options["memuse"] = bytes + "b";
                    if (percent != 0) options["memuse"] = percent + "%";
                    if (secret != null && zipEncryption.IsEnabled) options["em"] = zipEncryption.SelectedIndex == 0 ? "AES256" : "ZipCrypto";
                    if (secret != null && headers.IsEnabled && headers.IsChecked == true) options["he"] = "on";
                    if (precision.IsEnabled && precision.SelectedIndex > 0)
                        options["tp"] = precisions[precision.SelectedIndex].ToString(CultureInfo.InvariantCulture);
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
                        if (action.SelectedIndex != 0 || shared.IsChecked == true)
                            throw new IOException("Удаление исходников доступно в режиме «Добавить и заменить», без совместной записи.");
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
                var remove = NewDialog("Удалить исходники после сжатия?",
                    new TextBlock { Text = $"После сохранения архива выбранные файлы и папки будут удалены без корзины. Выбрано: {paths.Length:N0}.",
                        TextWrapping = TextWrapping.Wrap, MaxWidth = 480 }, "Сжать и удалить", "Отмена");
                remove.DefaultButton = ContentDialogButton.Close;
                if (await remove.ShowAsync() != ContentDialogResult.Primary) return null;
            }
            compressionPresets[request.Format] = CapturePreset();
            lastArchiveFormat = request.Format;
            return request;
        }
        catch (Exception error) { ShowError(error.Message); return null; }
        finally { configuring = false; UpdateStatus(); }
    }

    private static TextBox WriteField(string label, string placeholder = "") => new() { Header = label, PlaceholderText = placeholder };
    private static string SizeInMiB(ulong bytes) =>
        $"{bytes / (1024 * 1024) + (bytes % (1024 * 1024) == 0 ? 0UL : 1UL):N0} МиБ";
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
