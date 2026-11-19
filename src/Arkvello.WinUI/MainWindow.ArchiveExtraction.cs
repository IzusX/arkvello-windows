using Arkvello.Models;
using Arkvello.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices;
using Windows.Storage.Pickers;

namespace Arkvello;

public sealed partial class MainWindow
{
    private bool CanExtractOrTest => SelectionReady &&
        (archive != null && entries.Count > 0 ||
         FilesystemReady && (list.SelectedItems.Count > 0
             ? list.SelectedItems.Cast<FileEntry>().All(file => !file.IsDirectory)
             : CurrentItem() is { IsDirectory: false }));

    private static uint[] ArchiveIndices(IEnumerable<FileEntry> items) => items
        .SelectMany(e => e.DescendantIndices ?? (e.ArchiveIndex is { } index ? [index] : Array.Empty<uint>()))
        .Distinct().Order().ToArray();

    private sealed record ExtractionRequest(string Destination, ArchiveExtractSettings Settings, string? Password,
        bool SeparateFolders, bool RemoveDuplicateFolder);

    private async Task Extract(bool test, IEnumerable<FileEntry>? explicitItems = null)
    {
        if (!CanExtractOrTest) return;
        var session = archiveSession;
        var items = (explicitItems ?? list.SelectedItems.Cast<FileEntry>()).ToArray();
        if (items.Length == 0 && session != null) items = entries.ToArray();
        uint[] indices = session == null ? [] : ArchiveIndices(items);
        string[] files = session != null ? [] : OperatedFiles().Select(e => e.FullPath).ToArray();
        var request = new ExtractionRequest("", new(), null, false, false);
        if (!test)
        {
            string source = session?.RootPath ?? files[0];
            string folder = Path.GetDirectoryName(source)!;
            string destination = OtherFilesystemDirectory.Length > 0 ? OtherFilesystemDirectory :
                files.Length > 1 ? folder : Path.Combine(folder, ExtractedFolderName(source));
            var configured = await ConfigureExtraction(destination, files.Length > 1, archivePrefix,
                session == null ? files.Length : indices.Length);
            if (configured == null || closing) return;
            request = configured;
        }

        var result = await RunOperation(test ? "Проверка архива" : "Извлечение", job =>
        {
            if (session == null) return ExtractFiles(files, request, test, job);
            if (indices.Length == 0) return new(0, job.Issues);
            var selected = indices.ToHashSet();
            var settings = ResolveExtractSettings(request, request.Destination,
                session.Entries.Where(e => selected.Contains(e.Index)));
            return session.Archive.Extract(indices, request.Destination, job, test, settings);
        }, initialPassword: request.Password);
        if (test || closing) return;
        if (result?.Succeeded == true) RememberPath(extractDestinations, request.Destination);
        if (session == null) await NavigateDirectory(activePanel, directory, remember: false, refresh: true);
        if (OtherFilesystemDirectory.Length > 0)
            await NavigateDirectory(OtherPanel, OtherFilesystemDirectory, remember: false, refresh: true);
    }

    private async Task<ExtractionRequest?> ConfigureExtraction(string destination, bool multiple, string prefix, int count)
    {
        var path = new TextBox { Header = "Папка назначения", Text = destination };
        var browse = new Button { Content = "Обзор…" };
        var errorText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        browse.Click += async (_, _) =>
        {
            try
            {
                var picker = new FolderPicker(); picker.FileTypeFilter.Add("*");
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                var folder = await picker.PickSingleFolderAsync(); if (folder != null) path.Text = folder.Path;
            }
            catch (Exception error) { errorText.Text = error.Message; }
        };
        var modes = new[] { "Полные пути внутри архива", "Относительно открытой папки", "Без папок", "Абсолютные пути из архива" };
        ArchiveExtractPath[] pathChoices = prefix.Length > 0
            ? [ArchiveExtractPath.Full, ArchiveExtractPath.Current, ArchiveExtractPath.Flat, ArchiveExtractPath.Absolute]
            : [ArchiveExtractPath.Full, ArchiveExtractPath.Flat, ArchiveExtractPath.Absolute];
        var mode = WriteChoice("Режим путей", pathChoices.Select(p => modes[(int)p]).ToArray());
        var overwrite = WriteChoice("Если файл уже существует", new[] { "Спрашивать", "Заменить", "Пропустить",
            "Переименовать извлекаемый", "Переименовать существующий" });
        var password = new PasswordBox { Header = "Пароль (если известен)", MaxLength = 4095 };
        var permissions = new CheckBox { Content = "Восстановить разрешения NTFS" };
        var streams = new CheckBox { Content = "Извлечь альтернативные потоки NTFS", IsChecked = true };
        var duplicate = new CheckBox { Content = "Убирать повторяющуюся корневую папку" };
        var separate = new CheckBox { Content = "Отдельная папка для каждого архива", IsChecked = multiple,
            Visibility = multiple ? Visibility.Visible : Visibility.Collapsed };
        var extra = new StackPanel { Spacing = 6, Padding = new(0, 8, 0, 0) };
        foreach (var control in new FrameworkElement[] { password, permissions, streams, duplicate }) extra.Children.Add(control);
        var form = new StackPanel { Spacing = 10, MaxWidth = 520 };
        foreach (var control in new FrameworkElement[]
        {
            new TextBlock { Text = $"Выбрано элементов: {count:N0}" }, path, DestinationActions(browse, path, extractDestinations),
            separate, mode, overwrite, new Expander { Header = "Дополнительные параметры", Content = extra }, errorText
        }) form.Children.Add(control);
        var dialog = NewDialog("Извлечение", new ScrollViewer { Content = form, MaxHeight = 540 }, "Извлечь", "Отмена");
        ExtractionRequest? request = null;
        dialog.PrimaryButtonClick += (_, e) =>
        {
            try
            {
                string fullPath = Path.GetFullPath(path.Text.Trim().Trim('"'));
                if (!Path.IsPathFullyQualified(path.Text.Trim().Trim('"')) || File.Exists(fullPath))
                    throw new IOException("Укажите абсолютный путь к папке назначения.");
                var settings = new ArchiveExtractSettings(pathChoices[mode.SelectedIndex], (ArchiveOverwrite)overwrite.SelectedIndex,
                    permissions.IsChecked == true, streams.IsChecked == true, prefix.TrimEnd('/'));
                request = new(fullPath, settings, password.Password.Length > 0 ? password.Password : null,
                    separate.IsChecked == true, duplicate.IsChecked == true);
            }
            catch (Exception error) when (error is IOException or ArgumentException or NotSupportedException)
            { e.Cancel = true; errorText.Text = error.Message; }
        };
        dialog.Opened += (_, _) => { path.Focus(FocusState.Programmatic); path.SelectAll(); };
        configuring = true; UpdateStatus();
        try { return await dialog.ShowAsync() == ContentDialogResult.Primary ? request : null; }
        finally { configuring = false; UpdateStatus(); }
    }

    private static ArchiveResult ExtractFiles(string[] files, ExtractionRequest request, bool test, ArchiveJob job)
    {
        int firstError = 0;
        int completed = 0;
        foreach (string path in files)
        {
            job.ThrowIfCancellationRequested();
            int status;
            try
            {
                var opened = job.OpenWithPasswordRetry(() => NativeArchive.Open(path, job));
                using var target = opened.Value;
                status = opened.Result.HResult;
                if (target != null && status == 0)
                {
                    var members = target.ReadEntries(job);
                    string destination = request.SeparateFolders ? Path.Combine(request.Destination, ExtractedFolderName(path)) : request.Destination;
                    if (members.Count > 0)
                        status = target.Extract(members.Select(e => e.Index), destination, job, test,
                            ResolveExtractSettings(request, destination, members)).HResult;
                }
            }
            catch (COMException error) { status = error.HResult; }
            if (status == unchecked((int)0x80004004)) return new(status, job.Issues);
            if (status == 0) { completed++; continue; }
            if (firstError == 0) firstError = status;
            job.ReportIssue(0, $"{path}: 7-Zip вернул код 0x{status:X8}.");
        }
        return new(completed > 0 ? 0 : firstError, job.Issues);
    }

    private static ArchiveExtractSettings ResolveExtractSettings(ExtractionRequest request, string destination, IEnumerable<ArchiveEntry> entries)
    {
        if (!request.RemoveDuplicateFolder || request.Settings.PathMode != ArchiveExtractPath.Full) return request.Settings;
        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(destination));
        if (name.Length == 0) return request.Settings;
        bool inFirstFolder = entries.All(e =>
        {
            string path = e.Path.Replace('\\', '/').TrimEnd('/');
            return path.StartsWith(name + "/", StringComparison.OrdinalIgnoreCase) ||
                e.IsDirectory && path.Equals(name, StringComparison.OrdinalIgnoreCase);
        });
        return inFirstFolder ? request.Settings with { PathMode = ArchiveExtractPath.Current, RelativePrefix = name } : request.Settings;
    }

    private static string ExtractedFolderName(string source)
    {
        string name = Path.GetFileNameWithoutExtension(source);
        if (Path.GetExtension(source).Equals(".001", StringComparison.OrdinalIgnoreCase) && Path.HasExtension(name))
            name = Path.GetFileNameWithoutExtension(name);
        return name.Length > 0 ? name : "Archive";
    }
}
