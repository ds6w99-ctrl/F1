using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using SystemCore.Core.MemoryTools;
using SystemCore.Core.Performance;
using SystemCore.Core.SystemInformation;
using SystemCore.Infrastructure.Dashboard;
using SystemCore.Infrastructure.MemoryTools;
using SystemCore.Infrastructure.Performance;

if (args.Length == 2 && args[0] == "--lifecycle") return await OptimizationTests.Lifecycle(args[1]);
var checks = new List<(string Name, bool Passed)>();
void Check(string name, bool pass) { checks.Add((name, pass)); Console.WriteLine($"{(pass ? "PASS" : "FAIL")}: {name}"); }
const ulong GB = 1024UL * 1024 * 1024, MB = 1024UL * 1024;
var now = DateTimeOffset.UtcNow;
MemorySnapshot Snapshot(ulong? available = 4 * GB, ulong? commit = 16 * GB, ulong? total = 16 * GB, ulong? limit = 32 * GB, DateTimeOffset? timestamp = null) => new(timestamp ?? now, total, available, commit, limit, GB, 100 * MB, 80 * MB);
HistoryPoint[] History(int seconds, ulong available, double commit, DateTimeOffset? end = null) => Enumerable.Range(0, seconds + 1)
    .Select(i => new HistoryPoint((end ?? now).AddSeconds(i - seconds), null, MemoryMetrics.Percent(16 * GB - available, 16 * GB), null, null, available, 16 * GB, commit)).ToArray();
Check("Physical used is total minus available", Snapshot().UsedPhysical == 12 * GB);
Check("Physical usage percent", Snapshot().UsagePercent == 75);
Check("Commit distinct from physical usage", Snapshot().CommitPercent == 50);
Check("Zero physical total unavailable", Snapshot(total: 0).UsagePercent is null);
Check("Invalid available cannot underflow used", Snapshot(available: 17 * GB).UsedPhysical is null);
Check("Missing available remains unknown", Snapshot(available: null).UsedPhysical is null);
Check("Zero commit limit unavailable", Snapshot(limit: 0).CommitPercent is null);
Check("Commit over limit rejected", Snapshot(commit: 33 * GB).CommitPercent is null);
Check("Missing commit unavailable", Snapshot(commit: null).CommitPercent is null);
Check("Large values use non-overflowing percent", MemoryMetrics.Percent(ulong.MaxValue / 2, ulong.MaxValue) is > 49.99 and < 50.01);
Check("Checked page multiplication", MemoryMetrics.Pages(1048576, 4096) == 4 * GB);
Check("Page multiplication overflow unavailable", MemoryMetrics.Pages(ulong.MaxValue, 4096) is null);
Check("Zero page size unavailable", MemoryMetrics.Pages(1, 0) is null);
foreach (var culture in new[] { CultureInfo.GetCultureInfo("en"), CultureInfo.GetCultureInfo("ar") })
foreach (var pair in new[] { (1024UL, "KB"), (MB, "MB"), (GB, "GB"), (1024 * GB, "TB") })
    Check("Shared 1024-based label " + pair.Item2 + "/" + culture.Name, InfoFormatting.Size(pair.Item1, culture).EndsWith(pair.Item2, StringComparison.Ordinal));
Check("Page file is explicitly unavailable", Snapshot().PageFileConfigured is null && Snapshot().PageFileUsed is null);
var evaluator = new MemoryPressureEvaluator();
Check("Normal multi-metric state", evaluator.Evaluate(Snapshot(), History(20, 4 * GB, 50), now).State == MemoryPressure.Normal);
Check("High usage alone is not pressure", new MemoryPressureEvaluator().Evaluate(Snapshot(256 * MB, 12 * GB), History(30, 256 * MB, 37.5), now).State == MemoryPressure.Normal);
Check("High commit alone is not pressure", new MemoryPressureEvaluator().Evaluate(Snapshot(4 * GB, 31 * GB), History(30, 4 * GB, 96.875), now).State == MemoryPressure.Normal);
Check("Short elevated spike does not trigger", new MemoryPressureEvaluator().Evaluate(Snapshot(GB, 29 * GB), History(4, GB, 90.625), now).State == MemoryPressure.Normal);
Check("Sustained elevated enters at five seconds", new MemoryPressureEvaluator().Evaluate(Snapshot(GB, 29 * GB), History(5, GB, 90.625), now).State == MemoryPressure.Elevated);
Check("Short high spike is not sustained high", new MemoryPressureEvaluator().Evaluate(Snapshot(512 * MB, 31 * GB), History(14, 512 * MB, 96.875), now).State != MemoryPressure.High);
Check("High enters at fifteen seconds", new MemoryPressureEvaluator().Evaluate(Snapshot(512 * MB, 31 * GB), History(15, 512 * MB, 96.875), now).State == MemoryPressure.High);
Check("Commit boundary below 85 does not elevate", new MemoryPressureEvaluator().Evaluate(Snapshot(GB, 27 * GB), History(20, GB, 84.99), now).State == MemoryPressure.Normal);
Check("Commit boundary 85 elevates", new MemoryPressureEvaluator().Evaluate(Snapshot(GB, 28 * GB), History(20, GB, 85), now).State == MemoryPressure.Elevated);
Check("Commit boundary 95 reaches high", new MemoryPressureEvaluator().Evaluate(Snapshot(512 * MB, 31 * GB), History(20, 512 * MB, 95), now).State == MemoryPressure.High);
Check("Invalid inputs unknown", evaluator.Evaluate(Snapshot(commit: null), History(20, GB, 90), now).State == MemoryPressure.Unknown);
Check("Missing snapshot unknown", evaluator.Evaluate(null, [], now).State == MemoryPressure.Unknown);
Check("Stale measurements unknown", evaluator.Evaluate(Snapshot(timestamp: now.AddSeconds(-6)), [], now).Reason == MemoryAssessmentReason.Stale);
Check("Future timestamps rejected", evaluator.Evaluate(Snapshot(timestamp: now.AddSeconds(2)), [], now).Reason == MemoryAssessmentReason.Stale);
Check("Insufficient observation explicit", evaluator.Evaluate(Snapshot(), History(2, 4 * GB, 50), now).State == MemoryPressure.Observing);
var gapped = History(20, 512 * MB, 97).Where(p => p.Timestamp <= now.AddSeconds(-10) || p.Timestamp >= now.AddSeconds(-2)).ToArray();
Check("History gap resets sustained observation", new MemoryPressureEvaluator().Evaluate(Snapshot(512 * MB, 31 * GB), gapped, now).State == MemoryPressure.Observing);
Check("Older history cannot classify fresh measurements", new MemoryPressureEvaluator().Evaluate(Snapshot(), History(20, 512 * MB, 97, now.AddSeconds(-1)), now).State == MemoryPressure.Observing);
var recovery = new MemoryPressureEvaluator(); var timeline = History(20, 512 * MB, 97).ToList();
recovery.Evaluate(Snapshot(512 * MB, 31 * GB), timeline, now);
for (var second = 1; second <= 11; second++)
{
    var time = now.AddSeconds(second); timeline.Add(new(time, null, 75, null, null, 4 * GB, 16 * GB, 50));
    var result = recovery.Evaluate(Snapshot(timestamp: time), timeline, time);
    if (second == 5) Check("Recovery hysteresis resists flapping", result.State == MemoryPressure.High);
    if (second == 11) Check("Ten-second recovery returns Normal", result.State == MemoryPressure.Normal);
}
var bounded = new BoundedHistory(); foreach (var point in History(150, GB, 90)) bounded.Add(point);
Check("Memory shares bounded sixty-point history", bounded.Snapshot().Count == 60);
Check("Available and commit retained as scalars", bounded.Snapshot().All(p => p.AvailableBytes == GB && p.CommitPercent == 90));
if (args.Contains("--integration"))
{
    var source = new CountingSource(); await using var monitor = new PerformanceMonitorService(source);
    var basic = new object(); var memory = new object(); var analysis = new MemoryAnalysisService(monitor, new MemoryPressureEvaluator());
    monitor.SetActive(basic, true); monitor.SetActive(memory, true, true);
    await Task.Delay(6500);
    var snapshot = monitor.Latest; var report = analysis.Analyze();
    Check("Real physical memory positive", snapshot?.Memory?.TotalPhysical > 0);
    Check("Real available memory valid", snapshot?.Memory?.UsedPhysical.HasValue == true);
    Check("Physical definition matches shared Dashboard", snapshot?.Memory?.UsedPhysical == snapshot?.Basic.Memory.Value?.UsedBytes);
    Check("History and memory sample share measurement timestamp", monitor.GetHistory().LastOrDefault()?.Timestamp == snapshot?.Memory?.Timestamp);
    Check("Real commit valid", snapshot?.Memory?.CommitPercent is >= 0 and <= 100);
    Check("Real system cache available", snapshot?.Memory?.SystemCache is not null);
    Check("Real self memory measurable", snapshot?.Memory?.SelfPrivateBytes > 0 && snapshot?.Memory.SelfWorkingSet > 0);
    Check("Real process list and CPU deltas", report.Consumers.TopMemory.Count > 0 && report.Consumers.TopCpu.Count > 0);
    Check("Top consumers bounded", report.Consumers.TopMemory.Count <= 10 && report.Consumers.TopCpu.Count <= 10);
    Check("Existing sampler supplies private bytes", snapshot?.Processes.Any(p => p.PrivateBytes > 0) == true);
    Check("Two consumers share exactly one worker", monitor.GetLifecycleDiagnostics() is { Workers: 1, Consumers: 2 } && source.Samples is >= 4 and <= 8 && !source.Overlapped);
    using var self = Process.GetCurrentProcess(); var detailProvider = new WindowsProcessResourceAnalyzer();
    var details = await detailProvider.ReadDetailsAsync(self.Id, self.StartTime.ToUniversalTime().Ticks, default);
    Check("On-demand own process details readable", details?.Pid == self.Id && details.ExecutablePath is not null);
    Check("PID creation-time mismatch rejected", await detailProvider.ReadDetailsAsync(self.Id, 1, default) is null);
    Check("Invalid process identity rejected", await detailProvider.ReadDetailsAsync(-1, 1, default) is null);
    using var cancel = new CancellationTokenSource(); cancel.Cancel(); var cancelled = false;
    try { await detailProvider.ReadDetailsAsync(self.Id, 1, cancel.Token); } catch (OperationCanceledException) { cancelled = true; }
    Check("Process details cancellation", cancelled);
    monitor.SetActive(memory, false); await Task.Delay(1500);
    Check("Hidden detailed page drops process snapshots", monitor.Latest is { Processes.Count: 0, Memory: null });
    monitor.SetActive(basic, false); await Task.Delay(1200); var before = source.Samples; await Task.Delay(1500);
    Check("No polling with all consumers hidden", source.Samples == before);
    monitor.SetActive(memory, true, true); await Task.Delay(1500);
    Check("Resume reuses worker with fresh history", monitor.GetLifecycleDiagnostics().Workers == 1 && monitor.GetHistory().Count <= 2);
    await monitor.StopAsync(); Check("Monitor stops cleanly", monitor.GetLifecycleDiagnostics().Workers == 0);
    Console.WriteLine(JsonSerializer.Serialize(new { Scope = "Real device; one requested sample, no paths", report.Snapshot, report.Pressure,
        TopMemory = report.Consumers.TopMemory.Select(p => new { p.Name, p.Pid, p.WorkingSet, p.PrivateBytes }),
        TopCpu = report.Consumers.TopCpu.Select(p => new { p.Name, p.Pid, p.CpuPercent }), source.Samples }, new JsonSerializerOptions { WriteIndented = true }));
}
await OptimizationTests.Run(Check);
Console.WriteLine(JsonSerializer.Serialize(new { Checks = checks.Count, Failed = checks.Count(c => !c.Passed) }));
return checks.All(c => c.Passed) ? 0 : 1;

internal sealed class CountingSource : IPerformanceSnapshotSource
{
    private readonly WindowsPerformanceSource inner = new(new WindowsPerformanceSampler());
    private int active;
    public int Samples { get; private set; }
    public bool Overlapped { get; private set; }
    public PerformanceSnapshot Sample(bool detailed, CancellationToken token)
    { if (Interlocked.Increment(ref active) != 1) Overlapped = true; try { Samples++; return inner.Sample(detailed, token); } finally { Interlocked.Decrement(ref active); } }
    public void Reset() => inner.Reset();
    public void Dispose() => inner.Dispose();
}
