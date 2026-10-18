using SystemCore.Core.Cleaner;
using SystemCore.Core.Uninstaller;
using SystemCore.Infrastructure.Cleaner;

namespace SystemCore.Infrastructure.Uninstaller;

internal sealed record DirectoryEvidence(string Path, CleanerIdentity Identity, string Reason);
internal sealed record RemovalEvidence(InstalledApplication Application, IReadOnlyList<DirectoryEvidence> Directories, IReadOnlyList<RegistryEvidence> Registry, DateTimeOffset CreatedAt, RemovalReceipt? Receipt, LeftoverFootprint? Footprint = null, IReadOnlyList<string>? Touched = null);

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
        // Game save/shared-launcher ownership is not established by discovery.
        // FIX 5 never issues a cleanup receipt for a discovered game.
        if (app.ItemKind == ItemKind.Game)
        {
            lock (sync) pending.Remove(app.Id);
            return;
        }
        var directories = new List<DirectoryEvidence>();
        var registry = new List<RegistryEvidence>();
        // InstallLocation plus a verified executable inside it supplies two concrete
        // pre-uninstall facts. Display-name similarity never authorizes a directory.
        // MSI products carry a product code verified by the Windows Installer API instead of a
        // native uninstaller executable inside the directory (the uninstaller is msiexec.exe).
        if (inventory.Complete && app.InstallLocation is { } location
            && CleanerSafety.Canonical(location) is { } canonical && LeftoverSafety.CanOwnDirectory(app, canonical, inventory.Applications)
            && Directory.Exists(canonical))
        {
            var reason = app.ApplicationType switch
            {
                ApplicationType.Win32 when UninstallCommandParser.Parse(app.RegisteredUninstallCommand) is { } command
                    && CleanerSafety.IsChild(canonical, command.Executable) =>
                    "Registered install location and validated native uninstaller inside the same non-shared directory",
                ApplicationType.Msi when app.ProductCode is { } product && product != Guid.Empty =>
                    "Registered MSI install location with a Windows Installer verified product code",
                _ => null
            };
            if (reason is not null)
            {
                try
                {
                    using var directory = PinnedCleanerPath.Open(canonical, true);
                    directories.Add(new(canonical, directory.Identity, reason));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { }
            }
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
        // Root-relative footprint. The application is still installed here, so its data
        // directories can be recorded with an exact identity instead of being guessed after
        // removal. Only an application that already has a verified pre-uninstall fact is
        // extended; a name alone never authorizes anything.
        if (inventory.Complete && !LeftoverSafety.SharedComponent(app) && (directories.Count != 0 || registry.Count != 0))
        {
            var names = new List<string>();
            if (LeftoverSafety.SafeRegistryName(app.DisplayName)) names.Add(app.DisplayName);
            if (app.Publisher is { } publisher && LeftoverSafety.SafeRegistryName(publisher)
                && !string.Equals(publisher, app.DisplayName, StringComparison.OrdinalIgnoreCase)) names.Add(Path.Combine(publisher, app.DisplayName));
            if (names.Count != 0)
            foreach (var baseRoot in new[] { Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ApplicationData,
                Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
                .Select(Environment.GetFolderPath).Where(root => !string.IsNullOrWhiteSpace(root)).Distinct(StringComparer.OrdinalIgnoreCase))
            foreach (var relative in names)
            {
                token.ThrowIfCancellationRequested();
                var candidate = Path.Combine(baseRoot, relative);
                // Existence is required: a footprint must be observed, never invented.
                if (CleanerSafety.Canonical(candidate) is null || !Directory.Exists(candidate)
                    || !LeftoverSafety.CanOwnDirectory(app, candidate, inventory.Applications)
                    || directories.Any(d => LeftoverSafety.Conflicts(d.Path, candidate))) continue;
                try
                {
                    using var pinned = PinnedCleanerPath.Open(candidate, true);
                    directories.Add(new(candidate, pinned.Identity, "Pre-uninstall footprint recorded under a non-shared application directory"));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { }
            }
        }
        // Observed last, so the window between the observation and the native uninstaller is as
        // short as the surrounding code allows. Only an application that already has a verified
        // pre-uninstall fact is observed; an unidentifiable registration never anchors anything.
        var footprint = directories.Count == 0 && registry.Count == 0 ? null : LeftoverFootprint.Snapshot(token);
        lock (sync)
        {
            Expire();
            if (pending.Count >= 16 && !pending.ContainsKey(app.Id)) pending.Remove(pending.MinBy(p => p.Value.CreatedAt).Key);
            pending[app.Id] = new(app, directories, registry, DateTimeOffset.UtcNow, null, footprint);
        }
    }, token);
    internal RemovalReceipt? ConfirmRemoved(string applicationId)
    {
        lock (sync)
        {
            Expire();
            if (!pending.TryGetValue(applicationId, out var evidence)) return null;
            var receipt = new RemovalReceipt(Guid.NewGuid(), applicationId, evidence.Application.DisplayName, DateTimeOffset.UtcNow);
            // The post-uninstall observation must never turn an already verified removal into a
            // failure: an unobserved removal degrades to no anchors, not to a lost result.
            IReadOnlyList<string> touched = Array.Empty<string>();
            try { if (evidence.Footprint is not null) touched = evidence.Footprint.Modified(LeftoverFootprint.Snapshot(CancellationToken.None)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Security.SecurityException) { }
            pending[applicationId] = evidence with { Receipt = receipt, Footprint = null, Touched = touched };
            return receipt;
        }
    }
    internal RemovalEvidence? Get(Guid receiptId)
    {
        lock (sync) { Expire(); return pending.Values.SingleOrDefault(p => p.Receipt?.Id == receiptId); }
    }
}
