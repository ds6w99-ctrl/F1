namespace SystemCore.Core.Uninstaller;

public enum ApplicationType { Win32, Msi, Msix }
public enum ItemKind { Application, Game }
public enum Source { Win32, MSI, MSIX, Steam, Epic, Xbox, EA, Ubisoft, BattleNet }
public enum InventoryHive { CurrentUser, LocalMachine }
public enum InventoryView { Registry32, Registry64 }
public enum UninstallState { Ready, Launching, WaitingForUninstaller, UninstallerExited, Verifying, Removed, StillInstalled, Cancelled, Failed, Unknown, ScanningLeftovers, LeftoversReady, CleaningLeftovers, CleanupComplete }
public sealed record RegistryApplicationSource(InventoryHive Hive, InventoryView View, string KeyName);
public sealed record UninstallCommand(string Executable, IReadOnlyList<string> Arguments);
public sealed record InstalledApplication(
    string Id, string DisplayName, string? Publisher, string? DisplayVersion,
    DateOnly? InstallDate, string? InstallLocation, long? EstimatedSize,
    string? Architecture, ApplicationType ApplicationType,
    string? RegisteredUninstallCommand, bool HasQuietUninstall,
    Guid? ProductCode, string? PackageFamilyName, string? PackageFullName,
    RegistryApplicationSource? RegistrySource, string? IconLocation,
    bool IsSystemComponent, bool IsFramework, bool IsProtected, bool CanUninstall,
    string? UnavailableReason)
{
    public ItemKind ItemKind { get; init; }
    public Source Source { get; init; } = ApplicationType switch { ApplicationType.Msi => Source.MSI, ApplicationType.Msix => Source.MSIX, _ => Source.Win32 };
    public string? GameIdentity { get; init; }
    public string? GameInstallLocation { get; init; }
    public string? LibraryPath { get; init; }
    public string? InstallState { get; init; }
    public string? ManifestExecutable { get; init; }
    public string? LauncherIcon { get; init; }
}
public sealed record GameDiscoveryStatus(Source Source, bool LauncherDetected, int Detected, int UnsupportedOrUnverified, string Status);
public sealed record GameLibraryResult(IReadOnlyList<InstalledApplication> Games, GameDiscoveryStatus Status);
public interface IGameLibraryProvider
{
    Task<GameLibraryResult> ReadAsync(CancellationToken token);
}
public interface IGameLauncher
{
    bool CanOpen(Source source);
    Task<bool> OpenAsync(Source source, CancellationToken token);
}
// This is a separate read-only presentation category, NOT native authorization.
public static class UnifiedLibraryPresentation
{
    public static bool IsLauncherManaged(InstalledApplication app) => app.ItemKind == ItemKind.Game
        && app.Source is Source.Steam or Source.Epic or Source.EA or Source.Ubisoft or Source.BattleNet
        && !app.CanUninstall && !app.IsProtected && !app.IsFramework && !app.IsSystemComponent
        && !string.IsNullOrWhiteSpace(app.GameIdentity) && !string.IsNullOrWhiteSpace(app.GameInstallLocation ?? app.InstallLocation);
    public static bool IsVisible(InstalledApplication app) => RemovableApplications.IsVisible(app) || IsLauncherManaged(app);
}
public sealed record InventoryResult(IReadOnlyList<InstalledApplication> Applications, bool Complete, IReadOnlyList<string> UnavailableSources)
{
    public IReadOnlyList<GameDiscoveryStatus> GameDiscovery { get; init; } = [];
    public int GameDuplicatesMerged { get; init; }
}
public sealed record UninstallRequest(Guid InventorySessionId, string ApplicationId);
public sealed record ApplicationInventorySession(Guid Id, DateTimeOffset CreatedAt, InventoryResult Inventory);
public sealed record UninstallOutcome(UninstallState State, string ApplicationId, int? NativeExitCode, string? Detail, RemovalReceipt? Receipt = null);

public interface IInstalledApplicationProvider
{
    Task<InventoryResult> ReadAsync(CancellationToken token);
}
public interface IWin32ApplicationProvider : IInstalledApplicationProvider;
public interface IMsiApplicationProvider : IInstalledApplicationProvider;
public interface IMsixApplicationProvider : IInstalledApplicationProvider;
public interface IInstalledApplicationInventory
{
    Task<InventoryResult> ReadAsync(CancellationToken token);
}
public interface IApplicationIdentityResolver
{
    string Resolve(ApplicationType type, RegistryApplicationSource? source, Guid? productCode, string? packageFullName);
}
public interface IUninstallCompletionDetector
{
    Task<UninstallState> VerifyAsync(InstalledApplication original, CancellationToken token);
}
public interface IUninstallExecutor
{
    Task<ApplicationInventorySession> RefreshAsync(CancellationToken token);
    Task<UninstallOutcome> UninstallAsync(UninstallRequest request, IProgress<UninstallState>? progress, CancellationToken token);
}
