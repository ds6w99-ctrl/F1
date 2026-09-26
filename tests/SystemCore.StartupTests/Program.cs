using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32;
using SystemCore.Core.Startup;
using SystemCore.Infrastructure.Startup;

if (args.Length == 2 && args[0] == "--lifecycle") return await StartupLifecycle.Run(args[1]);
if (args.Contains("--mutation") && !args.Contains("--machine-fixture")) return await MutationTests.Run();
if (args.Contains("--machine-fixture"))
{
    var keyPath = "Software\\SystemCore-Startup-Test-" + Guid.NewGuid().ToString("N");
    var stateRoot = Path.Combine(Path.GetTempPath(), "SystemCore-Startup-Machine-Test-" + Guid.NewGuid().ToString("N"));
    try
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = root.CreateSubKey(keyPath);
            key.SetValue("SameName", "\"" + Path.Combine(AppContext.BaseDirectory, "SystemCore.StartupTests.exe") + "\"");
        }
        var result = await new RegistryStartupProvider(new MachineLocations(keyPath)).ReadAsync(default);
        if (!result.Complete || result.Items.Count != 2 || result.Items.Select(i => i.Id).Distinct().Count() != 2
            || result.Items.Any(i => !i.RequiresElevation || i.Scope != StartupScope.AllUsers)) return 1;
        Console.WriteLine("PASS: isolated HKLM 32/64-bit inventory, distinct identities and elevation boundary");
        if (args.Contains("--mutation"))
        {
            var common = Path.Combine(stateRoot, "common"); Directory.CreateDirectory(common);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "SystemCore.StartupTests.exe"), Path.Combine(common, "machine-fixture.exe"));
            var locations = new MachineLocations(keyPath, common); var registry = new RegistryStartupProvider(locations); var folders = new StartupFolderProvider(locations);
            var tasks = new TaskSchedulerStartupProvider(new FixtureTasks(Path.Combine(AppContext.BaseDirectory, "SystemCore.StartupTests.exe")));
            Directory.CreateDirectory(stateRoot);
            var state = new StartupStateService(locations, Path.Combine(stateRoot, "user"), Path.Combine(stateRoot, "machine"));
            var inventory = new StartupInventoryService(registry, folders, tasks, state); var mutation = new StartupMutationService(inventory, registry, folders, tasks, state) { DiagnosticFailure = e => Console.WriteLine(e) };
            var machineItems = (await inventory.RefreshAsync(default)).Inventory.Items.Where(i => i.RequiresElevation && !i.IsProtected).ToArray();
            if (machineItems.Length != 3) return 1;
            foreach (var row in machineItems)
            {
                var snapshot = await inventory.RefreshAsync(default);
                if ((await mutation.ApplyAsync(new(snapshot.Id, row.Id, StartupAction.Disable, true), default)).Outcome != StartupOutcome.Success) return 1;
                StartupStateService.ValidateMachineAcl(state.MachineRoot);
                snapshot = await inventory.RefreshAsync(default);
                if (snapshot.Inventory.Items.Single(i => i.Id == row.Id).Status != StartupStatus.DisabledBySystemCore) return 1;
                if ((await mutation.ApplyAsync(new(snapshot.Id, row.Id, StartupAction.Enable, true), default)).Outcome != StartupOutcome.Success) return 1;
            }
            Console.WriteLine("PASS: both isolated HKLM views and Common Startup fixture disable/restore; machine state ACL verified");
        }
        return 0;
    }
    finally
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        { using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view); root.DeleteSubKeyTree(keyPath, false); }
        if (Directory.Exists(stateRoot)) Directory.Delete(stateRoot, true);
    }
}
if (args.Contains("--inventory"))
{
    var clock = Stopwatch.StartNew();
    var service = new StartupInventoryService(new RegistryStartupProvider(), new StartupFolderProvider(), new TaskSchedulerStartupProvider(), new StartupStateService(), new WindowsElevatedStartupClient());
    var inventory = await service.RefreshAsync(default);
    var visible = inventory.Inventory.Items.Where(ManageableStartupItems.IsVisible).ToArray();
    Console.WriteLine($"Raw startup inventory: {inventory.Inventory.Items.Count}\nProtected/non-manageable hidden: {inventory.Inventory.Items.Count - visible.Length}\nVisible manageable items: {visible.Length}\nRegistry: {visible.Count(i => i.SourceType is StartupSourceType.RegistryRun or StartupSourceType.RegistryRunOnce)}\nStartup folders: {visible.Count(i => i.SourceType is StartupSourceType.StartupFolderUser or StartupSourceType.StartupFolderCommon)}\nScheduled tasks: {visible.Count(i => i.SourceType is StartupSourceType.ScheduledTaskLogon or StartupSourceType.ScheduledTaskBoot)}");
    var icons = new StartupIconCache(); var iconSources = new Dictionary<string, int>();
    foreach (var item in visible) { var icon = await icons.GetAsync(item, default); iconSources[icon.Source] = iconSources.GetValueOrDefault(icon.Source) + 1; }
    Console.WriteLine(JsonSerializer.Serialize(new { IconSources = iconSources }));
    Console.WriteLine(JsonSerializer.Serialize(new { inventory.Inventory.Complete, Milliseconds = clock.Elapsed.TotalMilliseconds,
        Total = inventory.Inventory.Items.Count, Sources = inventory.Inventory.Items.GroupBy(i => i.SourceType).ToDictionary(g => g.Key.ToString(), g => g.Count()),
        Protected = inventory.Inventory.Items.Count(i => i.IsProtected), Enabled = inventory.Inventory.Items.Count(i => i.Status == StartupStatus.Enabled),
        Disabled = inventory.Inventory.Items.Count(i => i.Status is StartupStatus.DisabledBySystemCore or StartupStatus.DisabledByWindowsOrSource),
        Unknown = inventory.Inventory.Items.Count(i => i.Status == StartupStatus.Unknown), inventory.Inventory.UnavailableSources }, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
var checks = new List<(string Name, bool Pass)>();
void Check(string name, bool value) { checks.Add((name, value)); Console.WriteLine($"{(value ? "PASS" : "FAIL")}: {name}"); }
using (var fixture = new StartupFixture())
{
    fixture.Create();
    var registry = new RegistryStartupProvider(fixture);
    var folder = new StartupFolderProvider(fixture);
    var taskSource = new FixtureTasks(fixture.Executable);
    var tasks = new TaskSchedulerStartupProvider(taskSource);
    var service = new StartupInventoryService(registry, folder, tasks,
        new StartupStateService(fixture, Path.Combine(fixture.Root, "state"), Path.Combine(fixture.Root, "machine")));
    var snapshot = await service.RefreshAsync(default);
    Check("Complete isolated inventory", snapshot.Inventory.Complete);
    Check("HKCU Run and RunOnce discovered", snapshot.Inventory.Items.Any(i => i.SourceType == StartupSourceType.RegistryRun) && snapshot.Inventory.Items.Any(i => i.SourceType == StartupSourceType.RegistryRunOnce));
    Check("Registry views fold only exact HKCU aliases", snapshot.Inventory.Items.Count(i => i.Name == "SameName" && i.SourceType == StartupSourceType.RegistryRun) == 1);
    Check("RunOnce separate identity", snapshot.Inventory.Items.Where(i => i.Name == "SameName").Select(i => i.Id).Distinct().Count() == 2);
    Check("Malformed command retained protected", snapshot.Inventory.Items.Single(i => i.Name == "Malformed").IsProtected);
    Check("Non-string Registry source retained protected", snapshot.Inventory.Items.Single(i => i.Name == "WrongKind").ProtectionReason == StartupProtection.UnsupportedSource);
    Check("User and common synthetic folders", snapshot.Inventory.Items.Any(i => i.SourceType == StartupSourceType.StartupFolderUser) && snapshot.Inventory.Items.Any(i => i.SourceType == StartupSourceType.StartupFolderCommon));
    Check("Unknown file remains read-only", snapshot.Inventory.Items.Single(i => i.Name == "read-only.cmd").IsProtected);
    Check("Executable parsed without execution", snapshot.Inventory.Items.Where(i => i.Name == "fixture.exe").All(i => i.ExecutablePath is not null));
    Check("Task with nonstartup trigger omitted", snapshot.Inventory.Items.All(i => i.Name != "DailyOnly"));
    Check("Logon and boot detected", snapshot.Inventory.Items.Any(i => i.SourceType == StartupSourceType.ScheduledTaskLogon) && snapshot.Inventory.Items.Any(i => i.SourceType == StartupSourceType.ScheduledTaskBoot));
    Check("Disabled task state reported", snapshot.Inventory.Items.Single(i => i.Name == "DisabledTask").Status == StartupStatus.DisabledByWindowsOrSource);
    Check("System task read-only", snapshot.Inventory.Items.Single(i => i.Name == "SystemTask").IsProtected);
    Check("Multiple startup triggers produce one task identity", snapshot.Inventory.Items.Count(i => i.Name == "Multiple") == 1);
    Check("Unrelated triggers protect a mixed task", snapshot.Inventory.Items.Single(i => i.Name == "Mixed").IsProtected);
    Check("Impact never fabricated", snapshot.Inventory.Items.All(i => i.Impact == "NotMeasured"));
    Check("Dashboard summary counts only manageable items", service.Summary?.Total == snapshot.Inventory.Items.Count(ManageableStartupItems.IsVisible));
    Check("Read-only task provider cannot advertise a mutation path", !snapshot.Inventory.Items.Where(i => i.SourceType is StartupSourceType.ScheduledTaskLogon or StartupSourceType.ScheduledTaskBoot).Any(ManageableStartupItems.IsVisible));
    var refresh = await service.RefreshAsync(default);
    Check("Stable source identities across refresh", snapshot.Inventory.Items.Select(i => i.Id).SequenceEqual(refresh.Inventory.Items.Select(i => i.Id)));
    Check("Refresh creates new session", snapshot.Id != refresh.Id);
    Check("Inventory did not modify synthetic sources", fixture.SourceIntact());
    Check("Shortcut target read without resolving/executing", snapshot.Inventory.Items.Single(i => i.Name == "fixture.lnk").ExecutablePath == fixture.Executable);
    var link = Path.Combine(fixture.Root, "reparse-startup");
    JunctionFixture.Create(link, fixture.Folders[0].Path);
    try
    {
        var unsafeLocations = new FolderOnlyLocations(link);
        var blocked = await new StartupFolderProvider(unsafeLocations).ReadAsync(default);
        Check("Actual reparse Startup root is rejected", !blocked.Complete && blocked.Items.Count == 0);
    }
    finally { Directory.Delete(link); }
    foreach (var command in new[] { "", "cmd.exe /c x", "powershell.exe -c x", "relative.exe", "\\\\server\\x.exe", "C:\\x.exe | y", "C:\\x.exe ^ y", "\"broken" })
        Check("Ambiguous command cannot become executable", StartupCommandParser.Read(command).Executable is null);
    Check("Quoted executable supported", StartupCommandParser.Read("\"" + fixture.Executable + "\" --argument").Executable == fixture.Executable);
    var variable = "SYSTEMCORE_STARTUP_TEST_" + Guid.NewGuid().ToString("N");
    try { Environment.SetEnvironmentVariable(variable, fixture.Executable); Check("Environment expansion is display-only", StartupCommandParser.Read("\"%" + variable + "%\"").Executable == fixture.Executable); }
    finally { Environment.SetEnvironmentVariable(variable, null); }
    var identity = new StartupIdentityResolver();
    Check("Same names different hives not merged", identity.Resolve("Registry:CurrentUser:Registry64:Run:App") != identity.Resolve("Registry:LocalMachine:Registry64:Run:App"));
    Check("Machine views distinct", identity.Resolve("Registry:LocalMachine:Registry64:Run:App") != identity.Resolve("Registry:LocalMachine:Registry32:Run:App"));
    Check("Task path traversal rejected", !TaskSchedulerStartupProvider.SafeTaskPath("\\test\\..\\Windows"));
    Check("Task device/path injection rejected", !TaskSchedulerStartupProvider.SafeTaskPath("\\\\device\\item") && !TaskSchedulerStartupProvider.SafeTaskPath("C:\\task"));
    using var cancel = new CancellationTokenSource(); cancel.Cancel(); var cancelled = false;
    try { await service.RefreshAsync(cancel.Token); } catch (OperationCanceledException) { cancelled = true; }
    Check("Inventory cancellation respected", cancelled);
}
await Fix2Tests.Run(Check);
Console.WriteLine(JsonSerializer.Serialize(new { Checks = checks.Count, Failed = checks.Count(c => !c.Pass), Scope = "Real-device inventory is read-only; mutations use isolated synthetic fixtures only" }));
return checks.All(c => c.Pass) ? 0 : 1;

internal sealed class StartupFixture : IStartupLocations, IDisposable
{
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "SystemCore-Startup-" + Guid.NewGuid().ToString("N"));
    internal string RegistryRoot { get; } = "Software\\SystemCore-Startup-Test-" + Guid.NewGuid().ToString("N");
    internal string Executable => Path.Combine(Root, "display-target.exe");
    public IReadOnlyList<StartupRegistryLocation> Registry => [new(RegistryHive.CurrentUser, RegistryView.Registry64, RegistryRoot + "\\Run", StartupSourceType.RegistryRun),
        new(RegistryHive.CurrentUser, RegistryView.Registry32, RegistryRoot + "\\Run", StartupSourceType.RegistryRun), new(RegistryHive.CurrentUser, RegistryView.Registry64, RegistryRoot + "\\RunOnce", StartupSourceType.RegistryRunOnce)];
    public IReadOnlyList<StartupFolderLocation> Folders => [new(Path.Combine(Root, "UserStartup"), StartupScope.CurrentUser), new(Path.Combine(Root, "CommonStartup"), StartupScope.AllUsers)];
    public void Create()
    {
        Directory.CreateDirectory(Root);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "SystemCore.StartupTests.exe"), Executable);
        using var user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        foreach (var type in new[] { "Run", "RunOnce" })
        { using var key = user.CreateSubKey(RegistryRoot + "\\" + type); key.SetValue("SameName", "\"" + Executable + "\" --never-execute", RegistryValueKind.ExpandString); }
        using (var key = user.OpenSubKey(RegistryRoot + "\\Run", true)) { key!.SetValue("Malformed", "cmd.exe /c forbidden"); key.SetValue("WrongKind", 123); }
        foreach (var folder in Folders) { Directory.CreateDirectory(folder.Path); File.Copy(Executable, Path.Combine(folder.Path, "fixture.exe")); }
        File.WriteAllText(Path.Combine(Folders[0].Path, "read-only.cmd"), "synthetic inert text");
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        try
        {
            dynamic shortcut = shell.CreateShortcut(Path.Combine(Folders[0].Path, "fixture.lnk"));
            try { shortcut.TargetPath = Executable; shortcut.Arguments = "--never-execute"; shortcut.Save(); }
            finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut); }
        }
        finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
    }
    public bool SourceIntact()
    {
        using var user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        using var key = user.OpenSubKey(RegistryRoot + "\\Run");
        return key?.GetValue("SameName", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string == "\"" + Executable + "\" --never-execute"
            && Folders.All(f => File.Exists(Path.Combine(f.Path, "fixture.exe")));
    }
    public void Dispose()
    {
        using var user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64); user.DeleteSubKeyTree(RegistryRoot, false);
        if (Directory.Exists(Root)) Directory.Delete(Root, true);
    }
}
internal sealed class FolderOnlyLocations(string path) : IStartupLocations
{
    public IReadOnlyList<StartupRegistryLocation> Registry => [];
    public IReadOnlyList<StartupFolderLocation> Folders => [new(path, StartupScope.CurrentUser)];
}
internal sealed class MachineLocations(string key, string? folder = null) : IStartupLocations
{
    public IReadOnlyList<StartupRegistryLocation> Registry => [new(RegistryHive.LocalMachine, RegistryView.Registry64, key, StartupSourceType.RegistryRun),
        new(RegistryHive.LocalMachine, RegistryView.Registry32, key, StartupSourceType.RegistryRun)];
    public IReadOnlyList<StartupFolderLocation> Folders => folder is null ? [] : [new(folder, StartupScope.AllUsers)];
}
internal sealed class FixtureTasks(string executable) : IStartupTaskSource
{
    public (IReadOnlyList<StartupTaskSnapshot> Tasks, bool Complete) Read(CancellationToken token)
    {
        using var identity = WindowsIdentity.GetCurrent(); var sid = identity.User!.Value;
        string Xml(string triggers) => $"<Task xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\"><Triggers>{triggers}</Triggers><Principals><Principal><UserId>{sid}</UserId><RunLevel>LeastPrivilege</RunLevel></Principal></Principals><Actions><Exec><Command>{System.Security.SecurityElement.Escape(executable)}</Command></Exec></Actions></Task>";
        return ([new("\\SystemCoreTest\\Logon", Xml("<LogonTrigger/>"), true), new("\\SystemCoreTest\\Boot", Xml("<BootTrigger/>"), true),
            new("\\SystemCoreTest\\DisabledTask", Xml("<LogonTrigger/>"), false), new("\\Microsoft\\Windows\\SystemTask", Xml("<LogonTrigger/>"), true),
            new("\\SystemCoreTest\\Multiple", Xml("<LogonTrigger/><BootTrigger/>"), true), new("\\SystemCoreTest\\DailyOnly", Xml("<CalendarTrigger/>"), true),
            new("\\SystemCoreTest\\Mixed", Xml("<LogonTrigger/><CalendarTrigger/>"), true)], true);
    }
}
