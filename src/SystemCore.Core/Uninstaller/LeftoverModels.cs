namespace SystemCore.Core.Uninstaller;

public enum LeftoverType { File, Directory, RegistryKey, Shortcut }
public enum LeftoverConfidence { Safe, Review, Ignore }
public sealed record RemovalReceipt(Guid Id, string ApplicationId, string DisplayName, DateTimeOffset VerifiedAt);
public sealed record LeftoverItem(Guid Id, string ApplicationIdentityId, LeftoverType Type, string DisplayLocation,
    string Evidence, LeftoverConfidence Confidence, string Risk, long? Size, bool RequiresElevation,
    bool SelectedByDefault, string SafetyStatus);
public sealed record LeftoverScanSession(Guid Id, Guid ReceiptId, string ApplicationIdentityId, DateTimeOffset CreatedAt,
    IReadOnlyList<LeftoverItem> Items, bool Complete);
public record LeftoverScanOptions(bool DeepScan);
public interface ILeftoverScanner
{
    Task<LeftoverScanSession> ScanAsync(Guid removalReceiptId, CancellationToken token);
    // Deep scan adds name-derived candidates that are never receipt-verified and therefore
    // always classified Review. Providers that ignore the option keep the conservative result.
    Task<LeftoverScanSession> ScanAsync(Guid removalReceiptId, bool deepScan, CancellationToken token) => ScanAsync(removalReceiptId, token);
}
public enum LeftoverDeletionStatus { Deleted, Skipped, Changed, SafetyRejected, AccessDenied, InUse, AlreadyGone, BackupFailed, Failed, Cancelled }
// IncludePersonalData acknowledges that Review items (personal data, name-derived candidates)
// may be destroyed. It is false everywhere by default: no caller can widen deletion implicitly.
public sealed record LeftoverDeletionRequest(Guid SessionId, string ApplicationIdentityId, IReadOnlyList<Guid> ItemIds, bool IncludePersonalData = false);
public sealed record LeftoverDeletionItemResult(Guid ItemId, LeftoverDeletionStatus Status, long RemovedBytes, string? BackupFile = null);
public sealed record LeftoverDeletionResult(IReadOnlyList<LeftoverDeletionItemResult> Items, long RemovedBytes)
{
    // Where the pre-deletion file archive was written, when file backups are enabled.
    public string? BackupFile { get; init; }
}
public interface ILeftoverDeletionService
{
    Task<LeftoverDeletionResult> DeleteAsync(LeftoverDeletionRequest request, CancellationToken token);
}
