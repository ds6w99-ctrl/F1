namespace SystemCore.Core.Dashboard;

public enum ReadStatus { Ready, WarmingUp, Unavailable, Unsupported, Partial, PermissionRequired }

public sealed record Reading<T>(T? Value, ReadStatus Status) where T : struct
{
    public static Reading<T> Ready(T value) => new(value, ReadStatus.Ready);
    public static Reading<T> Missing(ReadStatus status = ReadStatus.Unavailable) => new(null, status);
}

public readonly record struct CpuTimes(ulong Idle, ulong Kernel, ulong User);
public readonly record struct MemoryUsage(ulong UsedBytes, ulong TotalBytes);
public readonly record struct VolumeUsage(string Root, ulong UsedBytes, ulong TotalBytes, ulong AvailableBytes);
public sealed record DashboardSample(DateTimeOffset Timestamp, Reading<double> CpuPercent,
    Reading<MemoryUsage> Memory, Reading<VolumeUsage> SystemVolume, TimeSpan Uptime);
public sealed record MachineOverview(string DeviceName, string WindowsDescription);
public sealed record InventoryCounts(Reading<int> InstalledApps, Reading<int> StartupEntries);

public interface IPerformanceSampler
{
    DashboardSample Sample();
    void ResetCpuBaseline();
}

public interface IDashboardInventoryProvider
{
    MachineOverview ReadMachine();
    InventoryCounts ReadCounts(CancellationToken cancellationToken);
}
