using System.Diagnostics;
using SystemCore.Core.Performance;

namespace SystemCore.Core.MemoryTools;

// Manual callers only. Production registers no cache owners until a genuine cache
// exposes a safe atomic release contract. Test fixtures live in test assemblies.
public sealed class MemoryOptimizationService : IMemoryOptimizationService
{
    private readonly IMemoryAnalysisService analysis;
    private readonly IPerformanceMonitorService monitor;
    private readonly ISystemCoreMemoryCacheOwner[] owners;
    private readonly SemaphoreSlim operation = new(1, 1);
    public MemoryOptimizationService(IMemoryAnalysisService analysis, IPerformanceMonitorService monitor,
        IEnumerable<ISystemCoreMemoryCacheOwner> owners)
    {
        this.analysis = analysis; this.monitor = monitor; this.owners = owners.Take(33).ToArray();
        if (this.owners.Length > 32) throw new ArgumentException("At most 32 trusted cache owners are supported", nameof(owners));
    }
    public async Task<MemoryOptimizationResult> OptimizeAsync(CancellationToken token)
    {
        await operation.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            MemoryToolsDiagnostics.Log.OptimizationRequested();
            var watch = Stopwatch.StartNew();
            var current = analysis.Analyze();
            var inspected = Inspect();
            var actions = new List<MemoryOptimizationAction>(); ulong estimate = 0;
            var reliable = Fresh(current.Snapshot) && current.Snapshot!.ValidPressureInputs;
            var reason = !reliable || current.Pressure.State is MemoryPressure.Unknown or MemoryPressure.Observing
                ? MemoryOptimizationReason.MeasurementsUnavailable : current.Pressure.State == MemoryPressure.Normal
                ? MemoryOptimizationReason.Normal : inspected.Failed ? MemoryOptimizationReason.OwnerUnavailable : MemoryOptimizationReason.NoSafeAction;
            if (reliable && current.Pressure.State is MemoryPressure.Elevated or MemoryPressure.High)
            {
                foreach (var (index, cache) in inspected.Caches)
                    if (Safe(cache) && cache.KnownBytes!.Value <= ulong.MaxValue - estimate)
                    { actions.Add(new(index, cache.Version, cache.KnownBytes.Value)); estimate += cache.KnownBytes.Value; }
                if (actions.Count > 0) reason = MemoryOptimizationReason.SafeOwnedCache;
            }
            var plan = new MemoryOptimizationPlan(current.Pressure, actions.AsReadOnly(), estimate, reason);
            MemoryToolsDiagnostics.Log.PlanActionCount(actions.Count);
            var before = new MemoryOptimizationMeasurement(DateTimeOffset.UtcNow, Fresh(current.Snapshot) ? current.Snapshot : null, inspected.Bytes);
            var executed = new List<int>(); var skipped = new List<MemorySkippedAction>(); ulong released = 0;
            foreach (var action in actions)
            {
                if (token.IsCancellationRequested) { skipped.Add(new(action.OwnerIndex, MemoryActionSkipReason.Cancelled)); continue; }
                try
                {
                    var owner = owners[action.OwnerIndex]; var recheck = owner.Inspect();
                    if (recheck is null || !Safe(recheck) || recheck.Version != action.Version || recheck.KnownBytes != action.KnownBytes)
                    { skipped.Add(new(action.OwnerIndex, MemoryActionSkipReason.Changed)); continue; }
                    var result = owner.ReleaseIfInactive(action.Version);
                    if (result.Status == CacheReleaseStatus.Released && result.KnownReleasedBytes is { } bytes && bytes <= action.KnownBytes)
                    { executed.Add(action.OwnerIndex); released += bytes; }
                    else skipped.Add(new(action.OwnerIndex, result.Status == CacheReleaseStatus.Changed ? MemoryActionSkipReason.Changed : MemoryActionSkipReason.Failed));
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                { skipped.Add(new(action.OwnerIndex, MemoryActionSkipReason.Failed)); MemoryToolsDiagnostics.Log.FailureReason("CacheOwnerFailure"); }
            }
            // Observe the next shared sample after real actions. This is an event
            // subscription with a bounded wait, never a second sampling loop.
            MemorySnapshot? afterMemory;
            if (executed.Count > 0)
                afterMemory = await NextMeasurement(DateTimeOffset.UtcNow, token).ConfigureAwait(false);
            else { var latest = monitor.Latest?.Memory; afterMemory = Fresh(latest) ? latest : null; }
            var after = new MemoryOptimizationMeasurement(DateTimeOffset.UtcNow, afterMemory, Inspect().Bytes);
            var outcome = actions.Count == 0 ? MemoryOptimizationOutcome.NoAction : skipped.Count == 0 ? MemoryOptimizationOutcome.Completed
                : executed.Count > 0 ? MemoryOptimizationOutcome.Partial : skipped.All(s => s.Reason == MemoryActionSkipReason.Cancelled)
                ? MemoryOptimizationOutcome.Cancelled : MemoryOptimizationOutcome.Failed;
            MemoryToolsDiagnostics.Log.KnownBytesReleased(released);
            return new(plan, executed.AsReadOnly(), skipped.AsReadOnly(), released, before, after, watch.Elapsed, outcome);
        }
        finally { operation.Release(); }
    }
    private static bool Safe(OwnedMemoryCache cache) => cache.Version != Guid.Empty && cache.ExclusivelyOwned && cache.Stale && !cache.Active && cache.KnownBytes.HasValue;
    private static bool Fresh(MemorySnapshot? value) => value is not null && DateTimeOffset.UtcNow - value.Timestamp <= TimeSpan.FromSeconds(5)
        && value.Timestamp <= DateTimeOffset.UtcNow.AddSeconds(1);
    private (List<(int Index, OwnedMemoryCache Cache)> Caches, ulong? Bytes, bool Failed) Inspect()
    {
        var caches = new List<(int, OwnedMemoryCache)>(); ulong? bytes = 0; var failed = false;
        for (var index = 0; index < owners.Length; index++)
        {
            try
            {
                var cache = owners[index].Inspect(); if (cache is null) continue;
                caches.Add((index, cache));
                bytes = bytes is { } total && cache.KnownBytes is { } size && size <= ulong.MaxValue - total ? total + size : null;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            { failed = true; bytes = null; MemoryToolsDiagnostics.Log.FailureReason("CacheInspectionFailure"); }
        }
        return (caches, bytes, failed);
    }
    private async Task<MemorySnapshot?> NextMeasurement(DateTimeOffset after, CancellationToken token)
    {
        var completion = new TaskCompletionSource<MemorySnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Updated(object? sender, PerformanceSnapshot value)
        { if (value.Memory is { } memory && memory.Timestamp >= after && Fresh(memory)) completion.TrySetResult(memory); }
        monitor.Updated += Updated;
        try
        {
            if (monitor.Latest is { } latest) Updated(null, latest);
            try { return await completion.Task.WaitAsync(TimeSpan.FromSeconds(3), token).ConfigureAwait(false); }
            catch (TimeoutException) { return null; }
            catch (OperationCanceledException) { return null; }
        }
        finally { monitor.Updated -= Updated; }
    }
}
