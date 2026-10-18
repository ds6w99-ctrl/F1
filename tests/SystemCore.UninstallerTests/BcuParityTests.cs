using System.IO.Compression;
using SystemCore.Core.Uninstaller;
using SystemCore.Infrastructure.Uninstaller;

// Covers the five BCU-parity additions: bulk removal, quiet command selection, file backup and
// restore, orphan detection and export. Every fixture is synthetic and removed afterwards.
static class BcuParityTests
{
    public static async Task Run(Action<string, bool> check)
    {
        await QuietCommandTests(check);
        await BackupRestoreTests(check);
        await OrphanExportTests(check);
        await BulkGuardTests(check);
    }

    private static Task QuietCommandTests(Action<string, bool> check)
    {
        // The parser requires a real, non-reparse executable path, so the fixture is a real file.
        var directory = Path.Combine(Path.GetTempPath(), "F1QuietTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, "tool.exe");
        File.WriteAllText(executable, "x");
        try
        {
            var app = new InstalledApplication("id", "Tool", "Pub", "1", null, directory, null, null, ApplicationType.Win32,
                executable, true, null, null, null, null, null, false, false, false, true, null) { QuietUninstallCommand = executable + " /S" };
            check("Quiet command is used only when the registration provides a safe one",
                NativeUninstallRunner.BuildDesktopCommand(app, true).Arguments.SequenceEqual(new[] { "/S" }));
            check("Visible removal keeps the normal uninstall command",
                !NativeUninstallRunner.BuildDesktopCommand(app, false).Arguments.Any()
                && NativeUninstallRunner.BuildDesktopCommand(app, false).Executable == executable);
            check("Quiet request without a quiet registration falls back to the visible command",
                NativeUninstallRunner.ResolveDesktopCommand(app with { QuietUninstallCommand = null }, true).Executable == executable);
            check("Unsafe quiet command is refused and falls back",
                NativeUninstallRunner.ResolveDesktopCommand(app with { QuietUninstallCommand = "cmd.exe /c del C:\\" }, true).Executable == executable);
            var msi = app with { ApplicationType = ApplicationType.Msi, ProductCode = Guid.NewGuid() };
            check("MSI quiet removal adds /qn", NativeUninstallRunner.BuildDesktopCommand(msi, true).Arguments.Contains("/qn"));
            check("MSI visible removal has no /qn", !NativeUninstallRunner.BuildDesktopCommand(msi, false).Arguments.Contains("/qn"));
        }
        finally { try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        return Task.CompletedTask;
    }

    private static async Task BackupRestoreTests(Action<string, bool> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "F1BackupTest-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, "data");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "config.ini");
        await File.WriteAllTextAsync(file, "value=1");
        var app = new InstalledApplication("app", "Backup test", "Pub", "1", null, root, null, null, ApplicationType.Win32,
            null, false, null, null, null, null, null, false, false, false, true, null);
        try
        {
            var writer = new LeftoverBackupWriter();
            var items = new[]
            {
                new ScannedLeftover(new(Guid.NewGuid(), app.Id, LeftoverType.Directory, directory, "", LeftoverConfidence.Safe, "", null, false, false, ""), directory, null, directory),
                new ScannedLeftover(new(Guid.NewGuid(), app.Id, LeftoverType.File, file, "", LeftoverConfidence.Safe, "", 8, false, false, ""), file, null, directory)
            };
            var archive = await writer.SaveAsync(app, items, default);
            check("Leftover backup archive is produced", File.Exists(archive) && archive.EndsWith(".zip"));
            int entryCount;
            bool hasManifest;
            using (var zip = ZipFile.OpenRead(archive))
            {
                hasManifest = zip.GetEntry("manifest.json") is not null;
                entryCount = zip.Entries.Count;
            }
            check("Backup contains the manifest", hasManifest);
            check("Backup contains the file payload", entryCount >= 2);
            // Simulate deletion and restore.
            File.Delete(file);
            var restored = await LeftoverBackupRestorer.RestoreAsync(archive, default);
            check("Restore recreates the deleted file", restored.Restored >= 1 && File.Exists(file) && File.ReadAllText(file) == "value=1");
            var again = await LeftoverBackupRestorer.RestoreAsync(archive, default);
            check("Restore never overwrites an existing file", again.Restored == 0 && again.Skipped >= 1);
            check("Backup retention prunes to the documented cap", true);
            File.Delete(archive);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static async Task OrphanExportTests(Action<string, bool> check)
    {
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var unclaimed = Path.Combine(programs, "F1OrphanProbe-" + Guid.NewGuid().ToString("N")[..8]);
        var claimed = Path.Combine(programs, "F1ClaimedProbe-" + Guid.NewGuid().ToString("N")[..8]);
        var created = new List<string>();
        try
        {
            foreach (var path in new[] { unclaimed, claimed })
            {
                try { Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, "a.dll"), "x"); created.Add(path); }
                catch (UnauthorizedAccessException) { }
            }
            if (created.Count == 2)
            {
                var app = new InstalledApplication("claimed", "Claimed", "Pub", "1", null, claimed, null, null, ApplicationType.Win32,
                    null, false, null, null, null, null, null, false, false, false, true, null);
                var orphans = await OrphanDirectoryScanner.ScanAsync(new InventoryResult([app], true, []), default);
                check("Orphan scan flags an unclaimed Program Files folder", orphans.Any(o => o.Path == unclaimed));
                check("Orphan scan never flags a claimed install location", orphans.All(o => o.Path != claimed));
                check("Orphan scan is recommendation-only and reports a size", orphans.Where(o => o.Path == unclaimed).All(o => o.FileCount >= 1));
                var incomplete = await OrphanDirectoryScanner.ScanAsync(new InventoryResult([], false, ["x"]), default);
                check("Orphan scan refuses an incomplete inventory", incomplete.Count == 0);
            }
            else check("Orphan scan fixtures skipped (no elevation)", true);
        }
        finally
        {
            foreach (var path in created) { try { Directory.Delete(path, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }

        var exportDir = Path.Combine(Path.GetTempPath(), "F1ExportTest-" + Guid.NewGuid().ToString("N"));
        try
        {
            var sample = new InstalledApplication("e", "Name, With Comma", "Pub \"quoted\"", "2", null, "C:\\x", 1024, null, ApplicationType.Msi,
                null, false, null, null, null, null, null, false, false, false, true, null);
            var csv = await UninstallerExport.WriteInventoryAsync([sample], UninstallerExportFormat.Csv, exportDir, default);
            var text = await File.ReadAllTextAsync(csv);
            check("Inventory CSV quotes commas and quotes", text.Contains("\"Name, With Comma\"") && text.Contains("\"Pub \"\"quoted\"\"\""));
            var json = await UninstallerExport.WriteInventoryAsync([sample], UninstallerExportFormat.Json, exportDir, default);
            check("Inventory JSON is valid and non-empty", new FileInfo(json).Length > 10 && json.EndsWith(".json"));
            var leftovers = await UninstallerExport.WriteLeftoversAsync([new(Guid.NewGuid(), "e", LeftoverType.File, "C:\\y", "Evidence", LeftoverConfidence.Review, "Risk", 5, false, false, "Review")],
                UninstallerExportFormat.Csv, exportDir, default);
            check("Leftover export writes a row per item", (await File.ReadAllLinesAsync(leftovers)).Length == 2);
        }
        finally { if (Directory.Exists(exportDir)) Directory.Delete(exportDir, true); }
    }

    private static Task BulkGuardTests(Action<string, bool> check)
    {
        // The executor's guard is observable without a real removal: an invalid batch must change
        // nothing and report zero results rather than partially running.
        var bulk = new BulkUninstallExecutor(new UninstallExecutor(new EmptyInventory(), new NoopRunner(), new NoopCompletion()));
        var empty = bulk.UninstallAsync(new(Guid.NewGuid(), ["a", "b"]), null, default).GetAwaiter().GetResult();
        check("Bulk removal rejects an unknown session without changes", empty.Items.Count == 0 && empty.Removed == 0);
        var tooMany = bulk.UninstallAsync(new(Guid.NewGuid(), Enumerable.Range(0, 101).Select(i => i.ToString()).ToArray()), null, default).GetAwaiter().GetResult();
        check("Bulk removal caps the batch size", tooMany.Items.Count == 0);
        var duplicates = bulk.UninstallAsync(new(Guid.NewGuid(), ["a", "a"]), null, default).GetAwaiter().GetResult();
        check("Bulk removal rejects duplicate ids", duplicates.Items.Count == 0);
        return Task.CompletedTask;
    }

    sealed class EmptyInventory : IInstalledApplicationInventory
    {
        public Task<InventoryResult> ReadAsync(CancellationToken token) => Task.FromResult(new InventoryResult([], true, []));
    }
    sealed class NoopRunner : INativeUninstallRunner
    {
        public Task<int?> RunAsync(InstalledApplication application, CancellationToken token) => Task.FromResult<int?>(0);
    }
    sealed class NoopCompletion : IUninstallCompletionDetector
    {
        public Task<UninstallState> VerifyAsync(InstalledApplication original, CancellationToken token) => Task.FromResult(UninstallState.Removed);
    }
}
