using Microsoft.Win32;
using SystemCore.Core.Startup;
using SystemCore.Infrastructure.Startup;

internal static class MutationTests
{
    internal static async Task<int> Run()
    {
        var failed = 0; var checks = 0;
        void Check(string name, bool pass) { checks++; if (!pass) failed++; Console.WriteLine($"{(pass ? "PASS" : "FAIL")}: {name}"); }
        using var fixture = new StartupFixture(); fixture.Create();
        var registry = new RegistryStartupProvider(fixture); var folder = new StartupFolderProvider(fixture);
        var native = new MutableTasks(fixture.Executable); var tasks = new TaskSchedulerStartupProvider(native);
        var state = new StartupStateService(fixture, Path.Combine(fixture.Root, "state"), Path.Combine(fixture.Root, "machine-state"));
        var inventory = new StartupInventoryService(registry, folder, tasks, state);
        var mutation = new StartupMutationService(inventory, registry, folder, tasks, state);
        mutation.DiagnosticFailure = e => Console.WriteLine("Fixture diagnostic: " + e);
        async Task<(StartupInventorySession Session, StartupItem Item)> Select(Func<StartupItem, bool> predicate)
        { var snapshot = await inventory.RefreshAsync(default); return (snapshot, snapshot.Inventory.Items.Single(predicate)); }
        Task<StartupOperationResult> Apply((StartupInventorySession Session, StartupItem Item) selected, StartupAction action, bool confirmed = true)
            => mutation.ApplyAsync(new(selected.Session.Id, selected.Item.Id, action, confirmed), default);
        Func<StartupItem, bool> reg = i => i.Name == "SameName" && i.SourceType == StartupSourceType.RegistryRun;
        var selected = await Select(reg);
        Check("No confirmation cannot mutate", (await Apply(selected, StartupAction.Disable, false)).Outcome == StartupOutcome.RefreshRequired && fixture.SourceIntact());
        selected = await Select(reg);
        Check("Unknown ItemId rejected", (await mutation.ApplyAsync(new(selected.Session.Id, new string('A', 64), StartupAction.Disable, true), default)).Outcome == StartupOutcome.RefreshRequired);
        selected = await Select(reg); var old = selected; await inventory.RefreshAsync(default);
        Check("Superseded session rejected", (await Apply(old, StartupAction.Disable)).Outcome == StartupOutcome.RefreshRequired);
        selected = await Select(reg);
        using (var key = Registry.CurrentUser.OpenSubKey(fixture.RegistryRoot + "\\Run", true)) key!.SetValue("SameName", "changed.exe");
        Check("Registry changed since inventory rejected", (await Apply(selected, StartupAction.Disable)).Outcome == StartupOutcome.RefreshRequired);
        var original = "\"" + fixture.Executable + "\" --never-execute";
        using (var key = Registry.CurrentUser.OpenSubKey(fixture.RegistryRoot + "\\Run", true)) key!.SetValue("SameName", original, RegistryValueKind.ExpandString);
        selected = await Select(reg); var id = selected.Item.Id;
        Check("Registry disable succeeds", (await Apply(selected, StartupAction.Disable)).Outcome == StartupOutcome.Success);
        using (var key = Registry.CurrentUser.OpenSubKey(fixture.RegistryRoot + "\\Run")) Check("Exact value removed; sibling retained", key!.GetValue("SameName") is null && key.GetValue("Malformed") is not null);
        Check("Verified Registry backup persists", state.Load(id, false).RegistryBytes is { Length: > 0 });
        selected = await Select(reg);
        Check("Refresh retains disabled Registry item", selected.Item.Status == StartupStatus.DisabledBySystemCore);
        using (var key = Registry.CurrentUser.OpenSubKey(fixture.RegistryRoot + "\\Run", true)) key!.SetValue("SameName", "newer-value");
        Check("Registry restore conflict cannot overwrite", (await Apply(selected, StartupAction.Enable)).Outcome == StartupOutcome.Conflict);
        using (var key = Registry.CurrentUser.OpenSubKey(fixture.RegistryRoot + "\\Run", true))
        { Check("Newer Registry value unchanged", key!.GetValue("SameName") as string == "newer-value"); key.DeleteValue("SameName"); }
        // A new service instance models restart. Prepared/restoring state is retained.
        inventory = new(registry, folder, tasks, state); mutation = new(inventory, registry, folder, tasks, state) { DiagnosticFailure = e => Console.WriteLine("Fixture diagnostic: " + e) };
        selected = await Select(reg);
        Check("Restart exposes recovery state", selected.Item.Status == StartupStatus.RecoveryRequired);
        Check("Registry recovery restore succeeds", (await Apply(selected, StartupAction.Enable)).Outcome == StartupOutcome.Success);
        using (var key = Registry.CurrentUser.OpenSubKey(fixture.RegistryRoot + "\\Run"))
            Check("Exact Registry type and command restored", key!.GetValueKind("SameName") == RegistryValueKind.ExpandString && key.GetValue("SameName", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string == original);
        Check("Successful restore finalizes state", !File.Exists(state.StatePath(id, false)));
        selected = await Select(reg);
        Check("Restored Registry item enabled", selected.Item.Status == StartupStatus.Enabled);
        Check("Repeated Registry disable", (await Apply(selected, StartupAction.Disable)).Outcome == StartupOutcome.Success);
        var statePath = state.StatePath(id, false); var valid = File.ReadAllBytes(statePath); var corrupt = valid.ToArray(); corrupt[^1] ^= 1; File.WriteAllBytes(statePath, corrupt);
        var corruptInventory = await inventory.RefreshAsync(default);
        Check("Tampered protected state is reported incomplete", !corruptInventory.Inventory.Complete && corruptInventory.Inventory.Items.All(i => i.Id != id));
        File.WriteAllBytes(statePath, valid);
        using (state.Lock(false)) { var saved = state.Load(id, false); state.Save(saved with { Phase = StartupStatePhase.Prepared }); }
        selected = await Select(reg);
        Check("Crash after removal before state commit remains recoverable", selected.Item.Status == StartupStatus.RecoveryRequired && (await Apply(selected, StartupAction.Enable)).Outcome == StartupOutcome.Success);

        Func<StartupItem, bool> file = i => i.Name == "fixture.exe" && i.SourceType == StartupSourceType.StartupFolderUser;
        selected = await Select(file); var fileId = selected.Item.Id; var originalPath = selected.Item.SourceLocation; var before = File.ReadAllBytes(originalPath);
        File.AppendAllText(originalPath, "changed");
        Check("File changed since inventory rejected", (await Apply(selected, StartupAction.Disable)).Outcome == StartupOutcome.RefreshRequired && File.Exists(originalPath));
        File.WriteAllBytes(originalPath, before); selected = await Select(file);
        Check("Folder disable moves without deleting content", (await Apply(selected, StartupAction.Disable)).Outcome == StartupOutcome.Success
            && !File.Exists(originalPath) && File.ReadAllBytes(state.DisabledPath(fileId, false)).SequenceEqual(before));
        selected = await Select(file);
        Check("Folder refresh shows disabled state", selected.Item.Status == StartupStatus.DisabledBySystemCore);
        File.WriteAllText(originalPath, "newer file");
        Check("Folder restore conflict refused", (await Apply(selected, StartupAction.Enable)).Outcome == StartupOutcome.Conflict && File.ReadAllText(originalPath) == "newer file");
        File.Delete(originalPath); selected = await Select(file);
        Check("Folder restore succeeds", (await Apply(selected, StartupAction.Enable)).Outcome == StartupOutcome.Success);
        Check("Folder restored byte-for-byte; state finalized", File.ReadAllBytes(originalPath).SequenceEqual(before) && !File.Exists(state.StatePath(fileId, false)));
        selected = await Select(file); Check("Second folder disable", (await Apply(selected, StartupAction.Disable)).Outcome == StartupOutcome.Success);
        var disabled = state.DisabledPath(fileId, false); var backup = File.ReadAllBytes(disabled); File.AppendAllText(disabled, "tamper"); selected = await Select(file);
        Check("Changed disabled file cannot be restored", (await Apply(selected, StartupAction.Enable)).Outcome == StartupOutcome.RecoveryRequired && !File.Exists(originalPath));
        // Preserve identity metadata by restoring only the fixture for teardown; do
        // not relabel this tampered backup as a successful recovery.
        Check("Tampered backup remains present for review", File.Exists(disabled) && File.Exists(state.StatePath(fileId, false)));
        var missingFixture = disabled + ".missing-fixture";
        File.Move(disabled, missingFixture);
        try
        {
            selected = await Select(file);
            Check("Missing disabled backup cannot report restored", (await Apply(selected, StartupAction.Enable)).Outcome == StartupOutcome.RecoveryRequired && !File.Exists(originalPath));
            Check("Missing backup preserves recovery receipt and unrelated bytes", File.Exists(state.StatePath(fileId, false)) && File.ReadAllBytes(missingFixture).Length > backup.Length);
        }
        finally { File.Move(missingFixture, disabled); }

        Func<StartupItem, bool> task = i => i.Name == "Logon";
        selected = await Select(task);
        Check("Task disable uses state control", (await Apply(selected, StartupAction.Disable)).Outcome == StartupOutcome.Success && !native.Get(selected.Item.SourceLocation)!.Enabled);
        selected = await Select(task);
        Check("Task disabled state survives refresh", selected.Item.Status == StartupStatus.DisabledBySystemCore);
        Check("Task enable verifies state and preserves definition", (await Apply(selected, StartupAction.Enable)).Outcome == StartupOutcome.Success && native.Get(selected.Item.SourceLocation)!.Enabled);
        selected = await Select(task); native.ChangeDefinition(selected.Item.SourceLocation);
        Check("Task definition changed before disable rejected", (await Apply(selected, StartupAction.Disable)).Outcome == StartupOutcome.RefreshRequired);
        selected = await Select(i => i.Name == "SystemTask");
        var calls = native.Mutations;
        Check("Protected task cannot reach mutation API", (await Apply(selected, StartupAction.Disable)).Outcome == StartupOutcome.Protected && native.Mutations == calls);
        selected = await Select(i => i.Name == "DisabledTask");
        Check("Source-disabled task can be enabled through supported state", (await Apply(selected, StartupAction.Enable)).Outcome == StartupOutcome.Success);
        selected = await Select(i => i.Name == "Boot"); native.FailReadBack = true;
        Check("Task read-back mismatch leaves recovery receipt", (await Apply(selected, StartupAction.Disable)).Outcome == StartupOutcome.RecoveryRequired);
        native.FailReadBack = false;
        selected = await Select(i => i.Name == "Boot");
        Check("Task recovery receipt can restore enabled state", (await Apply(selected, StartupAction.Enable)).Outcome == StartupOutcome.Success);
        var nonce = new string('A', 64); var request = new StartupHelperRequest(1, nonce, DateTimeOffset.UtcNow, new string('B', 64), new string('C', 64), StartupAction.Disable);
        Check("Typed helper request accepted", StartupElevationProtocol.Validate(request, nonce, DateTimeOffset.UtcNow));
        Check("Unknown helper version rejected", !StartupElevationProtocol.Validate(request with { Version = 2 }, nonce, DateTimeOffset.UtcNow));
        Check("Helper nonce mismatch rejected", !StartupElevationProtocol.Validate(request, new string('D', 64), DateTimeOffset.UtcNow));
        Check("Expired helper request rejected", !StartupElevationProtocol.Validate(request with { CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5) }, nonce, DateTimeOffset.UtcNow));
        foreach (var injection in new[] { "..\\Run", "\\\\server\\item", "C:\\Windows\\system32", "\\Microsoft\\Windows\\Task", "HKLM\\Software" })
            Check("Arbitrary source injection cannot be an ItemId", !StartupElevationProtocol.Validate(request with { ItemId = injection }, nonce, DateTimeOffset.UtcNow));
        var rejected = false;
        try { await StartupElevationProtocol.ReadAsync<StartupHelperRequest>(new MemoryStream(BitConverter.GetBytes(4097)), default); } catch (IOException) { rejected = true; }
        Check("Helper oversized frame rejected before allocation", rejected);
        rejected = false;
        try { await StartupElevationProtocol.WriteAsync(new MemoryStream(), new string('x', 4097), default); } catch (IOException) { rejected = true; }
        Check("Helper oversized serialization rejected", rejected);
        using (var injected = new MemoryStream())
        {
            await StartupElevationProtocol.WriteAsync(injected, new { request.Version, request.Nonce, request.CreatedAt, request.ItemId, request.Fingerprint, request.Action, RegistryPath = "HKLM\\Software\\Arbitrary" }, default);
            injected.Position = 0; rejected = false;
            try { await StartupElevationProtocol.ReadAsync<StartupHelperRequest>(injected, default); } catch (System.Text.Json.JsonException) { rejected = true; }
            Check("Helper rejects injected address fields", rejected);
        }
        Check("Helper invalid endpoint args rejected", await StartupHelperHost.RunAsync(["--startup", "arbitrary", "1", nonce]) == 64);
        using (var user = System.Security.Principal.WindowsIdentity.GetCurrent())
        {
            var sid = user.User!.Value;
            Check("Task user-write ACL avoids unnecessary UAC", StartupTaskAccess.CanWrite($"O:{sid}G:{sid}D:(A;;FA;;;{sid})", (stage, error) => Console.WriteLine($"Access fixture: {stage}/{error}")) == true);
            Check("Task read-only ACL requires elevation", StartupTaskAccess.CanWrite($"O:SYG:SYD:(A;;FR;;;{sid})(A;;FA;;;SY)", (stage, error) => Console.WriteLine($"Access fixture: {stage}/{error}")) == false);
            Check("Unknown task ACL fails closed", StartupTaskAccess.CanWrite("invalid") is null);
            var highest = native.Get("\\SystemCoreTest\\Logon")!;
            highest = highest with { Xml = highest.Xml.Replace("LeastPrivilege", "HighestAvailable"), SecurityDescriptor = $"O:{sid}G:{sid}D:(A;;FA;;;{sid})" };
            Check("Task RunLevel does not invent mutation permission", TaskSchedulerStartupProvider.Normalize(highest) is { RequiresElevation: false, IsProtected: false });
            var without = highest with { Xml = "<Task xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\"><Actions/></Task>" };
            var withEnabled = without with { Xml = without.Xml.Replace("<Actions/>", "<Settings><Enabled>false</Enabled></Settings><Actions/>") };
            Check("Task compare tolerates only an added Enabled setting", StartupMutationService.SameTaskDefinition(without, withEnabled));
            Check("Task compare retains other setting changes", !StartupMutationService.SameTaskDefinition(without, withEnabled with { Xml = withEnabled.Xml.Replace("</Settings>", "<Hidden>true</Hidden></Settings>") }));
        }
        Check("Helper arbitrary operation rejected", !StartupElevationProtocol.Validate(request with { Action = (StartupAction)99 }, nonce, DateTimeOffset.UtcNow));
        selected = await Select(i => i.Name == "Logon");
        Check("Changed-definition task fresh disable", (await Apply(selected, StartupAction.Disable)).Outcome == StartupOutcome.Success);
        native.ChangeDefinition(selected.Item.SourceLocation); selected = await Select(i => i.Name == "Logon");
        Check("Task restore definition conflict refused", (await Apply(selected, StartupAction.Enable)).Outcome == StartupOutcome.Conflict && !native.Get(selected.Item.SourceLocation)!.Enabled);
        selected = await Select(i => i.Name == "DisabledTask"); native.ChangeSecurity(selected.Item.SourceLocation);
        Check("Task ACL changed since inventory rejected", (await Apply(selected, StartupAction.Disable)).Outcome == StartupOutcome.RefreshRequired);
        selected = await Select(reg);
        Check("Prepare further state-security fixture", (await Apply(selected, StartupAction.Disable)).Outcome == StartupOutcome.Success);
        var trusted = state.Load(id, false);
        using (state.Lock(false))
        {
            rejected = false;
            try { state.Save(trusted with { Version = 77 }); } catch (IOException) { rejected = true; }
            Check("Unsupported stored schema rejected", rejected);
            rejected = false;
            try { state.Save(trusted with { Registry = trusted.Registry! with { Location = trusted.Registry.Location with { KeyPath = "Software\\Arbitrary" } } }); } catch (IOException) { rejected = true; }
            Check("State cannot inject non-allowlisted Registry root", rejected);
            rejected = false;
            try { state.Save(trusted with { Item = trusted.Item with { Id = new string('F', 64) } }); } catch (IOException) { rejected = true; }
            Check("State ItemId/source mismatch rejected", rejected);
        }
        selected = await Select(reg); Check("Security fixture Registry restore", (await Apply(selected, StartupAction.Enable)).Outcome == StartupOutcome.Success);
        using (state.Lock(false)) state.Save(trusted with { Phase = StartupStatePhase.Prepared });
        selected = await Select(reg);
        Check("Crash before Registry removal finalizes exact unchanged source", (await Apply(selected, StartupAction.Enable)).Outcome == StartupOutcome.Success && !File.Exists(state.StatePath(id, false)));
        var alternateRoot = Path.Combine(fixture.Root, "unsafe-state"); var targetRoot = Path.Combine(fixture.Root, "outside-state"); Directory.CreateDirectory(targetRoot);
        JunctionFixture.Create(alternateRoot, targetRoot);
        try
        {
            var unsafeState = new StartupStateService(fixture, alternateRoot, Path.Combine(fixture.Root, "unused-machine"));
            var unsafeInventory = new StartupInventoryService(registry, folder, tasks, unsafeState);
            var unsafeMutation = new StartupMutationService(unsafeInventory, registry, folder, tasks, unsafeState);
            var session = await unsafeInventory.RefreshAsync(default); var entry = session.Inventory.Items.Single(reg);
            Check("Reparse state root cannot receive backup or mutate source", (await unsafeMutation.ApplyAsync(new(session.Id, entry.Id, StartupAction.Disable, true), default)).Outcome == StartupOutcome.StateInvalid && Directory.GetFiles(targetRoot).Length == 0);
        }
        finally { Directory.Delete(alternateRoot); }
        selected = await Select(i => i.SourceType == StartupSourceType.RegistryRunOnce);
        Check("RunOnce disable remains separate and reversible", (await Apply(selected, StartupAction.Disable)).Outcome == StartupOutcome.Success);
        selected = await Select(i => i.SourceType == StartupSourceType.RegistryRunOnce);
        Check("RunOnce exact restore", (await Apply(selected, StartupAction.Enable)).Outcome == StartupOutcome.Success);
        Check("Unauthenticated helper caller rejected", await StartupHelperHost.RunAsync(["--startup", "SystemCore-Startup-" + Guid.NewGuid().ToString("N"), Environment.ProcessId.ToString(), nonce]) == 5);
        var failedStore = new StartupStateService(fixture, Path.Combine(fixture.Root, "failed-state"), Path.Combine(fixture.Root, "failed-machine"));
        using (failedStore.Lock(false)) Directory.CreateDirectory(failedStore.StatePath(id, false));
        var failedInventory = new StartupInventoryService(registry, folder, tasks, failedStore);
        var failedMutation = new StartupMutationService(failedInventory, registry, folder, tasks, failedStore);
        var failedSession = await failedInventory.RefreshAsync(default);
        var failedResult = await failedMutation.ApplyAsync(new(failedSession.Id, id, StartupAction.Disable, true), default);
        using (var key = Registry.CurrentUser.OpenSubKey(fixture.RegistryRoot + "\\Run"))
            Check("Failed atomic backup prevents source mutation", failedResult.Outcome is StartupOutcome.AccessDenied or StartupOutcome.StateInvalid
                && key!.GetValue("SameName", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string == original);
        Check("Failed pending backup cleaned by its exact handle", Directory.GetFiles(failedStore.UserRoot, "*.pending").Length == 0);
        using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
        {
            if (!new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
            {
                selected = await Select(i => i.Name == "fixture.exe" && i.SourceType == StartupSourceType.StartupFolderCommon);
                Check("Standard caller cannot bypass Common Startup elevation", (await Apply(selected, StartupAction.Disable)).Outcome == StartupOutcome.AccessDenied && File.Exists(selected.Item.SourceLocation));
            }
        }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); selected = await Select(reg);
        var cancelResult = await mutation.ApplyAsync(new(selected.Session.Id, selected.Item.Id, StartupAction.Disable, true), cancelled.Token);
        using (var key = Registry.CurrentUser.OpenSubKey(fixture.RegistryRoot + "\\Run"))
            Check("Cancellation before dispatch changes no source", cancelResult.Outcome == StartupOutcome.Cancelled && key!.GetValue("SameName", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string == original);
        Console.WriteLine($"Mutation checks: {checks}; failed: {failed}");
        return failed == 0 ? 0 : 1;
    }
}
internal sealed class MutableTasks : IStartupTaskControl
{
    private readonly Dictionary<string, StartupTaskSnapshot> tasks;
    internal int Mutations { get; private set; }
    internal bool FailReadBack { get; set; }
    internal MutableTasks(string executable) => tasks = new FixtureTasks(executable).Read(default).Tasks.ToDictionary(t => t.Path);
    public (IReadOnlyList<StartupTaskSnapshot> Tasks, bool Complete) Read(CancellationToken token) => (tasks.Values.ToArray(), true);
    public StartupTaskSnapshot? Get(string path) => tasks.GetValueOrDefault(path);
    public void SetEnabled(StartupTaskSnapshot expected, bool enabled)
    {
        if (tasks[expected.Path] != expected) throw new IOException("Changed task");
        Mutations++; if (!FailReadBack) tasks[expected.Path] = expected with { Enabled = enabled };
    }
    internal void ChangeDefinition(string path) => tasks[path] = tasks[path] with { Xml = tasks[path].Xml.Replace("</Exec>", "<Arguments>changed</Arguments></Exec>") };
    internal void ChangeSecurity(string path)
    {
        using var user = System.Security.Principal.WindowsIdentity.GetCurrent(); var sid = user.User!.Value;
        tasks[path] = tasks[path] with { SecurityDescriptor = $"O:{sid}G:{sid}D:(A;;FA;;;{sid})" };
    }
}
