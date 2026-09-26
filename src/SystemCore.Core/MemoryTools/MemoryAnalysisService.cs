using SystemCore.Core.Performance;

namespace SystemCore.Core.MemoryTools;

public sealed class MemoryAnalysisService(IPerformanceMonitorService monitor, IMemoryPressureEvaluator pressure) : IMemoryAnalysisService
{
    private readonly object sync = new();
    private MemoryPressure? lastPressure;
    public MemoryAnalysis Analyze()
    {
        lock (sync)
        {
            var current = monitor.Latest; var now = DateTimeOffset.UtcNow;
            var fresh = current is not null && now - current.Timestamp <= TimeSpan.FromSeconds(5);
            var processesFresh = fresh && current?.ProcessesAvailable == true && current.ProcessTimestamp is { } timestamp && now - timestamp <= TimeSpan.FromSeconds(5);
            var processes = processesFresh ? current!.Processes : [];
            var assessment = pressure.Evaluate(current?.Memory, monitor.GetHistory(), now);
            if (assessment.State != lastPressure) { lastPressure = assessment.State; MemoryToolsDiagnostics.Log.PressureState((int)assessment.State); }
            return new(current?.Memory, assessment,
                new(processes.Where(p => p.WorkingSet.HasValue).OrderByDescending(p => p.WorkingSet).ThenBy(p => p.Pid).Take(10).ToArray(),
                    processes.Where(p => p.CpuPercent.HasValue).OrderByDescending(p => p.CpuPercent).ThenBy(p => p.Pid).Take(10).ToArray(), processesFresh, current?.ProcessTimestamp));
        }
    }
}
