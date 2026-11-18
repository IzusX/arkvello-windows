using System.Text.Json;
using Arkvello.Services;

string repo = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
string root = Path.Combine(repo, "build", "file-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var checks = new List<object>();
void Check(string name, bool passed)
{
    checks.Add(new { name, passed }); Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}");
    File.WriteAllText(Path.Combine(root, "checks.json"), JsonSerializer.Serialize(new { root, checks }, new JsonSerializerOptions { WriteIndented = true }));
    if (!passed) throw new Exception(name);
}
void Reject(string name, Action action)
{
    try { action(); } catch (Exception e) when (e is ArgumentException or IOException or NotSupportedException or System.ComponentModel.Win32Exception) { Check(name, true); return; }
    Check(name, false);
}
string Folder(string name) => Directory.CreateDirectory(Path.Combine(root, name)).FullName;
ArchiveResult Transfer(string[] paths, string target, bool move = false, int answer = 2)
{
    using var job = new ArchiveJob(e => e.Kind == ArchiveEventKind.Overwrite ? new(answer) : new());
    return FileSystemOperations.Transfer(paths, target, move, job);
}

foreach (string invalid in new[] { "", ".", "..", "a/b", "a\\b", "a:b", "x*", "bad.", "bad ", "NUL.txt", "COM1", "lpt².txt" })
    Reject("invalid leaf: " + invalid, () => FileSystemOperations.ValidateName(invalid));
string input = Folder("исходник Ω");
string text = FileSystemOperations.Create(input, "точный [1].txt", false);
File.WriteAllText(text, "real UTF-8 data Ω");
string child = FileSystemOperations.Create(input, "папка", true);
File.WriteAllText(Path.Combine(child, "inner.txt"), "inside");
Check("create exact Unicode file and folder", File.Exists(text) && Directory.Exists(child));
Reject("create file never truncates existing data", () => FileSystemOperations.Create(input, "точный [1].txt", false));
Reject("create existing folder reports conflict", () => FileSystemOperations.Create(input, "папка", true));
string renamed = FileSystemOperations.Rename(text, "новое Ω.txt");
Check("rename file preserves bytes", !File.Exists(text) && File.ReadAllText(renamed) == "real UTF-8 data Ω");
string occupied = Path.Combine(input, "occupied.txt"); File.WriteAllText(occupied, "untouched");
Reject("rename collision never overwrites", () => FileSystemOperations.Rename(renamed, "occupied.txt"));
Check("rename conflict keeps both files", File.Exists(renamed) && File.ReadAllText(occupied) == "untouched");
renamed = FileSystemOperations.Rename(renamed, "НОВОЕ Ω.txt");
Check("case-only file rename preserves contents", Path.GetFileName(Directory.GetFiles(input).Single(p => Path.GetFileName(p).Equals("новое Ω.txt", StringComparison.OrdinalIgnoreCase))) == "НОВОЕ Ω.txt" && File.ReadAllText(renamed) == "real UTF-8 data Ω");
child = FileSystemOperations.Rename(child, "папка Ω");
Check("rename directory keeps its children", File.ReadAllText(Path.Combine(child, "inner.txt")) == "inside");
Check("overlapping selection collapses to its parent", FileSystemOperations.NormalizeSelection([child, Path.Combine(child, "inner.txt"), child]).SequenceEqual([child]));
using (var job = new ArchiveJob(_ => new()))
{
    Reject("copy into descendant rejected before mutation", () => FileSystemOperations.Transfer([input], child, false, job));
    Reject("copy onto same file rejected before mutation", () => FileSystemOperations.Transfer([renamed], input, false, job));
    Reject("drive root cannot be a destructive source", () => FileSystemOperations.NormalizeSelection([Path.GetPathRoot(root)!]));
}
DateTime stamp = new(2020, 4, 3, 2, 1, 0, DateTimeKind.Utc);
File.WriteAllText(renamed + ":arkvello-check", "alternate stream");
File.SetLastWriteTimeUtc(renamed, stamp);
string copied = Folder("copy");
Check("upstream copies exact files and recursive directories", Transfer([renamed, child], copied).Succeeded && File.ReadAllText(Path.Combine(copied, Path.GetFileName(renamed))) == File.ReadAllText(renamed) && File.ReadAllText(Path.Combine(copied, Path.GetFileName(child), "inner.txt")) == "inside");
Check("upstream copy preserves modification time and ADS", File.GetLastWriteTimeUtc(Path.Combine(copied, Path.GetFileName(renamed))) == stamp && File.ReadAllText(Path.Combine(copied, Path.GetFileName(renamed)) + ":arkvello-check") == "alternate stream");
foreach (int answer in new[] { 0, 1, 2, 3, 4, 5 })
{
    string target = Folder("conflict-" + answer);
    string destination = Path.Combine(target, Path.GetFileName(renamed)); File.WriteAllText(destination, "old");
    var result = Transfer([renamed], target, answer: answer);
    Check("real copy overwrite answer " + answer, answer switch
    {
        0 or 1 => result.Succeeded && File.ReadAllText(destination) == File.ReadAllText(renamed),
        2 or 3 => result.Succeeded && File.ReadAllText(destination) == "old",
        4 => result.Succeeded && File.ReadAllText(destination) == "old" && Directory.GetFiles(target).Length == 2,
        _ => result.Cancelled && File.ReadAllText(destination) == "old"
    });
}
foreach (int answer in new[] { 1, 3, 4 })
{
    string target = Folder("all-" + answer);
    foreach (string p in new[] { renamed, occupied }) File.WriteAllText(Path.Combine(target, Path.GetFileName(p)), "old");
    int prompts = 0;
    using var job = new ArchiveJob(e => { if (e.Kind == ArchiveEventKind.Overwrite) { prompts++; return new(answer); } return new(); });
    var result = FileSystemOperations.Transfer([renamed, occupied], target, false, job);
    Check("overwrite policy is scoped to job " + answer, result.Succeeded && prompts == 1 && (answer != 4 || Directory.GetFiles(target).Length == 4));
}
string moved = Folder("move");
Check("upstream move retains file bytes and removes source", Transfer([Path.Combine(copied, Path.GetFileName(renamed))], moved, true).Succeeded && !File.Exists(Path.Combine(copied, Path.GetFileName(renamed))) && File.ReadAllText(Path.Combine(moved, Path.GetFileName(renamed))) == File.ReadAllText(renamed));
Check("upstream move retains recursive directory", Transfer([Path.Combine(copied, Path.GetFileName(child))], moved, true).Succeeded && !Directory.Exists(Path.Combine(copied, Path.GetFileName(child))) && File.ReadAllText(Path.Combine(moved, Path.GetFileName(child), "inner.txt")) == "inside");
string skipMove = Folder("move-skip"); File.WriteAllText(Path.Combine(skipMove, Path.GetFileName(renamed)), "old");
Check("skipped move keeps source and destination", Transfer([renamed], skipMove, true).Succeeded && File.Exists(renamed) && File.ReadAllText(Path.Combine(skipMove, Path.GetFileName(renamed))) == "old");
using (var cancelled = new ArchiveJob(_ => new()))
{
    cancelled.Cancel();
    try { FileSystemOperations.Transfer([renamed], copied, false, cancelled); Check("preflight cancel leaves destination untouched", false); }
    catch (OperationCanceledException) { Check("preflight cancel leaves destination untouched", !File.Exists(Path.Combine(copied, Path.GetFileName(renamed)))); }
}
string large = Path.Combine(input, "large.bin");
using (var file = File.Create(large)) { byte[] data = new byte[1024 * 1024]; new Random(71).NextBytes(data); for (int i = 0; i < 96; i++) file.Write(data); }
bool sawProgress = false;
using (var job = new ArchiveJob(e => { if (e.Kind == ArchiveEventKind.Progress && e.Completed > 0) { sawProgress = true; return new(-1); } return new(); }))
{
    var result = FileSystemOperations.Transfer([large], copied, false, job);
    Check("cancel during real upstream copy", result.Cancelled && sawProgress && File.Exists(large));
}
string denied = Path.Combine(input, "locked.txt"); File.WriteAllText(denied, "locked");
using (File.Open(denied, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
    Check("sharing denial cannot report clean copy", !Transfer([denied], copied).Succeeded);
using (var paused = new ManualResetEventSlim())
using (var job = new ArchiveJob(e => { if (e.Kind == ArchiveEventKind.Paused) paused.Set(); return new(); }))
{
    job.Pause(true);
    var copying = Task.Run(() => FileSystemOperations.Transfer([occupied], copied, false, job));
    try
    {
        Check("filesystem preflight acknowledges pause", paused.Wait(TimeSpan.FromSeconds(5)) && !copying.IsCompleted);
        job.Pause(false);
        Check("filesystem copy resumes from pause", copying.GetAwaiter().GetResult().Succeeded && File.ReadAllText(Path.Combine(copied, "occupied.txt")) == "untouched");
    }
    finally { job.Cancel(); try { copying.GetAwaiter().GetResult(); } catch (OperationCanceledException) { } }
}
string longParent = Folder("long-path");
for (int i = 0; i < 12; i++) longParent = Directory.CreateDirectory(Path.Combine(longParent, "подпапка-" + i + "-длинный-путь")).FullName;
string longFile = FileSystemOperations.Create(longParent, "файл Ω.txt", false); File.WriteAllText(longFile, "long path content");
Check("upstream copies a real source longer than MAX_PATH", longFile.Length > 260 && Transfer([longFile], copied).Succeeded && File.ReadAllText(Path.Combine(copied, "файл Ω.txt")) == "long path content");
// Destructive checks are confined to a newly owned fixture directory, verified
// before passing its exact leaf to Windows; no user folders or global cleanup.
string deletion = Folder("delete-fixture"); File.WriteAllText(Path.Combine(deletion, "owned.txt"), "owned");
if (!Path.GetFullPath(deletion).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new Exception("Fixture escape");
using (var job = new ArchiveJob(_ => new()))
    Check("Windows permanent delete removes only owned tree", FileSystemOperations.Delete([deletion], false, 0, job).Succeeded && !Directory.Exists(deletion) && File.Exists(renamed));
using (File.Open(denied, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
using (var job = new ArchiveJob(_ => new()))
    Check("Windows deletion sharing denial reports failure", !FileSystemOperations.Delete([denied], false, 0, job).Succeeded && File.Exists(denied));
string keep = Path.Combine(input, "keep.txt"); File.WriteAllText(keep, "keep");
using (var job = new ArchiveJob(_ => new()))
{
    job.Cancel();
    try { FileSystemOperations.Delete([keep], true, 0, job); Check("delete cancel preserves source", false); }
    catch (OperationCanceledException) { Check("delete cancel preserves source", File.Exists(keep)); }
}
Console.WriteLine(root);
