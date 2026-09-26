using System.Globalization;
using SystemCore.Core.SystemInformation;

namespace SystemCore.Core.Performance;

public static class PerformanceMath
{
    public static double? Rate(long? previous, long current, double seconds) =>
        !previous.HasValue || previous < 0 || current < previous || seconds <= 0 || !double.IsFinite(seconds)
            ? null : (current - previous.Value) / seconds;
    public static double? ProcessCpu(long? previousTicks, long currentTicks, double seconds, int processors)
    {
        if (processors <= 0) return null;
        var delta = Rate(previousTicks, currentTicks, seconds);
        if (!delta.HasValue) return null;
        var percent = delta.Value / TimeSpan.TicksPerSecond / processors * 100;
        // Beyond a tiny timing tolerance is an invalid sample, not something to hide by clamping.
        return percent > 100.5 ? null : Math.Clamp(percent, 0, 100);
    }
    public static string RateText(double bytes, CultureInfo culture) =>
        InfoFormatting.Rate((ulong)Math.Clamp(bytes, 0, (double)ulong.MaxValue - 4096), culture);
    public static IEnumerable<ProcessPerformanceInfo> Filter(IEnumerable<ProcessPerformanceInfo> rows, string search, bool byMemory) =>
        (byMemory ? rows.OrderByDescending(p => p.WorkingSet) : rows.OrderByDescending(p => p.CpuPercent))
        .ThenBy(p => p.Pid).Where(p => p.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase));
}

public sealed class BoundedHistory
{
    private readonly Queue<HistoryPoint> points = new(60);
    public void Add(HistoryPoint point)
    {
        if (points.Count > 0 && point.Timestamp < points.Last().Timestamp) points.Clear();
        points.Enqueue(point);
        while (points.Count > 60 || points.Count > 0 && point.Timestamp - points.Peek().Timestamp >= TimeSpan.FromSeconds(60)) points.Dequeue();
    }
    public IReadOnlyList<HistoryPoint> Snapshot() => points.ToArray();
    public void Clear() => points.Clear();
}

public sealed class ProcessDeltaTracker
{
    private Dictionary<(int, long), (long Cpu, double Time)> previous = [];
    public IReadOnlyList<ProcessPerformanceInfo> Update(IEnumerable<ProcessCounter> rows, double now, int processors)
    {
        var next = new Dictionary<(int, long), (long, double)>();
        var result = new List<ProcessPerformanceInfo>();
        foreach (var row in rows)
        {
            double? percent = null;
            if (row.StartTicks is { } start && row.CpuTicks is { } cpu)
            {
                if (previous.TryGetValue((row.Pid, start), out var before)) percent = PerformanceMath.ProcessCpu(before.Cpu, cpu, now - before.Time, processors);
                next[(row.Pid, start)] = (cpu, now);
            }
            result.Add(new(row.Pid, row.StartTicks, row.Name, percent, row.WorkingSet, row.PrivateBytes));
        }
        previous = next; // Exited processes and inaccessible baselines are dropped.
        return result;
    }
    public void Reset() => previous.Clear();
}
public sealed record ProcessCounter(int Pid, long? StartTicks, string Name, long? CpuTicks, ulong? WorkingSet, ulong? PrivateBytes = null);

public sealed class NetworkDeltaTracker
{
    private Dictionary<string, (long Down, long Up, double Time)> previous = [];
    public NetworkPerformanceInfo Update(IEnumerable<NetworkCounter> counters, double now)
    {
        var next = new Dictionary<string, (long, long, double)>();
        var values = new List<AdapterPerformanceInfo>();
        foreach (var c in counters.Where(c => c.Active))
        {
            double? down = null, up = null;
            if (c.Down >= 0 && c.Up >= 0)
            {
                if (previous.TryGetValue(c.Id, out var before))
                {
                    down = PerformanceMath.Rate(before.Down, c.Down.Value, now - before.Time);
                    up = PerformanceMath.Rate(before.Up, c.Up.Value, now - before.Time);
                }
                next[c.Id] = (c.Down.Value, c.Up.Value, now);
            }
            values.Add(new(c.Id, c.Name, c.Speed, down, up));
        }
        previous = next;
        return new(values, true);
    }
    public void Reset() => previous.Clear();
}
public sealed record NetworkCounter(string Id, string Name, bool Active, long? Down, long? Up, long? Speed);
