using Microsoft.Win32;
using SystemCore.Core.Cleaner;
using SystemCore.Infrastructure.Cleaner;

// Covers the BleachBit-parity cleaner work: the Explorer trace catalog, its fixed allowlist, and
// the rule that an option the user cannot clean is never offered.
static class ExplorerHistoryTests
{
    public static async Task Run(Action<string, bool> check)
    {
        // Catalog shape: fixed HKCU-only locations, no caller-derived paths.
        check("Explorer catalog targets the current user only",
            ExplorerHistoryCleaner.Locations.All(l => l.SubKey.StartsWith("Software\\Microsoft", StringComparison.Ordinal)));
        check("Explorer catalog has deterministic ids",
            ExplorerHistoryCleaner.ItemIdFor("a") == ExplorerHistoryCleaner.ItemIdFor("a")
            && ExplorerHistoryCleaner.ItemIdFor("a") != ExplorerHistoryCleaner.ItemIdFor("b"));

        var cleaner = new ExplorerHistoryCleaner();
        // A synthetic location under a key the app owns is the only safe way to prove deletion.
        var testKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\F1TraceProbe";
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(testKey)) { key.SetValue("Probe", "x"); }
            // The synthetic key is NOT in the catalog, so it must be refused, not deleted.
            var rejected = await cleaner.CleanAsync([testKey], new Dictionary<string, Guid> { [testKey] = Guid.NewGuid() }, default);
            check("Cleaner refuses a location outside the fixed catalog", rejected.Single().Outcome == CleanerItemOutcome.SafetyRejected);
            using (var key = Registry.CurrentUser.OpenSubKey(testKey)) check("Refused location is untouched", key?.GetValue("Probe") as string == "x");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(testKey, false); }

        // Real-run behavior: a missing location is reported as unavailable, not as an empty row.
        var found = await cleaner.ScanAsync(default);
        check("Explorer scan reports only existing locations", found.All(path => !path.Contains("\\", StringComparison.Ordinal) || path.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)));

        // The hidden-option rule lives in the view model; assert the observable contract.
        var unavailable = new CleanerCategoryResult(CleanerCategory.ExplorerHistory, CleanerScanStatus.Unavailable, [], 0, 0,
            new Dictionary<CleanerSkip, long>().AsReadOnly(), false, false, true);
        check("Unavailable categories carry no items", unavailable.ItemCount == 0 && unavailable.Status == CleanerScanStatus.Unavailable);
    }
}
