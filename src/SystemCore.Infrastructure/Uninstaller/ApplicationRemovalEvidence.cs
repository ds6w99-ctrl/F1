using SystemCore.Core.Cleaner;
using SystemCore.Core.Uninstaller;
using SystemCore.Infrastructure.Cleaner;

namespace SystemCore.Infrastructure.Uninstaller;

internal sealed record DirectoryEvidence(string Path, CleanerIdentity Identity, string Reason);
internal sealed record RemovalEvidence(InstalledApplication Application, IReadOnlyList<DirectoryEvidence> Directories, IReadOnlyList<RegistryEvidence> Registry, DateTimeOffset CreatedAt, RemovalReceipt? Receipt);

// In-memory only, bounded and short-lived. A receipt is issued only after native
// uninstall completion is independently verified by the executor.
public sealed class ApplicationRemovalEvidence
{
    private readonly object sync = new();
    private readonly Dictionary<string, RemovalEvidence> pending = new(StringComparer.Ordinal);
    internal IReadOnlyList<RemovalReceipt> Receipts { get { lock (sync) { Expire(); return pending.Values.Where(e => e.Receipt is not null).Select(e => e.Receipt!).ToArray(); } } }
    private void Expire()
    {
        foreach (var key in pending.Where(p => DateTimeOffset.UtcNow - p.Value.CreatedAt > TimeSpan.FromMinutes(30)).Select(p => p.Key).ToArray()) pending.Remove(key);
    }
    internal Task CaptureAsync(InstalledApplication app, InventoryResult inventory, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        var directories = new List<DirectoryEvidence>();
        var registry = new List<RegistryEvidence>();
        // InstallLocation plus a verified executable inside it supplies two concrete
        // pre-uninstall facts. Display-name similarity never authorizes a directory.
        if (inventory.Complete && app.ApplicationType == ApplicationType.Win32 && app.InstallLocation is { } location
            && CleanerSafety.Canonical(location) is { } canonical && LeftoverSafety.CanOwnDirectory(app, canonical, inventory.Applications)
            && UninstallCommandParser.Parse(app.RegisteredUninstallCommand) is { } command && CleanerSafety.IsChild(canonical, command.Executable))
        {
            try
            {
                using var directory = PinnedCleanerPath.Open(canonical, true);
                using var executable = new FileStream(command.Executable, FileMode.Open, FileAccess.Read, FileShare.Read);
                directories.Add(new(canonical, directory.Identity, "Registered install location and validated native uninstaller inside the same non-shared directory"));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { }
        }
        if (inventory.Complete && !LeftoverSafety.SharedComponent(app) && LeftoverSafety.SafeRegistryName(app.DisplayName))
        {
            var paths = new List<string> { "Software\\" + app.DisplayName };
            if (app.Publisher is { } publisher && LeftoverSafety.SafeRegistryName(publisher)) paths.Add("Software\\" + publisher + "\\" + app.DisplayName);
            foreach (var hive in Enum.GetValues<InventoryHive>())
            foreach (var view in Enum.GetValues<InventoryView>().OrderByDescending(v => v))
            foreach (var path in paths)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    using var key = RegistryLeftoverEvidence.Open(hive, view, path);
                    if (!RegistryLeftoverEvidence.Matches(app, key)) continue;
                    if (hive == InventoryHive.CurrentUser && view == InventoryView.Registry32 && registry.Any(r => r.Hive == hive && r.View == InventoryView.Registry64 && r.Path == path)) continue;
                    // Same named applications with a different registered identity
                    // make a derived app key ambiguous even with matching publisher.
                    if (inventory.Applications.Any(a => a.Id != app.Id && string.Equals(a.DisplayName, app.DisplayName, StringComparison.OrdinalIgnoreCase))) continue;
                    registry.Add(RegistryLeftoverEvidence.Capture(hive, view, path));
                    if (key.GetValue("DataPath") is string dataPath && CleanerSafety.Canonical(dataPath) is { } data
                        && LeftoverSafety.CanOwnDirectory(app, data, inventory.Applications) && directories.All(d => !LeftoverSafety.Conflicts(d.Path, data)))
                    {
                        using var pinned = PinnedCleanerPath.Open(data, true);
                        directories.Add(new(data, pinned.Identity, "Exact DataPath recorded before uninstall in an app-specific registry key with matching publisher and product/install identity"));
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Security.SecurityException) { }
            }
        }
        lock (sync)
        {
            Expire();
            if (pending.Count >= 16 && !pending.ContainsKey(app.Id)) pending.Remove(pending.MinBy(p => p.Value.CreatedAt).Key);
            pending[app.Id] = new(app, directories, registry, DateTimeOffset.UtcNow, null);
        }
    }, token);
    internal RemovalReceipt? ConfirmRemoved(string applicationId)
    {
        lock (sync)
        {
            Expire();
            if (!pending.TryGetValue(applicationId, out var evidence)) return null;
            var receipt = new RemovalReceipt(Guid.NewGuid(), applicationId, evidence.Application.DisplayName, DateTimeOffset.UtcNow);
            pending[applicationId] = evidence with { Receipt = receipt };
            return receipt;
        }
    }
    internal RemovalEvidence? Get(Guid receiptId)
    {
        lock (sync) { Expire(); return pending.Values.SingleOrDefault(p => p.Receipt?.Id == receiptId); }
    }
}
