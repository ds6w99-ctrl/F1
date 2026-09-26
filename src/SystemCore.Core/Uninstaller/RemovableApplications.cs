namespace SystemCore.Core.Uninstaller;

// Presentation eligibility is a subset of native authorization, never a grant.
public static class RemovableApplications
{
    public static bool IsVisible(InstalledApplication app) => app.CanUninstall
        && !app.IsProtected && !app.IsSystemComponent && !app.IsFramework
        && app.ApplicationType switch
        {
            ApplicationType.Win32 => !string.IsNullOrWhiteSpace(app.RegisteredUninstallCommand),
            ApplicationType.Msi => app.ProductCode is { } code && code != Guid.Empty,
            ApplicationType.Msix => !string.IsNullOrWhiteSpace(app.PackageFullName),
            _ => false
        };
}
