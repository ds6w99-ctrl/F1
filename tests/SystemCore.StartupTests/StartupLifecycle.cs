using System.Diagnostics;
using System.Text.Json;
using SystemCore.Core.Startup;
using SystemCore.Infrastructure.Startup;

internal static class StartupLifecycle
{
    internal static async Task<int> Run(string evidence)
    {
        using var fixture = new StartupFixture(); fixture.Create();
        var registry = new RegistryStartupProvider(fixture); var folder = new StartupFolderProvider(fixture);
        var source = new MutableTasks(fixture.Executable); var tasks = new TaskSchedulerStartupProvider(source);
        var state = new StartupStateService(fixture, Path.Combine(fixture.Root, "state"), Path.Combine(fixture.Root, "machine"));
        var inventory = new StartupInventoryService(registry, folder, tasks, state);
        var mutation = new StartupMutationService(inventory, registry, folder, tasks, state);
        using var process = Process.GetCurrentProcess(); var watch = Stopwatch.StartNew(); var samples = new List<object>(); var checks = 0;
        void Sample(string phase, int cycle)
        {
            process.Refresh(); var managed = GC.GetTotalMemory(false);
            samples.Add(new { Phase = phase, Cycle = cycle, Seconds = watch.Elapsed.TotalSeconds, PrivateBytes = process.PrivateMemorySize64,
                WorkingSet = process.WorkingSet64, Handles = process.HandleCount, Threads = process.Threads.Count, ManagedBytes = managed, ManagedValid = managed >= 0 });
            File.WriteAllText(evidence, JsonSerializer.Serialize(new { Checks = checks, Samples = samples }, new JsonSerializerOptions { WriteIndented = true }));
        }
        Sample("Baseline", 0);
        for (var cycle = 1; cycle <= 20; cycle++)
        {
            foreach (var kind in new[] { StartupSourceType.RegistryRun, StartupSourceType.StartupFolderUser, StartupSourceType.ScheduledTaskLogon })
            foreach (var action in new[] { StartupAction.Disable, StartupAction.Enable })
            {
                var snapshot = await inventory.RefreshAsync(default);
                var item = snapshot.Inventory.Items.Single(i => i.SourceType == kind && i.Name == (kind == StartupSourceType.RegistryRun ? "SameName" : kind == StartupSourceType.StartupFolderUser ? "fixture.exe" : "Logon"));
                var result = await mutation.ApplyAsync(new(snapshot.Id, item.Id, action, true), default);
                if (result.Outcome != StartupOutcome.Success) throw new IOException($"Lifecycle {cycle}/{kind}/{action}: {result.Outcome}");
                checks++;
                var refreshed = await inventory.RefreshAsync(default);
                if (refreshed.Inventory.Items.Single(i => i.Id == item.Id).Status != (action == StartupAction.Disable ? StartupStatus.DisabledBySystemCore : StartupStatus.Enabled)) throw new IOException("Stale lifecycle state");
                checks++;
                if (File.Exists(state.StatePath(item.Id, false)) != (action == StartupAction.Disable)) throw new IOException("Unexpected state retention");
                checks++;
            }
            if (!fixture.SourceIntact() || !source.Get("\\SystemCoreTest\\Logon")!.Enabled) throw new IOException("Fixture not restored");
            checks++; Sample("Cycle", cycle); await Task.Delay(500);
        }
        for (var second = 0; second < 120; second += 5) { await Task.Delay(5000); Sample("Cooldown", 20); }
        Console.WriteLine($"PASS: 20 repeated Registry/folder/task disable+enable cycles, {checks} checks; 120-second same-process cooldown; no forced GC.");
        return 0;
    }
}
