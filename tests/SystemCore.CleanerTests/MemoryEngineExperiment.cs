using System.Diagnostics;
using System.Text.Json;
using SystemCore.Core.Cleaner;
using SystemCore.Infrastructure.Cleaner;

// Diagnostic control: same scanner/native executor/STA wrapper, without WinUI/UIA.
// Never resolves a real cleanup target; only this process's newly created fixture.
internal static class MemoryEngineExperiment
{
    public static async Task<int> Run(int cooldownSeconds = 0, string? output = null)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "SystemCore-Cleaner-Test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var process = Process.GetCurrentProcess();
        var service = new WindowsCleanerService(new FixtureTargets(root), recycleBin: new WindowsRecycleBinService(new EmptyFakeShell()));
        var rows = new List<object>();
        var sessions = new List<WeakReference<CleanerScanSession>>();
        var observedItems = new List<WeakReference<CleanerItem>>();
        var clock = Stopwatch.StartNew();
        void Record(string phase, int cycle)
        {
            process.Refresh();
            var gc = GC.GetGCMemoryInfo();
            rows.Add(new { Phase = phase, Cycle = cycle, Seconds = clock.Elapsed.TotalSeconds,
                PrivateBytes = process.PrivateMemorySize64, WorkingSet = process.WorkingSet64,
                Handles = process.HandleCount, Threads = process.Threads.Count, ManagedBytes = GC.GetTotalMemory(false),
                gc.HeapSizeBytes, gc.TotalCommittedBytes, Gen0 = GC.CollectionCount(0), Gen1 = GC.CollectionCount(1), Gen2 = GC.CollectionCount(2),
                WeakSessionTargets = sessions.Count(w => w.TryGetTarget(out _)),
                WeakItemTargets = observedItems.Count(w => w.TryGetTarget(out _)) });
            if (output is not null) File.WriteAllText(output, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        }
        for (var cycle = 0; cycle <= 100; cycle++)
        {
            if (cycle > 0)
            {
                for (var i = 0; i < 111; i++)
                {
                    var path = Path.Combine(root, $"fixture-{i:000}.tmp");
                    File.WriteAllText(path, "x"); File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2));
                }
                await Cycle(service, sessions, observedItems);
            }
            Record("Workload", cycle);
            if (output is not null && cycle > 0) await Task.Delay(500);
        }
        for (var elapsed = 60; elapsed <= cooldownSeconds; elapsed += 60)
        {
            await Task.Delay(TimeSpan.FromSeconds(60));
            Record("Cooldown", elapsed / 60);
        }
        GC.KeepAlive(service);
        Console.WriteLine(JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
    private static async Task Cycle(WindowsCleanerService service, List<WeakReference<CleanerScanSession>> sessions, List<WeakReference<CleanerItem>> observedItems)
    {
        var scan = await service.ScanAsync(default);
        var items = scan.Categories.Single(c => c.Category == CleanerCategory.UserTemp).Items;
        sessions.Add(new(scan));
        foreach (var item in items) observedItems.Add(new(item));
        if (items.Count != 111) throw new InvalidOperationException("Incomplete fixture scan");
        var result = await service.CleanAsync(new(scan.Id, items.Select(i => i.Id).ToArray()), default);
        if (result.Deleted != 111 || result.RemovedBytes != 111) throw new InvalidOperationException("Fixture clean failed");
        var rescan = await service.ScanAsync(default);
        sessions.Add(new(rescan));
        if (rescan.Categories.Single(c => c.Category == CleanerCategory.UserTemp).Items.Count != 0)
            throw new InvalidOperationException("Synthetic rescan found undeleted fixtures");
        service.Invalidate();
    }
    private sealed class EmptyFakeShell : IRecycleBinShell
    {
        public RecycleBinSnapshot Query() => new(false, 0, 0);
        public bool Empty() => throw new InvalidOperationException("Not part of this experiment");
    }
}
