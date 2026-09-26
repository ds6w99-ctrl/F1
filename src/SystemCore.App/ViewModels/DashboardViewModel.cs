using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using SystemCore.App.Services;
using SystemCore.Core.Dashboard;

namespace SystemCore.App.ViewModels;

public sealed class DashboardViewModel : LocalizedViewModel, IAsyncDisposable
{
    private readonly DashboardMonitor monitor;
    private readonly UiDispatcher dispatcher;
    private readonly IDashboardInventoryProvider inventory;
    private readonly SystemCore.Core.Cleaner.CleanerScanState cleaner;
    private readonly SystemCore.Core.Startup.IStartupInventoryService startup;
    private readonly CancellationTokenSource lifetime = new();
    private bool started, disposed, active;
    private DashboardSample? latest;
    private MachineOverview? machine;
    private InventoryCounts? counts;
    private bool inventoryFinished;
    private bool startupFailed;
    private Task? inventoryLoad, startupLoad;
    private bool machineFailed;
    private string updatedText = "", systemDescription = "", deviceName = "", uptime = "", status = "";
    private Visibility statusVisibility = Visibility.Collapsed;
    private readonly Queue<SystemCore.Core.Performance.HistoryPoint> capacityHistory = new();
    private Action<string>? navigate;
    private CommunityToolkit.Mvvm.Input.IAsyncRelayCommand? scan;
    public CommunityToolkit.Mvvm.Input.IAsyncRelayCommand StartScanCommand { get; }
    public CommunityToolkit.Mvvm.Input.IRelayCommand ManageProgramsCommand { get; }
    public CommunityToolkit.Mvvm.Input.IRelayCommand ManageStartupCommand { get; }
    public IReadOnlyList<SystemCore.Core.Performance.HistoryPoint> History => monitor.GetHistory();
    public IReadOnlyList<SystemCore.Core.Performance.HistoryPoint> CapacityHistory => capacityHistory.ToArray();
    public event EventHandler? MetricsChanged;
    public string HeroTitle => L["F1Welcome"];
    public string HeroDescription => L["F1Intro"];
    public string ScanLabel => L["F1Scan"];
    public string ScanDetail => L["F1ScanDetail"];
    public string CleanupTitle => L["F1CleanupTitle"];
    public string CleanupBody => L["F1CleanupBody"];
    public string ProgramsTitle => L["NavUninstaller"];
    public string ProgramsBody => L["F1UninstallBody"];
    public string StartupTitle => L["NavStartup"];
    public string StartupBody => L["F1StartupBody"];
    public string ManagePrograms => L["F1ManagePrograms"];
    public string OpenManager => L["F1OpenManager"];
    public string ReadyLabel => L["F1Ready"];
    public string LastScanLabel => L["F1LastScan"];
    public string LastScan => cleaner.Latest is { } scan ? Core.SystemInformation.InfoFormatting.Date(scan.Completed.ToLocalTime(), L.Culture) + " · " + Core.SystemInformation.InfoFormatting.Time(scan.Completed, L.Culture) : L["NotScanned"];
    public string Attribution => Core.ProductIdentity.Attribution;
    public string CpuMetadata => monitor.LogicalProcessors is { } count ? L.Format("F1Processors", count) : L["Unavailable"];
    public string MemoryMetadata => L["F1PhysicalMemory"];
    public string DiskMetadata => L["F1Capacity"];
    public string HistoryLabel => L["F1History"];
    public string GaugeText(int index) => Cards[index].ProgressVisibility == Visibility.Visible ? L.Format("Percent", Cards[index].Progress) : L["Unavailable"];
    public void ConfigureActions(Action<string> navigate, CommunityToolkit.Mvvm.Input.IAsyncRelayCommand scan)
    { this.navigate = navigate; this.scan = scan; }

    public ObservableCollection<MetricCardViewModel> Cards { get; } = new(
        new[] { "MetricCpu", "MetricMemory", "MetricDisk", "MetricFree", "MetricApps", "MetricStartup" }
        .Select(id => new MetricCardViewModel(id)));
    public string Title => L["NavDashboard"];
    public string Description => L["DashboardDescription"];
    public string SystemTitle => L["SystemTitle"];
    public string DeviceLabel => L["DeviceName"];
    public string UptimeLabel => L["Uptime"];
    public string CleanerTitle => L["JunkFiles"];
    public string CleanerValue => cleaner.Latest is { } scan ? scan.Complete ? SystemCore.Core.SystemInformation.InfoFormatting.Size((ulong)scan.Bytes, L.Culture) : L["CleanPartial"] : L["NotScanned"];
    public string CleanerDescription => cleaner.Latest is null ? L["CleanDashboardInitial"] : L[cleaner.Latest.Complete ? "CleanPotential" : "CleanPartialDescription"];
    public string Coverage => L["InventoryCoverage"];
    public string UpdatedText { get => updatedText; private set => SetProperty(ref updatedText, value); }
    public string SystemDescription { get => systemDescription; private set => SetProperty(ref systemDescription, value); }
    public string DeviceName { get => deviceName; private set => SetProperty(ref deviceName, value); }
    public string Uptime { get => uptime; private set => SetProperty(ref uptime, value); }
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public Visibility StatusVisibility { get => statusVisibility; private set => SetProperty(ref statusVisibility, value); }

    public DashboardViewModel(LocalizationService localizer, DashboardMonitor monitor,
        UiDispatcher dispatcher, IDashboardInventoryProvider inventory, SystemCore.Core.Cleaner.CleanerScanState cleaner,
        SystemCore.Core.Startup.IStartupInventoryService startup) : base(localizer)
    {
        this.monitor = monitor;
        this.dispatcher = dispatcher;
        this.inventory = inventory;
        this.cleaner = cleaner;
        this.startup = startup;
        StartScanCommand = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(async () =>
        {
            navigate?.Invoke("Cleaner");
            if (scan?.CanExecute(null) == true) await scan.ExecuteAsync(null);
        });
        ManageProgramsCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => navigate?.Invoke("Uninstaller"));
        ManageStartupCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => navigate?.Invoke("Startup"));
        startup.Changed += OnStartupChanged;
        cleaner.Changed += OnCleanerChanged;
        monitor.SampleAvailable += OnSample;
        monitor.SampleFailed += OnSampleFailed;
        Render();
    }

    private void OnCleanerChanged(object? sender, EventArgs e)
    {
        dispatcher.Post(() => { if (!disposed) { OnPropertyChanged(nameof(CleanerValue)); OnPropertyChanged(nameof(CleanerDescription)); OnPropertyChanged(nameof(LastScan)); } });
    }
    private void OnStartupChanged(object? sender, EventArgs e) => dispatcher.Post(() => { if (!disposed) Render(); });
    private async Task LoadStartupAsync()
    {
        try { await startup.RefreshAsync(lifetime.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            SystemCore.Core.Diagnostics.ReliabilityDiagnostics.Log.UnexpectedFailure((int)SystemCore.Core.Diagnostics.ReliabilityComponent.Dashboard, e.HResult);
            dispatcher.Post(() => { if (!disposed) { startupFailed = true; Render(); } });
        }
    }

    public void SetActive(bool enabled)
    {
        if (disposed) return;
        active = enabled;
        if (!enabled) capacityHistory.Clear();
        monitor.SetActive(this, enabled);
        if (enabled && !started)
        {
            started = true;
            inventoryLoad = LoadInventoryAsync();
            startupLoad = LoadStartupAsync(); // Owned, observed inside the operation; cancellation follows this view model.
        }
    }

    private void OnSample(object? sender, DashboardSample sample) => dispatcher.Post(() =>
    {
        if (disposed || !active) return;
        latest = sample;
        var volume = sample.SystemVolume.Value;
        capacityHistory.Enqueue(new(sample.Timestamp, volume is { TotalBytes: > 0 } v ? v.UsedBytes * 100.0 / v.TotalBytes : null, null, null, null));
        while (capacityHistory.Count > 60 || capacityHistory.TryPeek(out var first) && sample.Timestamp - first.Timestamp > TimeSpan.FromSeconds(60)) capacityHistory.Dequeue();
        Render();
    });

    private void OnSampleFailed(object? sender, EventArgs args) => dispatcher.Post(() =>
    {
        if (disposed) return;
        latest = null;
        Status = L["ReadError"];
        StatusVisibility = Visibility.Visible;
        RenderValues();
    });

    private async Task LoadInventoryAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            var overview = await Task.Run(inventory.ReadMachine, lifetime.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
            dispatcher.Post(() => { if (!disposed) { machine = overview; Render(); } });
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            dispatcher.Post(() => { if (!disposed) { machineFailed = true; Render(); } });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            SystemCore.Core.Diagnostics.ReliabilityDiagnostics.Log.UnexpectedFailure((int)SystemCore.Core.Diagnostics.ReliabilityComponent.Dashboard, ex.HResult);
            dispatcher.Post(() => { if (!disposed) { machineFailed = true; Render(); } });
        }
        try
        {
            // WaitAsync bounds the UI wait, not the duration of a native registry/package call.
            var result = await Task.Run(() => inventory.ReadCounts(timeout.Token), lifetime.Token)
                .WaitAsync(timeout.Token).ConfigureAwait(false);
            dispatcher.Post(() => { if (!disposed) { counts = result; inventoryFinished = true; Render(); } });
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            dispatcher.Post(() => { if (!disposed) { inventoryFinished = true; Render(); } });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            SystemCore.Core.Diagnostics.ReliabilityDiagnostics.Log.UnexpectedFailure((int)SystemCore.Core.Diagnostics.ReliabilityComponent.Dashboard, ex.HResult);
            dispatcher.Post(() => { if (!disposed) { inventoryFinished = true; Render(); } });
        }
    }

    protected override void RefreshLanguage()
    {
        base.RefreshLanguage();
        Render();
    }

    private void Render()
    {
        var keys = new[] { "Cpu", "Memory", "Disk", "FreeSpace", "InstalledApps", "StartupApps" };
        for (var i = 0; i < Cards.Count; i++)
        {
            Cards[i].Title = L[keys[i]];
            Cards[i].ValueAlignment = L.IsRightToLeft ? TextAlignment.Right : TextAlignment.Left;
        }
        SystemDescription = machine?.WindowsDescription ?? L[machineFailed ? "Unavailable" : "Loading"];
        DeviceName = machine?.DeviceName ?? L[machineFailed ? "Unavailable" : "Loading"];
        var partial = latest is { } data &&
            (data.CpuPercent.Status is ReadStatus.Unavailable or ReadStatus.Unsupported || data.Memory.Value is null || data.SystemVolume.Value is null);
        partial |= machineFailed || startupFailed || (inventoryFinished && counts is null) ||
            counts?.InstalledApps.Status == ReadStatus.Partial || startup.Summary?.Complete == false;
        Status = L["PartialData"];
        StatusVisibility = partial ? Visibility.Visible : Visibility.Collapsed;
        RenderValues();
    }

    private void RenderValues()
    {
        var cpu = latest?.CpuPercent;
        Cards[0].Value = cpu?.Value is { } percent ? L.Format("Percent", percent) :
            L[cpu?.Status == ReadStatus.Unsupported ? "Unsupported" : cpu?.Status == ReadStatus.Unavailable ? "Unavailable" : "Collecting"];
        Cards[0].Detail = L["CpuDetail"];
        SetProgress(Cards[0], cpu?.Value);
        var memory = latest?.Memory.Value;
        Cards[1].Value = memory is { } ram ? L.Format("UsedTotal", Bytes(ram.UsedBytes), Bytes(ram.TotalBytes)) : L["Unavailable"];
        Cards[1].Detail = L["MemoryDetail"];
        SetProgress(Cards[1], memory is { } m ? m.UsedBytes * 100.0 / m.TotalBytes : null);
        var volume = latest?.SystemVolume.Value;
        Cards[2].Value = volume is { } disk ? L.Format("UsedTotal", Bytes(disk.UsedBytes), Bytes(disk.TotalBytes)) : L["Unavailable"];
        Cards[2].Detail = volume is { } d ? L.Format("DiskDetail", d.Root) : L["DiskCapacity"];
        SetProgress(Cards[2], volume is { } v ? v.UsedBytes * 100.0 / v.TotalBytes : null);
        Cards[3].Value = volume is { } free ? Bytes(free.AvailableBytes) : L["Unavailable"];
        Cards[3].Detail = L["FreeSpaceDetail"];
        Cards[4].Value = CountText(counts?.InstalledApps);
        Cards[4].Detail = L["InstalledAppsDetail"];
        Cards[5].Value = startup.Summary is { } summary ? summary.Complete ? summary.Total.ToString("N0", L.Culture) : L.Format("AtLeast", summary.Total) : L[startupFailed ? "Unavailable" : "Collecting"];
        Cards[5].Detail = L["StartupAppsDetail"];
        Uptime = latest is { } sample ? Core.SystemInformation.InfoFormatting.Duration(sample.Uptime, L.Culture, L["UptimeValue"]) : L["Collecting"];
        UpdatedText = latest is { } current ? L.Format("Updated", Core.SystemInformation.InfoFormatting.Time(current.Timestamp, L.Culture)) : L["Collecting"];
        MetricsChanged?.Invoke(this, EventArgs.Empty);
    }

    private string CountText(Reading<int>? count) => count?.Value is { } number
        ? count.Status == ReadStatus.Partial ? L.Format("AtLeast", number) : number.ToString("N0", L.Culture)
        : L[inventoryFinished ? "Unavailable" : "Loading"];
    private string Bytes(ulong bytes) => SystemCore.Core.SystemInformation.InfoFormatting.Size(bytes, L.Culture);
    private static void SetProgress(MetricCardViewModel card, double? value)
    {
        card.ProgressVisibility = value.HasValue ? Visibility.Visible : Visibility.Collapsed;
        if (value.HasValue) card.Progress = value.Value;
    }

    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        cleaner.Changed -= OnCleanerChanged;
        startup.Changed -= OnStartupChanged;
        lifetime.Cancel();
        monitor.SetActive(this, false);
        monitor.SampleAvailable -= OnSample;
        monitor.SampleFailed -= OnSampleFailed;
        base.Dispose();
        // Source calls observe cancellation between entries; no UI waits or process termination.
    }
    public async ValueTask DisposeAsync()
    {
        Dispose();
        await Task.WhenAll(inventoryLoad ?? Task.CompletedTask, startupLoad ?? Task.CompletedTask);
        lifetime.Dispose();
    }
}
