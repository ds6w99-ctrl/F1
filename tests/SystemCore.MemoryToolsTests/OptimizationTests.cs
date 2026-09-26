using System.Diagnostics;
using System.Text.Json;
using SystemCore.Core.Dashboard;
using SystemCore.Core.MemoryTools;
using SystemCore.Core.Performance;
using SystemCore.Infrastructure.Dashboard;
using SystemCore.Infrastructure.MemoryTools;

internal static class OptimizationTests
{
    internal static async Task Run(Action<string, bool> check)
    {
        using var monitor = new FixtureMonitor(); var analysis = new FixtureAnalysis(monitor);
        MemoryOptimizationService Engine(params ISystemCoreMemoryCacheOwner[] owners) => new(analysis, monitor, owners);
        using var buffer = new FixtureCache();
        analysis.State = MemoryPressure.Normal;
        var normal = await Engine(buffer).OptimizeAsync(default);
        check("Normal state never releases even an owned stale cache", normal.Result == MemoryOptimizationOutcome.NoAction && normal.Plan.Reason == MemoryOptimizationReason.Normal && buffer.ReleaseCalls == 0);
        check("No-op bytes are zero, not available-RAM delta", normal.KnownReleasedBytes == 0 && normal.ExecutedActions.Count == 0);
        check("No elevation in any plan", !normal.Plan.RequiresElevation);
        analysis.State = MemoryPressure.High;
        var high = await Engine().OptimizeAsync(default);
        check("High pressure with no safe owner is a no-op", high.Plan.Reason == MemoryOptimizationReason.NoSafeAction && high.Result == MemoryOptimizationOutcome.NoAction);
        foreach (var state in new[] { MemoryPressure.Unknown, MemoryPressure.Observing })
        {
            analysis.State = state;
            var unknown = await Engine(buffer).OptimizeAsync(default);
            check($"{state} cannot authorize cache release", unknown.Plan.Actions.Count == 0 && buffer.ReleaseCalls == 0);
        }
        analysis.State = MemoryPressure.High; monitor.Missing = true;
        check("Missing memory measurements refuse action", (await Engine(buffer).OptimizeAsync(default)).Plan.Actions.Count == 0);
        monitor.Missing = false; monitor.Stale = true;
        check("Stale snapshot refuses action despite injected High assessment", (await Engine(buffer).OptimizeAsync(default)).Plan.Actions.Count == 0);
        monitor.Stale = false;
        buffer.Active = true;
        check("Active cache remains intact", (await Engine(buffer).OptimizeAsync(default)).Plan.Actions.Count == 0 && buffer.Intact);
        buffer.Active = false; buffer.Stale = false;
        check("Current cache remains intact", (await Engine(buffer).OptimizeAsync(default)).Plan.Actions.Count == 0 && buffer.Intact);
        buffer.Stale = true; buffer.Owned = false;
        check("Shared ownership cannot release cache", (await Engine(buffer).OptimizeAsync(default)).Plan.Actions.Count == 0 && buffer.Intact);
        buffer.Owned = true; buffer.UnknownSize = true;
        check("Unknown cache size is not guessed", (await Engine(buffer).OptimizeAsync(default)).Plan.Actions.Count == 0);
        buffer.UnknownSize = false;
        var released = await Engine(buffer).OptimizeAsync(default);
        check("Injected 16 MB stale cache is released", released.Result == MemoryOptimizationOutcome.Completed && !buffer.Intact && released.KnownReleasedBytes == 16UL * 1024 * 1024);
        check("Before-after known cache count is owner-reported", released.BeforeSnapshot.KnownCacheBytes == 16UL * 1024 * 1024 && released.AfterSnapshot.KnownCacheBytes == 0);
        check("Before-after shared measurements retained", released.BeforeSnapshot.Memory is not null && released.AfterSnapshot.Memory is not null && released.AfterSnapshot.Memory.Timestamp > released.BeforeSnapshot.Memory.Timestamp);
        check("Temporary measurement subscription is removed", monitor.Subscribers == 0);
        check("Released cache cannot execute twice", (await Engine(buffer).OptimizeAsync(default)).Plan.Actions.Count == 0 && buffer.ReleaseCalls == 1);
        using var race = new FixtureCache { ActivateOnRecheck = true };
        var changed = await Engine(race).OptimizeAsync(default);
        check("Cache becoming active between plan and execute is skipped", changed.SkippedActions.Single().Reason == MemoryActionSkipReason.Changed && race.Intact && race.ReleaseCalls == 0);
        using var atomicRace = new FixtureCache { ActivateOnRelease = true };
        check("Owner atomically refuses last-moment activation", (await Engine(atomicRace).OptimizeAsync(default)).SkippedActions.Single().Reason == MemoryActionSkipReason.Changed && atomicRace.Intact);
        using var safe = new FixtureCache(); using var failed = new FixtureCache { FailRelease = true };
        var partial = await Engine(safe, failed).OptimizeAsync(default);
        check("Partial action failure preserves successful accounting", partial.Result == MemoryOptimizationOutcome.Partial && partial.ExecutedActions.Count == 1 && partial.SkippedActions.Count == 1 && partial.KnownReleasedBytes == 16UL * 1024 * 1024 && failed.Intact);
        using var inspectFailure = new FixtureCache { FailInspect = true };
        check("Inspection failure becomes explicit no-action reason", (await Engine(inspectFailure).OptimizeAsync(default)).Plan.Reason == MemoryOptimizationReason.OwnerUnavailable);
        using var zero = new FixtureCache(0);
        var empty = await Engine(zero).OptimizeAsync(default);
        check("Zero-byte release stays zero", empty.Result == MemoryOptimizationOutcome.Completed && empty.KnownReleasedBytes == 0);
        using var missingAfter = new FixtureCache(); monitor.AutoSample = false;
        var unavailable = await Engine(missingAfter).OptimizeAsync(default);
        check("After-measurement timeout is unavailable, not fabricated", unavailable.AfterSnapshot.Memory is null && unavailable.KnownReleasedBytes == 16UL * 1024 * 1024 && monitor.Subscribers == 0);
        monitor.AutoSample = true;
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var cancelledCache = new FixtureCache(); var cancelled = false;
        try { await Engine(cancelledCache).OptimizeAsync(cancellation.Token); } catch (OperationCanceledException) { cancelled = true; }
        check("Cancellation before execution preserves cache", cancelled && cancelledCache.Intact && cancelledCache.ReleaseCalls == 0);
        using var serialCache = new FixtureCache(); var service = Engine(serialCache);
        var concurrent = await Task.WhenAll(service.OptimizeAsync(default), service.OptimizeAsync(default));
        check("Concurrent clicks serialize and release once", concurrent.Sum(r => (long)r.KnownReleasedBytes) == 16L * 1024 * 1024 && serialCache.ReleaseCalls == 1 && monitor.Subscribers == 0);
        check("Result contract contains no performance improvement claim", !JsonSerializer.Serialize(released).Contains("Improvement", StringComparison.OrdinalIgnoreCase));
        Console.WriteLine("SYNTHETIC OPTIMIZATION EVIDENCE " + JsonSerializer.Serialize(new { Normal = normal, Synthetic = released, Partial = partial, MissingAfter = unavailable }));
    }

    internal static async Task<int> Lifecycle(string path)
    {
        using var monitor = new FixtureMonitor(); var analysis = new FixtureAnalysis(monitor) { State = MemoryPressure.High };
        using var process = Process.GetCurrentProcess(); var clock = Stopwatch.StartNew(); var samples = new List<object>(); var checks = 0;
        void Sample(string phase, int cycle)
        {
            process.Refresh(); var managed = GC.GetTotalMemory(false); var gc = GC.GetGCMemoryInfo();
            samples.Add(new { Phase = phase, Cycle = cycle, Seconds = clock.Elapsed.TotalSeconds, PrivateBytes = process.PrivateMemorySize64,
                WorkingSet = process.WorkingSet64, Handles = process.HandleCount, Threads = process.Threads.Count, ManagedBytes = managed,
                ManagedValid = managed >= 0, LastGcHeap = gc.HeapSizeBytes, LastGcCommitted = gc.TotalCommittedBytes, Subscribers = monitor.Subscribers });
            File.WriteAllText(path, JsonSerializer.Serialize(new { Checks = checks, Samples = samples }, new JsonSerializerOptions { WriteIndented = true }));
        }
        Sample("Baseline", 0);
        for (var cycle = 1; cycle <= 40; cycle++)
        {
            using var cache = new FixtureCache(); using var active = new FixtureCache { Active = true };
            var service = new MemoryOptimizationService(analysis, monitor, [cache, active]);
            analysis.State = MemoryPressure.Normal;
            var noOp = await service.OptimizeAsync(default);
            if (noOp.KnownReleasedBytes != 0 || !cache.Intact || !active.Intact) throw new IOException("Normal-state mutation"); checks++;
            analysis.State = MemoryPressure.High;
            var result = await service.OptimizeAsync(default);
            if (result.KnownReleasedBytes != 16UL * 1024 * 1024 || cache.Intact || !active.Intact) throw new IOException("Synthetic release mismatch"); checks++;
            if (monitor.Subscribers != 0 || result.AfterSnapshot.Memory is null) throw new IOException("Measurement lifecycle failure"); checks++;
            if ((await service.OptimizeAsync(default)).Plan.Actions.Count != 0) throw new IOException("Repeated release"); checks++;
            Sample("Cycle", cycle); await Task.Delay(100);
        }
        for (var second = 0; second < 120; second += 5) { await Task.Delay(5000); Sample("Cooldown", 40); }
        Console.WriteLine($"PASS: 40 cycles / {checks} checks; 16 MB stale + 16 MB active test buffers; no forced GC; 120-second cooldown.");
        return 0;
    }
}

internal sealed class FixtureCache : ISystemCoreMemoryCacheOwner, IDisposable
{
    private readonly object sync = new(); private byte[]? bytes; private readonly Guid version = Guid.NewGuid(); private int inspections;
    internal bool Active, UnknownSize, ActivateOnRecheck, ActivateOnRelease, FailRelease, FailInspect;
    internal bool Stale = true, Owned = true;
    internal int ReleaseCalls;
    internal bool Intact { get { lock (sync) return bytes is not null; } }
    internal FixtureCache(int size = 16 * 1024 * 1024) { bytes = new byte[size]; for (var index = 0; index < bytes.Length; index += 4096) bytes[index] = 0x5a; }
    public OwnedMemoryCache? Inspect()
    {
        lock (sync)
        {
            if (FailInspect) throw new IOException("Test inspection failure");
            if (++inspections == 2 && ActivateOnRecheck) Active = true;
            return bytes is null ? null : new(version, Owned, Stale, Active, UnknownSize ? null : (ulong)bytes.Length);
        }
    }
    public CacheReleaseResult ReleaseIfInactive(Guid expected)
    {
        lock (sync)
        {
            ReleaseCalls++;
            if (FailRelease) throw new IOException("Test release failure");
            if (ActivateOnRelease) Active = true;
            if (bytes is null || expected != version || Active || !Stale || !Owned) return new(CacheReleaseStatus.Changed, 0);
            var size = (ulong)bytes.Length; bytes = null; return new(CacheReleaseStatus.Released, size);
        }
    }
    public void Dispose() { lock (sync) bytes = null; }
}

internal sealed class FixtureAnalysis(FixtureMonitor monitor) : IMemoryAnalysisService
{
    internal MemoryPressure State = MemoryPressure.High;
    public MemoryAnalysis Analyze() { monitor.Refresh(); return new(monitor.Latest?.Memory, new(State, State == MemoryPressure.Normal ? MemoryAssessmentReason.WithinRange : MemoryAssessmentReason.SustainedPressure, 30), new([], [], false, null)); }
}
internal sealed class FixtureMonitor : IPerformanceMonitorService, IDisposable
{
    private EventHandler<PerformanceSnapshot>? updated; private readonly CancellationTokenSource lifetime = new();
    internal bool Missing, Stale, AutoSample = true;
    internal int Subscribers => updated?.GetInvocationList().Length ?? 0;
    public event EventHandler<PerformanceSnapshot>? Updated
    {
        add { updated += value; if (AutoSample) _ = PublishLater(); }
        remove { updated -= value; }
    }
    private async Task PublishLater()
    {
        try { await Task.Delay(50, lifetime.Token); Refresh(); updated?.Invoke(this, Latest!); }
        catch (OperationCanceledException) { }
    }
    public PerformanceSnapshot? Latest { get; private set; }
    internal void Refresh()
    {
            var basic = new WindowsPerformanceSampler().Sample();
            var memory = Missing ? null : new WindowsMemoryMetricsProvider().Read(basic);
            if (Stale && memory is not null) memory = memory with { Timestamp = DateTimeOffset.UtcNow.AddSeconds(-10) };
            Latest = new(DateTimeOffset.UtcNow, basic, new(null, null), new([], false), [], false, null, Environment.ProcessorCount, memory);
    }
    public IReadOnlyList<HistoryPoint> GetHistory() => [];
    public void SetActive(object consumer, bool active, bool detailed = false) { }
    public Task StopAsync() { lifetime.Cancel(); return Task.CompletedTask; }
    public void Dispose() { lifetime.Cancel(); lifetime.Dispose(); }
    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
}
