using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCore.App.Services;
using SystemCore.App.ViewModels;
using SystemCore.Core.Cleaner;
using SystemCore.Core.SystemInformation;

namespace SystemCore.App.Cleaner;

public sealed class CleanerCategoryRow : ObservableObject
{
    public CleanerCategoryResult Result { get; }
    public string Name { get; }
    public string Detail { get; }
    public bool CanSelect => Result.Status == CleanerScanStatus.Complete && Result.ItemCount > 0;
    private bool selected;
    public bool Selected { get => selected; set => SetProperty(ref selected, value && CanSelect); }
    public CleanerCategoryRow(CleanerCategoryResult result, LocalizationService l)
    {
        Result = result; Name = l["Clean" + result.Category];
        selected = result.DefaultSelected && CanSelect;
        Detail = l.Format("CleanCategoryDetail", result.ItemCount, InfoFormatting.Size((ulong)result.Bytes, l.Culture),
            result.Skipped.Values.Sum(), l["CleanStatus" + result.Status], l[result.Review ? "CleanReview" : "CleanLow"],
            result.RequiresElevation ? l["CleanElevation"] : l["CleanUserAccess"]);
    }
}
public sealed class CleanerItemRow(CleanerItem item) : ObservableObject
{
    public CleanerItem Item { get; } = item;
    public string Display => Item.DisplayPath;
    public string AutomationId => "CleanerItem-" + Item.RelativePath;
    private bool selected = true;
    public bool Selected { get => selected; set => SetProperty(ref selected, value); }
}
public sealed class CleanerViewModel : LocalizedViewModel
{
    private readonly ICleanerService scanner;
    private readonly CleanerScanState state;
    private CancellationTokenSource? cancellation;
    private CleanerScanSession? session;
    private bool busy, disposed;
    private int selectedCategory = -1;
    private readonly HashSet<Guid> excluded = [];
    private CleanerCleanResult? cleanResult;
    private bool cleaning, failed;
    private int previewPage;
#if CLEANER_VALIDATION
    private int selectionSubscriptions;
#endif
    private CleanerScanProgress? scanProgress;
    public string ScanProgress => !IsBusy || cleaning || scanProgress is not { } p ? "" :
        L.Format("CleanLiveProgress", L["Clean" + p.Category], p.Visited, p.Eligible,
            InfoFormatting.Size((ulong)p.Bytes, L.Culture), p.Status is { } status ? L["CleanStatus" + status] : L["CleanScanning"]);
    public ObservableCollection<CleanerCategoryRow> Categories { get; } = [];
    public ObservableCollection<CleanerItemRow> Preview { get; } = [];
    public string Title => L["NavCleaner"];
    public string Description => L["CleanDescription"];
    public string ScanLabel => L["CleanScan"];
    public string CancelLabel => L["CleanCancel"];
    public string CleanLabel => L["CleanAction"];
    public string PreviewLabel => L["CleanPreview"];
    public string SafetyNotice => L["CleanSafety"];
    public string PreviousLabel => L["CleanPrevious"];
    public string NextLabel => L["CleanNext"];
    public string Summary => failed ? L["CleanFailure"] : IsBusy ? L[cleaning ? "CleanCleaning" : "CleanScanning"] : cleanResult is { } result ?
        L.Format("CleanFinalResult", result.Deleted, result.Skipped, result.Failed, InfoFormatting.Size((ulong)result.RemovedBytes, L.Culture),
            result.Accepted ? L["CleanRescan"] : L["CleanRejected"]) +
        (result.Items.Any(i => i.Outcome == CleanerItemOutcome.Unknown) ? "\n" + L["CleanUnknownResult"] : "") +
        (result.RecycleBin is { } bin ? "\n" + L["CleanBin" + bin] : "") : session is null ? L["CleanReady"] :
        L.Format("CleanSummary", session.Categories.Sum(c => c.ItemCount), InfoFormatting.Size((ulong)session.Categories.Sum(c => c.Bytes), L.Culture));
    public string OutcomeDetails => cleanResult is null ? "" : string.Join(" · ", cleanResult.Items.GroupBy(i => i.Outcome).Select(g => L["CleanOutcome" + g.Key] + ": " + g.Count().ToString(L.Culture)));
    public bool IsBusy { get => busy; private set { SetProperty(ref busy, value); OnPropertyChanged(nameof(Summary)); OnPropertyChanged(nameof(ScanProgress)); UpdateCommands(); } }
    public bool CanClean => !IsBusy && !disposed && session is not null && !session.Categories.Any(c => c.Status == CleanerScanStatus.Cancelled) && Categories.Any(c => c.Selected && c.CanSelect && (c.Result.Category == CleanerCategory.RecycleBin || c.Result.Items.Any(i => !excluded.Contains(i.Id))));
    public bool CanEdit => !IsBusy;
    public int SelectedCategory
    {
        get => selectedCategory;
        set { if (SetProperty(ref selectedCategory, value)) { previewPage = 0; UpdatePreview(); } }
    }
    public IAsyncRelayCommand ScanCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IRelayCommand PreviousCommand { get; }
    public IRelayCommand NextCommand { get; }
    public CleanerViewModel(LocalizationService localizer, ICleanerService scanner, CleanerScanState state) : base(localizer)
    {
        this.scanner = scanner; this.state = state;
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy);
        CancelCommand = new RelayCommand(() => cancellation?.Cancel());
        PreviousCommand = new RelayCommand(() => { previewPage--; UpdatePreview(); }, () => previewPage > 0 && !IsBusy);
        NextCommand = new RelayCommand(() => { previewPage++; UpdatePreview(); }, () => !IsBusy && selectedCategory >= 0 && selectedCategory < Categories.Count && (previewPage + 1) * 100 < Categories[selectedCategory].Result.Items.Count);
    }
    public void Leave() { cancellation?.Cancel(); scanner.Invalidate(); session = null; excluded.Clear(); Render(); }
    // ---- deep scan (explicit, opt-in, preview-first) ----
    private IReadOnlyList<SystemCore.Infrastructure.Cleaner.DeepScanCandidate> deepFindings = Array.Empty<SystemCore.Infrastructure.Cleaner.DeepScanCandidate>();
    private readonly HashSet<string> deepSelected = [];
    private bool deepScanning, deepFailed;
    public IReadOnlyList<SystemCore.Infrastructure.Cleaner.DeepScanCandidate> DeepFindings => deepFindings;
    public bool IsDeepScanning => deepScanning;
    public string DeepScanLabel => L["CleanDeepScan"];
    public string DeepScanNote => L["CleanDeepScanNote"];
    public string DeepScanSummary => deepScanning ? L["CleanDeepScanning"] : deepFailed ? L["CleanFailure"] :
        deepFindings.Count == 0 ? "" : L.Format("CleanDeepResult", deepFindings.Count, InfoFormatting.Size((ulong)deepFindings.Sum(f => f.Size), L.Culture), deepSelected.Count);
    public bool IsDeepSelected(string path) => deepSelected.Contains(path);
    public void SelectDeep(string path, bool selected) { if (selected) deepSelected.Add(path); else deepSelected.Remove(path); OnPropertyChanged(nameof(DeepScanSummary)); OnPropertyChanged(nameof(CanCleanSelectedDeep)); }
    public bool CanCleanSelectedDeep => !IsBusy && !deepScanning && deepSelected.Count > 0;
    public async Task RunDeepScanAsync()
    {
        if (IsBusy || deepScanning) return;
        deepScanning = true; deepFailed = false; deepFindings = Array.Empty<SystemCore.Infrastructure.Cleaner.DeepScanCandidate>(); deepSelected.Clear(); OnPropertyChanged(string.Empty);
        try
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            deepFindings = await new SystemCore.Infrastructure.Cleaner.DeepScanService().ScanAsync(new string[] { profile }, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { deepFailed = true; Core.Diagnostics.ReliabilityDiagnostics.Log.UnexpectedFailure((int)Core.Diagnostics.ReliabilityComponent.Cleaner, ex.HResult); }
        finally { deepScanning = false; OnPropertyChanged(string.Empty); }
    }
    // Removal goes item by item through the ordinary pinned-handle path, so every safety check
    // that protects a normal clean also protects a deep-scan choice.
    public async Task CleanDeepSelectionAsync()
    {
        if (!CanCleanSelectedDeep) return;
        var selected = deepSelected.ToArray();
        var results = await new SystemCore.Infrastructure.Cleaner.DeepScanService().RemoveAsync(selected, CancellationToken.None);
        cleanResult = new(true, results);
        deepFindings = deepFindings.Where(f => !selected.Contains(f.Path, StringComparer.OrdinalIgnoreCase)).ToArray();
        deepSelected.Clear();
        OnPropertyChanged(string.Empty);
    }
    public CleanerCleanRequest? Selection() => CanClean ? new(session!.Id, Categories.Where(c => c.Selected && c.CanSelect).SelectMany(c => c.Result.Items).Where(i => !excluded.Contains(i.Id)).Select(i => i.Id).ToArray(),
        Categories.Any(c => c.Selected && c.CanSelect && c.Result.Category == CleanerCategory.RecycleBin)) : null;
    public string ConfirmationTitle => L["CleanConfirmTitle"];
    public string ConfirmationText(CleanerCleanRequest request)
    {
        var selected = request.ItemIds.ToHashSet();
        var categories = Categories.Where(c => c.Result.Items.Any(i => selected.Contains(i.Id))).ToArray();
        return L.Format("CleanConfirmText", string.Join(", ", categories.Select(c => c.Name)), request.ItemIds.Count,
            InfoFormatting.Size((ulong)categories.SelectMany(c => c.Result.Items).Where(i => selected.Contains(i.Id)).Sum(i => i.Size), L.Culture)) +
            (request.EmptyRecycleBin ? "\n\n" + L["CleanBinConfirm"] : "") +
            (categories.Any(c => c.Result.RequiresElevation) ? "\n\n" + L["CleanElevationConfirm"] : "");
    }
    public async Task CleanAsync(CleanerCleanRequest request)
    {
        if (!CanClean || request.SessionId != session?.Id) return;
        using var source = new CancellationTokenSource();
        scanProgress = null;
        cancellation = source; cleaning = true; IsBusy = true; state.Set(null); failed = false;
        try { cleanResult = await scanner.CleanAsync(request, source.Token); }
        catch (OperationCanceledException) when (source.IsCancellationRequested) { failed = true; }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { failed = true; Core.Diagnostics.ReliabilityDiagnostics.Log.UnexpectedFailure((int)Core.Diagnostics.ReliabilityComponent.Cleaner, ex.HResult); }
        finally { scanner.Invalidate(); session = null; excluded.Clear(); cancellation = null; cleaning = false; IsBusy = false; Render(); }
    }
    private void UpdateCommands()
    {
        ScanCommand.NotifyCanExecuteChanged(); PreviousCommand.NotifyCanExecuteChanged(); NextCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanClean)); OnPropertyChanged(nameof(CanEdit));
#if CLEANER_VALIDATION
        OnPropertyChanged(nameof(ValidationStatus));
#endif
    }
    private async Task ScanAsync()
    {
        if (disposed) return;
        using var source = new CancellationTokenSource();
        cancellation = source; failed = false; cleanResult = null; IsBusy = true; session = null; excluded.Clear(); state.Set(null); ClearCategories(); ClearPreview();
        try
        {
            // Created on the UI context; scanner coalesces before Report posts here.
            // Queued updates from cancelled/previous scans cannot revive stale UI.
            var progress = new Progress<CleanerScanProgress>(value =>
            {
                if (disposed || !ReferenceEquals(cancellation, source) || source.IsCancellationRequested) return;
                scanProgress = value;
                OnPropertyChanged(nameof(ScanProgress));
            });
            session = await scanner.ScanAsync(source.Token, progress);
            if (disposed) return;
            if (source.IsCancellationRequested) { session = null; Render(); return; }
            Render();
            if (!source.IsCancellationRequested) state.Set(session);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested) { scanner.Invalidate(); session = null; Render(); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { failed = true; Core.Diagnostics.ReliabilityDiagnostics.Log.UnexpectedFailure((int)Core.Diagnostics.ReliabilityComponent.Cleaner, ex.HResult); scanner.Invalidate(); session = null; Render(); }
        finally { cancellation = null; scanProgress = null; IsBusy = false; }
    }
    private void Render()
    {
        ClearCategories(); ClearPreview(); selectedCategory = -1;
        if (session is not null) foreach (var category in session.Categories)
        {
            // Requested behavior: an option the user cannot actually clean is not shown at all.
            // Unavailable categories (no existing target after the allowlist) are hidden instead
            // of being displayed as a disabled "unsupported" row.
            if (category.Status == CleanerScanStatus.Unavailable) continue;
            var row = new CleanerCategoryRow(category, L);
            row.PropertyChanged += CategoryChanged;
#if CLEANER_VALIDATION
            selectionSubscriptions++;
#endif
            Categories.Add(row);
        }
        OnPropertyChanged(string.Empty);
        UpdateCommands();
    }
    private void UpdatePreview()
    {
        ClearPreview();
        if (selectedCategory >= 0 && selectedCategory < Categories.Count)
            foreach (var item in Categories[selectedCategory].Result.Items.Skip(previewPage * 100).Take(100))
            {
                var row = new CleanerItemRow(item) { Selected = !excluded.Contains(item.Id) };
                row.PropertyChanged += ItemChanged;
#if CLEANER_VALIDATION
                selectionSubscriptions++;
#endif
                Preview.Add(row);
            }
        UpdateCommands();
    }
    protected override void RefreshLanguage() { Leave(); base.RefreshLanguage(); }
    private void CategoryChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => UpdateCommands();
    private void ItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is CleanerItemRow row) { if (row.Selected) excluded.Remove(row.Item.Id); else excluded.Add(row.Item.Id); }
        UpdateCommands();
    }
    private void ClearCategories()
    {
        foreach (var row in Categories) row.PropertyChanged -= CategoryChanged;
#if CLEANER_VALIDATION
        selectionSubscriptions -= Categories.Count;
#endif
        Categories.Clear();
    }
    private void ClearPreview()
    {
        foreach (var row in Preview) row.PropertyChanged -= ItemChanged;
#if CLEANER_VALIDATION
        selectionSubscriptions -= Preview.Count;
#endif
        Preview.Clear();
    }
    public override void Dispose() { disposed = true; cancellation?.Cancel(); ClearCategories(); ClearPreview(); base.Dispose(); }
#if CLEANER_VALIDATION
    public Func<string>? ValidationUiDiagnostics { get; set; }
    public string ValidationStatus => ((ValidationCleanerService)scanner).Status + ((ValidationCleanerService)scanner).Diagnostics + $";SelectionSubscriptions:{selectionSubscriptions};PreviewRows:{Preview.Count};CleanerBusy:{IsBusy}" + ValidationUiDiagnostics?.Invoke();
    public void PrepareValidation() { if (IsBusy) return; Leave(); ((ValidationCleanerService)scanner).Prepare(); cleanResult = null; OnPropertyChanged(string.Empty); }
#endif
}
