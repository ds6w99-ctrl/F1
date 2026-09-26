using SystemCore.Core.Dashboard;
using SystemCore.Core.Performance;

namespace SystemCore.App.Services;

// Compatibility adapter for existing view models. No worker, timer or Windows sampling here.
public sealed class DashboardMonitor : IDisposable
{
    private readonly IPerformanceMonitorService monitor;
    private readonly UiDispatcher dispatcher;
    private PerformanceSnapshot? pending;
    private int queued;
    private bool disposed;
    public event EventHandler<DashboardSample>? SampleAvailable;
    public event EventHandler? SampleFailed;
    public DashboardMonitor(IPerformanceMonitorService monitor, UiDispatcher dispatcher)
    {
        this.monitor = monitor;
        this.dispatcher = dispatcher;
        monitor.Updated += Updated;
    }
    private void Updated(object? sender, PerformanceSnapshot sample)
    {
        Volatile.Write(ref pending, sample);
        if (Interlocked.Exchange(ref queued, 1) != 0) return;
        if (!dispatcher.Post(() =>
        {
            Interlocked.Exchange(ref queued, 0);
            if (disposed || Volatile.Read(ref pending) is not { } current) return;
            if (current.Basic.Memory.Value is null && current.Basic.CpuPercent.Value is null) SampleFailed?.Invoke(this, EventArgs.Empty);
            else SampleAvailable?.Invoke(this, current.Basic);
        })) Interlocked.Exchange(ref queued, 0);
    }
    public void SetActive(object consumer, bool enabled) => monitor.SetActive(consumer, enabled);
    public IReadOnlyList<HistoryPoint> GetHistory() => monitor.GetHistory();
    public int? LogicalProcessors => monitor.Latest?.LogicalProcessors;
    public void Dispose() { disposed = true; monitor.Updated -= Updated; }
}
