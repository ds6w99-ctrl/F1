using System.Text.Json;
using SystemCore.Core.Performance;
using SystemCore.Core.Dashboard;
using SystemCore.Infrastructure.Dashboard;
using SystemCore.Infrastructure.Performance;

var source = new WindowsPerformanceSource(new WindowsPerformanceSampler());
await using var service = new PerformanceMonitorService(source);
var consumer = new object();
var samples = 0;
service.Updated += (_, _) => Interlocked.Increment(ref samples);
service.SetActive(consumer, true, true);
await Task.Delay(6500);
var s = service.Latest;
var before = samples;
service.SetActive(consumer, false);
await Task.Delay(1500);
var paused = samples == before;
service.SetActive(consumer, true, true);
await Task.Delay(2500);
var resumed = samples > before;
await service.StopAsync();
before = samples;
await Task.Delay(1200);
using var faultedSource = new WindowsPerformanceSource(new UnavailableBasicSampler());
var partial = faultedSource.Sample(true, CancellationToken.None);
var checks = new (string, bool)[]
{
 ("CPU valid", s?.Basic.CpuPercent.Value is >= 0 and <= 100),
 ("RAM positive", s?.Basic.Memory.Value?.TotalBytes > 0),
 ("Disk structure valid", s?.Disk is not null && (s.Disk.ReadBytesPerSecond is null or >= 0) && (s.Disk.WriteBytesPerSecond is null or >= 0)),
 ("Network sampling completes", s?.Network.Available == true),
 ("Real process list", s?.Processes.Count > 0),
 ("Process CPU deltas exist", s?.Processes.Any(p => p.CpuPercent.HasValue) == true),
 ("Pause stops publishing", paused), ("Resume publishes", resumed), ("Stop is clean", samples == before),
 ("History bounded", service.GetHistory().Count <= 60),
 ("Collector isolates a thrown component failure", partial.Basic.Memory.Value is null && partial.Network.Available && partial.Processes.Count > 0)
};
foreach (var (name, ok) in checks) Console.WriteLine($"{(ok ? "PASS" : "FAIL")}: {name}");
// Small requested validation sample only, not continuous process logging. No paths or addresses.
Console.WriteLine(JsonSerializer.Serialize(new { s?.Timestamp, Cpu = s?.Basic.CpuPercent.Value, Memory = s?.Basic.Memory.Value,
 s?.Disk, Network = new { s?.Network.Download, s?.Network.Upload, AdapterCount = s?.Network.Adapters.Count },
 TopCpu = s?.Processes.OrderByDescending(p => p.CpuPercent).Take(5).Select(p => new { p.Name, p.Pid, p.CpuPercent }),
 TopMemory = s?.Processes.OrderByDescending(p => p.WorkingSet).Take(5).Select(p => new { p.Name, p.Pid, p.WorkingSet }),
 Samples = samples }, new JsonSerializerOptions { WriteIndented = true }));
return checks.All(c => c.Item2) ? 0 : 1;

sealed class UnavailableBasicSampler : IPerformanceSampler
{
    public DashboardSample Sample() => throw new IOException("Deliberate integration-test component failure");
    public void ResetCpuBaseline() { }
}
