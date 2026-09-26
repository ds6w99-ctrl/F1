#if CLEANER_VALIDATION
using SystemCore.Core.Cleaner;
using SystemCore.Infrastructure.Cleaner;

namespace SystemCore.App.Cleaner;

// Compiled ONLY into the separately published validation app. No path, command-line,
// environment or IPC input can enable this fixture adapter in the production binary.
public sealed class ValidationCleanerService : ICleanerService
{
    private readonly SystemCore.Core.Performance.IPerformanceMonitorService monitor;
    private int activeTasks;
    private readonly List<WeakReference<CleanerScanSession>> observedSessions = [];
    private readonly List<WeakReference<CleanerItem>> observedItems = [];
    private WindowsCleanerService service = null!;
    private string root = "";
    private bool mutated;
    public string Status { get; private set; } = "Synthetic validation only";
    public string Diagnostics
    {
        get
        {
            var d = ((SystemCore.Core.Performance.PerformanceMonitorService)monitor).GetLifecycleDiagnostics();
            observedSessions.RemoveAll(w => !w.TryGetTarget(out _));
            observedItems.RemoveAll(w => !w.TryGetTarget(out _));
            var gc = GC.GetGCMemoryInfo();
            var loh = gc.GenerationInfo.Length > 3 ? gc.GenerationInfo[3].SizeAfterBytes : 0;
            return $";CleanerTasks:{Volatile.Read(ref activeTasks)};MonitorWorkers:{d.Workers};MonitorConsumers:{d.Consumers};MonitorSubscribers:{d.Subscribers};ManagedBytes:{GC.GetTotalMemory(false)};Gen2Collections:{GC.CollectionCount(2)};Gen0Collections:{GC.CollectionCount(0)};Gen1Collections:{GC.CollectionCount(1)};HeapSize:{gc.HeapSizeBytes};HeapCommitted:{gc.TotalCommittedBytes};LOHAfterLastGC:{loh};ObservedLiveSessions:{observedSessions.Count};ObservedLiveItems:{observedItems.Count}";
        }
    }
    public ValidationCleanerService(SystemCore.Core.Performance.IPerformanceMonitorService monitor) { this.monitor = monitor; Prepare(); }
    public void Prepare()
    {
        root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "SystemCore-Cleaner-Test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        foreach (var name in new[] { "00-delete.tmp", "01-changed.tmp", "02-locked.tmp", "03-gone.tmp", "04-rejected.tmp", "05-unselected.tmp" }) Make(name, "fixture");
        for (var i = 0; i < 105; i++) Make($"page-{i:000}.tmp", "x");
        service = new WindowsCleanerService(new FixtureTargets(root), recycleBin: new WindowsRecycleBinService(new UnavailableBin())); mutated = false;
        Status = "Prepared:111";
    }
    private void Make(string name, string text)
    {
        var path = Path.Combine(root, name); File.WriteAllText(path, text); File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2));
    }
    public void Invalidate() => service.Invalidate();
    public async Task<CleanerScanSession> ScanAsync(CancellationToken token)
    {
        Interlocked.Increment(ref activeTasks);
        try
        {
            await Task.Delay(150, token);
            var scan = await service.ScanAsync(token);
            observedSessions.Add(new(scan));
            foreach (var item in scan.Categories.SelectMany(c => c.Items)) observedItems.Add(new(item));
            return scan;
        }
        finally { Interlocked.Decrement(ref activeTasks); }
    }
    public async Task<CleanerCleanResult> CleanAsync(CleanerCleanRequest request, CancellationToken token)
    {
        FileStream? locked = null;
        Interlocked.Increment(ref activeTasks);
        try
        {
            if (!mutated)
            {
                File.AppendAllText(Path.Combine(root, "01-changed.tmp"), "changed");
                locked = File.Open(Path.Combine(root, "02-locked.tmp"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                var backup = root + "-kept"; Directory.CreateDirectory(backup);
                foreach (var name in new[] { "03-gone.tmp", "04-rejected.tmp" }) File.Move(Path.Combine(root, name), Path.Combine(backup, name));
                Directory.CreateDirectory(Path.Combine(root, "04-rejected.tmp"));
                mutated = true;
            }
            var result = await service.CleanAsync(request, token);
            Status = $"Deleted:{result.Deleted};Skipped:{result.Skipped};Failed:{result.Failed};Bytes:{result.RemovedBytes};" +
                $"Root:{Directory.Exists(root)};Unselected:{File.Exists(Path.Combine(root, "05-unselected.tmp"))};" +
                $"PageExcluded:{File.Exists(Path.Combine(root, "page-104.tmp"))};Changed:{File.Exists(Path.Combine(root, "01-changed.tmp"))};" +
                $"Gone:{!File.Exists(Path.Combine(root, "03-gone.tmp"))};Locked:{File.Exists(Path.Combine(root, "02-locked.tmp"))};" +
                $"Rejected:{Directory.Exists(Path.Combine(root, "04-rejected.tmp"))}";
            return result;
        }
        finally { locked?.Dispose(); Interlocked.Decrement(ref activeTasks); }
    }
    private sealed class FixtureTargets(string root) : ICleanerTargetProvider
    {
        public IReadOnlyList<CleanerTarget> Resolve() => new CleanerTarget[] { new(CleanerCategory.UserTemp, root, "*", true, TimeSpan.FromHours(24), true, false, false) };
    }
    private sealed class UnavailableBin : IRecycleBinShell
    {
        public RecycleBinSnapshot Query() => new(false, 0, 0);
        public bool Empty() => throw new InvalidOperationException("Validation fixtures cannot empty a real Recycle Bin");
    }
}
#endif
