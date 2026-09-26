using SystemCore.Core.Dashboard;

namespace SystemCore.Core.Performance;

public sealed record DiskPerformanceInfo(double? ReadBytesPerSecond, double? WriteBytesPerSecond)
{
    public double? TotalBytesPerSecond => ReadBytesPerSecond + WriteBytesPerSecond;
}
public sealed record AdapterPerformanceInfo(string Id, string Name, long? LinkBitsPerSecond, double? Download, double? Upload);
public sealed record NetworkPerformanceInfo(IReadOnlyList<AdapterPerformanceInfo> Adapters, bool Available)
{
    // Never silently substitute zero for a warming-up/reset/failed active adapter.
    public double? Download => Available && Adapters.All(a => a.Download.HasValue) ? Adapters.Sum(a => a.Download!.Value) : null;
    public double? Upload => Available && Adapters.All(a => a.Upload.HasValue) ? Adapters.Sum(a => a.Upload!.Value) : null;
}
public sealed record ProcessPerformanceInfo(int Pid, long? StartTicks, string Name, double? CpuPercent, ulong? WorkingSet, ulong? PrivateBytes = null);
public sealed record PerformanceSnapshot(DateTimeOffset Timestamp, DashboardSample Basic, DiskPerformanceInfo Disk,
    NetworkPerformanceInfo Network, IReadOnlyList<ProcessPerformanceInfo> Processes, bool ProcessesAvailable,
    DateTimeOffset? ProcessTimestamp, int LogicalProcessors, MemoryTools.MemorySnapshot? Memory = null);
public sealed record HistoryPoint(DateTimeOffset Timestamp, double? Cpu, double? Memory, double? Disk, double? Network,
    ulong? AvailableBytes = null, ulong? TotalPhysical = null, double? CommitPercent = null);

public interface IPerformanceSnapshotSource : IDisposable
{
    PerformanceSnapshot Sample(bool detailed, CancellationToken token);
    void Reset();
}
public interface IPerformanceMonitorService : IAsyncDisposable
{
    event EventHandler<PerformanceSnapshot>? Updated;
    void SetActive(object consumer, bool active, bool detailed = false);
    PerformanceSnapshot? Latest { get; }
    IReadOnlyList<HistoryPoint> GetHistory();
    Task StopAsync();
}
