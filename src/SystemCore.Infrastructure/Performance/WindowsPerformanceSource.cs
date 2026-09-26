using System.Diagnostics;
using System.Runtime.InteropServices;
using SystemCore.Core.Dashboard;
using SystemCore.Core.Performance;

namespace SystemCore.Infrastructure.Performance;

public sealed class WindowsPerformanceSource(IPerformanceSampler basic) : IPerformanceSnapshotSource
{
    private readonly PdhDiskSampler disk = new();
    private readonly WindowsNetworkSampler network = new();
    private readonly WindowsProcessSampler processes = new();
    private readonly MemoryTools.WindowsMemoryMetricsProvider memory = new();
    private readonly HashSet<string> failed = [];
    private readonly Dictionary<string, long> retryAfter = [];
    private IReadOnlyList<ProcessPerformanceInfo> lastProcesses = [];
    private DateTimeOffset? processTimestamp;
    private double lastProcessTime = double.NegativeInfinity;
    private bool processAvailable, detailedBefore;
    private readonly int logicalProcessors = (int)GetActiveProcessorCount(0xffff);

    public PerformanceSnapshot Sample(bool detailed, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        var sample = Read("basic", basic.Sample, () => new DashboardSample(DateTimeOffset.Now,
            Reading<double>.Missing(), Reading<MemoryUsage>.Missing(), Reading<VolumeUsage>.Missing(), TimeSpan.Zero));
        var d = new DiskPerformanceInfo(null, null);
        var n = new NetworkPerformanceInfo([], false);
        if (detailed)
        {
            if (!detailedBefore) { disk.Dispose(); network.Reset(); processes.Reset(); lastProcessTime = double.NegativeInfinity; }
            d = Read("disk", disk.Sample, () => new(null, null));
            n = Read("network", () => network.Sample(now), () => new([], false));
            if (now - lastProcessTime >= 2)
            {
                processAvailable = true;
                lastProcesses = Read("processes", () => processes.Sample(now, logicalProcessors, token), () =>
                { processAvailable = false; return Array.Empty<ProcessPerformanceInfo>(); });
                lastProcessTime = now; processTimestamp = DateTimeOffset.Now;
            }
        }
        else { lastProcesses = []; processTimestamp = null; processAvailable = false; if (detailedBefore) { disk.Dispose(); network.Reset(); processes.Reset(); } }
        detailedBefore = detailed;
        return new(DateTimeOffset.Now, sample, d, n, lastProcesses, processAvailable, processTimestamp, logicalProcessors,
            detailed ? Read<SystemCore.Core.MemoryTools.MemorySnapshot?>("memory", () => memory.Read(sample), () => null) : null);
    }
    private T Read<T>(string name, Func<T> read, Func<T> unavailable)
    {
        var now = Environment.TickCount64;
        if (retryAfter.TryGetValue(name, out var retry) && now < retry) return unavailable();
        try { var value = read(); failed.Remove(name); retryAfter.Remove(name); return value; }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            retryAfter[name] = now + 30000;
            if (failed.Add(name))
            {
                Trace.TraceWarning("Performance source {0}: {1}", name, ex.GetType().Name);
                Core.Diagnostics.ReliabilityDiagnostics.Log.UnexpectedFailure((int)Core.Diagnostics.ReliabilityComponent.Monitor, ex.HResult);
            }
            return unavailable();
        }
    }
    public void Reset() { basic.ResetCpuBaseline(); disk.Dispose(); network.Reset(); processes.Reset(); lastProcesses = []; lastProcessTime = double.NegativeInfinity; detailedBefore = false; }
    public void Dispose() => disk.Dispose();
    [DllImport("kernel32.dll")] private static extern uint GetActiveProcessorCount(ushort group);
}
