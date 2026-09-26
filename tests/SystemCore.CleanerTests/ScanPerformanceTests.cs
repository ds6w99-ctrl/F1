using System.Diagnostics;
using SystemCore.Core.Cleaner;
using SystemCore.Infrastructure.Cleaner;

internal static class ScanPerformanceTests
{
    public static async Task<int> Run()
    {
        var passed = true;
        void Check(string name, bool value) { Console.WriteLine($"{(value ? "PASS" : "FAIL")}: {name}"); passed &= value; }
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "SystemCore-ScanPerf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var nested = Directory.CreateDirectory(Path.Combine(root, "nested")).FullName;
        for (var i = 0; i < 512; i++)
        {
            var path = Path.Combine(i % 2 == 0 ? root : nested, $"{i}.tmp");
            File.WriteAllText(path, "fixture");
            if (i < 256) File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-3));
        }
        var expected = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Where(p => File.GetLastWriteTimeUtc(p) < DateTime.UtcNow.AddDays(-1))
            .Select(p => Path.GetRelativePath(root, p)).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var workers in new[] { 1, 2, 4, 0, 100 })
        {
            var active = 0; var peak = 0; var completed = 0; var gate = new object();
            WindowsCleanerScanner.ForEachCategory(16, workers, _ =>
            {
                lock (gate) { active++; peak = Math.Max(peak, active); }
                Thread.Sleep(10);
                lock (gate) { active--; completed++; }
            });
            Check($"workers={workers}: category scheduler bounded to 1..4", completed == 16 && active == 0 && peak >= 1 && peak <= Math.Clamp(workers, 1, 4));
            var provider = new CountingTargets(root);
            var scanner = new WindowsCleanerScanner(provider) { WorkerCount = workers };
            var scan = await scanner.ScanAsync(default);
            var temp = scan.Categories.Single(c => c.Category == CleanerCategory.UserTemp);
            Check($"workers={workers}: streaming matches array fixture oracle", temp.Items.Select(i => i.RelativePath).SequenceEqual(expected));
            Check($"workers={workers}: visited/bytes/age/status preserved", temp.Visited == 513 && temp.Bytes == 256 * 7 && temp.Status == CleanerScanStatus.Complete && temp.Skipped.GetValueOrDefault(CleanerSkip.Recent) == 256);
            Check($"workers={workers}: target provider resolved once", provider.Calls == 1);
            Check($"workers={workers}: stable category ordering", scan.Categories.Select(c => c.Category).SequenceEqual(Enum.GetValues<CleanerCategory>()));
            var dashboard = new CleanerScanState(); dashboard.Set(scan);
            Check($"workers={workers}: Dashboard reflects measured scanner bytes", dashboard.Latest is { Complete: true, Bytes: 1792 });
            using var cancel = new CancellationTokenSource();
            cancel.CancelAfter(10);
            var watch = Stopwatch.StartNew();
            var cancelled = await scanner.ScanAsync(cancel.Token);
            Check($"workers={workers}: in-flight cancellation responsive", watch.Elapsed < TimeSpan.FromSeconds(2) && cancelled.Categories.Single(c => c.Category == CleanerCategory.UserTemp) is { Status: CleanerScanStatus.Cancelled, Visited: > 0 and < 513 });
            var limited = await new WindowsCleanerScanner(new CountingTargets(root)) { WorkerCount = workers, CategoryLimit = 20 }.ScanAsync(default);
            Check($"workers={workers}: entry limit fail-closed", limited.Categories.Single(c => c.Category == CleanerCategory.UserTemp) is { Visited: 20, Status: CleanerScanStatus.Incomplete });
        }
        var deep = root;
        for (var i = 0; i <= WindowsCleanerScanner.MaximumDepth; i++) deep = Directory.CreateDirectory(Path.Combine(deep, "d")).FullName;
        var service = new WindowsCleanerService(new CountingTargets(root));
        var incomplete = await service.ScanAsync(default);
        var partial = new CleanerScanState(); partial.Set(incomplete);
        Check("Dashboard marks incomplete scanner result partial", partial.Latest is { Complete: false, Bytes: 1792 });
        var refused = await service.CleanAsync(new(incomplete.Id, incomplete.Categories.Single(c => c.Category == CleanerCategory.UserTemp).Items.Select(i => i.Id).ToArray()), default);
        Check("incomplete category cannot authorize discovered file IDs", !refused.Accepted && refused.RemovedBytes == 0);
        Check("fixtures retained; scan never deletes", Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length == 512);
        return passed ? 0 : 1;
    }

    private sealed class CountingTargets(string root) : ICleanerTargetProvider
    {
        public int Calls { get; private set; }
        public IReadOnlyList<CleanerTarget> Resolve()
        {
            Calls++;
            return [new(CleanerCategory.UserTemp, root, "*", true, TimeSpan.FromHours(24), true, false, false)];
        }
    }
}
