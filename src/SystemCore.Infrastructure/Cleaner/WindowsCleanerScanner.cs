using System.ComponentModel;
using System.IO.Enumeration;
using SystemCore.Core.Cleaner;

namespace SystemCore.Infrastructure.Cleaner;

public sealed class WindowsCleanerScanner(ICleanerTargetProvider targets) : ICleanerScanner
{
    public const int MaximumCategoryEntries = 200000;
    public const int MaximumTotalEntries = 500000;
    public const int MaximumDepth = 32;
    internal int CategoryLimit { get; init; } = MaximumCategoryEntries;
    public Task<CleanerScanSession> ScanAsync(CancellationToken token) => Task.Run(() => Scan(token), CancellationToken.None);
    private CleanerScanSession Scan(CancellationToken token)
    {
        var started = DateTimeOffset.UtcNow;
        var result = new List<CleanerCategoryResult>();
        IReadOnlyList<CleanerTarget> roots;
        try { roots = targets.Resolve(); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Core.Diagnostics.ReliabilityDiagnostics.Log.UnexpectedFailure((int)Core.Diagnostics.ReliabilityComponent.Cleaner, ex.HResult);
            roots = [];
        }
        long totalVisited = 0, totalBytes = 0;
        foreach (var category in Enum.GetValues<CleanerCategory>())
        {
            var matches = roots.Where(t => t.Category == category).ToArray();
            var root = matches.Length == 1 && WindowsCleanerTargets.IsAllowed(matches[0]) ? matches[0] : null;
            var items = new List<CleanerItem>();
            var skips = new Dictionary<CleanerSkip, long>();
            long bytes = 0, visited = 0;
            var status = root is null ? CleanerScanStatus.Unavailable : CleanerScanStatus.Complete;
            void Skip(CleanerSkip reason) => skips[reason] = skips.GetValueOrDefault(reason) + 1;
            void Walk(string directory, int depth)
            {
                if (token.IsCancellationRequested) { status = CleanerScanStatus.Cancelled; return; }
                if (depth > MaximumDepth) { status = CleanerScanStatus.Incomplete; Skip(CleanerSkip.Limit); return; }
                try
                {
                    if (!PinnedCleanerPath.TryOpenRead(directory, true, out var pinned, out var directoryFailure))
                    {
                        Skip(directoryFailure == PinFailure.Reparse ? CleanerSkip.Reparse : CleanerSkip.Inaccessible);
                        status = directoryFailure == PinFailure.Reparse || depth > 0 ? CleanerScanStatus.Incomplete : CleanerScanStatus.Unavailable;
                        return;
                    }
                    using var directoryLease = pinned;
                    foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                    {
                        if (token.IsCancellationRequested) { status = CleanerScanStatus.Cancelled; return; }
                        if (visited >= Math.Clamp(CategoryLimit, 1, MaximumCategoryEntries) || totalVisited >= MaximumTotalEntries)
                        { status = CleanerScanStatus.Incomplete; Skip(CleanerSkip.Limit); return; }
                        visited++; totalVisited++;
                        if (!CleanerSafety.IsChild(root!.Root, entry)) { Skip(CleanerSkip.UnsafePath); continue; }
                        try
                        {
                            var attributes = File.GetAttributes(entry);
                            if ((attributes & FileAttributes.ReparsePoint) != 0) { Skip(CleanerSkip.Reparse); continue; }
                            if ((attributes & FileAttributes.Directory) != 0)
                            { if (root.Recurse) Walk(entry, depth + 1); else Skip(CleanerSkip.UnsupportedType); continue; }
                            if (!FileSystemName.MatchesSimpleExpression(root.Pattern, Path.GetFileName(entry), true)) { Skip(CleanerSkip.UnsupportedType); continue; }
                            if (!PinnedCleanerPath.TryOpenRead(entry, false, out var file, out var fileFailure))
                            { Skip(fileFailure == PinFailure.Reparse ? CleanerSkip.Reparse : CleanerSkip.Inaccessible); continue; }
                            using var fileLease = file;
                            if (file.Identity.Links != 1) { Skip(CleanerSkip.HardLink); continue; }
                            if (!CleanerSafety.SafeFileAttributes(file.Attributes, file.Identity.Links)) { Skip(CleanerSkip.UnsupportedType); continue; }
                            if (!CleanerSafety.Eligible(file.Identity.Size, file.Identity.LastWriteFileTime, started, root.MinimumAge))
                            { Skip(CleanerSkip.Recent); continue; }
                            if (!CleanerSafety.TryAddSize(bytes, file.Identity.Size, out var sum) || !CleanerSafety.TryAddSize(totalBytes, file.Identity.Size, out var grandTotal))
                            { status = CleanerScanStatus.Incomplete; Skip(CleanerSkip.InvalidMetadata); return; }
                            bytes = sum;
                            totalBytes = grandTotal;
                            items.Add(new(Guid.NewGuid(), category, Path.GetRelativePath(root.Root, entry), entry,
                                file.Identity.Size, file.Identity.LastWriteFileTime, file.Identity));
                        }
                        catch (ReparseRejectedException) { Skip(CleanerSkip.Reparse); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or OverflowException)
                        { Skip(CleanerSkip.Inaccessible); }
                    }
                }
                catch (ReparseRejectedException) { Skip(CleanerSkip.Reparse); status = CleanerScanStatus.Incomplete; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
                { Skip(CleanerSkip.Inaccessible); if (depth == 0) status = CleanerScanStatus.Unavailable; }
            }
            if (root is not null) Walk(root.Root, 0);
            items.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.RelativePath, b.RelativePath));
            result.Add(new(category, status, items.AsReadOnly(), bytes, visited,
                new System.Collections.ObjectModel.ReadOnlyDictionary<CleanerSkip, long>(skips), root?.DefaultSelected ?? false,
                root?.RequiresElevation ?? false, root?.Review ?? true));
        }
        return new(Guid.NewGuid(), started, DateTimeOffset.UtcNow, result.AsReadOnly());
    }
}
