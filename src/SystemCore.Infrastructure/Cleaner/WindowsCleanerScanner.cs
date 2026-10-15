using System.ComponentModel;
using System.Diagnostics;
using System.IO.Enumeration;
using SystemCore.Core.Cleaner;

namespace SystemCore.Infrastructure.Cleaner;

public sealed class WindowsCleanerScanner(ICleanerTargetProvider targets) : ICleanerScanner
{
    public const int MaximumCategoryEntries = 200000;
    public const int MaximumTotalEntries = 500000;
    public const int MaximumDepth = 32;
    internal int CategoryLimit { get; init; } = MaximumCategoryEntries;
    // Read-only categories only; execution/deletion stays serialized in the service.
    internal int WorkerCount { get; init; } = 2;
    public Task<CleanerScanSession> ScanAsync(CancellationToken token) => ScanAsync(token, null);
    public Task<CleanerScanSession> ScanAsync(CancellationToken token, IProgress<CleanerScanProgress>? progress) =>
        Task.Run(() => Scan(token, progress), CancellationToken.None);
    // A category may own several disjoint roots (browser profiles, WER queues). Only targets
    // that survive the structural allowlist are used, and a nested root is dropped when an
    // accepted ancestor already covers it so files are never counted twice.
    private static CleanerTarget[] AcceptedTargets(IReadOnlyList<CleanerTarget> roots, CleanerCategory category)
    {
        var accepted = new List<CleanerTarget>();
        foreach (var target in roots.Where(t => t.Category == category).OrderBy(t => t.Root.Length))
        {
            if (!WindowsCleanerTargets.IsAllowed(target)) continue;
            if (accepted.Any(kept => CleanerSafety.IsChild(kept.Root, target.Root))) continue;
            accepted.Add(target);
        }
        return accepted.ToArray();
    }
    private CleanerScanSession Scan(CancellationToken token, IProgress<CleanerScanProgress>? progress)
    {
        var started = DateTimeOffset.UtcNow;
        var categories = Enum.GetValues<CleanerCategory>();
        var result = new CleanerCategoryResult[categories.Length];
        IReadOnlyList<CleanerTarget> roots;
        try { roots = targets.Resolve(); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Core.Diagnostics.ReliabilityDiagnostics.Log.UnexpectedFailure((int)Core.Diagnostics.ReliabilityComponent.Cleaner, ex.HResult);
            roots = [];
        }
        long totalVisited = 0, totalBytes = 0, totalEligible = 0;
        var budgetGate = new object();
        var lastProgress = Stopwatch.GetTimestamp();
        void Report(CleanerCategory category, bool transition = false, CleanerScanStatus? status = null)
        {
            if (progress is null || token.IsCancellationRequested) return;
            lock (budgetGate)
            {
                if (token.IsCancellationRequested) return;
                var now = Stopwatch.GetTimestamp();
                // One shared gate across workers. Switching the active worker per entry
                // must NOT bypass throttling; only category start/end are immediate.
                if (!transition && Stopwatch.GetElapsedTime(lastProgress, now).TotalMilliseconds < 150) return;
                lastProgress = now;
                progress.Report(new(category, totalVisited, totalEligible, totalBytes, status));
            }
        }
        ForEachCategory(categories.Length, WorkerCount, index =>
        {
            var category = categories[index];
            var accepted = AcceptedTargets(roots, category);
            var items = new List<CleanerItem>();
            var skips = new Dictionary<CleanerSkip, long>();
            long bytes = 0, visited = 0;
            bool cancelled = false, incomplete = false, unavailable = accepted.Length == 0, opened = false;
            Report(category, transition: true);
            void Skip(CleanerSkip reason) => skips[reason] = skips.GetValueOrDefault(reason) + 1;
            void Walk(CleanerTarget root, string directory, int depth)
            {
                if (cancelled || token.IsCancellationRequested) { cancelled = true; return; }
                if (depth > MaximumDepth) { incomplete = true; Skip(CleanerSkip.Limit); return; }
                try
                {
                    if (!PinnedCleanerPath.TryOpenRead(directory, true, out var pinned, out var directoryFailure))
                    {
                        Skip(directoryFailure == PinFailure.Reparse ? CleanerSkip.Reparse : CleanerSkip.Inaccessible);
                        if (directoryFailure == PinFailure.Reparse || depth > 0) incomplete = true; else unavailable = true;
                        return;
                    }
                    using var directoryLease = pinned;
                    if (depth == 0) opened = true;
                    // Enumeration supplies attributes/write time in one directory record. These
                    // are rejection hints ONLY: accepted candidates still get the unchanged
                    // exclusive native lease, ancestor checks and fresh identity/age validation.
                    var entries = new FileSystemEnumerable<ScanEntry>(directory,
                        (ref FileSystemEntry e) => new(e.ToFullPath(), e.Attributes, e.LastWriteTimeUtc),
                        new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = false,
                            AttributesToSkip = 0, ReturnSpecialDirectories = false });
                    foreach (var metadata in entries)
                    {
                        var entry = metadata.Path;
                        if (cancelled || token.IsCancellationRequested) { cancelled = true; return; }
                        Report(category);
                        if (cancelled || token.IsCancellationRequested) { cancelled = true; return; }
                        lock (budgetGate)
                        {
                            if (visited >= Math.Clamp(CategoryLimit, 1, MaximumCategoryEntries) || totalVisited >= MaximumTotalEntries)
                            { incomplete = true; Skip(CleanerSkip.Limit); return; }
                            visited++; totalVisited++;
                        }
                        if (!CleanerSafety.IsChild(root.Root, entry)) { Skip(CleanerSkip.UnsafePath); continue; }
                        try
                        {
                            var attributes = metadata.Attributes;
                            if ((attributes & FileAttributes.ReparsePoint) != 0) { Skip(CleanerSkip.Reparse); continue; }
                            if ((attributes & FileAttributes.Directory) != 0)
                            { if (root.Recurse) Walk(root, entry, depth + 1); else Skip(CleanerSkip.UnsupportedType); continue; }
                            if (!FileSystemName.MatchesSimpleExpression(root.Pattern, Path.GetFileName(entry), true)) { Skip(CleanerSkip.UnsupportedType); continue; }
                            if (!CleanerSafety.SafeFileAttributes((uint)attributes, 1)) { Skip(CleanerSkip.UnsupportedType); continue; }
                            // Do not read size or open ancestors for an obviously recent file.
                            if (metadata.LastWrite > started || started - metadata.LastWrite < root.MinimumAge)
                            { Skip(CleanerSkip.Recent); continue; }
                            if (!PinnedCleanerPath.TryOpenRead(entry, false, out var file, out var fileFailure))
                            { Skip(fileFailure == PinFailure.Reparse ? CleanerSkip.Reparse : CleanerSkip.Inaccessible); continue; }
                            using var fileLease = file;
                            if (file.Identity.Links != 1) { Skip(CleanerSkip.HardLink); continue; }
                            if (!CleanerSafety.SafeFileAttributes(file.Attributes, file.Identity.Links)) { Skip(CleanerSkip.UnsupportedType); continue; }
                            if (!CleanerSafety.Eligible(file.Identity.Size, file.Identity.LastWriteFileTime, started, root.MinimumAge))
                            { Skip(CleanerSkip.Recent); continue; }
                            lock (budgetGate)
                            {
                                if (!CleanerSafety.TryAddSize(bytes, file.Identity.Size, out var sum) || !CleanerSafety.TryAddSize(totalBytes, file.Identity.Size, out var grandTotal))
                                { incomplete = true; Skip(CleanerSkip.InvalidMetadata); return; }
                                bytes = sum;
                                totalBytes = grandTotal;
                                totalEligible++;
                            }
                            items.Add(new(Guid.NewGuid(), category, Path.GetRelativePath(root.Root, entry), entry,
                                file.Identity.Size, file.Identity.LastWriteFileTime, file.Identity));
                        }
                        catch (ReparseRejectedException) { Skip(CleanerSkip.Reparse); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or OverflowException)
                        { Skip(CleanerSkip.Inaccessible); }
                    }
                }
                catch (ReparseRejectedException) { Skip(CleanerSkip.Reparse); incomplete = true; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
                { Skip(CleanerSkip.Inaccessible); if (depth == 0) unavailable = true; }
            }
            foreach (var root in accepted)
            {
                Walk(root, root.Root, 0);
                if (cancelled) break;
            }
            var status = cancelled || token.IsCancellationRequested ? CleanerScanStatus.Cancelled
                : accepted.Length == 0 ? CleanerScanStatus.Unavailable
                : incomplete ? CleanerScanStatus.Incomplete
                : !opened ? CleanerScanStatus.Unavailable
                : CleanerScanStatus.Complete;
            items.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.RelativePath, b.RelativePath));
            result[index] = new(category, status, items.AsReadOnly(), bytes, visited,
                new System.Collections.ObjectModel.ReadOnlyDictionary<CleanerSkip, long>(skips), accepted.Any(t => t.DefaultSelected),
                accepted.Length > 0 && accepted.All(t => t.RequiresElevation), accepted.Length == 0 || accepted.All(t => t.Review));
            Report(category, transition: true, status: status);
        });
        return new(Guid.NewGuid(), started, DateTimeOffset.UtcNow, Array.AsReadOnly(result));
    }
    private readonly record struct ScanEntry(string Path, FileAttributes Attributes, DateTimeOffset LastWrite);
    internal static void ForEachCategory(int count, int workers, Action<int> action) =>
        Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(workers, 1, 4) }, action);
}
