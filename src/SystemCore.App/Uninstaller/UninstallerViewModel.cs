using CommunityToolkit.Mvvm.Input;
using SystemCore.App.Services;
using SystemCore.App.ViewModels;
using SystemCore.Core.Uninstaller;
using SystemCore.Core.Orchestration;
using SystemCore.Core.Diagnostics;

namespace SystemCore.App.Uninstaller;

public sealed class UninstallerViewModel : LocalizedViewModel
{
    // Installs are infrequent, but can occur while F1 stays open.
    public static readonly TimeSpan ApplicationsFreshness = TimeSpan.FromMinutes(10);
    // Games share the applications inventory's update cadence.
    public static readonly TimeSpan GamesFreshness = TimeSpan.FromMinutes(10);
    private readonly IApplicationDataOrchestrator orchestrator;
    private readonly IAppDiagnostics diagnostics;
    private Task? load;
    private bool loadFailed;
    private bool inventoryActionable;
    private ApplicationsSummarySnapshot? cachedSummary;
    private readonly IUninstallExecutor executor;
    private readonly ILeftoverScanner leftovers;
    private readonly ILeftoverDeletionService deletion;
    private readonly IGameLauncher launcher;
    private bool launcherFailed;
    private readonly HashSet<Guid> selectedLeftovers = [];
    private LeftoverDeletionResult? cleanup;
    private RemovalReceipt? receipt;
    private LeftoverScanSession? leftoverSession;
    // Explicit, per-session acknowledgement required before any Review item becomes selectable.
    private bool allowPersonalData;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? activeOperation;
    private CancellationToken BeginOperation()
    {
        // After Dispose the lifetime token throws. A late UI click must not fault the dispatcher
        // with an unhandled ObjectDisposedException, so it is refused here, at the single place
        // every operation token is minted.
        if (disposed) return new CancellationToken(true);
        activeOperation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        return activeOperation.Token;
    }
    private void EndOperation() { activeOperation?.Dispose(); activeOperation = null; Busy = false; OnPropertyChanged(string.Empty); }
    public void CancelOperation() => activeOperation?.Cancel();
    // A cancellation is genuine only when a token this operation was actually given is cancelled:
    // the caller's own or the view model's lifetime (Dispose). Shutdown alone is never evidence
    // of cancellation: only a cancelled token is.
    private bool GenuineCancel(CancellationToken token) => token.IsCancellationRequested || lifetime.IsCancellationRequested;
    // An ObjectDisposedException is expected only once teardown has genuinely begun: this view
    // model disposed, or the orchestrator it depends on shutting down. Shutting down is read
    // without a lock, so it is safe to call from an exception filter.
    private bool ShuttingDown => disposed || lifetime.IsCancellationRequested || orchestrator.IsShutdown;
    private ApplicationInventorySession? session;
    private string search = "", selectedId = "";
    private int filter, sort, page, kindFilter;
    private bool busy;
    private bool disposed;
    private UninstallState state = UninstallState.Ready;
    public UninstallerViewModel(LocalizationService localizer, IUninstallExecutor executor, ILeftoverScanner leftovers, ILeftoverDeletionService deletion, IGameLauncher launcher,
        IApplicationDataOrchestrator orchestrator, IAppDiagnostics diagnostics) : base(localizer)
    {
        this.executor = executor;
        this.leftovers = leftovers;
        this.deletion = deletion;
        this.launcher = launcher;
        this.orchestrator = orchestrator;
        this.diagnostics = diagnostics;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !Busy);
        orchestrator.RegisterBackgroundWarmup(DataDataset.InstalledApplications, LoadTier.Background,
            async token => await ReadApplicationsAsync(false, token), ApplicationsFreshness);
        orchestrator.RegisterBackgroundWarmup(DataDataset.Games, LoadTier.Background,
            async token => await ReadGamesAsync(false, token), GamesFreshness);
    }
    // Read-only inventory refresh, not execution. Only counts are persisted.
    private async Task<ApplicationInventorySession> ProduceAsync(CancellationToken token)
    {
        var value = await executor.RefreshAsync(token).ConfigureAwait(false);
        orchestrator.CacheApplicationsSummary(Summarize(value));
        return value;
    }
    private static ApplicationsSummarySnapshot Summarize(ApplicationInventorySession value) => new(
        value.Inventory.Applications.Count(i => i.ItemKind != ItemKind.Game),
        value.Inventory.Applications.Count(i => i.ItemKind == ItemKind.Game), value.Inventory.Complete, DateTimeOffset.UtcNow);
    private Task<ApplicationInventorySession> ReadApplicationsAsync(bool force, CancellationToken token) => force
        ? orchestrator.RefreshAsync(DataDataset.InstalledApplications, LoadTier.Background, ProduceAsync, token)
        : orchestrator.GetAsync(DataDataset.InstalledApplications, LoadTier.Background, ApplicationsFreshness, ProduceAsync, token);
    // Games are a separate summary dataset, projected from the same single-flight source.
    // No second call to the native applications/launcher enumerator is made.
    private async Task<ApplicationsSummarySnapshot> ProduceGamesAsync(CancellationToken token) => Summarize(await ReadApplicationsAsync(false, token).ConfigureAwait(false));
    private Task<ApplicationsSummarySnapshot> ReadGamesAsync(bool force, CancellationToken token) => force
        ? orchestrator.RefreshAsync(DataDataset.Games, LoadTier.Background, ProduceGamesAsync, token)
        : orchestrator.GetAsync(DataDataset.Games, LoadTier.Background, GamesFreshness, ProduceGamesAsync, token);
    private async Task<ApplicationInventorySession> ReadAsync(bool force, CancellationToken token)
    {
        var value = await ReadApplicationsAsync(force, token);
        await ReadGamesAsync(force, token);
        return value;
    }
    public Task RefreshIfStaleAsync() => orchestrator.IsStale(DataDataset.InstalledApplications, ApplicationsFreshness)
        || orchestrator.IsStale(DataDataset.Games, GamesFreshness) ? InitializeAsync() : Task.CompletedTask;
    public Task InitializeAsync() => StartLoad(false);
    public Task RefreshAsync() => StartLoad(true);
    private Task StartLoad(bool force) => disposed ? Task.CompletedTask
        : load is { IsCompleted: false } ? load : Busy ? Task.CompletedTask : load = LoadAsync(force);
    public AsyncRelayCommand RefreshCommand { get; }
    public string Text(string key) => L[key];
    public string FormatSize(long? bytes) => bytes is >= 0
        ? SystemCore.Core.SystemInformation.InfoFormatting.Size((ulong)bytes.Value, L.Culture) : L["UnUnknown"];
    public string LargeFilterLabel => L.Format("UnLarge", FormatSize(1024L * 1024 * 1024));
    // The surrounding technical field supplies its own direction. Embedded ICU
    // date marks otherwise split a numeric date into reordered RTL fragments.
    public string FormatDate(DateOnly? value) => value is { } date ? SystemCore.Core.SystemInformation.InfoFormatting.Date(date, L.Culture).Replace("\u200f", "").Replace("\u200e", "") : L["UnUnknown"];
    public string Title => L["NavUninstaller"];
    public bool InventoryLoadFailed => loadFailed;
    public int RawCount => session?.Inventory.Applications.Count ?? 0;
    public int RemovableCount => session?.Inventory.Applications.Count(RemovableApplications.IsVisible) ?? 0;
    public int ManagedCount => session?.Inventory.Applications.Count(UnifiedLibraryPresentation.IsLauncherManaged) ?? 0;
    public int HiddenCount => RawCount - RemovableCount - ManagedCount;
    public string SourceLabel(InstalledApplication app) => L["UnSource" + app.Source];
    public string ManagedLabel(InstalledApplication app) => L.Format("UnManagedBy", SourceLabel(app));
    public bool CanOpenLauncher => !Busy && Selected is { ItemKind: ItemKind.Game } app && launcher.CanOpen(app.Source);
    public async Task OpenLauncherAsync()
    {
        if (!CanOpenLauncher || Selected is not { } app) return;
        // See LoadAsync: Busy is set inside the guarding try, and the token is declared outside
        // it so the catches can ask whether a token was really cancelled.
        CancellationToken token = default;
        try
        {
            Busy = true;
            token = BeginOperation();
            launcherFailed = !await launcher.OpenAsync(app.Source, token);
        }
        // The launcher reports a user-cancelled start the same way it reports any cancellation.
        // Only a token that was actually cancelled makes it one.
        catch (OperationCanceledException) when (GenuineCancel(token)) { }
        catch (ObjectDisposedException) when (ShuttingDown) { }
        // A fault the launcher did not ask for is a real failure: it is shown as unavailable and
        // recorded, never silently read as "the user changed their mind".
        catch (Exception e) { launcherFailed = true; diagnostics.Record(DiagnosticFeature.Uninstaller, DiagnosticEvent.ProviderFailure, e.HResult); }
        finally { EndOperation(); }
    }
    public IReadOnlyList<InstalledApplication> Inventory => session?.Inventory.Applications ?? [];
    public bool CanScanLeftovers => !Busy && receipt is not null;
    public string LeftoverSummary => leftoverSession is not { } scan ? ""
        : scan.Items.Count == 0 ? L[scan.Complete ? "UnLeftoversEmpty" : "UnPartial"]
        : L["UnLeftoversFound"] + "\n" + L.Format("UnLeftoverCountSize", scan.Items.Count, FormatSize(scan.Items.Sum(i => i.Size ?? 0)))
            + (scan.Complete ? "" : "\n" + L["UnPartial"]);
    public IReadOnlyList<LeftoverItem> Leftovers => leftoverSession?.Items ?? [];
    public bool ShowLeftovers => leftoverSession is not null;
    public IReadOnlyList<LeftoverItem> LeftoverRows => Leftovers.Skip(page * 20).Take(20).ToArray();
    public bool IsLeftoverSelected(Guid id) => selectedLeftovers.Contains(id);
    public int SelectedLeftoverCount => selectedLeftovers.Count;
    public bool CanDeleteLeftovers => !Busy && cleanup is null && leftoverSession is { Complete: true } && selectedLeftovers.Count > 0;
    public bool CanChangeLeftoverSelection => !Busy && cleanup is null;
    public string LeftoverResult(Guid id)
    {
        var result = cleanup?.Items.FirstOrDefault(i => i.ItemId == id);
        return result is null ? "" : L["UnDelete" + result.Status] + (result.BackupFile is null ? "" : "\n" + L["UnBackupSaved"] + ": " + SystemCore.Core.SystemInformation.InfoFormatting.Technical(result.BackupFile));
    }
    public bool HasSelectedRegistry => Leftovers.Any(i => selectedLeftovers.Contains(i.Id) && i.Type == LeftoverType.RegistryKey);
    public string LeftoverLabel(LeftoverItem item)
    {
        var evidenceKey = item.Evidence switch
        {
            "Registered install location and validated native uninstaller inside the same non-shared directory" => "UnEvidenceInstall",
            "Exact DataPath recorded before uninstall in an app-specific registry key with matching publisher and product/install identity" => "UnEvidenceData",
            "Exact pre-uninstall registry snapshot with matching publisher and product/install identity" => "UnEvidenceRegistry",
            "Reparse point rejected" => "UnEvidenceReparse",
            "Unsupported file attributes or hard links" => "UnEvidenceAttributes",
            "Shared game data rejected" => "UnEvidenceSharedGame",
            "Name-derived candidate; not verified by a pre-uninstall receipt" => "UnEvidenceNameDerived",
            "Pre-uninstall footprint recorded under a non-shared application directory" => "UnEvidenceFootprint",
            _ => "UnEvidenceUnsupported"
        };
        var warningKey = item.Confidence == LeftoverConfidence.Ignore || (item.Confidence == LeftoverConfidence.Review && !allowPersonalData) ? "UnDeleteSafetyRejected"
            : item.Type == LeftoverType.RegistryKey ? "UnRegistryWarning" : "UnOwnedRisk";
        return $"{SystemCore.Core.SystemInformation.InfoFormatting.Technical(item.DisplayLocation)}\n{L["UnType" + item.Type]} | {L["UnConfidence" + item.Confidence]} | {FormatSize(item.Size)}"
            + $"\n{L[evidenceKey]} {L[warningKey]}"
            + (item.Confidence == LeftoverConfidence.Review ? "\n" + L["UnPersonalData"] : "")
            + (item.RequiresElevation ? "\n" + L[item.Type == LeftoverType.RegistryKey ? "UnElevationScope" : "UnElevationRetry"] : "") + "\n" + LeftoverResult(item.Id);
    }
    public string CleanupSummary => cleanup is null ? "" : $"{L["UnCleanupComplete"]}: {cleanup.Items.Count(i => i.Status == LeftoverDeletionStatus.Deleted)} / {cleanup.Items.Count}; {FormatSize(cleanup.RemovedBytes)}\n"
        + string.Join("\n", cleanup.Items.GroupBy(i => i.Status).Select(g => $"{L["UnDelete" + g.Key]}: {g.Count()}"));
    // Off by default. Enabling it never selects anything by itself: the user must still tick the
    // Review rows, so personal-data destruction is always an explicit per-item choice.
    public bool AllowPersonalData
    {
        get => allowPersonalData;
        set
        {
            if (!SetProperty(ref allowPersonalData, value)) return;
            if (!value) selectedLeftovers.RemoveWhere(id => Leftovers.Any(i => i.Id == id && i.Confidence != LeftoverConfidence.Safe));
            OnPropertyChanged(string.Empty);
        }
    }
    public bool CanSelectLeftover(LeftoverItem item) => !Busy && cleanup is null
        && (item.Confidence == LeftoverConfidence.Safe || (allowPersonalData && item.Confidence == LeftoverConfidence.Review));
    public void SelectLeftover(Guid id, bool selected)
    {
        var item = Leftovers.FirstOrDefault(i => i.Id == id);
        if (item is null || !CanSelectLeftover(item)) return;
        if (selected) selectedLeftovers.Add(id); else selectedLeftovers.Remove(id);
        OnPropertyChanged(nameof(SelectedLeftoverCount));
    }
    public bool Busy { get => busy; private set { SetProperty(ref busy, value); RefreshCommand.NotifyCanExecuteChanged(); } }
    public string Search { get => search; set { search = value; page = 0; selectedId = ""; OnPropertyChanged(nameof(Rows)); } }
    public int Filter { get => filter; set { filter = value; page = 0; selectedId = ""; OnPropertyChanged(nameof(Rows)); } }
    public int KindFilter { get => kindFilter; set { kindFilter = value; page = 0; selectedId = ""; OnPropertyChanged(nameof(Rows)); } }
    public int Sort { get => sort; set { sort = value; page = 0; selectedId = ""; OnPropertyChanged(nameof(Rows)); } }
    public string SelectedId { get => selectedId; set { selectedId = value; OnPropertyChanged(nameof(Selected)); } }
    public InstalledApplication? Selected => inventoryActionable ? session?.Inventory.Applications.FirstOrDefault(a => a.Id == selectedId && UnifiedLibraryPresentation.IsVisible(a)) : null;
    public string Status => (loadFailed ? L["UnLoadFailed"] : launcherFailed ? L["UnLauncherUnavailable"] : state == UninstallState.Ready ? L[Busy || session is null ? "UnLoading" : "UnLoaded"] : L["UnState" + state])
        + (session is null && cachedSummary is { } cached ? $" — {L["UnApplicationsKind"]}: {cached.Applications}; {L["UnGamesKind"]}: {cached.Games}" : "")
        + (session?.Inventory.Complete == false ? " — " + L["UnPartial"] : "")
        + (!Busy && !ShowLeftovers && state == UninstallState.Ready && session is { Inventory.Complete: true } && Count == 0 ? "\n" + L[RemovableCount + ManagedCount == 0 ? "UnRemovableEmpty" : "ProductNoMatches"] : "");
    private IEnumerable<InstalledApplication> Filtered()
    {
        var rows = (session?.Inventory.Applications.AsEnumerable() ?? []).Where(UnifiedLibraryPresentation.IsVisible);
        rows = kindFilter switch { 1 => rows.Where(a => a.ItemKind == ItemKind.Application), 2 => rows.Where(a => a.ItemKind == ItemKind.Game), _ => rows };
        rows = rows.Where(a => a.DisplayName.Contains(search, StringComparison.CurrentCultureIgnoreCase) || (a.Publisher?.Contains(search, StringComparison.CurrentCultureIgnoreCase) ?? false));
        rows = filter switch
        {
            1 => rows.Where(a => a.ApplicationType == ApplicationType.Win32),
            2 => rows.Where(a => a.ApplicationType == ApplicationType.Msi),
            3 => rows.Where(a => a.ApplicationType == ApplicationType.Msix),
            4 => rows.Where(a => a.InstallDate is { } d && d <= DateOnly.FromDateTime(DateTime.Today) && d >= DateOnly.FromDateTime(DateTime.Today.AddDays(-30))),
            5 => rows.Where(a => a.EstimatedSize >= 1024L * 1024 * 1024),
            _ => rows
        };
        return sort switch { 1 => rows.OrderBy(a => a.Publisher, StringComparer.CurrentCultureIgnoreCase), 2 => rows.OrderByDescending(a => a.InstallDate),
            3 => rows.OrderByDescending(a => a.EstimatedSize), _ => rows.OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase) };
    }
    public IReadOnlyList<InstalledApplication> Rows => Filtered().Skip(page * 20).Take(20).ToArray();
    public int Count => ShowLeftovers ? Leftovers.Count : Filtered().Count();
    public int Page => page + 1;
    public bool HasPrevious => page > 0;
    public bool HasNext => (page + 1) * 20 < Count;
    public void MovePage(int offset) { page = Math.Clamp(page + offset, 0, Math.Max(0, (Count - 1) / 20)); selectedId = ""; OnPropertyChanged(nameof(Rows)); }
    private async Task LoadAsync(bool force)
    {
        // A disposed view model must never start new provider work. Checked before any
        // lifetime token is read, because CancellationTokenSource.Token throws once disposed.
        if (disposed) return;
        // Automatic freshness must not consume a verified receipt or a cleanup review.
        if (force) { receipt = null; leftoverSession = null; cleanup = null; selectedLeftovers.Clear(); }
        selectedId = ""; if (!ShowLeftovers) state = UninstallState.Ready; launcherFailed = loadFailed = false;
        // Declared outside the try so the catches can ask whether a token was really cancelled.
        // It stays default when BeginOperation never ran, and a default token is never cancelled,
        // so GenuineCancel then reports only the lifetime, which is the honest answer.
        CancellationToken token = default;
        // Busy and the notification that reaches the view's XAML are inside the guarding try.
        // A subscriber that faults here used to skip the finally, leaving Busy set for the life
        // of the process: StartLoad refuses every later load while Busy, so the page never
        // recovered without a restart.
        try
        {
            Busy = true;
            OnPropertyChanged(string.Empty);
            token = BeginOperation();
            if (session is null) { cachedSummary = await Task.Run(() => orchestrator.CacheApplicationsSummary(), token); OnPropertyChanged(string.Empty); }
            session = await ReadAsync(force, token); inventoryActionable = true; if (!ShowLeftovers) page = 0;
        }
        // The UI cancel button cancels the active operation, not the lifetime. A genuine user
        // cancel must be classified as cancellation, never as a provider failure.
        catch (OperationCanceledException) when (GenuineCancel(token)) { inventoryActionable = false; state = UninstallState.Cancelled; }
        // Expected only when teardown has genuinely begun, which is the only reason a live read
        // would meet a disposed dependency.
        catch (ObjectDisposedException) when (ShuttingDown) { inventoryActionable = false; }
        // Retention rows must never authorize a mutation after a failed read.
        catch (Exception e) { inventoryActionable = false; loadFailed = true; diagnostics.Record(DiagnosticFeature.Uninstaller, DiagnosticEvent.ProviderFailure, e.HResult); }
        finally { EndOperation(); }
    }
    // Called only after the view's explicit ContentDialog confirmation.
    public async Task UninstallConfirmedAsync(string confirmedId)
    {
        if (Busy || session is null || Selected is not { CanUninstall: true } app || app.Id != confirmedId) return;
        receipt = null; leftoverSession = null; cleanup = null; selectedLeftovers.Clear();
        // See LoadAsync: Busy is set inside the guarding try so a faulting subscriber cannot
        // leave the page busy for the life of the process.
        var acceptingProgress = true;
        CancellationToken token = default;
        try
        {
            Busy = true;
            token = BeginOperation();
            // The executor consumes its live session even on cancellation/failure.
            // Expire both read caches before handing it off; a later page entry must
            // obtain a new service-owned session, not replay the consumed cached ID.
            inventoryActionable = false;
            orchestrator.Publish(DataDataset.InstalledApplications, session, TimeSpan.Zero);
            orchestrator.Publish(DataDataset.Games, Summarize(session), TimeSpan.Zero);
            var result = await executor.UninstallAsync(new(session.Id, app.Id), new Progress<UninstallState>(s => { if (acceptingProgress) { state = s; OnPropertyChanged(nameof(Status)); } }), token);
            acceptingProgress = false;
            state = result.State;
            receipt = result.State == UninstallState.Removed && result.ApplicationId == app.Id && result.Receipt?.ApplicationId == app.Id
                && app.ItemKind != ItemKind.Game ? result.Receipt : null;
            selectedId = ""; // Keep the last display, but the consumed session is not actionable.
            if (receipt is not null && !token.IsCancellationRequested) await ScanLeftoversCoreAsync(token);
            if (result.State == UninstallState.Removed && result.ApplicationId == app.Id && !token.IsCancellationRequested)
                await RefreshAfterMutationAsync(token);
        }
        // A cancelled token is a cancellation. The same exception type with no cancelled token
        // is a provider fault, and must not be reported to the user as a cancel they performed.
        catch (OperationCanceledException) when (GenuineCancel(token)) { state = UninstallState.Cancelled; inventoryActionable = false; }
        // Teardown interrupted an uninstall that was still in flight. It did not complete, and
        // nothing is reading the state because the window is closing, so Cancelled is the honest
        // reading. It is expected, so it is neither recorded nor shown as a failure.
        catch (ObjectDisposedException) when (ShuttingDown) { state = UninstallState.Cancelled; inventoryActionable = false; }
        // ObjectDisposedException derives from InvalidOperationException, so it must be excluded
        // here: outside shutdown it is an unexpected provider fault and must be recorded below.
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception && e is not ObjectDisposedException) { state = UninstallState.Failed; inventoryActionable = false; }
        // Anything else is a real defect. It is recorded and stays Failed, rather than escaping
        // the confirmation handler and taking the UI thread's async void with it.
        catch (Exception e) { state = UninstallState.Failed; inventoryActionable = false; diagnostics.Record(DiagnosticFeature.Uninstaller, DiagnosticEvent.ProviderFailure, e.HResult); }
        finally { acceptingProgress = false; EndOperation(); }
    }
    private async Task RefreshAfterMutationAsync(CancellationToken token)
    {
        try { session = await ReadAsync(true, token); inventoryActionable = true; loadFailed = false; }
        catch (OperationCanceledException) when (GenuineCancel(token)) { inventoryActionable = false; }
        catch (ObjectDisposedException) when (ShuttingDown) { inventoryActionable = false; }
        // A post-mutation read failure is a real fault, not a status to absorb silently.
        catch (Exception e) { inventoryActionable = false; loadFailed = true; diagnostics.Record(DiagnosticFeature.Uninstaller, DiagnosticEvent.ProviderFailure, e.HResult); }
    }
    public async Task ScanLeftoversAsync()
    {
        if (!CanScanLeftovers || receipt is null) return;
        // See LoadAsync: Busy is set inside the guarding try so a faulting subscriber cannot
        // leave the page busy for the life of the process.
        try { Busy = true; await ScanLeftoversCoreAsync(BeginOperation()); }
        finally { EndOperation(); }
    }
    // The automatic and retry paths share one read-only operation. Never chain deletion.
    private async Task ScanLeftoversCoreAsync(CancellationToken token)
    {
        state = UninstallState.ScanningLeftovers; leftoverSession = null; cleanup = null; selectedLeftovers.Clear(); allowPersonalData = false; page = 0; OnPropertyChanged(string.Empty);
        try
        {
            // Deep scan surfaces name-derived candidates as Review items so a user can still
            // clean what a receipt missed. Nothing it finds is ever selectable without consent.
            var scan = await leftovers.ScanAsync(receipt!.Id, true, token);
            token.ThrowIfCancellationRequested();
            leftoverSession = scan; state = UninstallState.LeftoversReady;
        }
        catch (OperationCanceledException) when (GenuineCancel(token)) { state = UninstallState.Removed; }
        // An interrupted scan means the optional cleanup review is simply absent, which is the
        // same state a user skipping it produces. Expected during teardown, so it is not recorded.
        catch (ObjectDisposedException) when (ShuttingDown) { state = UninstallState.Removed; }
        // Scanning is optional/read-only: an unexpected provider fault must not escape the async UI
        // handler or authorize a partial deletion session. It is recorded rather than absorbed,
        // so a scanner that fails for real is visible instead of silently reading as "no leftovers".
        catch (Exception e) { state = UninstallState.Failed; diagnostics.Record(DiagnosticFeature.Uninstaller, DiagnosticEvent.ProviderFailure, e.HResult); }
    }
    public void KeepLeftovers()
    {
        if (Busy || !ShowLeftovers) return;
        leftoverSession = null; cleanup = null; selectedLeftovers.Clear(); allowPersonalData = false; page = 0; state = UninstallState.Removed;
        OnPropertyChanged(string.Empty);
    }
    public async Task DeleteLeftoversConfirmedAsync(Guid[] confirmedIds)
    {
        if (!CanDeleteLeftovers || leftoverSession is null || !selectedLeftovers.SetEquals(confirmedIds)) return;
        // See LoadAsync: Busy is set inside the guarding try so a faulting subscriber cannot
        // leave the page busy for the life of the process. The token stays outside it so the
        // catches can ask whether a token was really cancelled.
        CancellationToken token = default;
        try
        {
            Busy = true;
            state = UninstallState.CleaningLeftovers; OnPropertyChanged(nameof(Status));
            token = BeginOperation();
            cleanup = await deletion.DeleteAsync(new(leftoverSession.Id, leftoverSession.ApplicationIdentityId, confirmedIds, allowPersonalData), token);
            state = UninstallState.CleanupComplete;
            await RefreshAfterMutationAsync(token);
        }
        catch (OperationCanceledException) when (GenuineCancel(token)) { state = UninstallState.Cancelled; }
        catch (ObjectDisposedException) when (ShuttingDown) { state = UninstallState.Cancelled; }
        // ObjectDisposedException derives from InvalidOperationException, so it must be excluded
        // here: outside shutdown it is an unexpected provider fault and must be recorded below.
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception && e is not ObjectDisposedException) { state = UninstallState.Failed; }
        // A deletion is user-confirmed and destructive, so an unclassified fault is recorded and
        // left Failed. It is never reported as a cancel the user did not request.
        catch (Exception e) { state = UninstallState.Failed; diagnostics.Record(DiagnosticFeature.Uninstaller, DiagnosticEvent.ProviderFailure, e.HResult); }
        finally { selectedLeftovers.Clear(); EndOperation(); }
    }
    public Guid[] SelectedLeftoverIds() => selectedLeftovers.ToArray();
    public override void Dispose() { disposed = true; lifetime.Cancel(); lifetime.Dispose(); session = null; base.Dispose(); }
}
