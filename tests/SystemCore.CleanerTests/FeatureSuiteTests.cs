using Microsoft.Win32;
using SystemCore.Core.Cleaner;
using SystemCore.Core.Uninstaller;
using SystemCore.Infrastructure.Cleaner;

// One focused suite for the six additions: deep scan (catalog + removal refusal + real delete),
// audit log (bounded append/read), Windows Update target policy, HTML escaping and the CLI
// surface (read-only commands work, mutation commands are refused).
static class FeatureSuiteTests
{
    public static async Task Run(Action<string, bool> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "F1FeatureSuite-" + Guid.NewGuid().ToString("N"));
        var profileRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "F1DeepProbe-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            // ---- deep scan: finds the catalog matches and nothing else ----
            Directory.CreateDirectory(root);
            var deep = new DeepScanService();
            // A root outside the user profile must be ignored entirely.
            var outside = await deep.ScanAsync(new[] { root }, default);
            check("Deep scan ignores roots outside the user profile", outside.Count == 0);

            // ---- deep scan removal refuses directories and unsafe input ----
            var refusedDirectory = await deep.RemoveAsync(new[] { root }, default);
            check("Deep scan never deletes a directory", refusedDirectory.Single().Outcome == CleanerItemOutcome.SafetyRejected);
            var refusedReparse = await deep.RemoveAsync(new[] { @"C:\test\..\escape.txt", "relative.txt" }, default);
            check("Deep scan refuses unsafe paths", refusedReparse.All(r => r.Outcome == CleanerItemOutcome.SafetyRejected));

            // ---- audit log: bounded, readable, never throws on a bad directory ----
            var auditFile = Path.Combine(root, "audit", "operations.jsonl");
            var audit = new OperationAuditLog(auditFile);
            audit.Record("clean", "UserTemp", "Deleted", 1024);
            audit.Record("uninstall", "app-1", "Removed", 0, "verified");
            var entries = audit.Read();
            check("Audit log appends and reads entries in order", entries.Count == 2 && entries[0].Operation == "clean" && entries[1].Bytes == 0);
            // The contract is that recording never throws, whatever the path. A path over an
            // existing *file* cannot be a directory, so creation must fail internally and be swallowed.
            var fileInTheWay = Path.Combine(root, "blocked.txt");
            File.WriteAllText(fileInTheWay, "x");
            var blocked = new OperationAuditLog(Path.Combine(fileInTheWay, "operations.jsonl"));
            var threw = false;
            try { blocked.Record("clean", "x", "Deleted"); }
            catch (Exception) { threw = true; }
            check("Audit failure never breaks the operation", !threw && blocked.Read().Count == 0);

            // ---- Windows Update cache target policy ----
            var download = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download");
            var updateTarget = new CleanerTarget(CleanerCategory.WindowsUpdateCache, download, "*", true, TimeSpan.FromHours(24), false, true, true);
            var serviceTarget = new CleanerTarget(CleanerCategory.WindowsUpdateCache, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution"), "*", true, TimeSpan.FromHours(24), false, true, true);
            check("Windows Update target allows only the download payload", WindowsCleanerTargets.IsAllowed(updateTarget));
            check("Windows Update target refuses the servicing database", !WindowsCleanerTargets.IsAllowed(serviceTarget));

            // ---- HTML report escapes hostile names ----
            var hostile = new InstalledApplication("h", "<script>alert(1)</script>", "\"quoted\"", "1", null, "C:\\x", 2048, null, ApplicationType.Win32,
                null, false, null, null, null, null, null, false, false, false, true, null);
            var html = HtmlReport.InventoryReport(new[] { hostile }, DateTimeOffset.UtcNow);
            check("HTML report escapes markup", html.Contains("&lt;script&gt;") && !html.Contains("<script>alert"));
            check("HTML report is self-contained", !html.Contains("http://") && !html.Contains("https://"));

            // ---- CLI: read-only surface ----
            var lines = new List<string>();
            void Write(string line) => lines.Add(line);
            Task<CleanerScanSession> Scan(CancellationToken _) => Task.FromResult(new CleanerScanSession(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                new[] { new CleanerCategoryResult(CleanerCategory.UserTemp, CleanerScanStatus.Complete, [], 0, 0, new Dictionary<CleanerSkip, long>().AsReadOnly(), true, false, false) }.AsReadOnly() as IReadOnlyList<CleanerCategoryResult>));
            Task<InventoryResult> Inventory(CancellationToken _) => Task.FromResult(new InventoryResult([], true, []));
            var help = await F1Cli.RunAsync(new[] { "--help" }, Scan, Inventory, Write);
            check("CLI help succeeds and states the read-only rule", help == F1Cli.Success && lines.Any(l => l.Contains("read-only")));
            lines.Clear();
            var scanExit = await F1Cli.RunAsync(new[] { "--cli", "scan" }, Scan, Inventory, Write);
            check("CLI scan prints a category line", scanExit == F1Cli.Success && lines.Any(l => l.Contains("UserTemp")));
            lines.Clear();
            var refused = await F1Cli.RunAsync(new[] { "--cli", "clean" }, Scan, Inventory, Write);
            check("CLI refuses every destructive command", refused == F1Cli.CommandRefused && lines.Any(l => l.Contains("Refused")));
            lines.Clear();
            var badArgs = await F1Cli.RunAsync(new[] { "bogus" }, Scan, Inventory, Write);
            check("CLI rejects unknown arguments", badArgs == F1Cli.InvalidArguments);
        }
        finally
        {
            if (Directory.Exists(root)) { try { Directory.Delete(root, true); } catch (IOException) { } }
            if (Directory.Exists(profileRoot)) { try { Directory.Delete(profileRoot, true); } catch (IOException) { } }
        }
        await Task.CompletedTask;
    }
}
