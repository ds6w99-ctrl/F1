using SystemCore.Core.Startup;
using SystemCore.Infrastructure.Startup;
using SystemCore.Infrastructure.Uninstaller;

internal static class Fix2Tests
{
    internal static async Task Run(Action<string, bool> check)
    {
        using var fixture = new StartupFixture(); fixture.Create();
        var registry = new RegistryStartupProvider(fixture); var folders = new StartupFolderProvider(fixture);
        var tasks = new TaskSchedulerStartupProvider(new MutableTasks(fixture.Executable));
        var state = new StartupStateService(fixture, Path.Combine(fixture.Root, "state"), Path.Combine(fixture.Root, "machine"));
        var inventory = new StartupInventoryService(registry, folders, tasks, state);
        var snapshot = await inventory.RefreshAsync(default);
        var run = snapshot.Inventory.Items.Single(i => i.Name == "SameName" && i.SourceType == StartupSourceType.RegistryRun);
        var folder = snapshot.Inventory.Items.Single(i => i.Name == "fixture.exe" && i.SourceType == StartupSourceType.StartupFolderUser);
        var task = snapshot.Inventory.Items.Single(i => i.Name == "Logon");
        check("HKCU Run with supported mutation visible", ManageableStartupItems.IsVisible(run));
        check("User Startup reversible move visible", ManageableStartupItems.IsVisible(folder));
        check("Supported third-party task visible", ManageableStartupItems.IsVisible(task));
        // Filter contract for already-proven helper capabilities; no real HKLM writes.
        check("HKLM Run manageable via helper visible", ManageableStartupItems.IsVisible(run with { Scope = StartupScope.AllUsers, RequiresElevation = true }));
        check("Common Startup manageable via helper visible", ManageableStartupItems.IsVisible(folder with { SourceType = StartupSourceType.StartupFolderCommon, Scope = StartupScope.AllUsers, RequiresElevation = true }));
        check("Microsoft protected task hidden", !ManageableStartupItems.IsVisible(task with { IsProtected = true, ProtectionReason = StartupProtection.SystemTask }));
        check("Windows maintenance task hidden", !ManageableStartupItems.IsVisible(task with { ProtectionReason = StartupProtection.SystemComponent }));
        check("Unsupported task hidden", !ManageableStartupItems.IsVisible(task with { ProtectionReason = StartupProtection.AmbiguousTask }));
        check("Read-only item hidden", !ManageableStartupItems.IsVisible(run with { CanManage = false }));
        check("Protected item hidden", !ManageableStartupItems.IsVisible(run with { IsProtected = true }));
        check("Unknown identity hidden", !ManageableStartupItems.IsVisible(run with { Identity = "" }));
        check("Unknown state hidden", !ManageableStartupItems.IsVisible(run with { Status = StartupStatus.Unknown }));
        check("Broken non-restorable item hidden", !ManageableStartupItems.IsVisible(folder with { ExecutablePath = null }));
        var capability = new StartupManageability(registry, folders, tasks, state, null);
        check("Unverifiable identity rejected by preflight", !capability.CanManage(run with { Identity = "changed" }));
        var readOnly = snapshot.Inventory.Items.Single(i => i.Name == "read-only.cmd");
        check("Unsupported folder strategy never visible", !ManageableStartupItems.IsVisible(readOnly));
        var taskSnapshot = new MutableTasks(fixture.Executable).Get("\\SystemCoreTest\\Logon")!;
        check("Task ACL denying writes is protected", TaskSchedulerStartupProvider.Normalize(taskSnapshot with { SecurityDescriptor = "O:BAG:BAD:(D;;FW;;;WD)(A;;FR;;;WD)" }) is { IsProtected: true });
        check("Capability never changes recovery JSON schema", !System.Text.Json.JsonSerializer.Serialize(run).Contains("CanManage", StringComparison.Ordinal));

        var mutation = new StartupMutationService(inventory, registry, folders, tasks, state);
        foreach (var original in new[] { run, folder, task })
        {
            var current = await inventory.RefreshAsync(default);
            var item = current.Inventory.Items.Single(i => i.Id == original.Id);
            var disabled = await mutation.ApplyAsync(new(current.Id, item.Id, StartupAction.Disable, true), default);
            check("No fake disable: visible synthetic " + original.SourceType, ManageableStartupItems.IsVisible(item) && disabled.Outcome == StartupOutcome.Success);
            current = await inventory.RefreshAsync(default); item = current.Inventory.Items.Single(i => i.Id == original.Id);
            check("Exact restore remains manageable: " + original.SourceType, ManageableStartupItems.IsVisible(item));
            check("Exact restore succeeds: " + original.SourceType, (await mutation.ApplyAsync(new(current.Id, item.Id, StartupAction.Enable, true), default)).Outcome == StartupOutcome.Success);
        }

        var cache = new StartupIconCache();
        var registryIcon = await cache.GetAsync(run, default);
        check("Real Registry executable icon extracted", registryIcon.Png is { Length: > 0 } && registryIcon.Source == "Registry");
        check("Real task executable icon extracted", (await cache.GetAsync(task, default)).Source == "ScheduledTask");
        var shortcut = snapshot.Inventory.Items.Single(i => i.Name == "fixture.lnk");
        check("Shortcut target icon used", (await cache.GetAsync(shortcut, default)).Source == "StartupFolderTarget");
        SetShortcutIcon(shortcut.SourceLocation, fixture.Executable + ",0");
        check("Explicit shortcut icon preferred", (await cache.GetAsync(shortcut, default)).Source == "ShortcutIcon");
        SetShortcutIcon(shortcut.SourceLocation, Path.Combine(fixture.Root, "missing.ico") + ",0");
        check("Missing shortcut icon falls back to target", (await cache.GetAsync(shortcut, default)).Source == "StartupFolderTarget");
        check("Missing executable uses fallback", (await cache.GetAsync(run with { CommandDisplay = "\"C:\\missing-startup-test.exe\"", ExecutablePath = "C:\\missing-startup-test.exe" }, default)).Source == "Fallback");
        check("Malformed command uses fallback", (await cache.GetAsync(run with { CommandDisplay = "cmd.exe /c forbidden" }, default)).Source == "Fallback");
        check("Untrusted task action uses fallback", (await cache.GetAsync(task with { ProtectionReason = StartupProtection.AmbiguousTask }, default)).Source == "Fallback");
        var noIcon = new StartupIconCache((_, _) => Task.FromResult<byte[]?>(null));
        check("Missing icon always has generic fallback", (await noIcon.GetAsync(run, default)) == StartupIconResult.Fallback);
        var calls = 0;
        var counted = new StartupIconCache(async (file, token) => { calls++; return await ApplicationIconReader.ReadFileAsync(file, token); });
        var first = await counted.GetAsync(run, default); var second = await counted.GetAsync(run, default);
        check("Second request reuses extracted bytes", calls == 1 && ReferenceEquals(first.Png, second.Png));
        File.SetLastWriteTimeUtc(fixture.Executable, File.GetLastWriteTimeUtc(fixture.Executable).AddSeconds(2));
        await counted.GetAsync(run, default);
        check("Changed resource invalidates icon cache", calls == 2);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); var sawCancel = false;
        try { await counted.GetAsync(run, cancelled.Token); } catch (OperationCanceledException) { sawCancel = true; }
        check("Icon cancellation respected", sawCancel);
        var concurrentCalls = 0;
        var concurrent = new StartupIconCache((_, _) => { Interlocked.Increment(ref concurrentCalls); return Task.FromResult<byte[]?>([1]); });
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => concurrent.GetAsync(run, default)));
        check("Concurrent icon requests extract once", concurrentCalls == 1);
        for (var index = 0; index < 130; index++)
        {
            var path = Path.Combine(fixture.Root, "icon-" + index + ".exe"); File.Copy(fixture.Executable, path);
            await concurrent.GetAsync(run with { ExecutablePath = path, CommandDisplay = "\"" + path + "\"" }, default);
        }
        check("Icon cache bounded to 128 entries", concurrent.Count == 128);
        var currentFolder = (await inventory.RefreshAsync(default)).Inventory.Items.Single(i => i.Id == folder.Id);
        File.SetAttributes(currentFolder.SourceLocation, FileAttributes.ReadOnly);
        try { check("Read-only startup file rejected by preflight", !capability.CanManage(currentFolder)); }
        finally { File.SetAttributes(currentFolder.SourceLocation, FileAttributes.Normal); }
        var disabledSource = taskSnapshot with { Enabled = false };
        ((IStartupTaskControl)tasks.Source).SetEnabled(taskSnapshot, false);
        var disabledTask = TaskSchedulerStartupProvider.Normalize(disabledSource)!;
        for (var index = 0; index < 256; index++) File.WriteAllText(Path.Combine(state.UserRoot, index + ".state"), "synthetic capacity fixture");
        check("Full recovery store hides source-disabled task needing a receipt", !capability.CanManage(disabledTask));
    }
    private static void SetShortcutIcon(string path, string icon)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        try
        {
            dynamic shortcut = shell.CreateShortcut(path);
            try { shortcut.IconLocation = icon; shortcut.Save(); }
            finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut); }
        }
        finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
    }
}
