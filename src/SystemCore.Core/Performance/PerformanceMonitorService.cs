using System.Diagnostics;
using SystemCore.Core.SystemInformation;

namespace SystemCore.Core.Performance;

// The single application sampler. Serial samples, bounded history and no per-page timers.
public sealed class PerformanceMonitorService(IPerformanceSnapshotSource source) : IPerformanceMonitorService
{
    private readonly object sync = new();
    private readonly Dictionary<object, bool> consumers = [];
    private readonly CancellationTokenSource shutdown = new();
    private readonly BoundedHistory history = new();
    private Task? worker;
    private PerformanceSnapshot? latest;
    private int generation;
    private bool disposed;
    public event EventHandler<PerformanceSnapshot>? Updated;
    public PerformanceSnapshot? Latest { get { lock (sync) return latest; } }
    public IReadOnlyList<HistoryPoint> GetHistory() { lock (sync) return history.Snapshot(); }
    public (int Workers, int Consumers, int Subscribers) GetLifecycleDiagnostics()
    {
        lock (sync) return (worker is { IsCompleted: false } ? 1 : 0, consumers.Count, Updated?.GetInvocationList().Length ?? 0);
    }
    public void SetActive(object consumer, bool active, bool detailed = false)
    {
        lock (sync)
        {
            if (disposed) return;
            var before = consumers.Count > 0;
            if (active) consumers[consumer] = detailed; else consumers.Remove(consumer);
            if (before != (consumers.Count > 0)) generation++;
            worker ??= Task.Run(RunAsync);
        }
    }
    private async Task RunAsync()
    {
        int seenGeneration = -1;
        bool failed = false;
        try
        {
            while (!shutdown.IsCancellationRequested)
            {
                var watch = Stopwatch.StartNew();
                bool active, detailed; int currentGeneration;
                lock (sync) { active = consumers.Count > 0; detailed = consumers.Values.Any(v => v); currentGeneration = generation; }
                if (active)
                {
                    try
                    {
                        if (seenGeneration != currentGeneration)
                        {
                            source.Reset();
                            lock (sync) history.Clear();
                            seenGeneration = currentGeneration;
                        }
                        var sample = source.Sample(detailed, shutdown.Token);
                        bool publish;
                        lock (sync)
                        {
                            publish = generation == currentGeneration && consumers.Count > 0 && !disposed;
                            if (publish)
                            {
                                latest = sample;
                                history.Add(new(sample.Memory?.Timestamp ?? sample.Timestamp, sample.Basic.CpuPercent.Value,
                                    sample.Basic.Memory.Value is { } m ? InfoFormatting.Percentage(m.UsedBytes, m.TotalBytes) : null,
                                    sample.Disk.TotalBytesPerSecond, sample.Network.Download + sample.Network.Upload,
                                    sample.Memory?.AvailablePhysical, sample.Memory?.TotalPhysical, sample.Memory?.CommitPercent));
                            }
                        }
                        if (publish) Updated?.Invoke(this, sample);
                        failed = false;
                    }
                    catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { break; }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        if (!failed)
                        {
                            Trace.TraceWarning("Monitoring failure: {0}", ex.GetType().Name);
                            Diagnostics.ReliabilityDiagnostics.Log.UnexpectedFailure((int)Diagnostics.ReliabilityComponent.Monitor, ex.HResult);
                        }
                        failed = true;
                    }
                }
                // Never overlap, never catch up with rapid samples after a slow provider.
                var delay = TimeSpan.FromSeconds(1) - watch.Elapsed;
                await Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.FromSeconds(1), shutdown.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
    }
    public async Task StopAsync()
    {
        Task? task;
        lock (sync) { disposed = true; consumers.Clear(); task = worker; }
        shutdown.Cancel();
        if (task is not null) await task.ConfigureAwait(false);
    }
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        source.Dispose();
        shutdown.Dispose();
    }
}
