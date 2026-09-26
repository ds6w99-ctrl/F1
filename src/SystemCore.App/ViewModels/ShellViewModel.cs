using SystemCore.App.Services;
using SystemCore.Core;
using SystemCore.Core.Dashboard;

namespace SystemCore.App.ViewModels;

public sealed class ShellViewModel : LocalizedViewModel
{
    private string selectedPage = "Dashboard";
    private bool windowVisible, minimized;
    public DashboardViewModel Dashboard { get; }
    public SettingsViewModel Settings { get; }
    public SystemInformation.SystemInformationViewModel SystemInformation { get; }
    public Performance.PerformanceViewModel Performance { get; }
    public Cleaner.CleanerViewModel Cleaner { get; }
    public Uninstaller.UninstallerViewModel Uninstaller { get; }
    public Startup.StartupViewModel Startup { get; }
    public MemoryTools.MemoryToolsViewModel MemoryTools { get; }
    public Tools.ToolsViewModel Tools { get; }
    public ProductSupportViewModel Support { get; }
    private readonly SystemCore.Core.Diagnostics.IAppDiagnostics diagnostics;
    public string MemoryToolsLabel => L["NavMemoryTools"];
    public string ProductName => ProductIdentity.Name;
    public string Attribution => ProductIdentity.Attribution;
    public string Tagline => ProductIdentity.Tagline;
    public string SearchLabel => L["F1Search"];
    public string SearchPlaceholder => L["F1SearchPlaceholder"];
    public string PremiumTitle => L["F1Premium"];
    public string PremiumFeatures => L["F1PremiumFeatures"];
    public string AboutAction => L["F1About"];
    public string DashboardLabel => L["NavDashboard"];
    public string CleanerLabel => L["NavCleaner"];
    public string UninstallerLabel => L["NavUninstaller"];
    public string PerformanceLabel => L["NavPerformance"];
    public string SystemInfoLabel => L["NavSystemInfo"];
    public string StartupLabel => L["NavStartup"];
    public string ToolsLabel => L["NavTools"];
    public string SettingsLabel => L["NavSettings"];
    public string AboutLabel => L["NavAbout"];
    public string NavigationLabel => L["Navigation"];
    public string OfflineLabel => L["Offline"];
    public string CurrentPageTitle => L[$"Nav{SelectedPage}"];
    public string PlaceholderTitle => L["LaterPhase"];
    public string PlaceholderDescription => L["PlaceholderDescription"];
    public string AboutDescription => L["AboutDescription"];
    public string AboutVersion => Support.Version;

    public string SelectedPage
    {
        get => selectedPage;
        private set
        {
            if (SetProperty(ref selectedPage, value))
            {
                OnPropertyChanged(nameof(CurrentPageTitle));
                if (Enum.TryParse<SystemCore.Core.Diagnostics.DiagnosticFeature>(value, out var feature)) diagnostics.Record(feature, SystemCore.Core.Diagnostics.DiagnosticEvent.Navigation);
                UpdateMonitoring();
            }
        }
    }

    public ShellViewModel(LocalizationService localizer, DashboardViewModel dashboard, SettingsViewModel settings,
        SystemInformation.SystemInformationViewModel systemInformation, Performance.PerformanceViewModel performance, Cleaner.CleanerViewModel cleaner,
        Uninstaller.UninstallerViewModel uninstaller, Startup.StartupViewModel startup, MemoryTools.MemoryToolsViewModel memoryTools,
        Tools.ToolsViewModel tools, ProductSupportViewModel support, SystemCore.Core.Diagnostics.IAppDiagnostics diagnostics) : base(localizer)
    {
        Dashboard = dashboard;
        Settings = settings;
        SystemInformation = systemInformation;
        Performance = performance;
        Cleaner = cleaner;
        Uninstaller = uninstaller;
        Startup = startup;
        MemoryTools = memoryTools;
        Tools = tools; Support = support; this.diagnostics = diagnostics;
        Dashboard.ConfigureActions(Navigate, Cleaner.ScanCommand);
        Settings.ConfigureNavigation(Navigate);
    }

    public void Navigate(string page)
    {
        if (page is "Dashboard" or "Cleaner" or "Uninstaller" or "Performance" or "SystemInfo" or
            "Startup" or "MemoryTools" or "Tools" or "Settings" or "About") SelectedPage = page;
    }

    public void SetWindowState(bool visible, bool isMinimized)
    {
        windowVisible = visible;
        minimized = isMinimized;
        UpdateMonitoring();
    }

    private void UpdateMonitoring()
    {
        Dashboard.SetActive(DashboardMath.ShouldMonitor(SelectedPage == "Dashboard", windowVisible, minimized));
        SystemInformation.SetActive(DashboardMath.ShouldMonitor(SelectedPage == "SystemInfo", windowVisible, minimized));
        Performance.SetActive(DashboardMath.ShouldMonitor(SelectedPage == "Performance", windowVisible, minimized));
        MemoryTools.SetActive(DashboardMath.ShouldMonitor(SelectedPage == "MemoryTools", windowVisible, minimized));
    }
}
