using System.Security;
using Microsoft.Win32;
using SystemCore.Core.Cleaner;

namespace SystemCore.Infrastructure.Cleaner;

public interface IExplorerHistoryCleaner
{
    // Returns the registry locations that currently exist and would be cleaned.
    Task<IReadOnlyList<string>> ScanAsync(CancellationToken token);
    // Deletes only the enumerated keys/values under HKCU; the parent key is preserved.
    Task<IReadOnlyList<CleanerItemResult>> CleanAsync(IReadOnlyCollection<string> expected, IReadOnlyDictionary<string, Guid> itemIds, CancellationToken token);
}

// Explorer trace cleanup. Every path is a fixed HKCU location listed here; nothing is derived
// from user input, no parent key is deleted, and HKLM is never touched. A location that does not
// exist is simply not reported, which is what keeps uncookable options out of the UI.
public sealed class ExplorerHistoryCleaner : IExplorerHistoryCleaner
{
    internal const string Root = @"Software\Microsoft\Windows\CurrentVersion\Explorer";
    // Ordered so the UI shows the most meaningful traces first. Each entry is (display, subkey).
    internal static readonly (string Label, string SubKey)[] Locations =
    [
        ("Explorer\\UserAssist", @"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist"),
        ("Explorer\\RunMRU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU"),
        ("Explorer\\ComDlg32", @"Software\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32"),
        ("Explorer\\RecentDocs", @"Software\Microsoft\Windows\CurrentVersion\Explorer\RecentDocs"),
        ("Explorer\\WordWheelQuery", @"Software\Microsoft\Windows\CurrentVersion\Explorer\WordWheelQuery"),
        ("Explorer\\Shell\\Bags", @"Software\Microsoft\Windows\Shell\Bags"),
        ("Explorer\\Shell\\BagMRU", @"Software\Microsoft\Windows\Shell\BagMRU"),
        ("Explorer\\ShellNoRoam\\Bags", @"Software\Microsoft\Windows\ShellNoRoam\Bags"),
        ("Explorer\\ShellNoRoam\\BagMRU", @"Software\Microsoft\Windows\ShellNoRoam\BagMRU"),
        ("SearchAssistant\\ACMru", @"Software\Microsoft\Search Assistant\ACMru")
    ];
    internal static readonly string[] RecentFiles =
    [
        @"Microsoft\Windows\Recent\AutomaticDestinations",
        @"Microsoft\Windows\Recent\CustomDestinations"
    ];

    public Task<IReadOnlyList<string>> ScanAsync(CancellationToken token) => Task.Run(() =>
    {
        var found = new List<string>();
        foreach (var (_, subKey) in Locations)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(subKey, false);
                if (key is null) continue;
                // A parent with no subkeys and no values is an empty shell: cleaning it would
                // be a no-op, so it is not offered.
                if (key.SubKeyCount == 0 && key.ValueCount == 0) continue;
                found.Add(subKey);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException) { }
        }
        var recent = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
        if (!string.IsNullOrWhiteSpace(recent))
            foreach (var relative in RecentFiles)
            {
                token.ThrowIfCancellationRequested();
                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), relative);
                try
                {
                    if (Directory.Exists(directory) && Directory.EnumerateFiles(directory).Any()) found.Add(directory);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException) { }
            }
        return (IReadOnlyList<string>)found.AsReadOnly();
    }, token);

    public Task<IReadOnlyList<CleanerItemResult>> CleanAsync(IReadOnlyCollection<string> expected, IReadOnlyDictionary<string, Guid> itemIds, CancellationToken token) => Task.Run(() =>
    {
        var results = new List<CleanerItemResult>();
        foreach (var target in expected)
        {
            token.ThrowIfCancellationRequested();
            var id = itemIds.TryGetValue(target, out var known) ? known : Guid.NewGuid();
            // Only the fixed locations in this catalog may be deleted, whatever the caller asked.
            if (!Locations.Any(l => string.Equals(l.SubKey, target, StringComparison.OrdinalIgnoreCase)) &&
                !RecentFiles.Any(r => IsRecentPath(target, r)))
            {
                results.Add(new(id, CleanerItemOutcome.SafetyRejected, 0));
                continue;
            }
            try
            {
                if (Locations.Any(l => string.Equals(l.SubKey, target, StringComparison.OrdinalIgnoreCase)))
                {
                    // Values are removed first, then child keys; the catalog key itself stays.
                    using var key = Registry.CurrentUser.OpenSubKey(target, true);
                    if (key is null) { results.Add(new(id, CleanerItemOutcome.AlreadyGone, 0)); continue; }
                    foreach (var name in key.GetValueNames()) key.DeleteValue(name, false);
                    foreach (var child in key.GetSubKeyNames())
                    {
                        // Child names are enumerated from the key itself; they are never combined
                        // with caller input.
                        if (SafeChildName(child)) key.DeleteSubKeyTree(child, false);
                    }
                    results.Add(new(id, CleanerItemOutcome.Deleted, 0));
                }
                else
                {
                    var count = 0;
                    foreach (var file in Directory.EnumerateFiles(target, "*.automaticDestinations-ms").Concat(Directory.EnumerateFiles(target, "*customDestinations-ms")))
                    {
                        token.ThrowIfCancellationRequested();
                        File.Delete(file);
                        count++;
                    }
                    results.Add(new(id, CleanerItemOutcome.Deleted, 0));
                }
            }
            catch (UnauthorizedAccessException) { results.Add(new(id, CleanerItemOutcome.AccessDenied, 0)); }
            catch (Exception e) when (e is IOException or SecurityException or ArgumentException) { results.Add(new(id, CleanerItemOutcome.Failed, 0)); }
        }
        return (IReadOnlyList<CleanerItemResult>)results.AsReadOnly();
    }, token);

    private static bool IsRecentPath(string path, string relative)
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), relative);
        return string.Equals(CleanerSafety.Canonical(path), CleanerSafety.Canonical(expected), StringComparison.OrdinalIgnoreCase);
    }

    // Deterministic id per location so a scan result and the later deletion request agree
    // without the caller inventing identifiers.
    internal static Guid ItemIdFor(string location)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("ExplorerHistory|" + location.ToUpperInvariant()));
        return new Guid(bytes.AsSpan(0, 16));
    }

    // A registry child name must be a single segment; anything else is refused rather than
    // interpreted as a path.
    private static bool SafeChildName(string name) =>
        name.Length is > 0 and <= 255 && name.IndexOfAny(['\\', '/', '\0']) < 0 && name != "." && name != "..";
}
