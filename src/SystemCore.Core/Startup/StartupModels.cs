namespace SystemCore.Core.Startup;

public enum StartupSourceType { RegistryRun, RegistryRunOnce, StartupFolderUser, StartupFolderCommon, ScheduledTaskLogon, ScheduledTaskBoot }
public enum StartupScope { CurrentUser, AllUsers, System }
public enum StartupStatus { Enabled, DisabledBySystemCore, DisabledByWindowsOrSource, Protected, Unknown, RecoveryRequired }
public enum StartupProtection { None, UnknownCommand, UnsafePath, SystemComponent, SecurityOrDriver, UnsupportedSource, SystemTask, AmbiguousTask }
public sealed record StartupItem(string Id, string Name, string? Publisher, StartupSourceType SourceType, string SourceLocation,
    string CommandDisplay, string? ExecutablePath, string? ArgumentsDisplay, StartupStatus Status, StartupScope Scope,
    bool RequiresElevation, bool IsProtected, StartupProtection ProtectionReason, string Impact, string Identity, string Fingerprint,
    string? TriggerDescription = null)
{
    // Inventory-only capability; never persist it in Phase 6 recovery receipts.
    [System.Text.Json.Serialization.JsonIgnore]
    public bool CanManage { get; init; }
}
public sealed record StartupInventoryResult(IReadOnlyList<StartupItem> Items, bool Complete, IReadOnlyList<string> UnavailableSources);
public sealed record StartupInventorySession(Guid Id, DateTimeOffset CreatedAt, StartupInventoryResult Inventory);
public sealed record StartupSummary(int Total, int Enabled, int Disabled, int Protected, bool Complete);
public interface IStartupProvider { Task<StartupInventoryResult> ReadAsync(CancellationToken token); }
public interface IRegistryStartupProvider : IStartupProvider;
public interface IStartupFolderProvider : IStartupProvider;
public interface ITaskSchedulerStartupProvider : IStartupProvider;
public interface IStartupInventoryService
{
    StartupSummary? Summary { get; }
    event EventHandler? Changed;
    Task<StartupInventorySession> RefreshAsync(CancellationToken token);
}
public interface IStartupSafetyClassifier { StartupProtection Classify(StartupItem item); }
public interface IStartupIdentityResolver { string Resolve(string sourceIdentity); }
public enum StartupAction { Disable, Enable }
public enum StartupOutcome { Success, Cancelled, RefreshRequired, Protected, Conflict, AccessDenied, StateInvalid, RecoveryRequired, Failed }
public sealed record StartupOperationRequest(Guid SessionId, string ItemId, StartupAction Action, bool Confirmed);
public sealed record StartupOperationResult(StartupOutcome Outcome);
public interface IStartupMutationService { Task<StartupOperationResult> ApplyAsync(StartupOperationRequest request, CancellationToken token); }
public interface IStartupStateService { Task<StartupInventoryResult> ReadAsync(CancellationToken token); }
public interface IElevatedStartupClient
{
    Task<StartupOperationResult> ApplyAsync(string itemId, string fingerprint, StartupAction action, CancellationToken token);
}
