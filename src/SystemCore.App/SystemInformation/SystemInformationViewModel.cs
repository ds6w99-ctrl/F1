using CommunityToolkit.Mvvm.Input;
using SystemCore.App.Services;
using SystemCore.App.ViewModels;
using SystemCore.Core.Dashboard;
using SystemCore.Core.SystemInformation;

namespace SystemCore.App.SystemInformation;

public sealed class SystemInformationViewModel : LocalizedViewModel, IAsyncDisposable
{
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
    public string LiveValues => telemetry is { } t ? string.Join("\n",
        L["Cpu"] + ": " + (t.CpuPercent.Value is { } c ? L.Format("Percent", c) : L["InfoUnavailable"]),
        t.Memory.Value is { } m ? L.Format("InfoLiveMemory", Size(m.TotalBytes), Size(m.UsedBytes), Size(m.TotalBytes - m.UsedBytes),
            L.Format("Percent", InfoFormatting.Percentage(m.UsedBytes, m.TotalBytes) ?? 0)) : L["InfoUnavailable"],
        t.SystemVolume.Value is { } v ? L.Format("InfoLiveDisk", v.Root, Size(v.UsedBytes), Size(v.TotalBytes), Size(v.AvailableBytes)) : L["InfoUnavailable"],
        L["Uptime"] + ": " + InfoFormatting.Duration(t.Uptime, L.Culture, L["UptimeValue"])) : L["Collecting"];
    private string Size(ulong bytes) => InfoFormatting.Size(bytes, L.Culture);
    public bool IsBusy
    {
        get => busy;
        private set { if (SetProperty(ref busy, value)) OnPropertyChanged(nameof(LoadingVisibility)); }
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
        DashboardMonitor monitor, UiDispatcher dispatcher) : base(localizer)
    {
        this.service = service; this.monitor = monitor; this.dispatcher = dispatcher;
        presentation = new(localizer);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        monitor.SampleAvailable += OnSample;
        monitor.SampleFailed += OnSampleFailed;
    }
    public void SetActive(bool value)
    {
        if (disposed) return;
        active = value;
        monitor.SetActive(this, value);
        if (!value) cancellation?.Cancel();
        else if (snapshot is null && activationLoad is not { IsCompleted: false }) activationLoad = LoadOnActivationAsync();
    }
    private async Task LoadOnActivationAsync()
    {
        try
        {
            if (RefreshCommand.ExecutionTask is { IsCompleted: false } pending) await pending;
            if (active && !disposed && snapshot is null && !RefreshCommand.IsRunning)
                await RefreshCommand.ExecuteAsync(null);
        }
        catch (OperationCanceledException) when (!active || disposed) { }
        catch (Exception ex) when (ex is not OutOfMemoryException) { ShowFailure(ex); }
    }
    private async Task RefreshAsync()
    {
        if (disposed || !active) return;
        using var source = new CancellationTokenSource();
        cancellation = source;
        IsBusy = true;
        try
        {
            // Keep all Windows collectors, including synchronous setup, off the UI thread.
            var result = await Task.Run(() => service.CollectAsync(source.Token), source.Token);
            if (!active || disposed || source.IsCancellationRequested) return;
            snapshot = result;
            Groups = presentation.Build(result);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested) { }
        catch (Exception ex) when (ex is not OutOfMemoryException) { ShowFailure(ex); }
        finally
        {
            cancellation = null;
            IsBusy = false;
        }
    }
    private void ShowFailure(Exception error)
    {
        SystemCore.Core.Diagnostics.ReliabilityDiagnostics.Log.UnexpectedFailure((int)SystemCore.Core.Diagnostics.ReliabilityComponent.SystemInformation, error.HResult);
        if (!disposed && active) Groups = new InformationGroup[] { new(L["InfoTitle"], new InformationRow[] { new(L["InfoStatus"], L["InfoFailed"]) }) };
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
