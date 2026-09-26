namespace SystemCore.Core.Uninstaller;

public enum ApplicationType { Win32, Msi, Msix }
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
    string? UnavailableReason);
public sealed record InventoryResult(IReadOnlyList<InstalledApplication> Applications, bool Complete, IReadOnlyList<string> UnavailableSources);
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
