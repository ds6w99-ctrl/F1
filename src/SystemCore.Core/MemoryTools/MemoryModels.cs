using SystemCore.Core.Performance;

namespace SystemCore.Core.MemoryTools;

public sealed record MemorySnapshot(DateTimeOffset Timestamp, ulong? TotalPhysical, ulong? AvailablePhysical,
    ulong? CommitUsed, ulong? CommitLimit, ulong? SystemCache, ulong? SelfWorkingSet, ulong? SelfPrivateBytes,
    ulong? PageFileConfigured = null, ulong? PageFileUsed = null)
{
    public ulong? UsedPhysical => TotalPhysical is > 0 && AvailablePhysical <= TotalPhysical ? TotalPhysical - AvailablePhysical : null;
    public double? UsagePercent => MemoryMetrics.Percent(UsedPhysical, TotalPhysical);
    public double? CommitPercent => MemoryMetrics.Percent(CommitUsed, CommitLimit);
    public bool ValidPressureInputs => UsedPhysical.HasValue && CommitPercent.HasValue;
}
public enum MemoryPressure { Unknown, Observing, Normal, Elevated, High }
public enum MemoryAssessmentReason { Unavailable, Stale, WarmingUp, WithinRange, SustainedReducedAvailable, SustainedPressure, RecoveryObservation }
public sealed record MemoryPressureAssessment(MemoryPressure State, MemoryAssessmentReason Reason, double ObservedSeconds);
public sealed record ResourceConsumerSummary(IReadOnlyList<ProcessPerformanceInfo> TopMemory, IReadOnlyList<ProcessPerformanceInfo> TopCpu, bool Available, DateTimeOffset? Timestamp);
public sealed record MemoryAnalysis(MemorySnapshot? Snapshot, MemoryPressureAssessment Pressure, ResourceConsumerSummary Consumers);
public interface IMemoryMetricsProvider { MemorySnapshot Read(SystemCore.Core.Dashboard.DashboardSample basic); }
public interface IMemoryPressureEvaluator { MemoryPressureAssessment Evaluate(MemorySnapshot? current, IReadOnlyList<HistoryPoint> history, DateTimeOffset now); }
public interface IMemoryAnalysisService { MemoryAnalysis Analyze(); }
public sealed record MemoryProcessDetails(int Pid, long StartTicks, string Name, string? ExecutablePath, string? Publisher);
public interface IProcessResourceAnalyzer
{
    Task<MemoryProcessDetails?> ReadDetailsAsync(int pid, long startTicks, CancellationToken token);
    Task<bool> OpenLocationAsync(int pid, long startTicks, CancellationToken token);
}
public static class MemoryMetrics
{
    public static double? Percent(ulong? value, ulong? total) => value.HasValue && total is > 0 && value <= total ? (double)value.Value / total.Value * 100 : null;
    public static ulong? Pages(ulong pages, ulong pageSize) => pageSize == 0 || pages > ulong.MaxValue / pageSize ? null : pages * pageSize;
}
