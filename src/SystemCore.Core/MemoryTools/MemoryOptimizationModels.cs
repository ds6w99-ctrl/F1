namespace SystemCore.Core.MemoryTools;

// An owner must atomically revalidate version, exclusive ownership, inactivity and
// staleness under its own lock before dropping references. No arbitrary caller IDs,
// paths, process handles or system-wide memory operations are accepted by the engine.
public sealed record OwnedMemoryCache(Guid Version, bool ExclusivelyOwned, bool Stale, bool Active, ulong? KnownBytes);
public enum CacheReleaseStatus { Released, Changed, Failed }
public sealed record CacheReleaseResult(CacheReleaseStatus Status, ulong? KnownReleasedBytes);
public interface ISystemCoreMemoryCacheOwner
{
    OwnedMemoryCache? Inspect();
    CacheReleaseResult ReleaseIfInactive(Guid version);
}
public enum MemoryOptimizationReason { Normal, NoSafeAction, MeasurementsUnavailable, SafeOwnedCache, OwnerUnavailable }
public enum MemoryOptimizationOutcome { NoAction, Completed, Partial, Failed, Cancelled }
public enum MemoryActionSkipReason { Changed, Failed, Cancelled }
public sealed record MemoryOptimizationAction(int OwnerIndex, Guid Version, ulong KnownBytes);
public sealed record MemoryOptimizationPlan(MemoryPressureAssessment Assessment, IReadOnlyList<MemoryOptimizationAction> Actions,
    ulong EstimatedKnownReleasableBytes, MemoryOptimizationReason Reason)
{
    public bool RequiresElevation => false;
}
public sealed record MemoryOptimizationMeasurement(DateTimeOffset Timestamp, MemorySnapshot? Memory, ulong? KnownCacheBytes);
public sealed record MemorySkippedAction(int OwnerIndex, MemoryActionSkipReason Reason);
public sealed record MemoryOptimizationResult(MemoryOptimizationPlan Plan, IReadOnlyList<int> ExecutedActions,
    IReadOnlyList<MemorySkippedAction> SkippedActions, ulong KnownReleasedBytes, MemoryOptimizationMeasurement BeforeSnapshot,
    MemoryOptimizationMeasurement AfterSnapshot, TimeSpan Duration, MemoryOptimizationOutcome Result);
public interface IMemoryOptimizationService
{
    Task<MemoryOptimizationResult> OptimizeAsync(CancellationToken token);
}
