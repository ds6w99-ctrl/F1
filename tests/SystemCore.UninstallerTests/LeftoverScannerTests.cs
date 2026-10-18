using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Text;
using SystemCore.Core.Uninstaller;
using SystemCore.Infrastructure.Uninstaller;

static class LeftoverScannerTests
{
    public static async Task Run(Action<string, bool> check, bool deletion = false)
    {
        var name = "SystemCoreLeftoverTest-" + Guid.NewGuid().ToString("N");
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), name);
        var outside = root + "-outside";
        var data = root + "-data";
        var keyPath = "Software\\" + name;
        var roaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), name);
        Directory.CreateDirectory(root); Directory.CreateDirectory(outside); Directory.CreateDirectory(data);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "SystemCore.UninstallerTests.exe"), Path.Combine(root, "uninstall.exe"));
        File.WriteAllText(Path.Combine(root, "owned.dll"), "owned");
        File.WriteAllText(Path.Combine(outside, "retain.txt"), "retain");
        File.WriteAllText(Path.Combine(data, "data.txt"), "app data");
        using var user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        try
        {
            using (var key = user.CreateSubKey(keyPath))
            {
                key.SetValue("Publisher", "SystemCore tests"); key.SetValue("InstallLocation", root); key.SetValue("DataPath", data);
                key.SetValue("VersionMajor", 42, RegistryValueKind.DWord);
            }
            var app = new InstalledApplication(name, name, "SystemCore tests", "1", null, root, null, null, ApplicationType.Win32,
                $"\"{Path.Combine(root, "uninstall.exe")}\"", false, null, null, null, new(InventoryHive.CurrentUser, InventoryView.Registry64, name), null,
                false, false, false, true, null);
            var before = new InventoryResult([app], true, []);
            var store = new ApplicationRemovalEvidence();
            await store.CaptureAsync(app, before, default);
            var receipt = store.ConfirmRemoved(app.Id)!;
            var inventory = new Inventory();
            var scanner = new LeftoverScanner(store, inventory);
            var scanClock = System.Diagnostics.Stopwatch.StartNew();
            var scan = await scanner.ScanAsync(receipt.Id, default);
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { Metric = "SyntheticLeftoverScan", Milliseconds = scanClock.Elapsed.TotalMilliseconds, Items = scan.Items.Count,
                Inventory = "Deterministic complete empty fixture inventory" }));
            check("Exact install directory with executable evidence discovered", scan.Complete && scan.Items.Any(i => i.DisplayLocation == Path.Combine(root, "owned.dll") && i.Confidence == LeftoverConfidence.Safe));
            check("Exact app-specific registry identity discovered", scan.Items.Any(i => i.Type == LeftoverType.RegistryKey && i.Confidence == LeftoverConfidence.Safe));
            check("Registry selection always defaults off", scan.Items.Where(i => i.Type == LeftoverType.RegistryKey).All(i => !i.SelectedByDefault));
            check("Strong registry DataPath discovered", scan.Items.Any(i => i.DisplayLocation == Path.Combine(data, "data.txt")));
            check("DataPath is personal-data Review, never default selected", scan.Items.Where(i => i.DisplayLocation.StartsWith(data, StringComparison.Ordinal)).All(i => i.Confidence == LeftoverConfidence.Review && !i.SelectedByDefault));
            check("Every leftover defaults off", scan.Items.All(i => !i.SelectedByDefault));
            // F1-005: an MSI product's install location is anchored when the Windows Installer
            // API verified the product, so residuals the MSI uninstaller left behind are found.
            var msiRoot = root + "-msi";
            Directory.CreateDirectory(msiRoot);
            File.WriteAllText(Path.Combine(msiRoot, "untracked.dll"), "msi residual");
            File.WriteAllText(Path.Combine(msiRoot, "user-notes.txt"), "notes");
            var msiProduct = Guid.NewGuid();
            var msiApp = new InstalledApplication(name + "-msi", name + " (MSI)", "SystemCore tests", "2", null, msiRoot, null, null, ApplicationType.Msi,
                null, false, msiProduct, null, null, new(InventoryHive.LocalMachine, InventoryView.Registry64, msiProduct.ToString("B")), null,
                false, false, false, true, null);
            var msiStore = new ApplicationRemovalEvidence();
            await msiStore.CaptureAsync(msiApp, new([msiApp], true, []), default);
            var msiScan = await new LeftoverScanner(msiStore, new Inventory()).ScanAsync(msiStore.ConfirmRemoved(msiApp.Id)!.Id, default);
            check("MSI install location anchored and residuals discovered (F1-005)", msiScan.Items.Any(i => i.DisplayLocation == Path.Combine(msiRoot, "untracked.dll")));
            check("MSI install directory carries the MSI anchor reason (F1-005)", msiScan.Items.Any(i => i.DisplayLocation == msiRoot && i.Evidence.Contains("Windows Installer verified product code", StringComparison.Ordinal)));
            Directory.Delete(msiRoot, true);
            var saves = Path.Combine(root, "Save Games"); Directory.CreateDirectory(saves);
            var save = Path.Combine(saves, "slot.bin"); File.WriteAllText(save, "personal save");
            var config = Path.Combine(root, "settings.ini"); File.WriteAllText(config, "personal config");
            var opaque = Path.Combine(root, "opaque.unknown"); File.WriteAllText(opaque, "unknown user content");
            var hardLink = Path.Combine(saves, "linked.sav");
            if (!CreateHardLink(hardLink, Path.Combine(outside, "retain.txt"), IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var personalScan = await scanner.ScanAsync(receipt.Id, default);
            check("Save folder and descendants require Review", personalScan.Items.Where(i => i.DisplayLocation == saves || i.DisplayLocation == save).Count(i => i.Confidence == LeftoverConfidence.Review && !i.SelectedByDefault) == 2);
            check("Config extension requires Review", personalScan.Items.Any(i => i.DisplayLocation == config && i.Confidence == LeftoverConfidence.Review));
            check("Unknown file format fails conservatively to Review", personalScan.Items.Any(i => i.DisplayLocation == opaque && i.Confidence == LeftoverConfidence.Review));
            check("Hard link remains Ignore even under personal-data path", personalScan.Items.Any(i => i.DisplayLocation == hardLink && i.Confidence == LeftoverConfidence.Ignore));
            var personalDeletion = new LeftoverDeletionService(scanner, store, inventory, new RegistryBackupWriter());
            var rejectedPersonal = await personalDeletion.DeleteAsync(new(personalScan.Id, app.Id, personalScan.Items.Where(i => i.DisplayLocation == save || i.DisplayLocation == hardLink).Select(i => i.Id).ToArray()), default);
            check("Forged Review and Ignore selection is rejected by deletion service", rejectedPersonal.Items.All(i => i.Status == LeftoverDeletionStatus.SafetyRejected) && File.Exists(save) && File.Exists(hardLink));
            File.Delete(hardLink); File.Delete(save); Directory.Delete(saves); File.Delete(config); File.Delete(opaque);
            foreach (var shared in new[] { "Steam", "Epic", "EA", "Ubisoft", "Battle.net", "Launcher", "shadercache", "Shader Cache", "Anti-Cheat", "EasyAntiCheat", "Redistributables", "_CommonRedist", "Mods", "Workshop", "workshop\\content" })
            {
                var sharedPath = Path.Combine(root, shared);
                check("Shared game leaf rejected: " + shared, !LeftoverSafety.CanOwnDirectory(app with { ItemKind = ItemKind.Game }, sharedPath, []));
            }
            check("Similar-prefix sibling not traversed", scan.Items.All(i => !i.DisplayLocation.Contains(outside, StringComparison.Ordinal)));
            using (var preserved = user.OpenSubKey(keyPath)) check("Read-only scan preserves exact files and registry", File.ReadAllText(Path.Combine(root, "owned.dll")) == "owned" && preserved is not null);
            var invalid = false;
            try { await scanner.ScanAsync(Guid.NewGuid(), default); } catch (InvalidOperationException) { invalid = true; }
            check("Unknown receipt rejected", invalid);
            inventory.Result = before;
            invalid = false;
            try { await scanner.ScanAsync(receipt.Id, default); } catch (InvalidOperationException) { invalid = true; }
            check("Still-installed identity prevents scan", invalid);
            inventory.Result = new([], false, ["unavailable"]);
            invalid = false;
            try { await scanner.ScanAsync(receipt.Id, default); } catch (InvalidOperationException) { invalid = true; }
            check("Incomplete inventory prevents absence assertion", invalid);
            inventory.Result = new([app with { Id = "another-app", InstallLocation = root, DisplayName = "Another app" }], true, []);
            scan = await scanner.ScanAsync(receipt.Id, default);
            check("Conflicting installed application owns install folder", scan.Items.All(i => i.DisplayLocation != Path.Combine(root, "owned.dll")));
            inventory.Result = new([], true, []);
            using (var locked = new FileStream(Path.Combine(root, "owned.dll"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                scan = await scanner.ScanAsync(receipt.Id, default);
                check("Locked file makes scan incomplete without authorization", !scan.Complete && scan.Items.All(i => i.DisplayLocation != Path.Combine(root, "owned.dll")));
            }
            var link = Path.Combine(root, "escape");
            Junction.Create(link, outside);
            scan = await scanner.ScanAsync(receipt.Id, default);
            check("Real junction is safety rejected", scan.Items.Any(i => i.DisplayLocation == link && i.Confidence == LeftoverConfidence.Ignore));
            check("Junction target remains unvisited", scan.Items.All(i => !i.DisplayLocation.EndsWith("retain.txt", StringComparison.Ordinal)));
            Directory.Delete(link);
            using (var key = user.OpenSubKey(keyPath, true)) key!.SetValue("VersionMajor", 43);
            scan = await scanner.ScanAsync(receipt.Id, default);
            check("Changed registry snapshot not accepted", scan.Items.All(i => i.Type != LeftoverType.RegistryKey));
            foreach (var path in new[] { @"C:\", Environment.GetFolderPath(Environment.SpecialFolder.Windows), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"\\server\share\app", root + "\\..\\other", root + ":ads" })
                check("Protected/invalid root denied", !LeftoverSafety.CanOwnDirectory(app, path, []));
            check("Shared vendor directory denied", !LeftoverSafety.CanOwnDirectory(app, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google"), []));
            check("Shared runtime leftovers ignored", !LeftoverSafety.CanOwnDirectory(app with { DisplayName = ".NET Runtime" }, root, []));
            foreach (var protectedChild in new[] { "Packages\\Synthetic", "Microsoft\\Windows\\Synthetic", "SystemCore\\Backups", "Temp\\Synthetic" })
                check("Protected subtree rejected even with matching registration", !LeftoverSafety.CanOwnDirectory(app, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), protectedChild), []));
            foreach (var forbidden in new[] { "CLSID", "Services", "Installer", "Policies", "SharedDLLs", "KnownDLLs", "x\\y", ".." }) check("Protected registry segment rejected", !LeftoverSafety.SafeRegistryName(forbidden));
            var noEvidence = new ApplicationRemovalEvidence();
            var weak = app with { Id = "weak", InstallLocation = outside };
            await noEvidence.CaptureAsync(weak, new([weak], true, []), default);
            scan = await new LeftoverScanner(noEvidence, inventory).ScanAsync(noEvidence.ConfirmRemoved(weak.Id)!.Id, default);
            check("Name/location without executable or registry evidence cannot authorize directory", scan.Items.All(i => i.Type is not LeftoverType.File and not LeftoverType.Directory));
            await CheckRegistryLink(app, user, check);
            foreach (var personal in new[] { "save-games\\slot.bin", "Screenshots\\image.bin", "Mods\\a.bin", "User-generated content\\a.bin", "Profiles\\a.bin", "Config\\a.bin", "a.cfg", "a.json" })
                check("Personal candidate classified conservatively: " + personal, LeftoverSafety.MayContainPersonalData(personal));
            using (var key = user.OpenSubKey(keyPath, true)) key!.SetValue("ProfileName", "Synthetic personal profile");
            var profileStore = new ApplicationRemovalEvidence();
            await profileStore.CaptureAsync(app, before, default);
            var profileScanner = new LeftoverScanner(profileStore, inventory);
            var profileScan = await profileScanner.ScanAsync(profileStore.ConfirmRemoved(app.Id)!.Id, default);
            var profileItem = profileScan.Items.Single(i => i.Type == LeftoverType.RegistryKey);
            check("Registry personal settings require non-default Review", profileItem.Confidence == LeftoverConfidence.Review && !profileItem.SelectedByDefault);
            var profileResult = await new LeftoverDeletionService(profileScanner, profileStore, inventory, new RegistryBackupWriter()).DeleteAsync(new(profileScan.Id, app.Id, [profileItem.Id]), default);
            using (var key = user.OpenSubKey(keyPath)) check("Review registry cannot be deleted even by forged request", profileResult.Items.Single().Status == LeftoverDeletionStatus.SafetyRejected && (string?)key?.GetValue("ProfileName") == "Synthetic personal profile");
            using (var key = user.OpenSubKey(keyPath, true)) key!.DeleteValue("ProfileName");
            // The pre-uninstall footprint is captured while the application is still installed,
            // so the conservative scan sees it without any name guessing. Its link to the
            // application is still only the name, so every footprint item stays Review.
            Directory.CreateDirectory(roaming);
            File.WriteAllText(Path.Combine(roaming, "settings.cfg"), "cfg");
            var footStore = new ApplicationRemovalEvidence();
            await footStore.CaptureAsync(app, before, default);
            var footScanner = new LeftoverScanner(footStore, inventory);
            var footScan = await footScanner.ScanAsync(footStore.ConfirmRemoved(app.Id)!.Id, default);
            check("Pre-uninstall footprint is found by the conservative scan", footScan.Items.Any(i => i.DisplayLocation == Path.Combine(roaming, "settings.cfg")));
            var footprintItems = footScan.Items.Where(i => i.DisplayLocation == roaming || i.DisplayLocation == Path.Combine(roaming, "settings.cfg")).ToArray();
            check("Footprint is verified receipt evidence held at Review", footprintItems.Length == 2 && footprintItems.All(i => i.Evidence == "Pre-uninstall footprint recorded under a non-shared application directory" && i.Confidence == LeftoverConfidence.Review && !i.SelectedByDefault));
            Directory.Delete(roaming, true);
            // A name-matched path that did not exist when the receipt was captured is not part of
            // the verified footprint: the conservative scan must never see it, and only the opt-in
            // deep scan may surface it, always as Review.
            var deepStore = new ApplicationRemovalEvidence();
            await deepStore.CaptureAsync(app, before, default);
            var deepReceipt = deepStore.ConfirmRemoved(app.Id)!;
            var deepScanner = new LeftoverScanner(deepStore, inventory);
            Directory.CreateDirectory(roaming);
            File.WriteAllText(Path.Combine(roaming, "settings.cfg"), "cfg");
            var conservative = await deepScanner.ScanAsync(deepReceipt.Id, default);
            check("Conservative scan ignores a path absent from the receipt", conservative.Items.All(i => !i.DisplayLocation.StartsWith(roaming, StringComparison.Ordinal)));
            var deep = await deepScanner.ScanAsync(deepReceipt.Id, true, default);
            check("Deep scan discovers name-derived candidate", deep.Items.Any(i => i.DisplayLocation == Path.Combine(roaming, "settings.cfg")));
            check("Name-derived candidates are always Review and unselected", deep.Items.Where(i => i.DisplayLocation.StartsWith(roaming, StringComparison.Ordinal)).All(i => i.Confidence == LeftoverConfidence.Review && !i.SelectedByDefault));
            // Observed anchors are the only signal that can prove ownership of a data directory
            // whose name does not resemble the application, so they are part of the conservative
            // scan. A shared vendor segment is still refused.
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            check("Observed anchor refuses a shared vendor segment", !LeftoverSafety.CanOwnObservedDirectory(app, Path.Combine(local, "Google", "Synthetic"), []));
            check("Observed anchor accepts a nested application folder", LeftoverSafety.CanOwnObservedDirectory(app, Path.Combine(local, "SyntheticVendor", "SyntheticProduct"), []));
            // An anchor now requires proof of an actual deletion: the last-write time moved AND the
            // direct entry count decreased. An active cache that only rewrites or grows must never
            // anchor a leftover review.
            var noisy = Path.Combine(local, "SystemCoreNoisy-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(noisy);
            File.WriteAllText(Path.Combine(noisy, "cache-1.bin"), "cache");
            Directory.SetLastWriteTimeUtc(noisy, DateTime.UtcNow.AddMinutes(-5));
            var noisyStore = new ApplicationRemovalEvidence();
            await noisyStore.CaptureAsync(app, before, default);
            // Active cache between the snapshots: it rewrites one file and adds two more, so its
            // entry count rises and its timestamp moves — activity without a deletion.
            File.Delete(Path.Combine(noisy, "cache-1.bin"));
            File.WriteAllText(Path.Combine(noisy, "cache-2.bin"), "cache");
            File.WriteAllText(Path.Combine(noisy, "cache-3.bin"), "cache");
            File.WriteAllText(Path.Combine(noisy, "cache-4.bin"), "cache");
            var noisyReceipt = noisyStore.ConfirmRemoved(app.Id)!;
            var noisyScan = await new LeftoverScanner(noisyStore, inventory).ScanAsync(noisyReceipt.Id, default);
            check("Rewrite/growth without deletion never anchors an observed review", noisyScan.Items.All(i => !i.DisplayLocation.StartsWith(noisy, StringComparison.Ordinal)));
            if (Directory.Exists(noisy)) Directory.Delete(noisy, true);
            var observed = Path.Combine(local, "SystemCoreObserved-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(observed);
            File.WriteAllText(Path.Combine(observed, "removed-a.tmp"), "removed");
            File.WriteAllText(Path.Combine(observed, "removed-b.tmp"), "removed");
            Directory.SetLastWriteTimeUtc(observed, DateTime.UtcNow.AddMinutes(-5));
            var observedStore = new ApplicationRemovalEvidence();
            await observedStore.CaptureAsync(app, before, default);
            // Simulate the native uninstaller pruning two children and leaving one behind:
            // two entries removed, one added — a net decrease that proves a real deletion.
            File.Delete(Path.Combine(observed, "removed-a.tmp"));
            File.Delete(Path.Combine(observed, "removed-b.tmp"));
            File.WriteAllText(Path.Combine(observed, "kept.dll"), "leftover");
            var observedReceipt = observedStore.ConfirmRemoved(app.Id)!;
            var observedScanner = new LeftoverScanner(observedStore, inventory);
            var observedScan = await observedScanner.ScanAsync(observedReceipt.Id, default);
            var observedItems = observedScan.Items.Where(i => i.DisplayLocation == observed || i.DisplayLocation == Path.Combine(observed, "kept.dll")).ToArray();
            check("Deletion-evidenced directory is discovered without a name match", observedItems.Any(i => i.DisplayLocation == Path.Combine(observed, "kept.dll")));
            check("Observed leftovers carry deletion evidence and stay Review", observedItems.Length == 2
                && observedItems.All(i => i.Confidence == LeftoverConfidence.Review && !i.SelectedByDefault && i.Evidence == "Ownership observed: the native uninstaller deleted entries in this directory during removal"));
            var observedFile = observedScan.Items.Single(i => i.DisplayLocation == Path.Combine(observed, "kept.dll"));
            var observedResult = await new LeftoverDeletionService(observedScanner, observedStore, inventory, new RegistryBackupWriter())
                .DeleteAsync(new(observedScan.Id, app.Id, [observedFile.Id]), default);
            check("Observed leftover cannot be deleted without consent", observedResult.Items.Single().Status == LeftoverDeletionStatus.SafetyRejected && File.Exists(Path.Combine(observed, "kept.dll")));
            if (deletion)
            {
                // Refusal alone would not prove removal. The same observed file is then deleted
                // for real on disk through the exact consent path the page sends. Every delete
                // attempt consumes its session, so the consent step scans a fresh one first.
                var consentObservedScan = await observedScanner.ScanAsync(observedReceipt.Id, default);
                var consentObservedFile = consentObservedScan.Items.Single(i => i.DisplayLocation == Path.Combine(observed, "kept.dll"));
                var consentObserved = await new LeftoverDeletionService(observedScanner, observedStore, inventory, new RegistryBackupWriter())
                    .DeleteAsync(new(consentObservedScan.Id, app.Id, [consentObservedFile.Id], IncludePersonalData: true), default);
                check("Observed leftover is really removed with explicit consent",
                    consentObserved.Items.Single().Status == LeftoverDeletionStatus.Deleted
                    && !File.Exists(Path.Combine(observed, "kept.dll")));
            }
            if (Directory.Exists(observed)) Directory.Delete(observed, true);
            // The observation records exactly two levels below the shared roots. A data directory
            // one level deeper is deliberately invisible, so a deletion inside it cannot anchor.
            var depthVendor = Path.Combine(local, "SystemCoreDeep-" + Guid.NewGuid().ToString("N"));
            var depthData = Path.Combine(depthVendor, "Product", "Data");
            Directory.CreateDirectory(depthData);
            File.WriteAllText(Path.Combine(depthData, "a.tmp"), "x");
            var depthStore = new ApplicationRemovalEvidence();
            await depthStore.CaptureAsync(app, before, default);
            File.Delete(Path.Combine(depthData, "a.tmp"));
            var depthReceipt = depthStore.ConfirmRemoved(app.Id)!;
            var depthScan = await new LeftoverScanner(depthStore, inventory).ScanAsync(depthReceipt.Id, default);
            check("Observation stays within the documented two levels", depthScan.Items.All(i => !i.DisplayLocation.StartsWith(depthVendor, StringComparison.Ordinal)));
            if (Directory.Exists(depthVendor)) Directory.Delete(depthVendor, true);
            if (deletion)
            {
                var deleteStore = new ApplicationRemovalEvidence();
                await deleteStore.CaptureAsync(app, before, default);
                var deleteReceipt = deleteStore.ConfirmRemoved(app.Id)!;
                var deleteScanner = new LeftoverScanner(deleteStore, inventory);
                var backupFailure = new LeftoverDeletionService(deleteScanner, deleteStore, inventory, new FailedBackup());
                var deletionService = new LeftoverDeletionService(deleteScanner, deleteStore, inventory, new RegistryBackupWriter());
                scan = await deleteScanner.ScanAsync(deleteReceipt.Id, default);
                var registryItem = scan.Items.First(i => i.Type == LeftoverType.RegistryKey);
                var result = await backupFailure.DeleteAsync(new(scan.Id, app.Id, [registryItem.Id]), default);
                using (var retainedKey = user.OpenSubKey(keyPath)) check("Backup failure preserves registry", result.Items.Single().Status == LeftoverDeletionStatus.BackupFailed && retainedKey is not null);
                scan = await deleteScanner.ScanAsync(deleteReceipt.Id, default);
                registryItem = scan.Items.First(i => i.Type == LeftoverType.RegistryKey);
                using (var key = user.OpenSubKey(keyPath, true)) key!.SetValue("VersionMajor", 99);
                result = await deletionService.DeleteAsync(new(scan.Id, app.Id, [registryItem.Id]), default);
                check("Registry modified after scan skipped", result.Items.Single().Status == LeftoverDeletionStatus.Changed);
                using (var key = user.OpenSubKey(keyPath, true)) key!.SetValue("VersionMajor", 43);
                scan = await deleteScanner.ScanAsync(deleteReceipt.Id, default);
                check("Registry metadata changes detected even after values restored", scan.Items.All(i => i.Type != LeftoverType.RegistryKey));
                // A fresh synthetic pre-uninstall snapshot starts the next independent
                // scenario; restoring values does not resurrect the old authorization.
                await deleteStore.CaptureAsync(app, before, default);
                deleteReceipt = deleteStore.ConfirmRemoved(app.Id)!;
                scan = await deleteScanner.ScanAsync(deleteReceipt.Id, default);
                var fileItem = scan.Items.First(i => i.DisplayLocation == Path.Combine(root, "owned.dll"));
                var injectionRejected = false;
                try { await deletionService.DeleteAsync(new(scan.Id, "other-application", [fileItem.Id]), default); } catch (InvalidOperationException) { injectionRejected = true; }
                check("Other application identity cannot delete", injectionRejected);
                injectionRejected = false;
                try { await deletionService.DeleteAsync(new(scan.Id, app.Id, [Guid.NewGuid()]), default); } catch (InvalidOperationException) { injectionRejected = true; }
                check("Unknown item ID cannot delete", injectionRejected);
                injectionRejected = false;
                try { await deletionService.DeleteAsync(new(Guid.NewGuid(), app.Id, [fileItem.Id]), default); } catch (InvalidOperationException) { injectionRejected = true; }
                check("Invalid leftover session rejected", injectionRejected);
                using (var cancelled = new CancellationTokenSource())
                {
                    cancelled.Cancel(); var wasCancelled = false;
                    try { await deletionService.DeleteAsync(new(scan.Id, app.Id, [fileItem.Id]), cancelled.Token); } catch (OperationCanceledException) { wasCancelled = true; }
                    check("Cancelled deletion leaves files intact", wasCancelled && File.Exists(Path.Combine(root, "owned.dll")));
                }
                File.WriteAllText(Path.Combine(root, "owned.dll"), "changed content");
                result = await deletionService.DeleteAsync(new(scan.Id, app.Id, [fileItem.Id]), default);
                check("File changed after scan skipped", result.Items.Single().Status == LeftoverDeletionStatus.Changed && File.Exists(Path.Combine(root, "owned.dll")));
                scan = await deleteScanner.ScanAsync(deleteReceipt.Id, default);
                fileItem = scan.Items.First(i => i.DisplayLocation == Path.Combine(root, "owned.dll"));
                using (var locked = new FileStream(Path.Combine(root, "owned.dll"), FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    result = await deletionService.DeleteAsync(new(scan.Id, app.Id, [fileItem.Id]), default);
                    check("Locked after scan skipped", result.Items.Single().Status == LeftoverDeletionStatus.InUse);
                }
                scan = await deleteScanner.ScanAsync(deleteReceipt.Id, default);
                fileItem = scan.Items.First(i => i.DisplayLocation == Path.Combine(root, "owned.dll"));
                File.WriteAllText(Path.Combine(root, "new-after-scan.txt"), "new");
                result = await deletionService.DeleteAsync(new(scan.Id, app.Id, [fileItem.Id]), default);
                check("Changed folder blocks selected descendants", result.Items.Single().Status == LeftoverDeletionStatus.Changed && File.Exists(Path.Combine(root, "owned.dll")));
                File.Delete(Path.Combine(root, "new-after-scan.txt"));
                // A truncated session must not disable deletion of the individually verified items
                // the user can still see. One overflowing observed anchor floods its own budget,
                // the session reports incomplete, and a receipt-verified Safe item still deletes.
                var big = Path.Combine(local, "SystemCoreBig-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(big);
                for (var i = 0; i < 350; i++) File.WriteAllText(Path.Combine(big, $"c{i:D3}.tmp"), "x");
                var bigStore = new ApplicationRemovalEvidence();
                await bigStore.CaptureAsync(app, before, default);
                for (var i = 0; i < 40; i++) File.Delete(Path.Combine(big, $"c{i:D3}.tmp"));
                var bigReceipt = bigStore.ConfirmRemoved(app.Id)!;
                var bigScanner = new LeftoverScanner(bigStore, inventory);
                var bigScan = await bigScanner.ScanAsync(bigReceipt.Id, default);
                check("Overflowing observed anchor is truncated, not unlimited", !bigScan.Complete && bigScan.Items.Count(i => i.DisplayLocation.StartsWith(big, StringComparison.Ordinal)) <= 300);
                var truncatedFile = bigScan.Items.First(i => i.DisplayLocation == Path.Combine(root, "owned.dll"));
                var truncatedDelete = await new LeftoverDeletionService(bigScanner, bigStore, inventory, new RegistryBackupWriter())
                    .DeleteAsync(new(bigScan.Id, app.Id, [truncatedFile.Id]), default);
                check("Truncated session still deletes individually verified safe items", truncatedDelete.Items.Single().Status == LeftoverDeletionStatus.Deleted && !File.Exists(Path.Combine(root, "owned.dll")));
                if (Directory.Exists(big)) Directory.Delete(big, true);
                File.WriteAllText(Path.Combine(root, "owned.dll"), "changed content");
                scan = await deleteScanner.ScanAsync(deleteReceipt.Id, default);
                var expectedBytes = scan.Items.Where(i => i.Confidence == LeftoverConfidence.Safe && i.Type == LeftoverType.File).Sum(i => i.Size ?? 0);
                var request = new LeftoverDeletionRequest(scan.Id, app.Id, scan.Items.Where(i => i.Confidence == LeftoverConfidence.Safe).Select(i => i.Id).ToArray());
                result = await deletionService.DeleteAsync(request, default);
                check("Selected exact safe install leftovers removed bottom-up", !Directory.Exists(root) && result.Items.All(i => i.Status == LeftoverDeletionStatus.Deleted));
                check("Review DataPath retained by safe-only deletion", File.ReadAllText(Path.Combine(data, "data.txt")) == "app data");
                check("Actual bytes agree with selected file sizes", result.RemovedBytes == expectedBytes);
                using (var removedKey = user.OpenSubKey(keyPath)) check("Registry transaction committed exact selected key", removedKey is null);
                check("Durable registry backup exists", result.Items.Where(i => i.BackupFile is not null).Any(i => File.Exists(i.BackupFile)));
                foreach (var backed in result.Items.Where(i => i.BackupFile is not null))
                {
                    using var backup = System.Text.Json.JsonDocument.Parse(File.ReadAllText(backed.BackupFile!));
                    var payload = Convert.FromBase64String(backup.RootElement.GetProperty("TypedSnapshotBase64").GetString()!);
                    check("Registry backup typed payload checksum and view preserved", backup.RootElement.GetProperty("SnapshotSha256").GetString() == Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload))
                        && backup.RootElement.GetProperty("OriginalPath").GetString() == keyPath && backup.RootElement.GetProperty("View").GetString() == "Registry64");
                }
                check("Shared/sibling objects retained", File.ReadAllText(Path.Combine(outside, "retain.txt")) == "retain");
                injectionRejected = false;
                try { await deletionService.DeleteAsync(request, default); } catch (InvalidOperationException) { injectionRejected = true; }
                check("Consumed leftover session rejected", injectionRejected);
                // Explicit personal-data consent is the only path that makes a Review item
                // deletable; without it the item stays refused and the file survives.
                var roamingFile = Path.Combine(roaming, "settings.cfg");
                Directory.CreateDirectory(roaming); File.WriteAllText(roamingFile, "cfg");
                var consentStore = new ApplicationRemovalEvidence();
                await consentStore.CaptureAsync(app, before, default);
                var consentReceipt = consentStore.ConfirmRemoved(app.Id)!;
                var consentScanner = new LeftoverScanner(consentStore, inventory);
                var consentDeletion = new LeftoverDeletionService(consentScanner, consentStore, inventory, new RegistryBackupWriter());
                var consentScan = await consentScanner.ScanAsync(consentReceipt.Id, true, default);
                var roamingItem = consentScan.Items.Single(i => i.DisplayLocation == roamingFile);
                var refused = await consentDeletion.DeleteAsync(new(consentScan.Id, app.Id, [roamingItem.Id]), default);
                check("Review candidate refused without explicit consent", refused.Items.Single().Status == LeftoverDeletionStatus.SafetyRejected && File.Exists(roamingFile));
                consentScan = await consentScanner.ScanAsync(consentReceipt.Id, true, default);
                roamingItem = consentScan.Items.Single(i => i.DisplayLocation == roamingFile);
                var accepted = await consentDeletion.DeleteAsync(new(consentScan.Id, app.Id, [roamingItem.Id], IncludePersonalData: true), default);
                check("Review candidate deleted with explicit consent", accepted.Items.Single().Status == LeftoverDeletionStatus.Deleted && !File.Exists(roamingFile));
                consentScan = await consentScanner.ScanAsync(consentReceipt.Id, true, default);
                var roamingDirectory = consentScan.Items.Single(i => i.DisplayLocation == roaming && i.Type == LeftoverType.Directory);
                var directoryRefused = await consentDeletion.DeleteAsync(new(consentScan.Id, app.Id, [roamingDirectory.Id]), default);
                check("Review directory refused without explicit consent", directoryRefused.Items.Single().Status == LeftoverDeletionStatus.SafetyRejected && Directory.Exists(roaming));
                consentScan = await consentScanner.ScanAsync(consentReceipt.Id, true, default);
                roamingDirectory = consentScan.Items.Single(i => i.DisplayLocation == roaming && i.Type == LeftoverType.Directory);
                var directoryAccepted = await consentDeletion.DeleteAsync(new(consentScan.Id, app.Id, [roamingDirectory.Id], IncludePersonalData: true), default);
                check("Emptied Review directory removed with explicit consent", directoryAccepted.Items.Single().Status == LeftoverDeletionStatus.Deleted && !Directory.Exists(roaming));
            }
        }
        finally
        {
            // Test teardown only, confined to GUID-named roots created above.
            var link = Path.Combine(root, "escape"); if (Directory.Exists(link)) Directory.Delete(link);
            user.DeleteSubKeyTree(keyPath, false);
            if (Directory.Exists(root)) Directory.Delete(root, true);
            if (Directory.Exists(roaming)) Directory.Delete(roaming, true);
            Directory.Delete(outside, true); if (Directory.Exists(data)) Directory.Delete(data, true);
        }
    }
    private static async Task CheckRegistryLink(InstalledApplication app, RegistryKey user, Action<string, bool> check)
    {
        var vendor = "SystemCoreLinkVendor-" + Guid.NewGuid().ToString("N");
        var target = vendor + "-target";
        using (var key = user.CreateSubKey("Software\\" + target + "\\" + app.DisplayName))
        { key.SetValue("Publisher", vendor); key.SetValue("InstallLocation", app.InstallLocation!); }
        var path = "Software\\" + vendor;
        var created = false;
        try
        {
            var error = RegCreateKeyExW(user.Handle, path, 0, null, 2, 0xF003F | 0x100, IntPtr.Zero, out var handle, out _);
            if (error != 0) { handle.Dispose(); throw new System.ComponentModel.Win32Exception(error); }
            created = true;
            using (handle)
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var sid = identity.User!.Value;
                var destination = Encoding.Unicode.GetBytes("\\Registry\\User\\" + sid + "\\Software\\" + target);
                error = RegSetValueExW(handle, "SymbolicLinkValue", 0, 6, destination, destination.Length);
                if (error != 0) throw new System.ComponentModel.Win32Exception(error);
            }
            var linkedApp = app with { Publisher = vendor, RegisteredUninstallCommand = null };
            var store = new ApplicationRemovalEvidence();
            await store.CaptureAsync(linkedApp, new([linkedApp], true, []), default);
            var scan = await new LeftoverScanner(store, new Inventory()).ScanAsync(store.ConfirmRemoved(linkedApp.Id)!.Id, default);
            check("Registry ancestor symbolic link rejected", scan.Items.Count == 0);
            using var retained = user.OpenSubKey("Software\\" + target + "\\" + app.DisplayName);
            check("Registry link target retained", retained is not null);
        }
        finally
        {
            if (created)
            {
                var error = RegOpenKeyExW(user.Handle, path, 8, 0x10000 | 0x100, out var link);
                using (link)
                    if (error != 0 || NtDeleteKey(link) != 0) throw new IOException("Synthetic registry link teardown failed");
            }
            user.DeleteSubKeyTree("Software\\" + target, false);
        }
    }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern int RegCreateKeyExW(SafeRegistryHandle key, string subkey, int reserved, string? keyClass, int options, int access, IntPtr security, out SafeRegistryHandle result, out int disposition);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern int RegSetValueExW(SafeRegistryHandle key, string name, int reserved, int type, byte[] data, int size);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern int RegOpenKeyExW(SafeRegistryHandle key, string subkey, int options, int access, out SafeRegistryHandle result);
    [DllImport("ntdll.dll")] private static extern int NtDeleteKey(SafeRegistryHandle key);
    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateHardLink(string link, string existing, IntPtr security);
    sealed class FailedBackup : IRegistryBackupWriter
    {
        public Task<string> SaveAsync(string applicationId, string displayName, string hive, string view, string path, byte[] snapshot, CancellationToken token) => throw new IOException("Synthetic backup failure");
    }
    sealed class Inventory : IInstalledApplicationInventory
    {
        public InventoryResult Result { get; set; } = new([], true, []);
        public Task<InventoryResult> ReadAsync(CancellationToken token) => Task.FromResult(Result);
    }
    static class Junction
    {
        public static void Create(string link, string target)
        {
            Directory.CreateDirectory(link);
            var substitute = Encoding.Unicode.GetBytes(@"\??\" + target); var print = Encoding.Unicode.GetBytes(target);
            var buffer = new byte[16 + substitute.Length + 2 + print.Length + 2];
            using (var stream = new MemoryStream(buffer)) using (var writer = new BinaryWriter(stream))
            {
                writer.Write(0xA0000003u); writer.Write((ushort)(buffer.Length - 8)); writer.Write((ushort)0);
                writer.Write((ushort)0); writer.Write((ushort)substitute.Length); writer.Write((ushort)(substitute.Length + 2)); writer.Write((ushort)print.Length);
                writer.Write(substitute); writer.Write((ushort)0); writer.Write(print); writer.Write((ushort)0);
            }
            using var handle = CreateFile(link, 0x40000000, 0, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
            if (handle.IsInvalid || !DeviceIoControl(handle, 0x900A4, buffer, buffer.Length, IntPtr.Zero, 0, out _, IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint mode, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, int size, IntPtr output, int outputSize, out int returned, IntPtr overlapped);
    }
}
