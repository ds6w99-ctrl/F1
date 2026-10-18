namespace SystemCore.Core.Cleaner;

public enum CleanerCategory { UserTemp, WindowsTemp, ShaderCache, EdgeCache, ChromeCache, FirefoxCache, CrashDumps, UserWer, Thumbnails, RecycleBin, ExplorerHistory }
public enum CleanerScanStatus { Complete, Unavailable, Incomplete, Cancelled }
public enum CleanerSkip { Recent, Reparse, Inaccessible, UnsafePath, UnsupportedType, HardLink, InvalidMetadata, Limit }
public sealed record CleanerTarget(CleanerCategory Category, string Root, string Pattern, bool Recurse,
    TimeSpan MinimumAge, bool DefaultSelected, bool RequiresElevation, bool Review, bool CanDeleteDirectories = false);
public sealed record CleanerIdentity(uint Volume, ulong FileId, long Size, long LastWriteFileTime, uint Links, long CreationFileTime);
public sealed record CleanerItem(Guid Id, CleanerCategory Category, string RelativePath, string DisplayPath,
    long Size, long LastWriteFileTime, CleanerIdentity Identity);
public sealed record CleanerCategoryResult(CleanerCategory Category, CleanerScanStatus Status, IReadOnlyList<CleanerItem> Items,
    long Bytes, long Visited, IReadOnlyDictionary<CleanerSkip, long> Skipped, bool DefaultSelected, bool RequiresElevation, bool Review)
{
    public long? ReportedItems { get; init; }
    public long ItemCount => ReportedItems ?? Items.Count;
}
public sealed record CleanerScanSession(Guid Id, DateTimeOffset Started, DateTimeOffset Completed, IReadOnlyList<CleanerCategoryResult> Categories)
{
    public bool Complete => Categories.Any(c => c.Status == CleanerScanStatus.Complete) &&
        Categories.All(c => c.Status is CleanerScanStatus.Complete or CleanerScanStatus.Unavailable);
}
public interface ICleanerTargetProvider { IReadOnlyList<CleanerTarget> Resolve(); }
public sealed record CleanerScanProgress(CleanerCategory Category, long Visited, long Eligible, long Bytes,
    CleanerScanStatus? Status = null);
public interface ICleanerScanner
{
    Task<CleanerScanSession> ScanAsync(CancellationToken token);
    Task<CleanerScanSession> ScanAsync(CancellationToken token, IProgress<CleanerScanProgress>? progress) => ScanAsync(token);
}
public enum CleanerItemOutcome { Deleted, Changed, InUse, AccessDenied, AlreadyGone, SafetyRejected, Failed, Cancelled, Unknown }
public sealed record CleanerCleanRequest(Guid SessionId, IReadOnlyList<Guid> ItemIds, bool EmptyRecycleBin = false);
public sealed record CleanerItemResult(Guid ItemId, CleanerItemOutcome Outcome, long RemovedBytes);
public sealed record CleanerCleanResult(bool Accepted, IReadOnlyList<CleanerItemResult> Items)
{
    public RecycleBinOutcome? RecycleBin { get; init; }
    public long RemovedBytes => Items.Sum(i => i.RemovedBytes);
    public int Deleted => Items.Count(i => i.Outcome == CleanerItemOutcome.Deleted);
    public int Failed => Items.Count(i => i.Outcome is CleanerItemOutcome.Failed or CleanerItemOutcome.Unknown);
    public int Skipped => Items.Count - Deleted - Failed;
}
public sealed record RecycleBinSnapshot(bool Available, long Count, long Bytes);
public enum RecycleBinOutcome { Completed, Changed, Cancelled, Unavailable, Failed }
public interface IRecycleBinService
{
    Task<RecycleBinSnapshot> QueryAsync(CancellationToken token);
    Task<RecycleBinOutcome> EmptyAsync(RecycleBinSnapshot expected, CancellationToken token);
}
public interface IElevatedCleanerClient
{
    Task<IReadOnlyList<CleanerItemResult>> CleanAsync(Guid session, DateTimeOffset scannedAt, IReadOnlyList<CleanerItem> items, CancellationToken token);
}
public interface ICleanerService : ICleanerScanner
{
    Task<CleanerCleanResult> CleanAsync(CleanerCleanRequest request, CancellationToken token);
    void Invalidate();
}
public sealed record CleanerScanSummary(bool Complete, long Bytes, DateTimeOffset Completed);
public sealed class CleanerScanState
{
    public CleanerScanSummary? Latest { get; private set; }
    public event EventHandler? Changed;
    public void Set(CleanerScanSession? session)
    {
        Latest = session is null ? null : new(session.Complete, session.Categories.Sum(c => c.Bytes), session.Completed);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
