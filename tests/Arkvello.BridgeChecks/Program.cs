using System.Diagnostics;
using System.Text.Json;
using System.Formats.Tar;
using Arkvello.Services;

string repo = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
string root = Path.Combine(repo, "build", "bridge-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
string input = Path.Combine(root, "input"); Directory.CreateDirectory(input);
File.WriteAllText(Path.Combine(input, "точный [1].txt"), "Содержимое Ω");
File.WriteAllText(Path.Combine(input, "another.txt"), "Another file");
Directory.CreateDirectory(Path.Combine(input, "папка"));
File.WriteAllText(Path.Combine(input, "папка", "inner.txt"), "nested folder");
// Enough actual data for progress/cancel; no sleeps or manufactured metrics.
using (var file = File.Create(Path.Combine(input, "large.bin")))
{
    byte[] bytes = new byte[1024 * 1024]; new Random(17).NextBytes(bytes);
    for (int i = 0; i < 96; ++i) file.Write(bytes);
}
var checks = new List<object>();
void Check(string name, bool ok) { checks.Add(new { name, passed = ok }); Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}"); if (!ok) throw new Exception(name); }
void Make(string name, params string[] options)
{
    var start = new ProcessStartInfo(Path.Combine(repo, "build", "windows-x64", "7z.exe")) { WorkingDirectory = input, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (string arg in new[] { "a", Path.Combine(root, name), ".", "-mx=0" }.Concat(options)) start.ArgumentList.Add(arg);
    using var process = Process.Start(start)!;
    var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
    process.WaitForExit(); Task.WaitAll(output, error);
    if (process.ExitCode != 0) throw new Exception("Fixture creation failed: " + error.Result);
}
NativeArchive Open(string name, string? password = null)
{
    using var job = new ArchiveJob(e => e.Kind == ArchiveEventKind.Password ? new(0, password) : new());
    var opened = NativeArchive.Open(Path.Combine(root, name), job);
    Check("open " + name, opened.Result.Succeeded && opened.Archive != null);
    return opened.Archive!;
}
int Original7Zip(params string[] arguments)
{
    var start = new ProcessStartInfo(Path.Combine(repo, "build", "windows-x64", "7z.exe"))
    {
        UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
    };
    foreach (string argument in arguments) start.ArgumentList.Add(argument);
    using var process = Process.Start(start)!;
    var output = process.StandardOutput.ReadToEndAsync();
    var errors = process.StandardError.ReadToEndAsync();
    process.WaitForExit();
    Task.WaitAll(output, errors);
    if (process.ExitCode != 0) Console.WriteLine(output.Result + errors.Result);
    return process.ExitCode;
}
void CheckArchiveWriting()
{
    var formats = ArchiveWriting.Formats();
    Check("read writable formats from adjacent codec", new[] { "7z", "zip", "tar", "gzip", "xz" }
        .All(name => formats.Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase))));
    string output = Path.Combine(root, "write"); Directory.CreateDirectory(output);
    string member = Path.Combine(input, "точный [1].txt");
    string folder = Path.Combine(input, "папка");
    var properties = new Dictionary<string, string> { ["x"] = "5" };
    var request = new ArchiveWriteRequest(Path.Combine(output, "данные.7z"), "7z", [member, folder], properties, []);
    using (var job = new ArchiveJob(_ => new()))
    {
        var result = ArchiveWriting.Run(request, job);
        Check($"create 7z through service (0x{result.HResult:X8}, {string.Join(" / ", result.Issues.Select(e => e.Path1))})", result.Succeeded);
    }
    string extracted = Path.Combine(output, "extracted");
    Check("original 7-Zip extracts exact Unicode files and folder hierarchy",
        Original7Zip("x", "-y", "-o" + extracted, request.Destination) == 0 &&
        File.ReadAllBytes(Path.Combine(extracted, Path.GetFileName(member))).SequenceEqual(File.ReadAllBytes(member)) &&
        File.ReadAllBytes(Path.Combine(extracted, "папка", "inner.txt")).SequenceEqual(File.ReadAllBytes(Path.Combine(folder, "inner.txt"))));
    using (var job = new ArchiveJob(_ => new()))
    using (var archive = NativeArchive.Open(request.Destination, job).Archive!)
    {
        var entries = archive.ReadEntries();
        uint inner = entries.Single(e => e.Path.EndsWith("inner.txt")).Index;
        uint top = entries.Single(e => e.Path == Path.GetFileName(member)).Index;
        string flat = Path.Combine(output, "flat"); Directory.CreateDirectory(flat);
        File.WriteAllText(Path.Combine(flat, "inner.txt"), "existing");
        Check("flat extraction and rename-existing policy", archive.Extract([inner], flat, job,
            settings: new(ArchiveExtractPath.Flat, ArchiveOverwrite.RenameExisting)).Succeeded &&
            File.ReadAllText(Path.Combine(flat, "inner.txt")) == "nested folder" &&
            Directory.GetFiles(flat, "inner*").Any(p => File.ReadAllText(p) == "existing"));
        string relative = Path.Combine(output, "relative");
        Check("extract relative to open archive folder", archive.Extract([inner], relative, job,
            settings: new(ArchiveExtractPath.Current, RelativePrefix: "папка")).Succeeded &&
            File.ReadAllText(Path.Combine(relative, "inner.txt")) == "nested folder");
        string selective = Path.Combine(output, "selected-deletion.7z");
        Check("delete exact native index through Agent", archive.CanDeleteItems && archive.DeleteItems([top], selective, job).Succeeded);
        using var retained = NativeArchive.Open(selective, job).Archive!;
        Check("deletion preserves folder and remaining member", retained.ReadEntries().Count == 2 &&
            retained.ReadEntries().Any(e => e.Path.EndsWith("inner.txt")) &&
            !retained.ReadEntries().Any(e => e.Path == Path.GetFileName(member)));
        string empty = Path.Combine(output, "empty-deletion.7z");
        Check("delete all entries writes a readable empty archive", archive.DeleteItems(entries.Select(e => e.Index).ToArray(), empty, job).Succeeded);
        using var emptyArchive = NativeArchive.Open(empty, job).Archive!;
        Check("empty archive can be opened", emptyArchive.ReadEntries().Count == 0);
    }
    using (var job = new ArchiveJob(_ => new()))
        Check("update preserves unselected archive members", ArchiveWriting.Run(request with
        { UpdateExisting = true, Paths = [Path.Combine(input, "another.txt")] }, job).Succeeded);
    using (var job = new ArchiveJob(_ => new()))
    using (var archive = NativeArchive.Open(request.Destination, job).Archive!)
        Check("updated archive contains both old and new members", archive.ReadEntries().Any(e => e.Path == "another.txt") &&
            archive.ReadEntries().Any(e => e.Path == Path.GetFileName(member)));
    using (var job = new ArchiveJob(_ => new()))
        Check("create encrypted ZIP", ArchiveWriting.Run(request with
        {
            Destination = Path.Combine(output, "password.zip"), Format = "ZIP",
            Password = "Fixture-Stage5", Properties = new Dictionary<string, string> { ["x"] = "5", ["em"] = "AES256" }
        }, job).Succeeded);
    Check("original 7-Zip tests encrypted ZIP", Original7Zip("t", "-pFixture-Stage5", Path.Combine(output, "password.zip")) == 0);
    using (var job = new ArchiveJob(_ => new()))
        Check("create encrypted headers in 7z", ArchiveWriting.Run(request with
        {
            Destination = Path.Combine(output, "headers.7z"), Password = "Fixture-Stage5",
            Properties = new Dictionary<string, string> { ["x"] = "5", ["he"] = "on" }
        }, job).Succeeded);
    Check("original 7-Zip tests encrypted headers", Original7Zip("t", "-pFixture-Stage5", Path.Combine(output, "headers.7z")) == 0);
    int passwordRequests = 0;
    bool retriedPassword = false;
    using (var job = new ArchiveJob(e =>
    {
        if (e.Kind != ArchiveEventKind.Password) return new();
        passwordRequests++;
        retriedPassword |= (e.Flags & ArchiveJob.RetryPassword) != 0;
        return new(0, passwordRequests == 1 ? "wrong" : "Fixture-Stage5");
    }))
    {
        var opened = job.OpenWithPasswordRetry(() => NativeArchive.Open(Path.Combine(output, "headers.7z"), job));
        using var encrypted = opened.Value;
        Check("retry encrypted headers with a corrected password", encrypted != null && opened.Result.Succeeded && retriedPassword && passwordRequests == 2);
    }
    string sfx = Path.Combine(output, "self-extract.exe");
    using (var job = new ArchiveJob(_ => new()))
        Check("create self-extracting 7z", ArchiveWriting.Run(request with
        { Destination = sfx, SfxModule = Path.Combine(repo, "build", "windows-x64", "7z.sfx") }, job).Succeeded);
    Check("original 7-Zip opens SFX archive", File.ReadAllBytes(sfx).AsSpan(0, 2).SequenceEqual("MZ"u8) &&
        Original7Zip("t", sfx) == 0);
    string split = Path.Combine(output, "parts.7z");
    using (var job = new ArchiveJob(_ => new()))
        Check("create several 7z volumes", ArchiveWriting.Run(request with
        { Destination = split, Properties = new Dictionary<string, string> { ["x"] = "0" }, Volumes = [40, 60] }, job).Succeeded);
    Check("original 7-Zip tests split archive", new FileInfo(split + ".001").Length == 40 &&
        new FileInfo(split + ".002").Length == 60 && Original7Zip("t", split + ".001") == 0);
    string disposable = Path.Combine(input, "remove-after-archive.txt");
    File.WriteAllText(disposable, "preserve this in the archive");
    string published = Path.Combine(output, "source-removal.7z");
    var removal = request with
    {
        Destination = published, Paths = [disposable], Volumes = [40], Flags = ArchiveWriteFlags.DeleteSources
    };
    File.WriteAllText(published + ".002", "existing volume");
    using (var job = new ArchiveJob(_ => new()))
    {
        bool conflict = false;
        try { ArchiveWriting.Run(removal, job); }
        catch (IOException) { conflict = true; }
        Check("volume publication conflict keeps sources and existing volume intact",
            conflict && File.Exists(disposable) && !File.Exists(published + ".001") && File.ReadAllText(published + ".002") == "existing volume");
    }
    File.Delete(published + ".002");
    using (var job = new ArchiveJob(_ => new()))
        Check("source removal runs after publishing all volumes",
            ArchiveWriting.Run(removal, job).Succeeded && !File.Exists(disposable) && Original7Zip("t", published + ".001") == 0);
    byte[] before = File.ReadAllBytes(request.Destination);
    bool cancelledDuringWriting = false, addingLargeFile = false;
    using (var job = new ArchiveJob(e =>
    {
        if (e.Kind == ArchiveEventKind.CurrentPath && e.Path1.EndsWith("large.bin")) addingLargeFile = true;
        if (!addingLargeFile || e.Kind != ArchiveEventKind.Progress || e.Completed == 0) return new();
        cancelledDuringWriting = true;
        return new(-1);
    }))
        Check("cancel update while writing retains original archive", ArchiveWriting.Run(request with
        {
            UpdateExisting = true, Paths = [Path.Combine(input, "large.bin")],
            Properties = new Dictionary<string, string> { ["x"] = "0" }
        }, job).Cancelled && cancelledDuringWriting && File.ReadAllBytes(request.Destination).SequenceEqual(before));
    using (var job = new ArchiveJob(_ => new()))
        Check("missing input reports an issue without publishing", !ArchiveWriting.Run(request with
        {
            Destination = Path.Combine(output, "missing.7z"), Paths = [Path.Combine(input, "missing.txt")]
        }, job).Succeeded && !File.Exists(Path.Combine(output, "missing.7z")));
    Check("completed and failed operations remove owned staging folders", !Directory.EnumerateDirectories(output, ".arkvello-*").Any());
    foreach (var (format, extension, paths, options) in new[]
    {
        ("tar", ".tar", new[] { member, folder }, new Dictionary<string, string> { ["m"] = "POSIX" }),
        ("gzip", ".gz", new[] { member }, new Dictionary<string, string> { ["x"] = "5", ["m"] = "Deflate" }),
        ("xz", ".xz", new[] { member }, new Dictionary<string, string> { ["x"] = "3", ["mt"] = "1", ["d"] = "1m" })
    })
    {
        string name = Path.Combine(output, "format" + extension);
        using var job = new ArchiveJob(_ => new());
        var result = ArchiveWriting.Run(request with { Destination = name, Format = format, Paths = paths, Properties = options }, job);
        Check($"create and test {format} (0x{result.HResult:X8})", result.Succeeded && Original7Zip("t", name) == 0);
    }
}
try
{
    Check("adjacent native payload loads", NativeDependencies.Check(AppContext.BaseDirectory) == null);
    Check("missing native DLL has an actionable message", NativeDependencies.Check(root)?.Contains("Arkvello.Native.dll", StringComparison.Ordinal) == true);
    string badPayload = Path.Combine(root, "bad-payload"); Directory.CreateDirectory(badPayload);
    File.WriteAllText(Path.Combine(badPayload, "Arkvello.Native.dll"), "not a Windows DLL");
    Check("invalid native DLL has an actionable message", NativeDependencies.Check(badPayload)?.Contains("Windows x64", StringComparison.Ordinal) == true);
    CheckArchiveWriting();
    Make("sample.7z"); Make("encrypted.7z", "-pStage3FixtureOnly", "-mhe=on");
    using (var pausedHash = new ManualResetEventSlim())
    using (var hashJob = new ArchiveJob(e => { if (e.Kind == ArchiveEventKind.Paused) pausedHash.Set(); return new(); }))
    {
        hashJob.Pause(true);
        var hashing = Task.Run(() => ArchiveSourceStamp.Capture(Path.Combine(root, "sample.7z"), hashJob));
        try
        {
            Check("managed source hashing cooperatively pauses", pausedHash.Wait(TimeSpan.FromSeconds(5)) && !hashing.IsCompleted);
            hashJob.Cancel();
            try { hashing.GetAwaiter().GetResult(); Check("cancel wakes paused managed hashing", false); }
            catch (OperationCanceledException) { Check("cancel wakes paused managed hashing", true); }
        }
        finally { hashJob.Cancel(); try { hashing.GetAwaiter().GetResult(); } catch (OperationCanceledException) { } }
    }
    using var arc = Open("sample.7z");
    var items = arc.ReadEntries();
    Check("typed Unicode item identity", items.Any(e => e.Path == "точный [1].txt" && e.Size > 0));
    var item = items.Single(e => e.Path == "точный [1].txt");
    Check("native schema exposes format property ids", arc.ReadColumns().Any(c => c.Id == 22));
    using (var propertyJob = new ArchiveJob(_ => new()))
    {
        var property = arc.ReadProperty(22, propertyJob);
        Check("dynamic method column uses native indices", property.Result.Succeeded && property.Values[item.Index].Text.Contains("Copy", StringComparison.OrdinalIgnoreCase));
    }
    using (var propertyJob = new ArchiveJob(_ => new()))
    {
        var sizes = arc.ReadProperty(7, propertyJob);
        Check("native numeric properties keep numeric sort keys", sizes.Result.Succeeded && Equals(sizes.Values[item.Index].SortKey, (decimal)item.Size!.Value));
    }
    using (var propertyJob = new ArchiveJob(_ => new()))
    {
        propertyJob.Cancel();
        var property = arc.ReadProperty(22, propertyJob);
        Check("dynamic property loading cancels without partial presentation", property.Result.Cancelled && property.Values.Count == 0);
    }
    string dest = Path.Combine(root, "output");
    using (var job = new ArchiveJob(_ => new())) Check("extract explicit index", arc.Extract([item.Index], dest, job).Succeeded);
    Check("exact Unicode content and no unselected files", File.ReadAllText(Path.Combine(dest, item.Path)) == "Содержимое Ω" && Directory.GetFiles(dest).Length == 1);
    for (int answer = 0; answer <= 5; ++answer)
    {
        string dir = Path.Combine(root, "conflict-" + answer); Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, item.Path), "existing");
        bool asked = false;
        using var job = new ArchiveJob(e => { if (e.Kind == ArchiveEventKind.Overwrite) { asked = true; Check("conflict sizes " + answer, (e.Flags & 3) == 3 && e.Size1 == 8); return new(answer); } return new(); });
        var result = arc.Extract([item.Index], dir, job);
        Check("overwrite response " + answer, asked && (answer == 5 ? result.Cancelled : result.Succeeded));
        Check("overwrite content " + answer, answer is 0 or 1 ? File.ReadAllText(Path.Combine(dir, item.Path)) == "Содержимое Ω" : File.ReadAllText(Path.Combine(dir, item.Path)) == "existing");
        if (answer == 4) Check("auto rename incoming", Directory.GetFiles(dir).Length == 2);
    }
    using (var job = new ArchiveJob(e => e.Kind == ArchiveEventKind.Password ? new(0, "wrong") : new()))
    {
        var opened = NativeArchive.Open(Path.Combine(root, "encrypted.7z"), job);
        Check("wrong header password is failure", opened.Result.HResult != 0 && opened.Archive == null);
    }
    using (var encrypted = Open("encrypted.7z", "Stage3FixtureOnly"))
    using (var job = new ArchiveJob(e => e.Kind == ArchiveEventKind.Password ? new(0, "Stage3FixtureOnly") : new()))
        Check("password retry then extract", encrypted.Extract([encrypted.ReadEntries().Single(e => e.Path == item.Path).Index], Path.Combine(root, "encrypted-out"), job).Succeeded);
    using (var job = new ArchiveJob(e => e.Kind == ArchiveEventKind.Password ? new(-1) : new()))
        Check("cancel password prompt", NativeArchive.Open(Path.Combine(root, "encrypted.7z"), job).Result.Cancelled);
    ArchiveJob? cancelJob = null; bool advanced = false;
    using (cancelJob = new ArchiveJob(e => { if (e.Kind == ArchiveEventKind.Progress && e.Completed > 0) { advanced = true; cancelJob!.Cancel(); } return new(); }))
        Check("cooperative cancel during actual work", arc.Extract([items.Single(e => e.Path == "large.bin").Index], Path.Combine(root, "cancel"), cancelJob).Cancelled && advanced);
    Check("cancel leaves partial output explicitly", File.Exists(Path.Combine(root, "cancel", "large.bin")) && new FileInfo(Path.Combine(root, "cancel", "large.bin")).Length < 96L * 1024 * 1024);
    ArchiveJob? pauseJob = null; bool requested = false, acknowledged = false;
    using (pauseJob = new ArchiveJob(e =>
    {
        if (e.Kind == ArchiveEventKind.Progress && e.Completed > 0 && !requested) { requested = true; pauseJob!.Pause(true); }
        if (e.Kind == ArchiveEventKind.Paused) { acknowledged = true; pauseJob!.Pause(false); }
        return new();
    })) Check("acknowledged cooperative pause/resume", arc.Extract([items.Single(e => e.Path == "large.bin").Index], Path.Combine(root, "pause"), pauseJob).Succeeded && acknowledged);
    var pausedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    ArchiveJob? blockedJob = null;
    using (blockedJob = new ArchiveJob(e =>
    {
        if (e.Kind == ArchiveEventKind.Progress && e.Completed > 0) blockedJob!.Pause(true);
        if (e.Kind == ArchiveEventKind.Paused) pausedSignal.TrySetResult();
        return new();
    }))
    {
        var worker = Task.Run(() => arc.Extract([items.Single(e => e.Path == "large.bin").Index], Path.Combine(root, "cancel-paused"), blockedJob));
        await pausedSignal.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(100);
        Check("paused native worker waits without completion", !worker.IsCompleted);
        blockedJob.Cancel();
        Check("cancel wakes paused worker and closes operation", (await worker.WaitAsync(TimeSpan.FromSeconds(10))).Cancelled);
    }
    using (var job = new ArchiveJob(_ => new())) Check("invalid item identity rejected", arc.Extract([uint.MaxValue], dest, job).HResult == unchecked((int)0x80070057));
    string exported = Path.Combine(root, "single-item.txt");
    using (var job = new ArchiveJob(_ => new())) Check("export exact native index to owned file", arc.ExportItem(item.Index, exported, job).Succeeded && File.ReadAllText(exported) == "Содержимое Ω");
    using (var job = new ArchiveJob(_ => new())) Check("export never overwrites existing output", !arc.ExportItem(item.Index, exported, job).Succeeded && File.ReadAllText(exported) == "Содержимое Ω");
    File.WriteAllText(exported, "Edited Ω with different length");
    string staged = Path.Combine(root, "staged.7z");
    byte[] originalHash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "sample.7z")));
    using (var job = new ArchiveJob(_ => new())) Check("Agent stages replacement by original index", arc.ReplaceItem(item.Index, exported, staged, job).Succeeded);
    Check("staging leaves original archive untouched", originalHash.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "sample.7z")))));
    using (var updated = Open("staged.7z"))
    {
        var names = updated.ReadEntries();
        Check("Agent replacement retains original item names", names.Select(e => e.Path).SequenceEqual(items.Select(e => e.Path)));
        string output = Path.Combine(root, "updated-item.txt");
        using var job = new ArchiveJob(_ => new());
        Check("Agent replacement data round trip", updated.ExportItem(item.Index, output, job).Succeeded && File.ReadAllText(output) == "Edited Ω with different length");
    }
    using (var job = new ArchiveJob(e => e.Kind == ArchiveEventKind.Password ? new(-1) : new()))
    {
        var opened = NativeArchive.Open(staged, job);
        using var plain = opened.Archive;
        using var exportJob = new ArchiveJob(e => e.Kind == ArchiveEventKind.Password ? new(-1) : new());
        Check("plain replacement remains unencrypted", plain != null && plain.ExportItem(item.Index, Path.Combine(root, "plain-updated.txt"), exportJob).Succeeded);
    }
    ArchiveJob? cancelUpdate = null;
    bool updateAdvanced = false;
    using (cancelUpdate = new ArchiveJob(e => { if (e.Kind == ArchiveEventKind.Progress && e.Completed > 0) { updateAdvanced = true; cancelUpdate!.Cancel(); } return new(); }))
        Check("cancel update during actual archive writing", arc.ReplaceItem(item.Index, Path.Combine(input, "large.bin"), Path.Combine(root, "cancel-active-update.7z"), cancelUpdate).Cancelled && updateAdvanced);
    Check("cancel update leaves original readable", arc.ReadEntries().Count == items.Count && originalHash.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "sample.7z")))));
    using (var job = new ArchiveJob(_ => new())) Check("update never overwrites existing output", !arc.ReplaceItem(item.Index, exported, staged, job).Succeeded);
    using (var job = new ArchiveJob(_ => new()))
    {
        job.Cancel();
        string output = Path.Combine(root, "cancel-update.7z");
        Check("cancel before update leaves source and no staged file", arc.ReplaceItem(item.Index, exported, output, job).Cancelled && !File.Exists(output));
    }
    // A path is not an identity: two entries have exactly the same name.
    using (var tar = new TarWriter(File.Create(Path.Combine(root, "duplicates.tar")), TarEntryFormat.Ustar))
        foreach (string text in new[] { "first duplicate", "second duplicate" })
        {
            using var bytes = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
            tar.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, "same.txt") { DataStream = bytes });
        }
    using (var duplicates = Open("duplicates.tar"))
    {
        Check("duplicate paths retain distinct indices", duplicates.ReadEntries().Count(e => e.Path == "same.txt") == 2);
        using (var job = new ArchiveJob(_ => new())) Check("replace second duplicate through Agent", duplicates.ReplaceItem(1, exported, Path.Combine(root, "duplicates-updated.tar"), job).Succeeded);
    }
    using (var memoryJob = new ArchiveJob(_ => new()))
    {
        byte[] virtualBytes = File.ReadAllBytes(Path.Combine(root, "duplicates.tar"));
        var openedMemory = NativeArchive.OpenMemory(virtualBytes, "virtual.tar", memoryJob);
        using var virtualArchive = openedMemory.Archive;
        Array.Clear(virtualBytes); GC.Collect(); GC.WaitForPendingFinalizers();
        Check("virtual input owns its bytes after managed buffer changes", openedMemory.Result.Succeeded && virtualArchive!.ReadEntries().Count == 2);
        string virtualOutput = Path.Combine(root, "virtual-second.txt");
        Check("virtual input exports exact duplicate native index", virtualArchive!.ExportItem(1, virtualOutput, memoryJob).Succeeded && File.ReadAllText(virtualOutput) == "second duplicate");
        Check("virtual update explicitly requires file-backed route", virtualArchive.ReplaceItem(1, exported, Path.Combine(root, "virtual-update.tar"), memoryJob).HResult == unchecked((int)0x80004001));
    }
    using (var memoryJob = new ArchiveJob(_ => new()))
    {
        memoryJob.Cancel();
        Check("virtual input cancels before copying bytes", NativeArchive.OpenMemory(File.ReadAllBytes(Path.Combine(root, "duplicates.tar")), "virtual.tar", memoryJob).Result.Cancelled);
    }
    using (var duplicates = Open("duplicates-updated.tar"))
    {
        using (var job = new ArchiveJob(_ => new())) Check("first duplicate unaffected", duplicates.ExportItem(0, Path.Combine(root, "dup-first.txt"), job).Succeeded && File.ReadAllText(Path.Combine(root, "dup-first.txt")) == "first duplicate");
        using (var job = new ArchiveJob(_ => new())) Check("second duplicate changed independently", duplicates.ExportItem(1, Path.Combine(root, "dup-second.txt"), job).Succeeded && File.ReadAllText(Path.Combine(root, "dup-second.txt")) == "Edited Ω with different length");
    }
    // Encrypted updates must not silently turn the changed entry into plaintext.
    using (var encrypted = Open("encrypted.7z", "Stage3FixtureOnly"))
    using (var job = new ArchiveJob(e => e.Kind == ArchiveEventKind.Password ? new(0, "Stage3FixtureOnly") : new()))
        Check("stage encrypted replacement", encrypted.ReplaceItem(item.Index, exported, Path.Combine(root, "encrypted-updated.7z"), job).Succeeded);
    using (var job = new ArchiveJob(e => e.Kind == ArchiveEventKind.Password ? new(-1) : new()))
    {
        var opened = NativeArchive.Open(Path.Combine(root, "encrypted-updated.7z"), job);
        using var encrypted = opened.Archive;
        Check("encrypted header remains protected after replacement", encrypted == null && opened.Result.Cancelled);
        bool protectedData = encrypted == null && opened.Result.Cancelled;
        if (encrypted != null)
        {
            using var exportJob = new ArchiveJob(e => e.Kind == ArchiveEventKind.Password ? new(-1) : new());
            protectedData = encrypted.ExportItem(encrypted.ReadEntries().Single(e => e.Path == item.Path).Index, Path.Combine(root, "without-password.txt"), exportJob).Cancelled;
        }
        Check("changed encrypted item still requires password", protectedData);
    }
    byte[] TarBytes(params (string Name, byte[] Data)[] files)
    {
        using var memory = new MemoryStream();
        using (var tar = new TarWriter(memory, TarEntryFormat.Ustar, leaveOpen: true))
            foreach (var file in files)
            {
                using var bytes = new MemoryStream(file.Data);
                tar.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, file.Name) { DataStream = bytes });
            }
        return memory.ToArray();
    }
    byte[] leaf = File.ReadAllBytes(Path.Combine(root, "duplicates.tar"));
    byte[] middle;
    using (var memory = new MemoryStream())
    {
        using (var zip = new System.IO.Compression.ZipArchive(memory, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            using var stream = zip.CreateEntry("папка/leaf.tar").Open(); stream.Write(leaf);
        }
        middle = memory.ToArray();
    }
    string nestedPath = Path.Combine(root, "nested.tar"), scratch = Path.Combine(root, "session-temp");
    byte[] nested = TarBytes(("middle.zip", middle), ("broken.7z", [1, 2, 3]), ("anchor.txt", [4, 5]));
    File.WriteAllBytes(nestedPath, nested);
    using (var job = new ArchiveJob(_ => new()))
    using (var session = ArchiveSession.Open(nestedPath, job).Session!)
    {
        Check("root workspace session opens", session != null && session.Entries.Count == 3);
        using (var cancel = new ArchiveJob(_ => new()))
        {
            cancel.Cancel();
            Check("cancel child open retains parent", session!.OpenChild(0, cancel, temporaryRoot: scratch).Result.Cancelled && session.Archive.ReadEntries().Count == 3);
        }
        using (var broken = new ArchiveJob(_ => new()))
            Check("bad child retains parent", session!.OpenChild(1, broken, temporaryRoot: scratch).Session == null && !session.IsClosed);
        using var middleJob = new ArchiveJob(_ => new());
        using var middleSession = session!.OpenChild(0, middleJob, temporaryRoot: scratch).Session!;
        using var leafJob = new ArchiveJob(_ => new());
        using var leafSession = middleSession.OpenChild(0, leafJob, "папка/", scratch).Session!;
        Check("three levels retain parent and index identity", leafSession.Parent == middleSession && middleSession.Parent == session && leafSession.ParentIndex == 0 && leafSession.ParentFolder == "папка/" && leafSession.Entries.Count == 2);
        using var updateJob = new ArchiveJob(_ => new());
        var stage = leafSession.StageReplacement(1, exported, updateJob, scratch);
        using var stagedRoot = stage.File;
        Check("stage nested edit through Agent chain", stage.Result.Succeeded && stagedRoot != null);
        Check("nested stage never mutates source", File.ReadAllBytes(nestedPath).SequenceEqual(nested));
        using var reopenJob = new ArchiveJob(_ => new());
        using var reopened = ArchiveSession.Open(stagedRoot!.Path, reopenJob).Session!;
        using var reopenMiddleJob = new ArchiveJob(_ => new());
        using var reopenedMiddle = reopened.OpenChild(0, reopenMiddleJob, temporaryRoot: scratch).Session!;
        using var reopenLeafJob = new ArchiveJob(_ => new());
        using var reopenedLeaf = reopenedMiddle.OpenChild(0, reopenLeafJob, temporaryRoot: scratch).Session!;
        using var verifyJob = new ArchiveJob(_ => new());
        string changed = Path.Combine(root, "nested-edited.txt");
        Check("nested edit survives reopening entire root chain", reopenedLeaf.Archive.ExportItem(1, changed, verifyJob).Succeeded && File.ReadAllText(changed) == "Edited Ω with different length");
    }
    Check("closed nested sessions clean only owned temporary files", !Directory.EnumerateFiles(scratch, "*", SearchOption.AllDirectories).Any());
    // Full editor hand-off: original identity, nested staging, atomic replacement,
    // durable backup and conflicts. Every file below is a disposable fixture.
    string editTarget = Path.Combine(root, "edit-target.tar"); File.WriteAllBytes(editTarget, nested);
    using (var rootJob = new ArchiveJob(_ => new()))
    using (var editRoot = ArchiveSession.Open(editTarget, rootJob).Session!)
    using (var middleJob = new ArchiveJob(_ => new()))
    using (var editMiddle = editRoot.OpenChild(0, middleJob, temporaryRoot: scratch).Session!)
    using (var leafJob = new ArchiveJob(_ => new()))
    using (var editLeaf = editMiddle.OpenChild(0, leafJob, temporaryRoot: scratch).Session!)
    using (var editJob = new ArchiveJob(_ => new()))
    {
        Check("browsing does not hash the root archive", editRoot.RootSourceStamp == null && editLeaf.RootSourceStamp == null);
        try
        {
            using var writer = File.OpenWrite(editTarget);
            Check("live archive source denies competing writes", false);
        }
        catch (IOException error) when ((error.HResult & 0xFFFF) == 32) { Check("live archive source denies competing writes", true); }
        var handoff = ArchiveEdit.Begin(editLeaf, 1, editJob, scratch);
        Check("editing captures the retained root bytes", editRoot.RootSourceStamp == ArchiveSourceStamp.Capture(editTarget) &&
            editLeaf.RootSourceStamp == editRoot.RootSourceStamp);
        var edit = handoff.Edit!;
        Check("editor handoff exports exact duplicate index and journals recovery", handoff.Result.Succeeded && File.ReadAllText(edit.WorkingFile.Path) == "second duplicate" && File.Exists(Path.Combine(edit.RecoveryPath, "recovery.json")));
        Check("unmodified editor handoff detects no changes", !edit.HasChangesSinceExport());
        File.WriteAllText(edit.WorkingFile.Path, "editor nested result Ω");
        Check("editor detects content change", edit.HasChangesSinceExport());
        using (var locked = new FileStream(edit.WorkingFile.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            try { edit.Stage(editJob); Check("editor write lease rejects a half-written file", false); }
            catch (IOException) { Check("editor write lease rejects a half-written file", true); }
        }
        var prepared = edit.Stage(editJob);
        using var stagedEdit = prepared.File!;
        Check("editor stages validated nested root", prepared.Result.Succeeded && File.ReadAllBytes(editTarget).SequenceEqual(nested));
        try { edit.Commit(stagedEdit); Check("commit refuses live source session", false); }
        catch (InvalidOperationException) { Check("commit refuses live source session", true); }
        editLeaf.Dispose(); editMiddle.Dispose(); editRoot.Dispose();
        File.WriteAllText(edit.WorkingFile.Path, "editor saved again after staging");
        try { edit.Commit(stagedEdit); Check("commit detects an editor save after staging", false); }
        catch (IOException) { Check("commit detects an editor save after staging", File.ReadAllBytes(editTarget).SequenceEqual(nested) && File.ReadAllText(edit.WorkingFile.Path) == "editor saved again after staging"); }
        File.WriteAllText(edit.WorkingFile.Path, "editor nested result Ω");
        using (var blocked = new FileStream(editTarget, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try { edit.Commit(stagedEdit); Check("failed replacement preserves source and recovery", false); }
            catch (IOException) { Check("failed replacement preserves source and recovery", File.ReadAllBytes(editTarget).SequenceEqual(nested) && File.Exists(edit.WorkingFile.Path)); }
        }
        using (var cancelled = new ArchiveJob(_ => new()))
        {
            cancelled.Cancel();
            try { edit.Commit(stagedEdit, cancelled); Check("cancelled commit leaves original intact", false); }
            catch (OperationCanceledException) { Check("cancelled commit leaves original intact", File.ReadAllBytes(editTarget).SequenceEqual(nested)); }
        }
        // Same length, different contents: timestamp/size alone would miss this.
        byte[] competing = nested.ToArray(); competing[^1] ^= 1; File.WriteAllBytes(editTarget, competing);
        try { edit.Commit(stagedEdit); Check("commit detects competing same-length source", false); }
        catch (IOException) { Check("commit detects competing same-length source", File.ReadAllBytes(editTarget).SequenceEqual(competing) && File.Exists(edit.WorkingFile.Path)); }
        File.WriteAllBytes(editTarget, nested);
        string backup = edit.Commit(stagedEdit);
        Check("atomic commit retains exact original backup and recovery", File.ReadAllBytes(backup).SequenceEqual(nested) && File.Exists(Path.Combine(edit.RecoveryPath, "recovery.json")));
        using var verifyRootJob = new ArchiveJob(_ => new());
        using var verifiedRoot = ArchiveSession.Open(editTarget, verifyRootJob).Session!;
        using var verifyMiddleJob = new ArchiveJob(_ => new());
        using var verifiedMiddle = verifiedRoot.OpenChild(0, verifyMiddleJob, temporaryRoot: scratch).Session!;
        using var verifyLeafJob = new ArchiveJob(_ => new());
        using var verifiedLeaf = verifiedMiddle.OpenChild(0, verifyLeafJob, temporaryRoot: scratch).Session!;
        using var verifyBytesJob = new ArchiveJob(_ => new());
        string verifiedText = Path.Combine(root, "editor-verified.txt");
        Check("committed nested editor content survives full reopen", verifiedLeaf.Archive.ExportItem(1, verifiedText, verifyBytesJob).Succeeded && File.ReadAllText(verifiedText) == "editor nested result Ω");
    }
    // RAR5 is the upstream handler implementing interactive memory requests.
    // A valid stored item declares a 2 GiB dictionary; Copy does not allocate it.
    // This exercises the real format callback without a multi-GiB allocation.
    byte[] Var(ulong value)
    {
        var data = new List<byte>();
        do { byte part = (byte)(value & 127); value >>= 7; data.Add((byte)(part | (value != 0 ? 128 : 0))); } while (value != 0);
        return data.ToArray();
    }
    byte[] RarBlock(byte[] header)
    {
        byte[] content = Var((ulong)header.Length).Concat(header).ToArray();
        uint crc = uint.MaxValue;
        foreach (byte value in content) { crc ^= value; for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0); }
        return BitConverter.GetBytes(~crc).Concat(content).ToArray();
    }
    byte[] rarName = System.Text.Encoding.UTF8.GetBytes("memory.txt");
    byte[] rarData = System.Text.Encoding.UTF8.GetBytes("bounded memory callback fixture");
    byte[] rarHeader = new byte[] { 2, 2 }.Concat(Var((ulong)rarData.Length)).Concat(new byte[] { 0 }).Concat(Var((ulong)rarData.Length)).Concat(new byte[] { 0 }).Concat(Var(14UL << 10)).Concat(new byte[] { 0 }).Concat(Var((ulong)rarName.Length)).Concat(rarName).ToArray();
    File.WriteAllBytes(Path.Combine(root, "memory.rar"), new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1a, 0x07, 0x01, 0 }.Concat(RarBlock([1, 0, 0])).Concat(RarBlock(rarHeader)).Concat(rarData).Concat(RarBlock([5, 0, 0])).ToArray());
    using (var memoryArc = Open("memory.rar"))
    {
        foreach (int response in new[] { 1, 4, -1 })
        {
            bool asked = false;
            using var memoryJob = new ArchiveJob(e => { if (e.Kind == ArchiveEventKind.Memory) { asked = e.Size1 > e.Size2; return new(response); } return new(); });
            memoryJob.SetMemoryLimit(1);
            var outcome = memoryArc.Extract(memoryArc.ReadEntries().Select(e => e.Index), "", memoryJob, test: true);
            Check("real decoder memory request response " + response, asked && (response == 1 ? outcome.Succeeded : response == -1 ? outcome.Cancelled : !outcome.Succeeded && outcome.Issues.Any(e => e.Code == 0x101)));
        }
    }
    using (var job = new ArchiveJob(_ => throw new InvalidOperationException("callback fixture")))
        Check("managed exception contained at ABI", NativeArchive.Open(Path.Combine(root, "encrypted.7z"), job).Result.Cancelled && job.CallbackError != null);
    File.WriteAllText(Path.Combine(root, "not-archive.7z"), "not an archive");
    using (var job = new ArchiveJob(_ => new())) Check("invalid archive has explicit failure", NativeArchive.Open(Path.Combine(root, "not-archive.7z"), job).Result.HResult != 0);
    // Corrupt stored payload, preserving header/index, to exercise item errors.
    byte[] corrupt = File.ReadAllBytes(Path.Combine(root, "sample.7z")); corrupt[100000] ^= 0xff;
    File.WriteAllBytes(Path.Combine(root, "corrupt.7z"), corrupt);
    using (var damaged = Open("corrupt.7z"))
    using (var job = new ArchiveJob(_ => new()))
    {
        var result = damaged.Extract(damaged.ReadEntries().Select(e => e.Index), "", job, test: true);
        Check("item error prevents false success", !result.Succeeded && result.Issues.Count > 0);
    }
    using (var tar = new TarWriter(File.Create(Path.Combine(root, "large-100000.tar")), TarEntryFormat.Ustar))
        for (int i = 0; i < 100_000; i++) tar.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, $"folder/file-{i:D6}.txt"));
    var archiveWatch = Stopwatch.StartNew();
    using (var large = Open("large-100000.tar"))
    {
        var largeItems = large.ReadEntries();
        Check("real C++ archive list returns 100000 typed entries", largeItems.Count == 100_000 && largeItems[^1].Index == 99_999);
        Console.WriteLine($"MEASURE real archive open/list: {archiveWatch.Elapsed.TotalMilliseconds:F1} ms");
        File.WriteAllText(Path.Combine(root, "large-archive.json"), JsonSerializer.Serialize(new { count = largeItems.Count, elapsedMs = archiveWatch.Elapsed.TotalMilliseconds, workingSet = Process.GetCurrentProcess().WorkingSet64 }));
        using var job = new ArchiveJob(_ => new());
        job.Cancel();
        try { large.ReadEntries(job); Check("cancelled listing rejected", false); }
        catch (System.Runtime.InteropServices.COMException error) { Check("cancelled listing rejected", error.HResult == unchecked((int)0x80004004)); }
    }
    File.WriteAllText(Path.Combine(root, "checks.json"), JsonSerializer.Serialize(new { root, checks }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(root);
}
catch
{
    File.WriteAllText(Path.Combine(root, "checks.json"), JsonSerializer.Serialize(new { root, checks }, new JsonSerializerOptions { WriteIndented = true }));
    throw;
}
