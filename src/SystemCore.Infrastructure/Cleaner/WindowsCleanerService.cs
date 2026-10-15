using System.ComponentModel;
using System.IO.Enumeration;
using SystemCore.Core.Cleaner;

namespace SystemCore.Infrastructure.Cleaner;

// Owns the only accepted session. Callers submit opaque IDs, never paths or metadata.
public sealed class WindowsCleanerService(ICleanerTargetProvider targets, TimeProvider? timeProvider = null,
    IRecycleBinService? recycleBin = null, IElevatedCleanerClient? elevated = null) : ICleanerService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim operation = new(1, 1);
    private readonly object sync = new();
    private CleanerScanSession? latest;
    private long generation;
    public void Invalidate() { lock (sync) { latest = null; generation++; } }
    public Task<CleanerScanSession> ScanAsync(CancellationToken token) => ScanAsync(token, null);
    public async Task<CleanerScanSession> ScanAsync(CancellationToken token, IProgress<CleanerScanProgress>? progress)
    {
        await operation.WaitAsync(token).ConfigureAwait(false);
        try
        {
            long version;
            lock (sync) { latest = null; version = ++generation; }
            var session = await new WindowsCleanerScanner(targets).ScanAsync(token, progress).ConfigureAwait(false);
            if (recycleBin is not null && !token.IsCancellationRequested)
            {
                progress?.Report(new(CleanerCategory.RecycleBin, session.Categories.Sum(c => c.Visited),
                    session.Categories.Sum(c => c.ItemCount), session.Categories.Sum(c => c.Bytes)));
                RecycleBinSnapshot bin;
                try { bin = await recycleBin.QueryAsync(token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { bin = new(false, 0, 0); }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                { Core.Diagnostics.ReliabilityDiagnostics.Log.UnexpectedFailure((int)Core.Diagnostics.ReliabilityComponent.Cleaner, ex.HResult); bin = new(false, 0, 0); }
                if (bin.Available && bin.Count >= 0 && bin.Bytes >= 0 &&
                    CleanerSafety.TryAddSize(session.Categories.Sum(c => c.Bytes), bin.Bytes, out _))
                {
                    session = session with { Categories = Array.AsReadOnly(session.Categories.Select(c => c.Category == CleanerCategory.RecycleBin ?
                        c with { Status = CleanerScanStatus.Complete, Bytes = bin.Bytes, ReportedItems = bin.Count } : c).ToArray()) };
                }
                if (!token.IsCancellationRequested)
                    progress?.Report(new(CleanerCategory.RecycleBin, session.Categories.Sum(c => c.Visited),
                        session.Categories.Sum(c => c.ItemCount), session.Categories.Sum(c => c.Bytes),
                        session.Categories.Single(c => c.Category == CleanerCategory.RecycleBin).Status));
            }
            lock (sync)
                if (generation == version && !token.IsCancellationRequested) latest = session;
            return session;
        }
        finally { operation.Release(); }
    }
    public async Task<CleanerCleanResult> CleanAsync(CleanerCleanRequest request, CancellationToken token)
    {
        // Snapshot caller-owned collections before asynchronous work.
        if (request.ItemIds is null || (request.ItemIds.Count == 0 && !request.EmptyRecycleBin) || request.ItemIds.Count > WindowsCleanerScanner.MaximumTotalEntries)
            return new(false, []);
        var ids = request.ItemIds.ToArray();
        await operation.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            CleanerScanSession? session;
            lock (sync) { session = latest; latest = null; generation++; }
            if (session is null || session.Id != request.SessionId ||
                clock.GetUtcNow() < session.Completed || clock.GetUtcNow() - session.Completed > TimeSpan.FromMinutes(10) ||
                session.Categories.Any(c => c.Status == CleanerScanStatus.Cancelled) || ids.Distinct().Count() != ids.Length)
                return new(false, []);
            var eligible = session.Categories.Where(c => c.Status == CleanerScanStatus.Complete)
                .SelectMany(c => c.Items).ToDictionary(i => i.Id);
            if (ids.Any(id => !eligible.ContainsKey(id))) return new(false, []);
            var bin = session.Categories.Single(c => c.Category == CleanerCategory.RecycleBin);
            if (request.EmptyRecycleBin && (recycleBin is null || bin.Status != CleanerScanStatus.Complete || bin.ReportedItems is null)) return new(false, []);
            var selected = ids.Select(id => eligible[id]).ToArray();
            var local = await Task.Run(() => Execute(selected.Where(i => i.Category != CleanerCategory.WindowsTemp).ToArray(), token), CancellationToken.None).ConfigureAwait(false);
            var results = local.Items.ToList();
            var systemItems = selected.Where(i => i.Category == CleanerCategory.WindowsTemp).ToArray();
            if (systemItems.Length > 0)
            {
                if (elevated is null) results.AddRange(systemItems.Select(i => new CleanerItemResult(i.Id, CleanerItemOutcome.AccessDenied, 0)));
                else results.AddRange(await elevated.CleanAsync(session.Id, session.Completed, systemItems, token).ConfigureAwait(false));
            }
            RecycleBinOutcome? binResult = null;
            if (request.EmptyRecycleBin)
            {
                try { binResult = await recycleBin!.EmptyAsync(new(true, bin.ReportedItems!.Value, bin.Bytes), token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { binResult = RecycleBinOutcome.Cancelled; }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                { Core.Diagnostics.ReliabilityDiagnostics.Log.UnexpectedFailure((int)Core.Diagnostics.ReliabilityComponent.Cleaner, ex.HResult); binResult = RecycleBinOutcome.Failed; }
            }
            return new(true, results.AsReadOnly()) { RecycleBin = binResult };
        }
        finally { operation.Release(); }
    }
    private CleanerCleanResult Execute(CleanerItem[] items, CancellationToken token)
    {
        IReadOnlyList<CleanerTarget> definitions;
        try { definitions = targets.Resolve(); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Core.Diagnostics.ReliabilityDiagnostics.Log.UnexpectedFailure((int)Core.Diagnostics.ReliabilityComponent.Cleaner, ex.HResult);
            return new(true, Array.AsReadOnly(items.Select(i => new CleanerItemResult(i.Id, CleanerItemOutcome.Failed, 0)).ToArray()));
        }
        return ExecuteValidated(items, definitions, token);
    }
    internal static CleanerCleanResult ExecuteValidated(CleanerItem[] items, IReadOnlyList<CleanerTarget> definitions,
        CancellationToken token, bool allowWindowsTemp = false)
    {
        var results = new List<CleanerItemResult>(items.Length);
        foreach (var item in items)
        {
            var outcome = CleanerItemOutcome.SafetyRejected;
            if (token.IsCancellationRequested) { results.Add(new(item.Id, CleanerItemOutcome.Cancelled, 0)); continue; }
            try
            {
                var target = definitions
                    .Where(t => t.Category == item.Category && CleanerSafety.IsChild(t.Root, item.DisplayPath))
                    .OrderByDescending(t => CleanerSafety.Canonical(t.Root)?.Length ?? 0)
                    .FirstOrDefault(t => ValidTarget(t, item, allowWindowsTemp));
                if (target is not null)
                {
                    var path = Path.Combine(target.Root, item.RelativePath);
                    using (var file = PinnedCleanerPath.Open(path, false, deleteAccess: true))
                    {
                        if (file.Identity != item.Identity) outcome = CleanerItemOutcome.Changed;
                        else if (!CleanerSafety.SafeFileAttributes(file.Attributes, file.Identity.Links) ||
                            !CleanerSafety.Eligible(file.Identity.Size, file.Identity.LastWriteFileTime, DateTimeOffset.UtcNow, target.MinimumAge))
                            outcome = CleanerItemOutcome.SafetyRejected;
                        else if (token.IsCancellationRequested) outcome = CleanerItemOutcome.Cancelled;
                        else { file.MarkFileForDeletion(); outcome = CleanerItemOutcome.Deleted; }
                    }
                }
            }
            catch (Win32Exception ex)
            {
                outcome = ex.NativeErrorCode switch
                {
                    2 or 3 => CleanerItemOutcome.AlreadyGone,
                    5 => CleanerItemOutcome.AccessDenied,
                    32 or 33 => CleanerItemOutcome.InUse,
                    _ => CleanerItemOutcome.Failed
                };
            }
            catch (UnauthorizedAccessException) { outcome = CleanerItemOutcome.AccessDenied; }
            catch (IOException) { outcome = CleanerItemOutcome.SafetyRejected; }
            catch (ArgumentException) { outcome = CleanerItemOutcome.SafetyRejected; }
            catch (OverflowException) { outcome = CleanerItemOutcome.SafetyRejected; }
            results.Add(new(item.Id, outcome, outcome == CleanerItemOutcome.Deleted ? item.Size : 0));
        }
        return new(true, results.AsReadOnly());
    }
    // A single category may now carry several disjoint roots; the caller-supplied path must
    // match exactly one allowed definition before any handle with delete access is opened.
    private static bool ValidTarget(CleanerTarget target, CleanerItem item, bool allowWindowsTemp)
    {
        if (target.RequiresElevation && !(allowWindowsTemp && target.Category == CleanerCategory.WindowsTemp)) return false;
        if (!WindowsCleanerTargets.IsAllowed(target)) return false;
        if (Path.IsPathRooted(item.RelativePath)) return false;
        var path = Path.Combine(target.Root, item.RelativePath);
        if (!CleanerSafety.IsChild(target.Root, path)) return false;
        if (!string.Equals(CleanerSafety.Canonical(path), CleanerSafety.Canonical(item.DisplayPath), StringComparison.OrdinalIgnoreCase)) return false;
        if (!target.Recurse && !string.Equals(Path.GetDirectoryName(path), target.Root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return false;
        if (!FileSystemName.MatchesSimpleExpression(target.Pattern, Path.GetFileName(path), true)) return false;
        return true;
    }
}
