using SystemCore.App.Services;
using SystemCore.App.Uninstaller;
using SystemCore.Core.Settings;
using SystemCore.Core.Uninstaller;

// Compile the actual presentation logic/resources without starting WinUI.
namespace SystemCore.App { internal sealed class App { } }

static class AutomaticLeftoverTests
{
    public static async Task Run(Action<string, bool> check)
    {
        foreach (var language in new[] { "en", "ar" })
        {
            var executor = new Executor(); var scanner = new Scanner(); var deletion = new Deletion();
            await using var orchestrator = InventoryTestOrchestrator.Create();
            using var vm = new UninstallerViewModel(new LocalizationService(new Settings(language)), executor, scanner, deletion, new Launcher(), orchestrator, new InventoryAutoLoadTests.Diagnostics());
            async Task Remove()
            {
                await vm.RefreshAsync(); vm.SelectedId = executor.App.Id;
                await vm.UninstallConfirmedAsync(executor.App.Id);
            }
            await Remove();
            check("Verified removal automatically scans " + language, scanner.Calls == 1 && vm.ShowLeftovers && !vm.Busy);
            check("Empty automatic result " + language, vm.LeftoverSummary == (language == "en" ? "No leftovers." : "لا توجد بقايا."));
            check("Automatic path never calls deletion " + language, deletion.Calls == 0);
            var safe = new LeftoverItem(Guid.NewGuid(), executor.App.Id, LeftoverType.File, "owned.bin", "", LeftoverConfidence.Safe, "", 1048576, false, false, "");
            var review = safe with { Id = Guid.NewGuid(), Confidence = LeftoverConfidence.Review };
            var ignored = safe with { Id = Guid.NewGuid(), Confidence = LeftoverConfidence.Ignore };
            var evidenceCases = new (string Evidence, string Key, LeftoverType Type)[]
            {
                ("Registered install location and validated native uninstaller inside the same non-shared directory", "UnEvidenceInstall", LeftoverType.File),
                ("Exact DataPath recorded before uninstall in an app-specific registry key with matching publisher and product/install identity", "UnEvidenceData", LeftoverType.Directory),
                ("Exact pre-uninstall registry snapshot with matching publisher and product/install identity", "UnEvidenceRegistry", LeftoverType.RegistryKey),
                ("Reparse point rejected", "UnEvidenceReparse", LeftoverType.Directory),
                ("Unsupported file attributes or hard links", "UnEvidenceAttributes", LeftoverType.File),
                ("Shared game data rejected", "UnEvidenceSharedGame", LeftoverType.Directory),
                ("Pre-uninstall footprint recorded under a non-shared application directory", "UnEvidenceFootprint", LeftoverType.Directory),
                ("Modified by the native uninstaller; ownership observed during removal", "UnEvidenceObserved", LeftoverType.Directory),
                ("UNRECOGNIZED_RAW_ENGLISH_EVIDENCE", "UnEvidenceUnsupported", LeftoverType.File)
            };
            foreach (var confidence in Enum.GetValues<LeftoverConfidence>())
            foreach (var evidenceCase in evidenceCases)
            {
                var item = safe with { Confidence = confidence, Evidence = evidenceCase.Evidence, Type = evidenceCase.Type,
                    Risk = "RAW_ENGLISH_RISK_MUST_NOT_RENDER", RequiresElevation = true };
                var label = vm.LeftoverLabel(item);
                var warningKey = confidence != LeftoverConfidence.Safe ? "UnDeleteSafetyRejected"
                    : item.Type == LeftoverType.RegistryKey ? "UnRegistryWarning" : "UnOwnedRisk";
                // Missing new resources yield an assertion failure in the red run, not a test crash.
                string Expected(string key) { try { return vm.Text(key); } catch (InvalidOperationException) { return "MISSING_RESOURCE_" + key; } }
                check($"Localized evidence and safety warning {language}/{confidence}/{evidenceCase.Key}",
                    label.Contains(Expected(evidenceCase.Key), StringComparison.Ordinal)
                    && label.Contains(vm.Text(warningKey), StringComparison.Ordinal)
                    && label.Contains(vm.Text(evidenceCase.Type == LeftoverType.RegistryKey ? "UnElevationScope" : "UnElevationRetry"), StringComparison.Ordinal)
                    && (confidence != LeftoverConfidence.Review || label.Contains(vm.Text("UnPersonalData"), StringComparison.Ordinal))
                    && !label.Contains(item.Evidence, StringComparison.Ordinal) && !label.Contains(item.Risk, StringComparison.Ordinal));
            }
            scanner.Items = [safe, review, ignored]; await Remove();
            check("Found automatic count and formatted size " + language, vm.LeftoverSummary == (language == "en" ? "Leftovers found\n3 items • " : "تم العثور على بقايا\n3 عناصر • ") + vm.FormatSize(3145728));
            check("Nothing selected automatically " + language, vm.SelectedLeftoverCount == 0 && !vm.CanDeleteLeftovers);
            vm.SelectLeftover(review.Id, true); vm.SelectLeftover(ignored.Id, true);
            check("Review and Ignore cannot be selected " + language, vm.SelectedLeftoverCount == 0);
            vm.AllowPersonalData = true;
            vm.SelectLeftover(review.Id, true);
            check("Consent allows Review selection " + language, vm.SelectedLeftoverCount == 1 && vm.CanSelectLeftover(review));
            vm.SelectLeftover(ignored.Id, true);
            check("Consent never allows Ignore selection " + language, vm.SelectedLeftoverCount == 1);
            vm.SelectLeftover(review.Id, false); vm.AllowPersonalData = false;
            check("Revoked consent leaves nothing selected " + language, vm.SelectedLeftoverCount == 0);
            check("Review short personal label " + language, vm.LeftoverLabel(review).Contains(language == "en" ? "may contain personal data" : "قد يحتوي بيانات شخصية", StringComparison.Ordinal));
            await vm.DeleteLeftoversConfirmedAsync([safe.Id]);
            check("Unselected confirmation cannot delete " + language, deletion.Calls == 0);
            vm.SelectLeftover(safe.Id, true);
            check("Safe selection requires confirmation " + language, vm.CanDeleteLeftovers && deletion.Calls == 0);
            await vm.DeleteLeftoversConfirmedAsync([review.Id]);
            check("Mismatched confirmation cannot delete " + language, deletion.Calls == 0);
            await vm.DeleteLeftoversConfirmedAsync([safe.Id]);
            check("Explicit exact confirmation deletes only selected safe item " + language, deletion.Calls == 1 && deletion.Last!.ItemIds.SequenceEqual(new[] { safe.Id }));
            var calls = scanner.Calls;
            executor.Outcome = UninstallState.Removed; executor.WithReceipt = false; await Remove();
            check("No receipt means no scan " + language, scanner.Calls == calls && !vm.CanScanLeftovers);
            executor.WithReceipt = true; executor.Outcome = UninstallState.Cancelled; await Remove();
            check("Cancelled uninstall never scans even with invalid receipt " + language, scanner.Calls == calls && !vm.CanScanLeftovers);
            executor.Outcome = UninstallState.Removed;
            scanner.Fail = true; await Remove();
            check("Scan failure restores retry and deletes nothing " + language, !vm.Busy && vm.CanScanLeftovers && !vm.ShowLeftovers && deletion.Calls == 1);
            scanner.Fail = false; await vm.ScanLeftoversAsync();
            check("Manual retry succeeds " + language, vm.ShowLeftovers && !vm.Busy);
            scanner.Wait = true;
            var removal = Remove();
            // A timeout is an assertion failure, never a hung test when auto-scan is missing.
            var started = await Task.WhenAny(scanner.Started.Task, removal, Task.Delay(3000));
            vm.CancelOperation(); await removal;
            check("Automatic scan cancellation is not failure " + language, started == scanner.Started.Task && !vm.Busy && vm.CanScanLeftovers && !vm.ShowLeftovers && vm.Status != vm.Text("UnStateFailed"));
            check("Automatic cancellation deletes nothing " + language, deletion.Calls == 1);
            scanner.Wait = false;
            await vm.ScanLeftoversAsync(); vm.SelectLeftover(safe.Id, true); vm.KeepLeftovers();
            check("Keep clears selection, retains retry and deletes nothing " + language, !vm.ShowLeftovers && vm.SelectedLeftoverCount == 0 && vm.CanScanLeftovers && deletion.Calls == 1);
            await vm.RefreshAsync();
            check("New inventory cannot reuse prior application's receipt " + language, !vm.CanScanLeftovers);
            scanner.UnexpectedFailure = true; await Remove();
            check("Unexpected scan fault restores coherent retry " + language, !vm.Busy && vm.CanScanLeftovers && !vm.ShowLeftovers && vm.Status == vm.Text("UnStateFailed") && deletion.Calls == 1);
            scanner.UnexpectedFailure = false;
            executor.ThrowCancellation = true; calls = scanner.Calls; await Remove();
            // This double throws a bare OperationCanceledException without cancelling the token it
            // was handed, which is not a cancellation the view model may honour: it is an unexpected
            // failure. The scan is still refused, and the status now says Failed rather than claiming
            // the user cancelled. The genuine-cancellation counterpart is asserted in InventoryAutoLoadTests.
            check("Unexpected uninstall cancellation exception never scans " + language,
                scanner.Calls == calls && !vm.CanScanLeftovers && !vm.Busy && vm.Status == vm.Text("UnStateFailed"));
            executor.ThrowCancellation = false;
            foreach (var state in new[] { UninstallState.Failed, UninstallState.Unknown, UninstallState.StillInstalled })
            {
                executor.Outcome = state; await Remove();
                check("Non-verified outcome rejects receipt " + state + " " + language, scanner.Calls == calls && !vm.CanScanLeftovers);
            }
            executor.Outcome = UninstallState.Removed; executor.ReceiptIdentity = "other-app"; await Remove();
            check("Wrong-identity receipt cannot scan " + language, scanner.Calls == calls && !vm.CanScanLeftovers);
            executor.ReceiptIdentity = null; executor.App = executor.App with { ItemKind = ItemKind.Game }; await Remove();
            check("Native game cannot scan even with erroneous receipt " + language, scanner.Calls == calls && !vm.CanScanLeftovers);
            // The consent flag is the only thing that lets a Review row reach the deletion
            // service; it must be forwarded verbatim with the exact confirmed ids.
            executor.App = executor.App with { ItemKind = ItemKind.Application };
            scanner.Items = [safe, review]; await Remove();
            vm.AllowPersonalData = true; vm.SelectLeftover(review.Id, true);
            var consentCalls = deletion.Calls;
            await vm.DeleteLeftoversConfirmedAsync([review.Id]);
            check("Consent flag forwarded to deletion " + language,
                deletion.Calls == consentCalls + 1 && deletion.Last!.IncludePersonalData && deletion.Last!.ItemIds.SequenceEqual(new[] { review.Id }));
            vm.AllowPersonalData = false;
        }
    }
    sealed class Executor : IUninstallExecutor
    {
        public InstalledApplication App { get; set; } = new("auto-test", "Auto test", "Fixture", "1", null, null, null, null, ApplicationType.Win32, "fixture.exe", false, null, null, null, new(InventoryHive.CurrentUser, InventoryView.Registry64, "fixture"), null, false, false, false, true, null);
        public UninstallState Outcome = UninstallState.Removed;
        public bool WithReceipt = true;
        public bool ThrowCancellation;
        public string? ReceiptIdentity;
        public Task<ApplicationInventorySession> RefreshAsync(CancellationToken token) => Task.FromResult(new ApplicationInventorySession(Guid.NewGuid(), DateTimeOffset.UtcNow, new([App], true, [])));
        public Task<UninstallOutcome> UninstallAsync(UninstallRequest request, IProgress<UninstallState>? progress, CancellationToken token) => ThrowCancellation ? throw new OperationCanceledException() : Task.FromResult(new UninstallOutcome(Outcome, App.Id, 0, null, WithReceipt ? new(Guid.NewGuid(), ReceiptIdentity ?? App.Id, App.DisplayName, DateTimeOffset.UtcNow) : null));
    }
    sealed class Scanner : ILeftoverScanner
    {
        public int Calls; public bool Fail, Wait, UnexpectedFailure;
        public IReadOnlyList<LeftoverItem> Items = [];
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<LeftoverScanSession> ScanAsync(Guid id, CancellationToken token)
        {
            Calls++;
            if (Fail) throw new IOException("Synthetic scan failure");
            if (UnexpectedFailure) throw new ArgumentException("Synthetic unexpected scanner fault");
            if (Wait) { Started.TrySetResult(); await Task.Delay(Timeout.Infinite, token); }
            return new(Guid.NewGuid(), id, "auto-test", DateTimeOffset.UtcNow, Items, true);
        }
    }
    sealed class Deletion : ILeftoverDeletionService
    {
        public int Calls; public LeftoverDeletionRequest? Last;
        public Task<LeftoverDeletionResult> DeleteAsync(LeftoverDeletionRequest request, CancellationToken token) { Calls++; Last = request; return Task.FromResult(new LeftoverDeletionResult([], 0)); }
    }
    sealed class Launcher : IGameLauncher
    {
        public bool CanOpen(Source source) => false;
        public Task<bool> OpenAsync(Source source, CancellationToken token) => Task.FromResult(false);
    }
    sealed class Settings(string language) : IUserSettingsService
    {
        public string GetLanguage() => language;
        public void SetLanguage(string value) { }
        public UserTheme GetTheme() => UserTheme.System;
        public void SetTheme(UserTheme theme) { }
    }
}
