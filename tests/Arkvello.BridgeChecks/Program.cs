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
try
{
    Make("sample.7z"); Make("encrypted.7z", "-pStage3FixtureOnly", "-mhe=on");
    using var arc = Open("sample.7z");
    var items = arc.ReadEntries();
    Check("typed Unicode item identity", items.Any(e => e.Path == "точный [1].txt" && e.Size > 0));
    var item = items.Single(e => e.Path == "точный [1].txt");
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
