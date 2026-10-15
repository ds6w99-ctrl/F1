using System.ComponentModel;
using SystemCore.Core.Cleaner;

namespace SystemCore.Infrastructure.Uninstaller;

// A bounded, read-only observation of the directories that sit beneath the places an application
// keeps data. Removing or renaming any direct child updates the parent's own last-write time, so a
// snapshot taken immediately before and immediately after the native uninstaller runs identifies
// the directories the uninstaller actually worked on. That is the one signal name matching cannot
// produce: it establishes ownership of a data directory even when the directory is named nothing
// like the application. The observation never follows a reparse point and never fails the removal:
// a directory it cannot read is skipped, and a gap can only narrow the observation, because a
// directory missing from either snapshot is simply not compared.
internal sealed class LeftoverFootprint
{
    // Two levels reach the vendor\product layout, and a deletion one level deeper still moves the
    // parent that is already recorded. Deeper levels cost enumeration for no extra certainty.
    internal const int MaximumDepth = 2;
    internal const int MaximumDirectories = 16384;
    private static readonly string[] ObservedRoots = new[] { Environment.SpecialFolder.LocalApplicationData,
        Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.CommonApplicationData }
        .Select(Environment.GetFolderPath).Where(root => !string.IsNullOrWhiteSpace(root)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private readonly IReadOnlyDictionary<string, long> observed;

    private LeftoverFootprint(IReadOnlyDictionary<string, long> observed) => this.observed = observed;

    internal static LeftoverFootprint Snapshot(CancellationToken token)
    {
        var found = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in ObservedRoots)
        {
            token.ThrowIfCancellationRequested();
            Collect(root, 0);
        }
        return new(found);
        void Collect(string directory, int depth)
        {
            if (depth > MaximumDepth || found.Count >= MaximumDirectories) return;
            try
            {
                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    token.ThrowIfCancellationRequested();
                    if (found.Count >= MaximumDirectories) return;
                    var info = new DirectoryInfo(child);
                    // A junction inside an observed root must never widen the observation.
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if (CleanerSafety.Canonical(info.FullName) is not { } canonical) continue;
                    found[canonical] = info.LastWriteTimeUtc.Ticks;
                    Collect(canonical, depth + 1);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or Win32Exception or System.Security.SecurityException) { }
        }
    }

    // Directories that still exist and whose own last-write time moved while the uninstaller ran.
    // Only the outermost directory of a branch is kept: walking it already covers the inner ones,
    // and no anchor is created for a directory the uninstaller did not provably touch.
    internal IReadOnlyList<string> Modified(LeftoverFootprint after)
    {
        var moved = new List<string>();
        foreach (var before in observed)
            if (after.observed.TryGetValue(before.Key, out var current) && current != before.Value) moved.Add(before.Key);
        // A parent path is always shorter than its children, so ordering by length visits every
        // ancestor first and one pass removes exactly the inner directories.
        var anchors = new List<string>();
        foreach (var candidate in moved.OrderBy(path => path.Length).Take(256))
            if (!anchors.Any(outer => CleanerSafety.IsChild(outer, candidate))) anchors.Add(candidate);
        return anchors;
    }
}
