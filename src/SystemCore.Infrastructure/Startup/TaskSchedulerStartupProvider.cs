using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Xml;
using System.Xml.Linq;
using SystemCore.Core.Startup;

namespace SystemCore.Infrastructure.Startup;

internal sealed record StartupTaskSnapshot(string Path, string Xml, bool Enabled, string? SecurityDescriptor = null);
internal interface IStartupTaskSource { (IReadOnlyList<StartupTaskSnapshot> Tasks, bool Complete) Read(CancellationToken token); }
internal interface IStartupTaskControl : IStartupTaskSource
{
    StartupTaskSnapshot? Get(string path);
    void SetEnabled(StartupTaskSnapshot expected, bool enabled);
}
internal sealed class WindowsStartupTaskSource : IStartupTaskControl
{
    public StartupTaskSnapshot? Get(string path) => WithTask(path, task => new StartupTaskSnapshot((string)task.Path, (string)task.Xml, (bool)task.Enabled, ReadSecurity(task)));
    public void SetEnabled(StartupTaskSnapshot expected, bool enabled) => WithTask(expected.Path, task =>
    {
        if ((string)task.Xml != expected.Xml || (bool)task.Enabled != expected.Enabled || ReadSecurity(task) != expected.SecurityDescriptor) throw new IOException("Task changed");
        task.Enabled = enabled;
        if ((bool)task.Enabled != enabled) throw new IOException("Task read-back failed");
        return true;
    });
    private static string ReadSecurity(dynamic task)
    {
        try { var descriptor = (string)task.GetSecurityDescriptor(7); return descriptor.Length <= 65536 ? descriptor : ""; }
        catch (COMException) { return ""; }
    }
    private static T WithTask<T>(string path, Func<dynamic, T> action)
    {
        if (!TaskSchedulerStartupProvider.SafeTaskPath(path) || path.StartsWith("\\Microsoft\\", StringComparison.OrdinalIgnoreCase)) throw new IOException("Protected task path");
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        try
        {
            service.Connect(); dynamic folder = service.GetFolder("\\");
            try { dynamic task = folder.GetTask(path); try { return action(task); } finally { Marshal.FinalReleaseComObject(task); } }
            finally { Marshal.FinalReleaseComObject(folder); }
        }
        finally { Marshal.FinalReleaseComObject(service); }
    }
    public (IReadOnlyList<StartupTaskSnapshot> Tasks, bool Complete) Read(CancellationToken token)
    {
        var tasks = new List<StartupTaskSnapshot>(); var complete = true; var visited = 0; var folders = 0; var retainedCharacters = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        try
        {
            service.Connect(); dynamic root = service.GetFolder("\\");
            try { Walk(root, 0); } finally { Marshal.FinalReleaseComObject(root); }
        }
        finally { Marshal.FinalReleaseComObject(service); }
        return (tasks, complete);
        void Walk(dynamic folder, int depth)
        {
            token.ThrowIfCancellationRequested();
            if (depth > 16 || visited >= 10000 || ++folders > 1024 || clock.Elapsed > TimeSpan.FromSeconds(20)) { complete = false; return; }
            try
            {
                dynamic collection = folder.GetTasks(1);
                try
                {
                    int count = collection.Count;
                    for (var index = 1; index <= count; index++)
                    {
                        token.ThrowIfCancellationRequested();
                        if (++visited > 10000 || clock.Elapsed > TimeSpan.FromSeconds(20)) { complete = false; break; }
                        dynamic task = collection.Item(index);
                        try
                        {
                            var xml = (string)task.Xml;
                            if (xml.Length > 1024 * 1024) { complete = false; continue; }
                            // Only a prefilter; Normalize validates actual namespace/triggers.
                            if (!xml.Contains("LogonTrigger", StringComparison.Ordinal) && !xml.Contains("BootTrigger", StringComparison.Ordinal)) continue;
                            var security = ReadSecurity(task);
                            if (tasks.Count >= 2000 || retainedCharacters + xml.Length + security.Length > 4 * 1024 * 1024) { complete = false; break; }
                            retainedCharacters += xml.Length + security.Length;
                            tasks.Add(new((string)task.Path, xml, (bool)task.Enabled, security));
                        }
                        catch (COMException) { complete = false; }
                        finally { Marshal.FinalReleaseComObject(task); }
                    }
                }
                finally { Marshal.FinalReleaseComObject(collection); }
                dynamic children = folder.GetFolders(0);
                try
                {
                    int count = children.Count;
                    for (var index = 1; index <= count; index++)
                    {
                        token.ThrowIfCancellationRequested(); dynamic child = children.Item(index);
                        try { Walk(child, depth + 1); } finally { Marshal.FinalReleaseComObject(child); }
                    }
                }
                finally { Marshal.FinalReleaseComObject(children); }
            }
            catch (COMException) { complete = false; }
        }
    }
}
public sealed class TaskSchedulerStartupProvider : ITaskSchedulerStartupProvider
{
    private readonly SemaphoreSlim nativeWorker = new(1, 1);
    internal IStartupTaskSource Source { get; }
    public TaskSchedulerStartupProvider() : this(new WindowsStartupTaskSource()) { }
    internal TaskSchedulerStartupProvider(IStartupTaskSource source) => Source = source;
    public async Task<StartupInventoryResult> ReadAsync(CancellationToken token)
    {
        if (!await nativeWorker.WaitAsync(0, token).ConfigureAwait(false)) return new([], false, ["TaskSchedulerBusy"]);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var workerToken = timeout.Token;
        var worker = Task.Run(() => { try { return Read(workerToken); } finally { nativeWorker.Release(); } });
        try { return await worker.WaitAsync(workerToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return new([], false, ["TaskSchedulerTimeout"]); }
    }
    private StartupInventoryResult Read(CancellationToken token)
    {
        var rows = new List<StartupItem>(); var complete = true;
        try
        {
            var result = Source.Read(token); complete = result.Complete;
            foreach (var snapshot in result.Tasks)
            {
                token.ThrowIfCancellationRequested();
                try { var item = Normalize(snapshot); if (item is not null) rows.Add(item); }
                catch (Exception e) when (e is XmlException or InvalidOperationException or ArgumentException) { complete = false; }
            }
        }
        catch (Exception e) when (e is COMException or UnauthorizedAccessException or InvalidOperationException) { complete = false; }
        return new StartupInventoryResult(rows.AsReadOnly(), complete, complete ? [] : ["TaskSchedulerUnavailableOrPartial"]);
    }
    internal static StartupItem? Normalize(StartupTaskSnapshot snapshot)
    {
        using var text = new StringReader(snapshot.Xml);
        using var reader = XmlReader.Create(text, new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 });
        var xml = XDocument.Load(reader); XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        if (xml.Root?.Name != ns + "Task") throw new XmlException("Unknown task schema");
        var triggers = xml.Root.Element(ns + "Triggers")?.Elements().ToArray() ?? [];
        var startup = triggers.Where(t => t.Name == ns + "LogonTrigger" || t.Name == ns + "BootTrigger").ToArray();
        if (startup.Length == 0) return null;
        var actions = xml.Root.Element(ns + "Actions")?.Elements().ToArray() ?? [];
        var action = actions.Length == 1 && actions[0].Name == ns + "Exec" ? actions[0] : null;
        var executable = action?.Element(ns + "Command")?.Value;
        var arguments = action?.Element(ns + "Arguments")?.Value ?? "";
        var raw = executable is null ? "" : "\"" + executable.Trim('"') + "\" " + arguments;
        var command = StartupCommandParser.Read(raw);
        var principal = xml.Root.Element(ns + "Principals")?.Elements(ns + "Principal").SingleOrDefault();
        var user = principal?.Element(ns + "UserId")?.Value;
        using var current = WindowsIdentity.GetCurrent();
        var isUser = string.Equals(user, current.User?.Value, StringComparison.OrdinalIgnoreCase)
            || string.Equals(user, current.Name, StringComparison.OrdinalIgnoreCase);
        // Null descriptors are reserved for internal fake providers. Production
        // always supplies the API result (empty if unavailable -> protected).
        bool? canWrite = snapshot.SecurityDescriptor is null ? isUser : StartupTaskAccess.CanWrite(snapshot.SecurityDescriptor);
        var elevated = canWrite != true;
        var protection = !SafeTaskPath(snapshot.Path) || snapshot.Path.StartsWith("\\Microsoft\\", StringComparison.OrdinalIgnoreCase)
            ? StartupProtection.SystemTask : action is null || !isUser || triggers.Length != startup.Length || canWrite is null ? StartupProtection.AmbiguousTask : StartupProtection.None;
        var identity = "Task:" + snapshot.Path;
        var item = new StartupItem(new StartupIdentityResolver().Resolve(identity), snapshot.Path.Split('\\').Last(), command.Publisher,
            startup.Any(t => t.Name == ns + "LogonTrigger") ? StartupSourceType.ScheduledTaskLogon : StartupSourceType.ScheduledTaskBoot,
            snapshot.Path, raw, command.Executable, command.Arguments, snapshot.Enabled ? StartupStatus.Enabled : StartupStatus.DisabledByWindowsOrSource,
            isUser ? StartupScope.CurrentUser : StartupScope.System, elevated, false, protection, "NotMeasured", identity,
            StartupIdentityResolver.Hash(snapshot.Path + "\0" + snapshot.Xml + "\0" + snapshot.Enabled + "\0" + snapshot.SecurityDescriptor), string.Join(", ", startup.Select(t => t.Name.LocalName).Distinct()));
        return new StartupSafetyClassifier().Apply(item);
    }
    internal static bool SafeTaskPath(string path) => path.Length is > 1 and <= 240 && path.StartsWith('\\') && !path.StartsWith("\\\\", StringComparison.Ordinal)
        && !path.Contains('/') && !path.Contains(':') && !path.Any(char.IsControl)
        && path[1..].Split('\\').All(p => p.Length > 0 && p is not "." and not ".." && !p.EndsWith(' ') && !p.EndsWith('.'));
}
