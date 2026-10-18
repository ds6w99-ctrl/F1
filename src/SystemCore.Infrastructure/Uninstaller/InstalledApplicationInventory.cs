using SystemCore.Core.Uninstaller;

namespace SystemCore.Infrastructure.Uninstaller;

public sealed class InstalledApplicationInventory(IWin32ApplicationProvider win32, IMsiApplicationProvider msi, IMsixApplicationProvider msix,
    IEnumerable<IGameLibraryProvider>? games = null) : IInstalledApplicationInventory
{
    public async Task<InventoryResult> ReadAsync(CancellationToken token)
    {
        var results = await Task.WhenAll(win32.ReadAsync(token), msi.ReadAsync(token), msix.ReadAsync(token)).ConfigureAwait(false);
        var applications = new List<InstalledApplication>();
        var all = results.SelectMany(r => r.Applications).ToArray();
        // HKCU\Software uninstall registrations are shared across WOW64 views on
        // supported Windows 11. Fold only identical source-key/metadata aliases;
        // differing registrations or publishers must remain separate.
        var distinctSources = all.Where(a => !(a.ApplicationType == ApplicationType.Win32
            && a.RegistrySource is { Hive: InventoryHive.CurrentUser, View: InventoryView.Registry32 } source
            && all.Any(peer => peer.RegistrySource is { Hive: InventoryHive.CurrentUser, View: InventoryView.Registry64 } other
                && string.Equals(source.KeyName, other.KeyName, StringComparison.OrdinalIgnoreCase)
                && (a with { Id = peer.Id, RegistrySource = peer.RegistrySource }) == peer)));
        foreach (var group in distinctSources.GroupBy(a => a.Id, StringComparer.Ordinal))
        {
            var registrations = group.ToArray();
            var first = registrations.FirstOrDefault(a => a.RegistrySource is not null) ?? registrations[0];
            var verifiedMsi = registrations.Any(a => a.ApplicationType == ApplicationType.Msi && a.RegistrySource is null && a.CanUninstall);
            var restricted = registrations.Any(a => a.IsProtected);
            var canRemove = !restricted && (first.ApplicationType == ApplicationType.Msi ? verifiedMsi : registrations.All(a => a.CanUninstall));
            // The Windows Installer API row carries the authoritative install location; registry
            // rows for MSI products often omit it. Merge it only when Windows Installer itself
            // verified the product, so a stale registry entry can never donate a location.
            var msiLocation = verifiedMsi
                ? registrations.FirstOrDefault(a => a.ApplicationType == ApplicationType.Msi && a.RegistrySource is null)?.InstallLocation
                : null;
            applications.Add(first with { IsProtected = restricted, CanUninstall = canRemove,
                InstallLocation = first.InstallLocation ?? msiLocation,
                IconLocation = first.IconLocation ?? registrations.Select(a => a.IconLocation).FirstOrDefault(i => i is not null),
                UnavailableReason = canRemove ? null : restricted ? "Protected registration" : first.UnavailableReason });
        }
        // Different source identities are retained even when names are identical.
        var native = new InventoryResult(applications.OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            results.All(r => r.Complete), results.SelectMany(r => r.UnavailableSources).Distinct().ToArray());
        // Optional discovery is read-only. Its diagnostics do not grant native rights
        // or change the meaning of Phase 5's native inventory Complete flag.
        var discovered = await Task.WhenAll((games ?? []).Select(p => p.ReadAsync(token))).ConfigureAwait(false);
        return GameLibraryMerge.Merge(native, discovered);
    }
}
