using System.Diagnostics;
using System.Text.Json;
using SystemCore.Core.Cleaner;
using SystemCore.Infrastructure.Cleaner;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Text;

var passed = true;
if (args.Contains("--scan-benchmark")) return await ScanBenchmark.Run(args.Length > 1 ? int.Parse(args[1]) : 1);
if (args.Contains("--executor")) return await ExecutorTests.Run();
if (args.Contains("--scan-performance")) return await ScanPerformanceTests.Run();
if (args.Contains("--scan-progress")) return await ScanProgressTests.Run();
if (args.Contains("--completion")) return await CompletionTests.Run();
if (args.Contains("--memory-engine")) return await MemoryEngineExperiment.Run();
if (args.Length == 2 && args[0] == "--memory-engine-isolation") return await MemoryEngineExperiment.Run(300, args[1]);
void Check(string name, bool result) { Console.WriteLine($"{(result ? "PASS" : "FAIL")}: {name}"); passed &= result; }
if (args.Contains("--real-scan"))
{
    using var process = Process.GetCurrentProcess(); process.Refresh();
    var privateBefore = process.PrivateMemorySize64;
    var timer = Stopwatch.StartNew();
    var scan = await new WindowsCleanerService(new WindowsCleanerTargets(), recycleBin: new WindowsRecycleBinService(new WindowsRecycleBinShell())).ScanAsync(default);
    process.Refresh();
    Console.WriteLine(JsonSerializer.Serialize(new { DurationMs = timer.Elapsed.TotalMilliseconds, scan.Complete,
        PrivateBytesBefore = privateBefore, PrivateBytesAfter = process.PrivateMemorySize64, PeakWorkingSetBytes = process.PeakWorkingSet64,
        Categories = scan.Categories.Select(c => new { Category = c.Category.ToString(), Status = c.Status.ToString(), c.Visited,
            Eligible = c.ItemCount, c.Bytes, Skipped = c.Skipped.ToDictionary(k => k.Key.ToString(), v => v.Value) }) }, new JsonSerializerOptions { WriteIndented = true }));
    return scan.Categories.Single(c => c.Category == CleanerCategory.UserTemp).Status == CleanerScanStatus.Complete ? 0 : 2;
}
// Only create fixture files. Phase 4A deliberately leaves them in place: no deletion code.
var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "SystemCore-Cleaner-Test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var old = Path.Combine(root, "old.tmp");
var recent = Path.Combine(root, "recent.tmp");
var locked = Path.Combine(root, "locked.tmp");
File.WriteAllText(old, "old-data"); File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-2));
File.WriteAllText(recent, "recent-data");
File.WriteAllText(locked, "locked-data"); File.SetLastWriteTimeUtc(locked, DateTime.UtcNow.AddDays(-2));
var nested = Path.Combine(root, "nested"); Directory.CreateDirectory(nested);
for (var i = 0; i < 1000; i++) { var file = Path.Combine(nested, $"fixture-{i}.tmp"); File.WriteAllText(file, "x"); File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-2)); }
using (var held = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
{
    var scan = await new WindowsCleanerScanner(new FixtureTargets(root)).ScanAsync(default);
    var temp = scan.Categories.Single(c => c.Category == CleanerCategory.UserTemp);
    Check("eligible old and nested files", temp.Items.Count == 1001);
    Check("recent skipped", temp.Items.All(i => i.RelativePath != "recent.tmp"));
    Check("locked skipped", temp.Items.All(i => i.RelativePath != "locked.tmp"));
    Check("real 64-bit byte total", temp.Bytes == 1008);
    Check("root not an item", temp.Items.All(i => CleanerSafety.IsChild(root, i.DisplayPath)));
    Check("unique item identities", temp.Items.Select(i => i.Id).Distinct().Count() == temp.Items.Count);
    Check("native identities recorded", temp.Items.All(i => i.Identity.FileId != 0 && i.Identity.Links == 1));
}
using (var heldRecent = new FileStream(recent, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
{
    var early = await new WindowsCleanerScanner(new FixtureTargets(root)).ScanAsync(default);
    Check("recent metadata rejects before exclusive native candidate open", early.Categories.Single(c => c.Category == CleanerCategory.UserTemp).Skipped.GetValueOrDefault(CleanerSkip.Recent) == 1);
}
Check("no fixture deletion", Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length == 1003 && File.ReadAllText(old) == "old-data");
using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
var cancelScan = await new WindowsCleanerScanner(new FixtureTargets(root)).ScanAsync(cancelled.Token);
Check("cancel scan safely", cancelScan.Categories.Single(c => c.Category == CleanerCategory.UserTemp).Status == CleanerScanStatus.Cancelled);
var forged = await new WindowsCleanerScanner(new FixtureTargets(Environment.GetFolderPath(Environment.SpecialFolder.System))).ScanAsync(default);
Check("forged protected target rejected", forged.Categories.All(c => c.Items.Count == 0));
var fail = await new WindowsCleanerScanner(new BrokenTargets()).ScanAsync(default);
Check("target provider failure contained", fail.Categories.All(c => c.Status == CleanerScanStatus.Unavailable));
var empty = Path.Combine(root, "empty"); Directory.CreateDirectory(empty);
var emptyScan = await new WindowsCleanerScanner(new FixtureTargets(empty)).ScanAsync(default);
Check("empty target complete", emptyScan.Categories.Single(c => c.Category == CleanerCategory.UserTemp) is { Status: CleanerScanStatus.Complete, Items.Count: 0 });
var outside = root + "-outside";
Directory.CreateDirectory(outside);
var sentinel = Path.Combine(outside, "outside-sentinel.tmp");
File.WriteAllText(sentinel, "outside-data"); File.SetLastWriteTimeUtc(sentinel, DateTime.UtcNow.AddDays(-2));
var junction = Path.Combine(root, "escape-junction");
JunctionFixture.Create(junction, outside);
var linked = await new WindowsCleanerScanner(new FixtureTargets(root)).ScanAsync(default);
var linkedCategory = linked.Categories.Single(c => c.Category == CleanerCategory.UserTemp);
Check("real NTFS junction is skipped", linkedCategory.Skipped.GetValueOrDefault(CleanerSkip.Reparse) > 0 && linkedCategory.Items.All(i => !i.RelativePath.Contains("escape-junction")));
Check("outside junction target unchanged", File.ReadAllText(sentinel) == "outside-data");
var linkedRoot = await new WindowsCleanerScanner(new FixtureTargets(junction)).ScanAsync(default);
Check("reparse root rejected", linkedRoot.Categories.Single(c => c.Category == CleanerCategory.UserTemp) is { Items.Count: 0, Status: CleanerScanStatus.Incomplete });
var stress = Path.Combine(root, "stress"); Directory.CreateDirectory(stress);
for (var i = 0; i <= 2000; i++) File.WriteAllText(Path.Combine(stress, $"limit-{i}.tmp"), "");
var limited = await new WindowsCleanerScanner(new FixtureTargets(stress)) { CategoryLimit = 2000 }.ScanAsync(default);
Check("real entry limit marks incomplete", limited.Categories.Single(c => c.Category == CleanerCategory.UserTemp) is { Status: CleanerScanStatus.Incomplete, Visited: 2000 });
Check("incomplete scan not accepted as complete", !limited.Complete);
if (await TargetTests.Run() != 0) passed = false;
Console.WriteLine("Fixture retained (no deletion): " + Path.GetFileName(root));
return passed ? 0 : 1;

sealed class FixtureTargets(string root) : ICleanerTargetProvider
{
    public IReadOnlyList<CleanerTarget> Resolve() => [new(CleanerCategory.UserTemp, root, "*", true, TimeSpan.FromHours(24), true, false, false)];
}
sealed class BrokenTargets : ICleanerTargetProvider
{
    public IReadOnlyList<CleanerTarget> Resolve() => throw new IOException("Test provider failure");
}
static class JunctionFixture
{
    public static void Create(string link, string target)
    {
        Directory.CreateDirectory(link);
        var substitute = Encoding.Unicode.GetBytes(@"\??\" + target);
        var print = Encoding.Unicode.GetBytes(target);
        var buffer = new byte[16 + substitute.Length + 2 + print.Length + 2];
        using (var stream = new MemoryStream(buffer))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(0xA0000003u); writer.Write((ushort)(buffer.Length - 8)); writer.Write((ushort)0);
            writer.Write((ushort)0); writer.Write((ushort)substitute.Length);
            writer.Write((ushort)(substitute.Length + 2)); writer.Write((ushort)print.Length);
            writer.Write(substitute); writer.Write((ushort)0); writer.Write(print); writer.Write((ushort)0);
        }
        using var handle = CreateFile(link, 0x40000000, 0, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        if (handle.IsInvalid || !DeviceIoControl(handle, 0x900A4, buffer, buffer.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint mode, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, int size, IntPtr output, int outputSize, out int returned, IntPtr overlapped);
}
