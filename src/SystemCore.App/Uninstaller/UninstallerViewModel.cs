using CommunityToolkit.Mvvm.Input;
using SystemCore.App.Services;
using SystemCore.App.ViewModels;
using SystemCore.Core.Uninstaller;

namespace SystemCore.App.Uninstaller;

public sealed class UninstallerViewModel : LocalizedViewModel
{
    private readonly IUninstallExecutor executor;
    private readonly ILeftoverScanner leftovers;
    private readonly ILeftoverDeletionService deletion;
    private readonly HashSet<Guid> selectedLeftovers = [];
    private LeftoverDeletionResult? cleanup;
    private RemovalReceipt? receipt;
    private LeftoverScanSession? leftoverSession;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? activeOperation;
    private CancellationToken BeginOperation()
    {
        activeOperation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        return activeOperation.Token;
    }
    private void EndOperation() { activeOperation?.Dispose(); activeOperation = null; Busy = false; OnPropertyChanged(string.Empty); }
    public void CancelOperation() => activeOperation?.Cancel();
    private ApplicationInventorySession? session;
    private string search = "", selectedId = "";
    private int filter, sort, page;
    private bool busy;
    private UninstallState state = UninstallState.Ready;
    public UninstallerViewModel(LocalizationService localizer, IUninstallExecutor executor, ILeftoverScanner leftovers, ILeftoverDeletionService deletion) : base(localizer)
    {
        this.executor = executor;
        this.leftovers = leftovers;
        this.deletion = deletion;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !Busy);
    }
    public AsyncRelayCommand RefreshCommand { get; }
    public string Text(string key) => L[key];
    public string FormatSize(long? bytes) => bytes is >= 0
        ? SystemCore.Core.SystemInformation.InfoFormatting.Size((ulong)bytes.Value, L.Culture) : L["UnUnknown"];
    public string LargeFilterLabel => L.Format("UnLarge", FormatSize(1024L * 1024 * 1024));
    // The surrounding technical field supplies its own direction. Embedded ICU
    // date marks otherwise split a numeric date into reordered RTL fragments.
    public string FormatDate(DateOnly? value) => value is { } date ? SystemCore.Core.SystemInformation.InfoFormatting.Date(date, L.Culture).Replace("\u200f", "").Replace("\u200e", "") : L["UnUnknown"];
    public string Title => L["NavUninstaller"];
    public int RawCount => session?.Inventory.Applications.Count ?? 0;
    public int RemovableCount => session?.Inventory.Applications.Count(RemovableApplications.IsVisible) ?? 0;
    public int HiddenCount => RawCount - RemovableCount;
    public IReadOnlyList<InstalledApplication> Inventory => session?.Inventory.Applications ?? [];
    public bool CanScanLeftovers => !Busy && receipt is not null;
    public string LeftoverSummary => leftoverSession is { } scan ? $"{receipt?.DisplayName}: {scan.Items.Count} — {L[scan.Complete ? "UnScanComplete" : "UnPartial"]}" + (scan.Complete && scan.Items.Count == 0 ? "\n" + L["UnLeftoversEmpty"] : "") : "";
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
            _ => "UnEvidenceUnsupported"
        };
        return $"{SystemCore.Core.SystemInformation.InfoFormatting.Technical(item.DisplayLocation)}\n{L["UnType" + item.Type]} | {L["UnConfidence" + item.Confidence]} | {FormatSize(item.Size)}\n{L[evidenceKey]}\n"
            + L[item.Confidence != LeftoverConfidence.Safe ? "UnDeleteSafetyRejected" : item.Type == LeftoverType.RegistryKey ? "UnRegistryWarning" : "UnOwnedRisk"]
            + (item.RequiresElevation ? "\n" + L["UnElevationScope"] : "") + "\n" + LeftoverResult(item.Id);
    }
    public string CleanupSummary => cleanup is null ? "" : $"{L["UnCleanupComplete"]}: {cleanup.Items.Count(i => i.Status == LeftoverDeletionStatus.Deleted)} / {cleanup.Items.Count}; {FormatSize(cleanup.RemovedBytes)}\n"
        + string.Join("\n", cleanup.Items.GroupBy(i => i.Status).Select(g => $"{L["UnDelete" + g.Key]}: {g.Count()}"));
    public void SelectLeftover(Guid id, bool selected)
    {
        if (Busy || cleanup is not null || !Leftovers.Any(i => i.Id == id && i.Confidence == LeftoverConfidence.Safe)) return;
        if (selected) selectedLeftovers.Add(id); else selectedLeftovers.Remove(id);
        OnPropertyChanged(nameof(SelectedLeftoverCount));
    }
    public bool Busy { get => busy; private set { SetProperty(ref busy, value); RefreshCommand.NotifyCanExecuteChanged(); } }
    public string Search { get => search; set { search = value; page = 0; selectedId = ""; OnPropertyChanged(nameof(Rows)); } }
    public int Filter { get => filter; set { filter = value; page = 0; selectedId = ""; OnPropertyChanged(nameof(Rows)); } }
    public int Sort { get => sort; set { sort = value; page = 0; selectedId = ""; OnPropertyChanged(nameof(Rows)); } }
    public string SelectedId { get => selectedId; set { selectedId = value; OnPropertyChanged(nameof(Selected)); } }
    public InstalledApplication? Selected => session?.Inventory.Applications.FirstOrDefault(a => a.Id == selectedId && RemovableApplications.IsVisible(a));
    public string Status => (state == UninstallState.Ready ? L[Busy ? "UnLoading" : session is not null ? "UnLoaded" : "UnStateReady"] : L["UnState" + state])
        + (session?.Inventory.Complete == false ? " — " + L["UnPartial"] : "")
        + (!Busy && !ShowLeftovers && state == UninstallState.Ready && session is { Inventory.Complete: true } && Count == 0 ? "\n" + L[RemovableCount == 0 ? "UnRemovableEmpty" : "ProductNoMatches"] : "");
    private IEnumerable<InstalledApplication> Filtered()
    {
        var rows = (session?.Inventory.Applications.AsEnumerable() ?? []).Where(RemovableApplications.IsVisible);
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
    public async Task RefreshAsync()
    {
        if (Busy) return;
        selectedId = ""; session = null; state = UninstallState.Ready;
        leftoverSession = null; cleanup = null; selectedLeftovers.Clear();
        Busy = true;
        try { session = await executor.RefreshAsync(BeginOperation()); page = 0; }
        catch (OperationCanceledException) { state = UninstallState.Cancelled; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidOperationException) { state = UninstallState.Failed; }
        finally { EndOperation(); }
    }
    // Called only after the view's explicit ContentDialog confirmation.
    public async Task UninstallConfirmedAsync(string confirmedId)
    {
        if (Busy || session is null || Selected is not { CanUninstall: true } app || app.Id != confirmedId) return;
        Busy = true;
        try
        {
            var result = await executor.UninstallAsync(new(session.Id, app.Id), new Progress<UninstallState>(s => { state = s; OnPropertyChanged(nameof(Status)); }), BeginOperation());
            state = result.State;
            receipt = result.Receipt; leftoverSession = null; cleanup = null; selectedLeftovers.Clear();
            session = null; selectedId = ""; // consume the UI's inventory selection as well
        }
        catch (OperationCanceledException) { state = UninstallState.Cancelled; session = null; }
        finally { EndOperation(); }
    }
    public async Task ScanLeftoversAsync()
    {
        if (!CanScanLeftovers || receipt is null) return;
        Busy = true; state = UninstallState.ScanningLeftovers; leftoverSession = null; cleanup = null; selectedLeftovers.Clear(); page = 0; OnPropertyChanged(nameof(Status));
        try { leftoverSession = await leftovers.ScanAsync(receipt.Id, BeginOperation()); state = UninstallState.LeftoversReady; }
        catch (OperationCanceledException) { state = UninstallState.Cancelled; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception) { state = UninstallState.Failed; }
        finally { EndOperation(); }
    }
    public async Task DeleteLeftoversConfirmedAsync(Guid[] confirmedIds)
    {
        if (!CanDeleteLeftovers || leftoverSession is null || !selectedLeftovers.SetEquals(confirmedIds)) return;
        Busy = true;
        state = UninstallState.CleaningLeftovers; OnPropertyChanged(nameof(Status));
        try { cleanup = await deletion.DeleteAsync(new(leftoverSession.Id, leftoverSession.ApplicationIdentityId, confirmedIds), BeginOperation()); state = UninstallState.CleanupComplete; }
        catch (OperationCanceledException) { state = UninstallState.Cancelled; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception) { state = UninstallState.Failed; }
        finally { selectedLeftovers.Clear(); EndOperation(); }
    }
    public Guid[] SelectedLeftoverIds() => selectedLeftovers.ToArray();
    public override void Dispose() { lifetime.Cancel(); lifetime.Dispose(); session = null; base.Dispose(); }
}
