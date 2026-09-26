using System.Diagnostics;
using System.Text.Json;
using SystemCore.Core.Cleaner;
using SystemCore.Infrastructure.Cleaner;

internal static class ScanBenchmark
{
    public static async Task<int> Run()
    {
        var targets = new WindowsCleanerTargets().Resolve();
        var measurements = new List<object>();
        using var process = Process.GetCurrentProcess();
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
        Console.WriteLine(JsonSerializer.Serialize(measurements, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
    private sealed class SingleTarget(CleanerTarget target) : ICleanerTargetProvider
    {
        public IReadOnlyList<CleanerTarget> Resolve() => [target];
    }
}
