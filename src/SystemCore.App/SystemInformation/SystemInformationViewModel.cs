using CommunityToolkit.Mvvm.Input;
using SystemCore.App.Services;
using SystemCore.App.ViewModels;
using SystemCore.Core.Dashboard;
using SystemCore.Core.SystemInformation;
using SystemCore.Core.Orchestration;
using SystemCore.Core.Diagnostics;

namespace SystemCore.App.SystemInformation;

public sealed class SystemInformationViewModel : LocalizedViewModel, IAsyncDisposable
{
    // Static details are cheap to re-read; live values already come from telemetry.
    public static readonly TimeSpan SystemInformationFreshness = TimeSpan.FromMinutes(2);
    private readonly IApplicationDataOrchestrator orchestrator;
    private readonly IAppDiagnostics diagnostics;
    private bool refreshFailed;
    private readonly ISystemInformationService service;
    private readonly DashboardMonitor monitor;
    private readonly UiDispatcher dispatcher;
    private readonly SystemInformationPresentation presentation;
    private CancellationTokenSource? cancellation;
    private SystemInformationSnapshot? snapshot;
    private DashboardSample? telemetry;
    private bool active, disposed, busy;
    private Task? activationLoad;
    private IReadOnlyList<InformationGroup> groups = [];
    public string Title => L["InfoTitle"];
    public string Description => L["InfoDescription"];
    public string RefreshLabel => L["InfoRefresh"];
    public string LoadingLabel => L["InfoLoading"];
    public Microsoft.UI.Xaml.Visibility LoadingVisibility => IsBusy ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public string LiveTitle => L["InfoLive"];
    public string LiveValues => (refreshFailed ? L["InfoRefreshFailed"] + "\n" : IsBusy ? L["InfoLoading"] + "\n" : "") + (telemetry is { } t ? string.Join("\n",
        L["Cpu"] + ": " + (t.CpuPercent.Value is { } c ? L.Format("Percent", c) : L["InfoUnavailable"]),
        t.Memory.Value is { } m ? L.Format("InfoLiveMemory", Size(m.TotalBytes), Size(m.UsedBytes), Size(m.TotalBytes - m.UsedBytes),
            L.Format("Percent", InfoFormatting.Percentage(m.UsedBytes, m.TotalBytes) ?? 0)) : L["InfoUnavailable"],
        t.SystemVolume.Value is { } v ? L.Format("InfoLiveDisk", v.Root, Size(v.UsedBytes), Size(v.TotalBytes), Size(v.AvailableBytes)) : L["InfoUnavailable"],
        L["Uptime"] + ": " + InfoFormatting.Duration(t.Uptime, L.Culture, L["UptimeValue"])) : L["Collecting"]);
    private string Size(ulong bytes) => InfoFormatting.Size(bytes, L.Culture);
    public bool IsBusy
    {
        get => busy;
        private set { if (SetProperty(ref busy, value)) { RefreshCommand.NotifyCanExecuteChanged(); OnPropertyChanged(nameof(LoadingVisibility)); OnPropertyChanged(nameof(LiveValues)); } }
    }
    public IReadOnlyList<InformationGroup> Groups
    {
        get => groups;
        private set { if (SetProperty(ref groups, value)) { OnPropertyChanged(nameof(Overview)); OnPropertyChanged(nameof(DetailGroups)); } }
    }
    public InformationGroup? Overview => groups.FirstOrDefault();
    public IReadOnlyList<InformationGroup> DetailGroups => groups.Skip(1).ToArray();
    public IAsyncRelayCommand RefreshCommand { get; }

    public SystemInformationViewModel(LocalizationService localizer, ISystemInformationService service,
        DashboardMonitor monitor, UiDispatcher dispatcher, IApplicationDataOrchestrator orchestrator,
        IAppDiagnostics diagnostics) : base(localizer)
    {
        this.service = service; this.monitor = monitor; this.dispatcher = dispatcher;
        this.orchestrator = orchestrator; this.diagnostics = diagnostics;
        presentation = new(localizer);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        monitor.SampleAvailable += OnSample;
        monitor.SampleFailed += OnSampleFailed;
        orchestrator.RegisterBackgroundWarmup(DataDataset.SystemInformation, LoadTier.Background,
            async token => await ReadAsync(false, token), SystemInformationFreshness);
    }
    private async Task<SystemInformationSnapshot> ProduceAsync(CancellationToken token)
    {
        var result = await service.CollectAsync(token).ConfigureAwait(false);
        orchestrator.CacheSystemInformationSummary(new((int)(result.Cpu.Data?.Sum(c => c.Threads ?? 0) ?? 0),
            result.Memory.Data?.Aggregate(0UL, (total, item) => total + (item.Capacity ?? 0)) ?? 0,
            result.Storage.Data?.Disks.Count ?? 0, DateTimeOffset.UtcNow));
        return result;
    }
    private Task<SystemInformationSnapshot> ReadAsync(bool force, CancellationToken token) => force
        ? orchestrator.RefreshAsync(DataDataset.SystemInformation, LoadTier.Background, ProduceAsync, token)
        : orchestrator.GetAsync(DataDataset.SystemInformation, LoadTier.Background, SystemInformationFreshness, ProduceAsync, token);
    public Task RefreshIfStaleAsync() => orchestrator.IsStale(DataDataset.SystemInformation, SystemInformationFreshness) ? InitializeAsync() : Task.CompletedTask;
    public Task InitializeAsync() => StartLoad(false);
    private Task RefreshAsync() => StartLoad(true);
    private Task StartLoad(bool force) => activationLoad is { IsCompleted: false } ? activationLoad : activationLoad = LoadAsync(force);
    public void SetActive(bool value)
    {
        if (disposed) return;
        active = value;
        monitor.SetActive(this, value);
        if (value) _ = InitializeAsync();
    }
    private async Task LoadAsync(bool force)
    {
        if (disposed) return;
        using var source = new CancellationTokenSource();
        cancellation = source;
        refreshFailed = false; IsBusy = true;
        try
        {
            if (snapshot is null)
            {
                var cached = await Task.Run(() => orchestrator.CacheSystemInformationSummary(), source.Token);
                if (cached is not null && !disposed) Groups = new InformationGroup[] { new(L["InfoOverview"], new InformationRow[] { new(L["InfoThreads"], cached.LogicalProcessors.ToString(L.Culture)),
                    new(L["InfoMemoryModules"], Size(cached.PhysicalMemoryBytes)), new(L["InfoStorage"], cached.StorageDevices.ToString(L.Culture)) }) };
            }
            // The orchestrator runs synchronous collector setup off the UI thread too.
            var result = await ReadAsync(force, source.Token);
            if (disposed || source.IsCancellationRequested) return;
            snapshot = result;
            Groups = presentation.Build(result);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested) { }
        // Disposal cancels the in-flight source, so this is the expected teardown race.
        catch (ObjectDisposedException) when (disposed || orchestrator.IsShutdown) { }
        catch (Exception ex) { ShowFailure(ex); diagnostics.Record(DiagnosticFeature.SystemInfo, DiagnosticEvent.ProviderFailure, ex.HResult); }
        finally
        {
            cancellation = null;
            IsBusy = false;
        }
    }
    private void ShowFailure(Exception error)
    {
        // A failed read is not a crash. Keep the last good presentation and never render exception text.
        if (!disposed) { refreshFailed = true; OnPropertyChanged(nameof(LiveValues)); }
    }
    private void OnSample(object? sender, DashboardSample sample) => dispatcher.Post(() =>
    {
        if (!active || disposed) return;
        telemetry = sample;
        OnPropertyChanged(nameof(LiveValues));
    });
    private void OnSampleFailed(object? sender, EventArgs e) => dispatcher.Post(() =>
    {
        if (disposed) return;
        telemetry = null;
        OnPropertyChanged(nameof(LiveValues));
    });
    protected override void RefreshLanguage()
    {
        if (snapshot is not null) Groups = presentation.Build(snapshot);
        base.RefreshLanguage();
    }
    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        cancellation?.Cancel();
        monitor.SetActive(this, false);
        monitor.SampleAvailable -= OnSample;
        monitor.SampleFailed -= OnSampleFailed;
        base.Dispose();
    }
    public async ValueTask DisposeAsync()
    {
        Dispose();
        if (activationLoad is { } pending) await pending;
        if (RefreshCommand.ExecutionTask is { IsCompleted: false } refresh) await refresh;
    }
}
