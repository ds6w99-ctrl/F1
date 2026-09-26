using System.Diagnostics;
using SystemCore.Core.Cleaner;
using SystemCore.Infrastructure.Cleaner;

internal static class ScanProgressTests
{
    public static async Task<int> Run()
    {
        var passed = true;
        void Check(string name, bool value) { Console.WriteLine($"{(value ? "PASS" : "FAIL")}: {name}"); passed &= value; }
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "SystemCore-Progress-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        for (var i = 0; i < 600; i++)
        {
            var file = Path.Combine(root, $"{i}.tmp"); File.WriteAllText(file, "fixture");
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-3));
        }
        foreach (var workers in new[] { 1, 2, 4 })
        {
            var sink = new Sink();
            ICleanerScanner scanner = new WindowsCleanerScanner(new FixtureTargets(root)) { WorkerCount = workers };
            var watch = Stopwatch.StartNew();
            var result = await scanner.ScanAsync(default, sink); watch.Stop();
            var rows = sink.Rows.ToArray();
            Check($"progress {workers}: all fixture categories reported", rows.Select(r => r.Value.Category).Distinct().Count() == result.Categories.Count);
            Check($"progress {workers}: start and final category status", result.Categories.All(c => rows.Any(r => r.Value.Category == c.Category && r.Value.Status is null) && rows.Any(r => r.Value.Category == c.Category && r.Value.Status == c.Status)));
            var last = rows.LastOrDefault().Value;
            Check($"progress {workers}: final values match scan", last is not null && last.Visited == result.Categories.Sum(c => c.Visited) && last.Eligible == result.Categories.Sum(c => c.Items.Count) && last.Bytes == result.Categories.Sum(c => c.Bytes));
            Check($"progress {workers}: running totals monotonic", rows.Zip(rows.Skip(1)).All(p => p.First.Value.Visited <= p.Second.Value.Visited && p.First.Value.Eligible <= p.Second.Value.Eligible && p.First.Value.Bytes <= p.Second.Value.Bytes));
            Check($"progress {workers}: no per-file event storm", rows.Length > 0 && rows.Length <= 2 * result.Categories.Count + (int)(watch.Elapsed.TotalMilliseconds / 150) + 1 && rows.Length < 100);
            var starts = new HashSet<CleanerCategory>();
            var periodic = rows.Where(r => r.Value.Status is null && !starts.Add(r.Value.Category)).ToArray();
            Check($"progress {workers}: periodic delivery interval >=150ms", periodic.Zip(periodic.Skip(1)).All(p => Stopwatch.GetElapsedTime(p.First.Time, p.Second.Time).TotalMilliseconds >= 149));
            using var cancel = new CancellationTokenSource();
            var cancellationSink = new Sink(p => { if (p.Category == CleanerCategory.UserTemp && p.Visited > 0 && p.Status is null) cancel.Cancel(); });
            watch.Restart();
            var cancelled = await scanner.ScanAsync(cancel.Token, cancellationSink);
            Check($"progress {workers}: cancel during flowing progress", cancel.IsCancellationRequested && watch.Elapsed < TimeSpan.FromSeconds(2) && cancelled.Categories.Single(c => c.Category == CleanerCategory.UserTemp) is { Status: CleanerScanStatus.Cancelled, Visited: > 0 and < 600 });
        }
        var serviceSink = new Sink();
        ICleanerScanner service = new WindowsCleanerService(new FixtureTargets(root), recycleBin: new WindowsRecycleBinService(new FakeBin()));
        var session = await service.ScanAsync(default, serviceSink);
        var final = serviceSink.Rows.LastOrDefault().Value;
        Check("service forwards file and Shell category progress", serviceSink.Rows.Any(r => r.Value.Category == CleanerCategory.UserTemp && r.Value.Eligible > 0) && serviceSink.Rows.Any(r => r.Value.Category == CleanerCategory.RecycleBin && r.Value.Status == CleanerScanStatus.Complete));
        Check("service progress matches aggregate including fake Shell", final is not null && final.Visited == session.Categories.Sum(c => c.Visited) && final.Eligible == session.Categories.Sum(c => c.ItemCount) && final.Bytes == session.Categories.Sum(c => c.Bytes));
        Check("progress fixtures not deleted", Directory.GetFiles(root).Length == 600);
        return passed ? 0 : 1;
    }
    private sealed class Sink(Action<CleanerScanProgress>? action = null) : IProgress<CleanerScanProgress>
    {
        public List<(long Time, CleanerScanProgress Value)> Rows { get; } = [];
        public void Report(CleanerScanProgress value) { lock (Rows) Rows.Add((Stopwatch.GetTimestamp(), value)); action?.Invoke(value); }
    }
    private sealed class FakeBin : IRecycleBinShell
    {
        public RecycleBinSnapshot Query() => new(true, 2, 17);
        public bool Empty() => throw new InvalidOperationException("Read-only test");
    }
}
