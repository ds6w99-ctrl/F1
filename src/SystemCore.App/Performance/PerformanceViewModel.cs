using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SystemCore.App.Services;
using SystemCore.App.ViewModels;
using SystemCore.Core.Performance;
using SystemCore.Core.SystemInformation;

namespace SystemCore.App.Performance;

public sealed class ProcessRowViewModel(int pid, long? start, string initialName) : ObservableObject
{
    public int Pid { get; } = pid;
    public long? Start { get; } = start;
    private string name = initialName;
    public string Name { get => name; set => SetProperty(ref name, value); }
    private string cpu = "", memory = "";
    public string Cpu { get => cpu; set => SetProperty(ref cpu, value); }
    public string Memory { get => memory; set => SetProperty(ref memory, value); }
}

public sealed class PerformanceViewModel : LocalizedViewModel
{
    private readonly IPerformanceMonitorService monitor;
    private readonly UiDispatcher dispatcher;
    private bool active, disposed;
    private int queued;
    private PerformanceSnapshot? pending, latest;
    private DateTimeOffset? lastProcessTimestamp;
    private IReadOnlyList<HistoryPoint> history = [];
    private string search = "";
    private int mode, sort;
    public ObservableCollection<ProcessRowViewModel> Processes { get; } = [];
    public IReadOnlyList<HistoryPoint> History { get => history; private set => SetProperty(ref history, value); }
    public string Title => L["NavPerformance"];
    public string Description => L["PerfDescription"];
    public string CpuTitle => L["Cpu"];
    public string MemoryTitle => L["Memory"];
    public string DiskTitle => L["PerfDisk"];
    public string NetworkTitle => L["PerfNetwork"];
    public string CpuValue => Percent(latest?.Basic.CpuPercent.Value);
    public string MemoryValue => latest?.Basic.Memory.Value is { } m ? L.Format("UsedTotal", Size(m.UsedBytes), Size(m.TotalBytes)) : Missing;
    public string MemoryDetail => latest?.Basic.Memory.Value is { } m ? L.Format("PerfAvailable", Size(m.TotalBytes - m.UsedBytes), Percent(InfoFormatting.Percentage(m.UsedBytes, m.TotalBytes))) : Missing;
    public string CpuDetail => latest is { } s ? L.Format("PerfCpuDetail", s.LogicalProcessors) : Missing;
    public string DiskValue => Rate(latest?.Disk.TotalBytesPerSecond);
    public string DiskDetail => L.Format("PerfReadWrite", Rate(latest?.Disk.ReadBytesPerSecond), Rate(latest?.Disk.WriteBytesPerSecond));
    public string DiskScope => L["PerfDiskScope"];
    public string NetworkValue => latest?.Network is { Available: true } n ? L.Format("PerfDownUp", Rate(n.Download), Rate(n.Upload)) : Missing;
    public string NetworkDetail => latest?.Network is { Available: true } n ? n.Adapters.Count == 0 ? L["PerfNoNetwork"] :
        string.Join(" · ", n.Adapters.Select(a => a.Name + (a.LinkBitsPerSecond is { } speed ? " (" + L.Format("InfoMbps", speed / 1000000.0) + ")" : ""))) : Missing;
    public string NetworkScope => L["PerfNetworkScope"];
    public string ProcessTitle => L["PerfProcesses"];
    public string ProcessNotice => L["PerfProcessNotice"];
    public string ProcessState => latest?.ProcessesAvailable == true ? L.Format("PerfProcessCount", Processes.Count) : Missing;
    public string TopCpu => L["PerfTopCpu"];
    public string TopMemory => L["PerfTopMemory"];
    public string AllProcesses => L["PerfAll"];
    public string SearchLabel => L["PerfSearch"];
    public string SortLabel => L["PerfSort"];
    public string ModeLabel => L["PerfMode"];
    public string NameLabel => L["InfoName"];
    public string PidLabel => L["PerfPid"];
    public string WorkingSetLabel => L["PerfWorkingSet"];
    public string ChartLabel => L["PerfChart"];
    public string HistoryStatus => L.Format("PerfHistoryCount", History.Count);
    private string Missing => L["InfoUnavailable"];
    private string Percent(double? value) => value.HasValue ? L.Format("Percent", value.Value) : Missing;
    private string Rate(double? value) => value.HasValue ? PerformanceMath.RateText(value.Value, L.Culture) : Missing;
    private string Size(ulong value) => InfoFormatting.Size(value, L.Culture);
    public string Search { get => search; set { if (SetProperty(ref search, value)) RefreshProcesses(); } }
    public int Mode { get => mode; set { if (value >= 0 && SetProperty(ref mode, Math.Clamp(value, 0, 2))) { RefreshProcesses(); OnPropertyChanged(nameof(SortEnabled)); } } }
    public int Sort { get => sort; set { if (value >= 0 && SetProperty(ref sort, Math.Clamp(value, 0, 1))) RefreshProcesses(); } }
    // Sorting only applies to the full list; the Top CPU / Top Memory views are ranked by their metric.
    public bool SortEnabled => mode == 2;
    public PerformanceViewModel(LocalizationService localizer, IPerformanceMonitorService monitor, UiDispatcher dispatcher) : base(localizer)
    {
        this.monitor = monitor; this.dispatcher = dispatcher;
        monitor.Updated += OnUpdated;
    }
    public void SetActive(bool value)
    {
        if (disposed) return;
        active = value;
        monitor.SetActive(this, value, detailed: true);
    }
    private void OnUpdated(object? sender, PerformanceSnapshot snapshot)
    {
        Volatile.Write(ref pending, snapshot);
        if (Interlocked.Exchange(ref queued, 1) != 0) return;
        if (!dispatcher.Post(() =>
        {
            Interlocked.Exchange(ref queued, 0);
            if (!active || disposed) return;
            latest = Volatile.Read(ref pending);
            History = monitor.GetHistory();
            if (latest?.ProcessTimestamp != lastProcessTimestamp)
            {
                lastProcessTimestamp = latest?.ProcessTimestamp;
                RefreshProcesses();
            }
            OnPropertyChanged(string.Empty);
        })) Interlocked.Exchange(ref queued, 0);
    }
    private void RefreshProcesses()
    {
        var selected = PerformanceMath.Filter(latest?.Processes ?? [], Search, Mode == 1 || Mode == 2 && Sort == 1);
        if (Mode != 2) selected = selected.Take(10);
        var values = selected.ToArray();
        var keys = values.Select(p => (p.Pid, p.StartTicks)).ToHashSet();
        for (var i = Processes.Count - 1; i >= 0; i--)
            if (!keys.Contains((Processes[i].Pid, Processes[i].Start))) Processes.RemoveAt(i);
        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i];
            var row = Processes.FirstOrDefault(r => r.Pid == value.Pid && r.Start == value.StartTicks);
            if (row is null) { row = new(value.Pid, value.StartTicks, value.Name); Processes.Insert(i, row); }
            else { var index = Processes.IndexOf(row); if (index != i) Processes.Move(index, i); }
            // A protected process may have no creation time; never retain a stale name if its PID is reused.
            row.Name = value.Name;
            row.Cpu = Percent(value.CpuPercent);
            row.Memory = value.WorkingSet is { } bytes ? Size(bytes) : Missing;
        }
        OnPropertyChanged(nameof(ProcessState));
    }
    protected override void RefreshLanguage() { RefreshProcesses(); base.RefreshLanguage(); }
    public override void Dispose()
    {
        disposed = true;
        monitor.SetActive(this, false);
        monitor.Updated -= OnUpdated;
        base.Dispose();
    }
}
