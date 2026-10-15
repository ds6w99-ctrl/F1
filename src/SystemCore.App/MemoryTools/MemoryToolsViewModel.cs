using SystemCore.App.Services;
using SystemCore.App.ViewModels;
using SystemCore.Core.MemoryTools;
using SystemCore.Core.Performance;
using SystemCore.Core.SystemInformation;

namespace SystemCore.App.MemoryTools;

public sealed class MemoryToolsViewModel : LocalizedViewModel
{
    private readonly IPerformanceMonitorService monitor;
    private readonly IMemoryAnalysisService analysis;
    private readonly IProcessResourceAnalyzer processes;
    private readonly UiDispatcher dispatcher;
    private readonly IMemoryOptimizationService optimization;
    private MemoryOptimizationResult? optimizationResult;
    private bool optimizing;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? detailsOperation;
    private MemoryAnalysis? current;
    private MemoryProcessDetails? details;
    private ProcessPerformanceInfo? selected;
    private IReadOnlyList<HistoryPoint> history = [];
    private bool active, disposed;
    private int queued, mode;
    private string status = "MemReady", disk = "", network = "";
    public MemoryToolsViewModel(LocalizationService localizer, IPerformanceMonitorService monitor, IMemoryAnalysisService analysis,
        IProcessResourceAnalyzer processes, UiDispatcher dispatcher, IMemoryOptimizationService optimization) : base(localizer)
    { this.monitor = monitor; this.analysis = analysis; this.processes = processes; this.dispatcher = dispatcher; this.optimization = optimization; monitor.Updated += OnUpdated; }
    public string Text(string key) => L[key];
    public string Title => L["NavMemoryTools"];
    public string Status => L[status];
    public bool CanOptimize => active && !optimizing && !disposed;
    public bool HasOptimizationResult => optimizationResult is not null;
    public string OptimizationSummary => optimizationResult is { } result ? L["MemOutcome" + result.Result] + "\n" + L["MemPlan" + result.Plan.Reason] + "\n" +
        L.Format("MemReleased", Size(result.KnownReleasedBytes), result.ExecutedActions.Count, result.SkippedActions.Count, result.Duration.TotalSeconds) : "";
    public string BeforeAfter => optimizationResult is { } result ? Measurement("MemBefore", result.BeforeSnapshot) + "\n" + Measurement("MemAfter", result.AfterSnapshot) + "\n" + L["MemMeasurementNotice"] : "";
    private string Measurement(string title, MemoryOptimizationMeasurement value) => L.Format("MemMeasurement", L[title],
        value.Memory is { } memory ? InfoFormatting.Time(memory.Timestamp, L.Culture) : L["InfoUnavailable"], Size(value.Memory?.AvailablePhysical), Size(value.Memory?.UsedPhysical),
        Size(value.Memory?.SelfWorkingSet), Size(value.Memory?.SelfPrivateBytes), Size(value.Memory?.CommitUsed), Size(value.KnownCacheBytes));
    public string Physical => current?.Snapshot is { } m ? L.Format("MemPhysicalValues", Size(m.UsedPhysical), Size(m.TotalPhysical), Percent(m.UsagePercent), Size(m.AvailablePhysical)) : L["InfoUnavailable"];
    public string Commit => current?.Snapshot is { } m ? L.Format("MemCommitValues", Size(m.CommitUsed), Size(m.CommitLimit), Percent(m.CommitPercent)) : L["InfoUnavailable"];
    public string Cache => Size(current?.Snapshot?.SystemCache);
    public string PageFile => L["InfoUnavailable"];
    public string Pressure => L["MemPressure" + (current?.Pressure.State ?? MemoryPressure.Unknown)] + "\n" + L["MemReason" + (current?.Pressure.Reason ?? MemoryAssessmentReason.Unavailable)];
    public string Updated => current?.Snapshot is { } m ? L.Format("MemUpdated", InfoFormatting.Time(m.Timestamp, L.Culture)) : "";
    public string ConsumersStatus => current?.Consumers.Available == true ? L.Format("MemConsumersCount", Rows.Count) : L["InfoUnavailable"];
    public string Disk => L["MemDiskUnavailable"] + "\n" + disk;
    public string Network => L["MemNetworkUnavailable"] + "\n" + network;
    public IReadOnlyList<HistoryPoint> History => history;
    public string HistoryStatus => L.Format("PerfHistoryCount", history.Count);
    public string ChartDescription => HistoryStatus + "; " + Physical;
    public int Mode { get => mode; set { if (value is < 0 or > 1) return; mode = value; selected = null; details = null; detailsOperation?.Cancel(); Notify(); } }
    public IReadOnlyList<ProcessPerformanceInfo> Rows => (mode == 0 ? current?.Consumers.TopMemory : current?.Consumers.TopCpu) ?? [];
    public string SelectedKey => selected is { } p ? Key(p) : "";
    public static string Key(ProcessPerformanceInfo p) => $"{p.Pid}:{p.StartTicks}";
    public string RowText(ProcessPerformanceInfo p) => $"\u2068{p.Name}\u2069 · {L["PerfPid"]} {p.Pid}\n{L["PerfWorkingSet"]}: {Size(p.WorkingSet)} · {L["MemPrivate"]}: {Size(p.PrivateBytes)} · {L["Cpu"]}: {Percent(p.CpuPercent)}";
    public string Details => selected is { } p ? RowText(p) + "\n" + L["MemDetailsReadOnly"] : "";
    public string TechnicalDetails => details is { } d ? $"{d.ExecutablePath ?? L["InfoUnavailable"]}\n{d.Publisher ?? L["MemUnknownPublisher"]}" : selected is null ? "" : L["InfoUnavailable"];
    public bool CanOpenLocation => active && details is { ExecutablePath: not null } resolved && selected is { } chosen && chosen.Pid == resolved.Pid && chosen.StartTicks == resolved.StartTicks;
    private string Numeric(string text) => L.Culture.TextInfo.IsRightToLeft ? "\u2066" + text + "\u2069" : text;
    private string Size(ulong? bytes) => bytes is { } value ? Numeric(InfoFormatting.Size(value, L.Culture)) : L["InfoUnavailable"];
    private string Percent(double? value) => value is { } p ? Numeric(L.Format("Percent", p)) : L["InfoUnavailable"];
    public void SetActive(bool value)
    {
        if (disposed) return; active = value; monitor.SetActive(this, value, detailed: true);
        if (!value) { detailsOperation?.Cancel(); current = null; selected = null; details = null; history = []; Notify(); }
        else Refresh();
    }
    private void OnUpdated(object? sender, PerformanceSnapshot snapshot)
    {
        if (!active || disposed || Interlocked.Exchange(ref queued, 1) != 0) return;
        if (!dispatcher.Post(() => { Interlocked.Exchange(ref queued, 0); if (active && !disposed) Refresh(); })) Interlocked.Exchange(ref queued, 0);
    }
    private void Refresh()
    {
        current = analysis.Analyze(); history = monitor.GetHistory();
        if (selected is { } old)
        {
            var updated = Rows.FirstOrDefault(p => p.Pid == old.Pid && p.StartTicks == old.StartTicks);
            selected = updated; if (updated is null) { details = null; detailsOperation?.Cancel(); }
        }
        var sample = monitor.Latest;
        string Rate(double? value) => value is { } rate ? PerformanceMath.RateText(rate, L.Culture) : L["InfoUnavailable"];
        disk = L.Format("MemSystemDisk", Rate(sample?.Disk.TotalBytesPerSecond));
        network = L.Format("MemSystemNetwork", Rate(sample?.Network.Download), Rate(sample?.Network.Upload));
        Notify();
    }
    public void Analyze() { MemoryToolsDiagnostics.Log.AnalysisRequested(); Refresh(); status = "MemAnalyzed"; Notify(); }
    public async Task OptimizeAsync()
    {
        if (!CanOptimize) return;
        optimizing = true; status = "MemOptimizing"; Notify();
        try
        {
            optimizationResult = await Task.Run(() => optimization.OptimizeAsync(lifetime.Token), lifetime.Token);
            status = "MemOptimizationFinished";
        }
        catch (OperationCanceledException) { status = "MemOptimizationCancelled"; }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { status = "MemOptimizationFailed"; MemoryToolsDiagnostics.Log.FailureReason("OptimizationFailure"); }
        finally { optimizing = false; if (!disposed) { if (active) Refresh(); else Notify(); } }
    }
    public async Task SelectAsync(string key)
    {
        detailsOperation?.Cancel(); selected = Rows.FirstOrDefault(p => Key(p) == key); details = null; Notify();
        if (selected is not { StartTicks: { } start } p) return;
        var operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); detailsOperation = operation;
        try { var result = await processes.ReadDetailsAsync(p.Pid, start, operation.Token); if (!operation.IsCancellationRequested && active && selected?.Pid == p.Pid && selected.StartTicks == start) { details = result; if (result is null) status = "MemProcessChanged"; Notify(); } }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(detailsOperation, operation)) detailsOperation = null; operation.Dispose(); }
    }
    public async Task OpenLocationAsync()
    {
        if (CanOpenLocation && selected is { StartTicks: { } start } p)
        { try { if (!await processes.OpenLocationAsync(p.Pid, start, lifetime.Token)) { status = "MemProcessChanged"; details = null; Notify(); } } catch (OperationCanceledException) { } }
    }
    private void Notify() => OnPropertyChanged(string.Empty);
    protected override void RefreshLanguage() { if (active) Refresh(); else Notify(); }
    public override void Dispose() { disposed = true; lifetime.Cancel(); monitor.SetActive(this, false); monitor.Updated -= OnUpdated; current = null; history = []; selected = null; details = null; base.Dispose(); }
}
