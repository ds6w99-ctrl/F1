#if STARTUP_VALIDATION
using Microsoft.Win32;
using System.Security.Principal;
using System.Text.Json;
using SystemCore.Core.Startup;

namespace SystemCore.Infrastructure.Startup;

// Compiled only into a separate validation publication. Production contains no
// fixture selector, environment-variable override or alternative source roots.
public sealed class StartupValidationEnvironment : IDisposable
{
    private readonly Locations locations = new();
    private readonly ValidationTasks taskSource;
    public IRegistryStartupProvider Registry { get; }
    public IStartupFolderProvider Folders { get; }
    public ITaskSchedulerStartupProvider Tasks { get; }
    public StartupStateService State { get; }
    public StartupInventoryService Inventory { get; }
    public IStartupMutationService Mutation { get; }
    public StartupValidationEnvironment()
    {
        Directory.CreateDirectory(locations.Root); Directory.CreateDirectory(locations.Folders[0].Path);
        var target = Path.Combine(locations.Root, "display-only.exe");
        File.Copy(Path.Combine(AppContext.BaseDirectory, Core.ProductIdentity.AppExecutableName), target);
        // These paths are not Windows Startup folders or Run registration keys.
        File.Copy(target, Path.Combine(locations.Folders[0].Path, "SystemCore Synthetic Folder.exe"));
        using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(locations.Registry[0].KeyPath))
            key.SetValue("SystemCore Synthetic Registry", "\"" + target + "\" --display-only", RegistryValueKind.ExpandString);
        taskSource = new(target);
        Registry = new RegistryStartupProvider(locations); Folders = new StartupFolderProvider(locations); Tasks = new TaskSchedulerStartupProvider(taskSource);
        State = new(locations, Path.Combine(locations.Root, "state"), Path.Combine(locations.Root, "machine-state"));
        Inventory = new(Registry, Folders, Tasks, State);
        Mutation = new AuditMutation(new StartupMutationService(Inventory, Registry, Folders, Tasks, State), this);
        File.WriteAllText(Path.Combine(locations.Root, "started.json"), JsonSerializer.Serialize(new { Root = locations.Root, Registry = locations.Registry[0].KeyPath }));
    }
    private readonly List<object> audit = [];
    private sealed class AuditMutation(IStartupMutationService inner, StartupValidationEnvironment fixture) : IStartupMutationService
    {
        public async Task<StartupOperationResult> ApplyAsync(StartupOperationRequest request, CancellationToken token)
        {
            var result = await inner.ApplyAsync(request, token);
            var snapshot = await fixture.Inventory.RefreshAsync(default);
            var item = snapshot.Inventory.Items.SingleOrDefault(i => i.Id == request.ItemId);
            fixture.audit.Add(new { request.Action, result.Outcome, ItemStatus = item?.Status, StateExists = File.Exists(fixture.State.StatePath(request.ItemId, false)) });
            File.WriteAllText(Path.Combine(fixture.locations.Root, "audit.json"), JsonSerializer.Serialize(fixture.audit));
            return result;
        }
    }
    public void Dispose()
    {
        Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(locations.Registry[0].KeyPath, false);
        File.WriteAllText(Path.Combine(locations.Root, "completed.json"), JsonSerializer.Serialize(new { Operations = audit.Count, TaskEnabled = taskSource.snapshot.Enabled,
            FolderRestored = File.Exists(Path.Combine(locations.Folders[0].Path, "SystemCore Synthetic Folder.exe")), RemainingStates = Directory.Exists(State.UserRoot) ? Directory.GetFiles(State.UserRoot, "*.state").Length : 0 }));
        // Non-startup files and audit are deliberately retained for evidence.
    }
    private sealed class Locations : IStartupLocations
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "SystemCore-Startup-UI-" + Guid.NewGuid().ToString("N"));
        public IReadOnlyList<StartupRegistryLocation> Registry { get; } = [new(RegistryHive.CurrentUser, RegistryView.Registry64,
            "Software\\SystemCore-Startup-UI-" + Guid.NewGuid().ToString("N"), StartupSourceType.RegistryRun)];
        public IReadOnlyList<StartupFolderLocation> Folders => [new(Path.Combine(Root, "SyntheticFolder"), StartupScope.CurrentUser)];
    }
    private sealed class ValidationTasks : IStartupTaskControl
    {
        internal StartupTaskSnapshot snapshot;
        internal ValidationTasks(string target)
        {
            using var identity = WindowsIdentity.GetCurrent();
            snapshot = new("\\SystemCoreValidation\\SystemCore Synthetic Task", $"<Task xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\"><Triggers><LogonTrigger/></Triggers><Principals><Principal><UserId>{identity.User!.Value}</UserId></Principal></Principals><Actions><Exec><Command>{System.Security.SecurityElement.Escape(target)}</Command></Exec></Actions></Task>", true);
        }
        public (IReadOnlyList<StartupTaskSnapshot> Tasks, bool Complete) Read(CancellationToken token) => ([snapshot], true);
        public StartupTaskSnapshot? Get(string path) => path == snapshot.Path ? snapshot : null;
        public void SetEnabled(StartupTaskSnapshot expected, bool enabled)
        { if (snapshot != expected) throw new IOException("Fixture task changed"); snapshot = snapshot with { Enabled = enabled }; }
    }
}
#endif
