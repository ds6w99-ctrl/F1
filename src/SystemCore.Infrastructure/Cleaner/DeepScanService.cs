using System.ComponentModel;
using System.Security;
using SystemCore.Core.Cleaner;

namespace SystemCore.Infrastructure.Cleaner;

public sealed record DeepScanCandidate(string Path, long Size, string Reason);

public interface IDeepScanService
{
    // Explicit, opt-in and bounded. Only the fixed pattern catalog below is matched.
    Task<IReadOnlyList<DeepScanCandidate>> ScanAsync(IReadOnlyList<string> roots, CancellationToken token);
    // Deletes only reviewed candidates, one by one, through the pinned-handle path.
    Task<IReadOnlyList<CleanerItemResult>> RemoveAsync(IReadOnlyCollection<string> paths, CancellationToken token);
}

// Deep scan finds developer/backup litter that is scattered across user folders. It is a separate
// opt-in mode because it walks trees that the conservative cleaner never touches, so it is
// bounded hard: fixed roots, fixed patterns, a hard file budget, a hard time budget and no
// deletion. Every finding is shown for review; the user must select it before anything is removed.
public sealed class DeepScanService : IDeepScanService
{
    internal const int MaximumFindings = 2000;
    internal const int MaximumVisited = 200000;
    internal static readonly TimeSpan MaximumDuration = TimeSpan.FromSeconds(60);
    // The complete pattern catalog. Names are matched case-insensitively against the leaf only.
    internal static readonly string[] DirectoryPatterns = ["__pycache__", "node_modules", ".venv", "venv"];
    internal static readonly string[] FilePatterns = ["Thumbs.db", "*.bak", "*.tmp", "*.swp", "*.swo"];

    public Task<IReadOnlyList<DeepScanCandidate>> ScanAsync(IReadOnlyList<string> roots, CancellationToken token) => Task.Run(() =>
    {
        var findings = new List<DeepScanCandidate>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        long visited = 0;
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var candidateRoot in roots.Take(20))
        {
            token.ThrowIfCancellationRequested();
            // A root must be a canonical directory inside the user's profile. Nothing else is walked,
            // so a caller cannot point the deep scan at a system tree.
            if (CleanerSafety.Canonical(candidateRoot) is not { } root) continue;
            var profile = CleanerSafety.Canonical(userProfile);
            if (profile is null || !(string.Equals(root, profile, StringComparison.OrdinalIgnoreCase) || CleanerSafety.IsChild(profile, root))) continue;
            if (!Directory.Exists(root)) continue;
            Walk(root, 0);
            if (findings.Count >= MaximumFindings || clock.Elapsed > MaximumDuration) break;
        }
        return (IReadOnlyList<DeepScanCandidate>)findings.AsReadOnly();

        void Walk(string directory, int depth)
        {
            if (depth > 24 || findings.Count >= MaximumFindings || visited >= MaximumVisited || clock.Elapsed > MaximumDuration || token.IsCancellationRequested) return;
            try
            {
                // Directory matches are reported as a whole subtree and not descended into, which
                // keeps the preview readable for node_modules-style trees.
                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    if (++visited >= MaximumVisited || findings.Count >= MaximumFindings || clock.Elapsed > MaximumDuration) return;
                    var name = Path.GetFileName(child);
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                    if (DirectoryPatterns.Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        findings.Add(new(child, Measure(child), "Development cache directory: " + name));
                        continue;
                    }
                    if (Protected(name)) continue;
                    Walk(child, depth + 1);
                }
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    if (++visited >= MaximumVisited || findings.Count >= MaximumFindings || clock.Elapsed > MaximumDuration) return;
                    var name = Path.GetFileName(file);
                    var matched = FilePatterns.FirstOrDefault(p => p.StartsWith('*') ? name.EndsWith(p[1..], StringComparison.OrdinalIgnoreCase) : string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
                    if (matched is null) continue;
                    try
                    {
                        var info = new FileInfo(file);
                        if ((info.Attributes & (FileAttributes.ReparsePoint | FileAttributes.System)) != 0) continue;
                        // A temporary file that is still being written is not offered.
                        if (DateTime.UtcNow - info.LastWriteTimeUtc < TimeSpan.FromHours(1)) continue;
                        findings.Add(new(file, info.Length, "Matched " + matched));
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException) { }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException) { }
        }
    }, token);

    public Task<IReadOnlyList<CleanerItemResult>> RemoveAsync(IReadOnlyCollection<string> paths, CancellationToken token) => Task.Run(() =>
    {
        var output = new List<CleanerItemResult>();
        foreach (var path in paths.Take(MaximumFindings))
        {
            token.ThrowIfCancellationRequested();
            var id = Guid.NewGuid();
            if (CleanerSafety.Canonical(path) is not { } canonical) { output.Add(new(id, CleanerItemOutcome.SafetyRejected, 0)); continue; }
            try
            {
                // Directories are never deleted by the deep scan: a cache tree is reported as a
                // whole but removal stays a reviewed file-by-file action.
                if (Directory.Exists(canonical)) { output.Add(new(id, CleanerItemOutcome.SafetyRejected, 0)); continue; }
                using var file = PinnedCleanerPath.Open(canonical, false, deleteAccess: true);
                if (!CleanerSafety.SafeFileAttributes(file.Attributes, file.Identity.Links)) { output.Add(new(id, CleanerItemOutcome.SafetyRejected, 0)); continue; }
                var size = file.Identity.Size;
                file.MarkFileForDeletion();
                output.Add(new(id, CleanerItemOutcome.Deleted, size));
            }
            catch (UnauthorizedAccessException) { output.Add(new(id, CleanerItemOutcome.AccessDenied, 0)); }
            catch (Win32Exception ex) { output.Add(new(id, ex.NativeErrorCode is 2 or 3 ? CleanerItemOutcome.AlreadyGone : ex.NativeErrorCode is 32 or 33 ? CleanerItemOutcome.InUse : CleanerItemOutcome.Failed, 0)); }
            catch (IOException) { output.Add(new(id, CleanerItemOutcome.Failed, 0)); }
        }
        return (IReadOnlyList<CleanerItemResult>)output.AsReadOnly();
    }, token);

    private static long Measure(string directory)    {
        long bytes = 0;
        try { foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Take(50000)) { try { bytes += new FileInfo(file).Length; } catch (IOException) { } } }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException) { }
        return bytes;
    }

    // Never descend into these even though they sit under the profile.
    private static bool Protected(string name) => name.Equals("AppData", StringComparison.OrdinalIgnoreCase)
        || name.Equals(".git", StringComparison.OrdinalIgnoreCase)
        || name.Equals(".ssh", StringComparison.OrdinalIgnoreCase)
        || name.Equals(".gnupg", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Documents", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Desktop", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Downloads", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Pictures", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Videos", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Music", StringComparison.OrdinalIgnoreCase)
        || name.Equals("OneDrive", StringComparison.OrdinalIgnoreCase);
}
