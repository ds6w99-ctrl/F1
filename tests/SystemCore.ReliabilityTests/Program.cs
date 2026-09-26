using System.Diagnostics.Tracing;
using SystemCore.Core.Diagnostics;
using SystemCore.Core.Performance;
using SystemCore.Infrastructure.Dashboard;
using SystemCore.Infrastructure.Performance;
using SystemCore.Infrastructure.Cleaner;

var checks = 0;
void Check(string name, bool condition)
{
    checks++;
    Console.WriteLine($"{(condition ? "PASS" : "FAIL")}: {name}");
    if (!condition) throw new InvalidOperationException(name);
}
using var listener = new Failures();
var source = new ResetFailure();
await using (var monitor = new PerformanceMonitorService(source))
{
    var owner = new object();
    monitor.SetActive(owner, true);
    await Task.Delay(2400);
    Check("Reset failure is contained and sampling recovers", source.Resets >= 2 && monitor.Latest is not null);
    Check("Reset failure is explicitly diagnosed once", listener.Count == 1);
    Check("No duplicate sampling worker after recovery", monitor.GetLifecycleDiagnostics().Workers == 1);
    var before = listener.Count;
    await monitor.StopAsync();
    Check("Owned worker stops without a fault", monitor.GetLifecycleDiagnostics().Workers == 0);
    Check("Expected shutdown cancellation is not an error", listener.Count == before);
}
Check("Sampler resources disposed", source.Disposed);
var brokenBasic = new FailingBasic();
using (var bounded = new WindowsPerformanceSource(brokenBasic))
{
    for (var i = 0; i < 100; i++) bounded.Sample(false, CancellationToken.None);
    Check("Unexpected provider failure is rate-limited across repeated samples", brokenBasic.Reads == 1);
    Check("Unavailable provider never fabricates telemetry", bounded.Sample(false, CancellationToken.None).Basic.CpuPercent.Value is null);
}
var firstChance = 0;
void Thrown(object? sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs args) => Interlocked.Increment(ref firstChance);
AppDomain.CurrentDomain.FirstChanceException += Thrown;
try
{
    var rejected = true;
    for (var i = 0; i < 100; i++) rejected &= !WindowsProcessSampler.TryReadTimes(0, out _, out _) && !WindowsProcessSampler.TryReadTimes(int.MaxValue, out _, out _);
    Check("Repeated unavailable process lookups return bounded status", rejected);
    Check("Unavailable process lookups cause no first-chance storm", firstChance == 0);
}
finally { AppDomain.CurrentDomain.FirstChanceException -= Thrown; }
using (var self = System.Diagnostics.Process.GetCurrentProcess())
{
    Check("Native process times match UTC process identity", WindowsProcessSampler.TryReadTimes(self.Id, out var start, out var cpu) && start == self.StartTime.ToUniversalTime().Ticks && cpu >= 0);
}
var fixture = Path.Combine(Path.GetTempPath(), "SystemCore-Reliability-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture);
try
{
    var path = Path.Combine(fixture, "locked.txt");
    File.WriteAllText(path, "synthetic fixture");
    using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
    {
        firstChance = 0; AppDomain.CurrentDomain.FirstChanceException += Thrown;
        try
        {
            var rejected = true;
            for (var i = 0; i < 100; i++) rejected &= !PinnedCleanerPath.TryOpenRead(path, false, out _, out var failure) && failure == PinFailure.Native;
            Check("Locked file scanning returns explicit native refusal", rejected);
            Check("Locked file scanning causes no first-chance storm", firstChance == 0);
        }
        finally { AppDomain.CurrentDomain.FirstChanceException -= Thrown; }
    }
    Check("Read lease succeeds after file becomes available", PinnedCleanerPath.TryOpenRead(path, false, out var lease, out _));
    lease!.Dispose();
    Check("Read lease rejects traversal without throwing", !PinnedCleanerPath.TryOpenRead(fixture + "\\..\\escape", false, out _, out var traversalFailure) && traversalFailure == PinFailure.UnsafePath);
    File.Delete(path);
    Check("Failed read attempts release ancestor and leaf handles", !File.Exists(path));
}
finally { Directory.Delete(fixture, true); }
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { Checks = checks, Failed = 0 }));

sealed class ResetFailure : IPerformanceSnapshotSource
{
    private readonly WindowsPerformanceSource inner = new(new WindowsPerformanceSampler());
    public int Resets;
    public bool Disposed;
    public PerformanceSnapshot Sample(bool detailed, CancellationToken token) => inner.Sample(detailed, token);
    public void Reset() { if (++Resets == 1) throw new IOException("Injected reset failure"); inner.Reset(); }
    public void Dispose() { Disposed = true; inner.Dispose(); }
}
sealed class Failures : EventListener
{
    public int Count;
    protected override void OnEventSourceCreated(EventSource source)
    { if (source.Name == "SystemCore-Reliability") EnableEvents(source, EventLevel.Error); }
    protected override void OnEventWritten(EventWrittenEventArgs args)
    { if (args.EventId == 1) Interlocked.Increment(ref Count); }
}
sealed class FailingBasic : SystemCore.Core.Dashboard.IPerformanceSampler
{
    public int Reads;
    public SystemCore.Core.Dashboard.DashboardSample Sample() { Reads++; throw new IOException("Injected unavailable source"); }
    public void ResetCpuBaseline() { }
}
