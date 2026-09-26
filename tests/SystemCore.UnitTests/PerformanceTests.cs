using System.Globalization;
using SystemCore.Core.Dashboard;
using SystemCore.Core.Performance;
using SystemCore.Core.SystemInformation;

internal static class PerformanceTests
{
    public static async Task<bool> Run()
    {
        var passed = true;
        void Check(string name, bool result) { Console.WriteLine($"{(result ? "PASS" : "FAIL")}: {name}"); passed &= result; }
        Check("Process first sample unavailable", PerformanceMath.ProcessCpu(null, 100, 1, 4) is null);
        Check("Process multicore normalization", PerformanceMath.ProcessCpu(0, TimeSpan.TicksPerSecond, 1, 4) == 25);
        Check("Process zero elapsed unavailable", PerformanceMath.ProcessCpu(0, 100, 0, 4) is null);
        Check("Process counter reset unavailable", PerformanceMath.ProcessCpu(100, 10, 1, 4) is null);
        Check("Process full machine boundary", PerformanceMath.ProcessCpu(0, TimeSpan.TicksPerSecond * 4, 1, 4) == 100);
        Check("Process idle boundary", PerformanceMath.ProcessCpu(0, 0, 1, 4) == 0);
        Check("Invalid process CPU not masked by clamp", PerformanceMath.ProcessCpu(0, TimeSpan.TicksPerSecond * 5, 1, 4) is null);
        Check("Invalid processor count", PerformanceMath.ProcessCpu(0, 1, 1, 0) is null);
        Check("Memory zero total", DashboardMath.Memory(0, 0) is null);
        Check("Memory used and available semantics", DashboardMath.Memory(100, 40) == new MemoryUsage(60, 100));
        Check("Memory percent", InfoFormatting.Percentage(60, 100) == 60);
        Check("All memory available", DashboardMath.Memory(100, 100)?.UsedBytes == 0);
        Check("No memory available", DashboardMath.Memory(100, 0)?.UsedBytes == 100);
        Check("Memory binary formatting", InfoFormatting.Size(1073741824, CultureInfo.InvariantCulture) == "1.0 GB");
        Check("Byte counter read delta", PerformanceMath.Rate(100, 300, 2) == 100);
        Check("Byte counter write delta", PerformanceMath.Rate(200, 500, 2) == 150);
        Check("Byte counter zero interval", PerformanceMath.Rate(100, 300, 0) is null);
        Check("Byte counter reset", PerformanceMath.Rate(300, 100, 1) is null);
        Check("Negative byte counter rejected", PerformanceMath.Rate(-1, 1, 1) is null);
        Check("Invalid elapsed rejected", PerformanceMath.Rate(1, 2, double.NaN) is null);
        Check("Throughput formatting", PerformanceMath.RateText(1048576, CultureInfo.InvariantCulture) == "1.0 MB/s");
        Check("Disk combines read/write", new DiskPerformanceInfo(100, 200).TotalBytesPerSecond == 300);
        Check("Disk partial missing is not zero", new DiskPerformanceInfo(null, 200).TotalBytesPerSecond is null);
        var network = new NetworkDeltaTracker();
        Check("Network first sample missing", network.Update([new("a", "A", true, 100, 200, null)], 1).Download is null);
        var n = network.Update([new("a", "A", true, 300, 300, null)], 3);
        Check("Network download delta", n.Download == 100);
        Check("Network upload delta", n.Upload == 50);
        Check("Disconnected adapters removed", network.Update([new("a", "A", false, 400, 400, null)], 4).Adapters.Count == 0);
        Check("Reconnect resets baseline", network.Update([new("a", "A", true, 500, 500, null)], 5).Download is null);
        Check("Network reset produces gap", network.Update([new("a", "A", true, 10, 10, null)], 6).Download is null);
        network.Reset();
        network.Update([new("a", "A", true, 100, 100, null), new("b", "B", true, 100, 100, null)], 1);
        n = network.Update([new("a", "A", true, 200, 150, null), new("b", "B", true, 400, 250, null)], 2);
        Check("Multiple network adapters retained", n.Adapters.Count == 2);
        Check("Network aggregate download", n.Download == 400);
        Check("Network aggregate upload", n.Upload == 200);
        Check("Failed adapter not silently zero", network.Update([new("a", "A", true, null, null, null)], 3).Download is null);
        var processes = new ProcessDeltaTracker();
        var p = processes.Update([new(10, 1, "alpha", 0, 100), new(11, 2, "beta", null, null)], 1, 4);
        Check("New process CPU missing", p[0].CpuPercent is null);
        Check("Access denied fields preserved as missing", p[1].CpuPercent is null && p[1].WorkingSet is null);
        p = processes.Update([new(10, 1, "alpha", TimeSpan.TicksPerSecond, 100)], 2, 4);
        Check("Process CPU delta", p[0].CpuPercent == 25);
        Check("Exited process removed", p.Count == 1);
        p = processes.Update([new(10, 3, "new", TimeSpan.TicksPerSecond, 100)], 3, 4);
        Check("PID reuse resets baseline", p[0].CpuPercent is null);
        processes.Reset();
        Check("Reset drops CPU baseline", processes.Update([new(10, 3, "new", TimeSpan.TicksPerSecond * 2, 100)], 4, 4)[0].CpuPercent is null);
        ProcessPerformanceInfo[] rows = [new(1, 1, "Alpha", 10, 100), new(2, 2, "Beta", 20, 50), new(3, 3, "alphabet", null, null)];
        Check("CPU sorting", PerformanceMath.Filter(rows, "", false).First().Pid == 2);
        Check("Memory sorting", PerformanceMath.Filter(rows, "", true).First().Pid == 1);
        Check("Case insensitive name filtering", PerformanceMath.Filter(rows, "ALPHA", false).Count() == 2);
        Check("No matching process is empty", !PerformanceMath.Filter(rows, "missing", false).Any());
        var history = new BoundedHistory();
        Check("History initially empty", history.Snapshot().Count == 0);
        var start = DateTimeOffset.UnixEpoch;
        history.Add(new(start, 1, 2, 3, 4));
        Check("History first sample", history.Snapshot().Count == 1);
        for (var i = 1; i < 60; i++) history.Add(new(start.AddSeconds(i), i, null, null, null));
        Check("History sixty samples", history.Snapshot().Count == 60);
        history.Add(new(start.AddSeconds(60), 60, null, null, null));
        Check("History 61 evicts oldest", history.Snapshot().Count == 60 && history.Snapshot()[0].Timestamp == start.AddSeconds(1));
        Check("History chronological", history.Snapshot().Select(p => p.Timestamp).SequenceEqual(history.Snapshot().Select(p => p.Timestamp).Order()));
        history.Add(new(start.AddSeconds(200), null, null, null, null));
        Check("History expires old time window", history.Snapshot().Count == 1);
        history.Clear(); Check("History clear", history.Snapshot().Count == 0);
        history.Add(new(start.AddSeconds(3), null, null, null, null));
        history.Add(new(start, null, null, null, null));
        Check("Clock reversal resets history", history.Snapshot().Count == 1);

        var source = new FakeSource();
        await using var service = new PerformanceMonitorService(source);
        var a = new object(); var b = new object();
        service.SetActive(a, true); service.SetActive(b, true, true);
        await Task.Delay(2250);
        Check("Shared service has no overlapping samples", source.MaxConcurrent == 1 && source.Calls is >= 2 and <= 3);
        Check("Partial disk failure retains CPU memory network", service.Latest is { Disk.TotalBytesPerSecond: null, Network.Available: true } s && s.Basic.CpuPercent.Value == 25 && s.Basic.Memory.Value?.TotalBytes == 100);
        Check("Detailed consumer requests detailed samples", source.SawDetailed);
        service.SetActive(a, false);
        var before = source.Calls;
        await Task.Delay(1100);
        Check("One remaining subscriber keeps sampling", source.Calls > before);
        service.SetActive(b, false);
        await Task.Delay(200);
        before = source.Calls;
        await Task.Delay(1100);
        Check("No subscribers skips providers", source.Calls == before);
        source.FailNext = true;
        service.SetActive(b, true, true);
        await Task.Delay(2300);
        Check("Provider exception does not kill lifecycle", service.Latest is not null && source.Calls >= before + 2);
        await service.StopAsync();
        before = source.Calls;
        await Task.Delay(1100);
        Check("Monitoring stops cleanly", source.Calls == before);
        using var gate = new ManualResetEventSlim(false);
        var slowSource = new FakeSource { Gate = gate };
        await using var slowService = new PerformanceMonitorService(slowSource);
        slowService.SetActive(new object(), true, true);
        await slowSource.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        slowService.SetActive(new object(), true, true);
        try
        {
            await Task.Delay(1200);
            Check("Slow provider applies backpressure across interval", slowSource.Calls == 1 && slowSource.MaxConcurrent == 1);
        }
        finally { gate.Set(); }
        await slowService.StopAsync();
        Check("Slow provider shutdown drains the worker", slowSource.Calls == 1);
        return passed;
    }

    private sealed class FakeSource : IPerformanceSnapshotSource
    {
        public int Calls, MaxConcurrent;
        private int concurrent;
        public bool SawDetailed, FailNext;
        public ManualResetEventSlim? Gate;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public PerformanceSnapshot Sample(bool detailed, CancellationToken token)
        {
            var current = Interlocked.Increment(ref concurrent);
            MaxConcurrent = Math.Max(current, MaxConcurrent);
            Interlocked.Increment(ref Calls);
            try
            {
                Started.TrySetResult();
                Gate?.Wait(token);
                token.ThrowIfCancellationRequested(); SawDetailed |= detailed;
                if (FailNext) { FailNext = false; throw new IOException("Test source failure"); }
                return new(DateTimeOffset.Now, new(DateTimeOffset.Now, Reading<double>.Ready(25), Reading<MemoryUsage>.Ready(new(50, 100)), Reading<VolumeUsage>.Missing(), TimeSpan.FromSeconds(1)),
                    new(null, null), new([], true), [], true, DateTimeOffset.Now, 4);
            }
            finally { Interlocked.Decrement(ref concurrent); }
        }
        public void Reset() { }
        public void Dispose() { }
    }
}
