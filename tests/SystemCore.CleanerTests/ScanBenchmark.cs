using System.Diagnostics;
using System.Text.Json;
using SystemCore.Core.Cleaner;
using SystemCore.Infrastructure.Cleaner;

internal static class ScanBenchmark
{
    public static async Task<int> Run(int workers = 1)
    {
        var targets = new WindowsCleanerTargets().Resolve();
        var measurements = new List<object>();
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var overallMemory = process.PrivateMemorySize64;
        var overallCpu = process.TotalProcessorTime;
        var overallWatch = Stopwatch.StartNew();
        var overallScan = await new WindowsCleanerScanner(new WindowsCleanerTargets()) { WorkerCount = workers }.ScanAsync(default);
        var shellWatch = Stopwatch.StartNew();
        var shell = await new WindowsRecycleBinService(new WindowsRecycleBinShell()).QueryAsync(default);
        shellWatch.Stop();
        overallWatch.Stop(); process.Refresh();
        var overall = new { DurationMs = overallWatch.Elapsed.TotalMilliseconds,
            Visited = overallScan.Categories.Sum(c => c.Visited), Eligible = overallScan.Categories.Sum(c => c.Items.Count),
            Skipped = overallScan.Categories.Sum(c => c.Skipped.Values.Sum()),
            ItemsPerSecond = overallScan.Categories.Sum(c => c.Visited) / overallWatch.Elapsed.TotalSeconds,
            CpuPercent = (process.TotalProcessorTime - overallCpu).TotalMilliseconds / overallWatch.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100,
            PrivateBytesDelta = process.PrivateMemorySize64 - overallMemory,
            ShellDurationMs = shellWatch.Elapsed.TotalMilliseconds, Shell = shell };
        foreach (var target in targets)
        {
            process.Refresh();
            var memory = process.PrivateMemorySize64;
            var cpu = process.TotalProcessorTime;
            var watch = Stopwatch.StartNew();
            var scan = await new WindowsCleanerScanner(new SingleTarget(target)).ScanAsync(default);
            watch.Stop(); process.Refresh();
            var result = scan.Categories.Single(c => c.Category == target.Category);
            measurements.Add(new { Category = target.Category.ToString(), DurationMs = watch.Elapsed.TotalMilliseconds,
                result.Visited, Eligible = result.Items.Count, Skipped = result.Skipped.Values.Sum(), result.Bytes,
                Status = result.Status.ToString(), ItemsPerSecond = result.Visited / Math.Max(.001, watch.Elapsed.TotalSeconds),
                CpuPercent = (process.TotalProcessorTime - cpu).TotalMilliseconds / Math.Max(1, watch.Elapsed.TotalMilliseconds) / Environment.ProcessorCount * 100,
                PrivateBytesDelta = process.PrivateMemorySize64 - memory });
        }
        Console.WriteLine(JsonSerializer.Serialize(new { Workers = workers, Overall = overall, Categories = measurements }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
    private sealed class SingleTarget(CleanerTarget target) : ICleanerTargetProvider
    {
        public IReadOnlyList<CleanerTarget> Resolve() => [target];
    }
}
