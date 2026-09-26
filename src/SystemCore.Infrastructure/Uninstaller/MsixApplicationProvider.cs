using System.Runtime.InteropServices;
using SystemCore.Core.Uninstaller;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace SystemCore.Infrastructure.Uninstaller;

public sealed class MsixApplicationProvider(IApplicationIdentityResolver identity) : IMsixApplicationProvider
{
    public Task<InventoryResult> ReadAsync(CancellationToken token) => Task.Run(async () =>
    {
        var rows = new List<InstalledApplication>();
        var failures = new List<string>();
        try
        {
            var manager = new PackageManager();
            var visited = 0;
            foreach (var package in manager.FindPackagesForUser(string.Empty))
            {
                if (++visited > 10000) { failures.Add("Package inventory limit"); break; }
                token.ThrowIfCancellationRequested();
                try
                {
                    var id = package.Id;
                    var restricted = IsRestricted(package.IsFramework, package.IsResourcePackage, package.IsOptional, package.IsBundle,
                        package.SignatureKind == PackageSignatureKind.System) || !package.Status.VerifyIsOK();
                    if (!restricted)
                    {
                        var entries = await package.GetAppListEntriesAsync().AsTask(token).ConfigureAwait(false);
                        restricted = entries.Count == 0; // Dependency/helper-only packages have no main app entry.
                    }
                    rows.Add(new(identity.Resolve(ApplicationType.Msix, null, null, id.FullName), package.DisplayName,
                        package.PublisherDisplayName, $"{id.Version.Major}.{id.Version.Minor}.{id.Version.Build}.{id.Version.Revision}",
                        null, null, null, id.Architecture.ToString(), ApplicationType.Msix, null, false, null,
                        id.FamilyName, id.FullName, null, null, package.SignatureKind == PackageSignatureKind.System,
                        package.IsFramework, restricted, !restricted, restricted ? "Protected or non-main package" : null));
                }
                catch (Exception e) when (e is COMException or UnauthorizedAccessException or InvalidOperationException) { failures.Add("Package metadata unavailable"); }
            }
        }
        catch (Exception e) when (e is COMException or UnauthorizedAccessException or InvalidOperationException) { failures.Add("Current-user package inventory unavailable"); }
        return new InventoryResult(rows.AsReadOnly(), failures.Count == 0, failures.Distinct().ToArray());
    }, token);
    public static bool IsRestricted(bool framework, bool resource, bool optional, bool bundle, bool system) => framework || resource || optional || bundle || system;
}
