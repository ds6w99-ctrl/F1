using Microsoft.UI.Xaml;
using Microsoft.Extensions.DependencyInjection;
using SystemCore.App.Services;
using SystemCore.App.ViewModels;
using SystemCore.Core.Dashboard;
using SystemCore.Infrastructure.Dashboard;
using SystemCore.Core.Settings;
using SystemCore.Infrastructure.Settings;
using SystemCore.Core.SystemInformation;
using SystemCore.Infrastructure.SystemInformation;
using SystemCore.App.SystemInformation;
using SystemCore.Core.Performance;
using SystemCore.Infrastructure.Performance;
using SystemCore.App.Performance;
using SystemCore.Core.Cleaner;
using SystemCore.Infrastructure.Cleaner;
using SystemCore.App.Cleaner;

namespace SystemCore.App;

public partial class App : Application
{
    private Window? window;
    private ServiceProvider? services;
    private SystemCore.Core.Diagnostics.IAppDiagnostics? diagnostics;
    private Services.ProductDiagnosticListener? diagnosticListener;
    private bool closing, allowClose;

    public App()
    {
#if RELIABILITY_AUDIT
        Services.ReliabilityAudit.Start(this);
#endif
        UnhandledException += (_, args) =>
        {
            // Do not output messages/stacks: native/provider errors may contain
            // private paths or command arguments. Never mark a fatal UI error handled.
            Console.Error.WriteLine($"SystemCore fatal UI failure: 0x{args.Exception.HResult:X8}");
            SystemCore.Core.Diagnostics.ReliabilityDiagnostics.Log.UnexpectedFailure((int)SystemCore.Core.Diagnostics.ReliabilityComponent.Ui, args.Exception.HResult);
            diagnostics?.Record(SystemCore.Core.Diagnostics.DiagnosticFeature.App, SystemCore.Core.Diagnostics.DiagnosticEvent.UnhandledFailure, args.Exception.HResult);
            var arabic = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";
            FatalMessage(IntPtr.Zero, arabic ? $"واجه {Core.ProductIdentity.Name} خطأً غير متوقع ويجب إغلاقه. أعد تشغيل التطبيق وراجع السجلات المحلية." : $"{Core.ProductIdentity.Name} encountered an unexpected error and must close. Restart the app and review its local logs.", Core.ProductIdentity.Name, 0x10);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var code = (args.ExceptionObject as Exception)?.HResult ?? 0;
            Console.Error.WriteLine($"SystemCore fatal runtime failure: 0x{code:X8}");
            SystemCore.Core.Diagnostics.ReliabilityDiagnostics.Log.UnexpectedFailure((int)SystemCore.Core.Diagnostics.ReliabilityComponent.Runtime, code);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            SystemCore.Core.Diagnostics.ReliabilityDiagnostics.Log.UnexpectedFailure((int)SystemCore.Core.Diagnostics.ReliabilityComponent.UnobservedTask, args.Exception.HResult);
            diagnostics?.Record(SystemCore.Core.Diagnostics.DiagnosticFeature.App, SystemCore.Core.Diagnostics.DiagnosticEvent.UnhandledFailure, args.Exception.HResult);
            // This is an observed diagnostic event, not recovery; do not SetObserved.
        };
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var registrations = new ServiceCollection();
        registrations.AddSingleton<SystemCore.Infrastructure.Tools.IWindowsToolPlatform, SystemCore.Infrastructure.Tools.WindowsToolPlatform>();
        registrations.AddSingleton<SystemCore.Core.Tools.IWindowsToolsService, SystemCore.Infrastructure.Tools.WindowsToolsService>();
        registrations.AddSingleton<SystemCore.Core.Diagnostics.IAppDiagnostics>(s => new SystemCore.Infrastructure.Diagnostics.LocalAppDiagnostics(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SystemCore"), ProductSupportViewModel.BuildVersion, s.GetRequiredService<SystemCore.Core.Tools.IWindowsToolsService>()));
        registrations.AddSingleton<ProductSupportViewModel>();
        registrations.AddSingleton<Tools.ToolsViewModel>();
        registrations.AddSingleton<IUserSettingsService>(_ => new LocalUserSettingsService(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SystemCore", "preferences.json"),
            System.Globalization.CultureInfo.CurrentUICulture));
        registrations.AddSingleton<LocalizationService>();
        registrations.AddSingleton(new UiDispatcher(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()));
        registrations.AddSingleton<IPerformanceSampler, WindowsPerformanceSampler>();
        registrations.AddSingleton<IDashboardInventoryProvider, WindowsDashboardInventory>();
        registrations.AddSingleton<DashboardMonitor>();
        registrations.AddSingleton<IPerformanceSnapshotSource, WindowsPerformanceSource>();
        registrations.AddSingleton<IPerformanceMonitorService, PerformanceMonitorService>();
        registrations.AddSingleton<SystemCore.Core.Orchestration.IApplicationDataOrchestrator>(s => new SystemCore.Core.Orchestration.ApplicationDataOrchestrator(
            s.GetRequiredService<IPerformanceMonitorService>(), s.GetRequiredService<SystemCore.Core.Diagnostics.IAppDiagnostics>(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SystemCore", "cache")));
        registrations.AddSingleton<PerformanceViewModel>();
        registrations.AddSingleton<ICleanerTargetProvider, WindowsCleanerTargets>();
        registrations.AddSingleton<ICleanerScanner, WindowsCleanerScanner>();
        registrations.AddSingleton<ICleanerService, WindowsCleanerService>();
        registrations.AddSingleton<IRecycleBinShell, WindowsRecycleBinShell>();
        registrations.AddSingleton<IRecycleBinService, WindowsRecycleBinService>();
        registrations.AddSingleton<IElevatedCleanerClient, WindowsElevatedCleanerClient>();
#if CLEANER_VALIDATION
        registrations.AddSingleton<ICleanerService, Cleaner.ValidationCleanerService>();
#endif
        registrations.AddSingleton<CleanerScanState>();
        registrations.AddSingleton<CleanerViewModel>();
        registrations.AddSingleton<SystemCore.Core.Uninstaller.IApplicationIdentityResolver, SystemCore.Infrastructure.Uninstaller.ApplicationIdentityResolver>();
        registrations.AddSingleton<SystemCore.Core.Uninstaller.IWin32ApplicationProvider, SystemCore.Infrastructure.Uninstaller.RegistryApplicationProvider>();
        registrations.AddSingleton<SystemCore.Core.Uninstaller.IMsiApplicationProvider, SystemCore.Infrastructure.Uninstaller.MsiApplicationProvider>();
        registrations.AddSingleton<SystemCore.Core.Uninstaller.IMsixApplicationProvider, SystemCore.Infrastructure.Uninstaller.MsixApplicationProvider>();
        registrations.AddSingleton<SystemCore.Infrastructure.Uninstaller.IGameLibraryEnvironment, SystemCore.Infrastructure.Uninstaller.WindowsGameLibraryEnvironment>();
        registrations.AddSingleton<SystemCore.Core.Uninstaller.IGameLibraryProvider, SystemCore.Infrastructure.Uninstaller.SteamGameLibraryProvider>();
        registrations.AddSingleton<SystemCore.Core.Uninstaller.IGameLibraryProvider, SystemCore.Infrastructure.Uninstaller.EpicGameLibraryProvider>();
        registrations.AddSingleton<SystemCore.Core.Uninstaller.IGameLibraryProvider, SystemCore.Infrastructure.Uninstaller.XboxGameLibraryProvider>();
        registrations.AddSingleton<SystemCore.Core.Uninstaller.IGameLibraryProvider, SystemCore.Infrastructure.Uninstaller.EaGameLibraryProvider>();
        registrations.AddSingleton<SystemCore.Core.Uninstaller.IGameLibraryProvider, SystemCore.Infrastructure.Uninstaller.UbisoftGameLibraryProvider>();
        registrations.AddSingleton<SystemCore.Core.Uninstaller.IGameLibraryProvider, SystemCore.Infrastructure.Uninstaller.BattleNetGameLibraryProvider>();
        registrations.AddSingleton<SystemCore.Core.Uninstaller.IGameLauncher, SystemCore.Infrastructure.Uninstaller.GameLauncher>();
        registrations.AddSingleton<SystemCore.Core.Uninstaller.IInstalledApplicationInventory, SystemCore.Infrastructure.Uninstaller.InstalledApplicationInventory>();
        registrations.AddSingleton<SystemCore.Infrastructure.Uninstaller.INativeUninstallRunner, SystemCore.Infrastructure.Uninstaller.NativeUninstallRunner>();
        registrations.AddSingleton<SystemCore.Core.Uninstaller.IUninstallCompletionDetector, SystemCore.Infrastructure.Uninstaller.UninstallCompletionDetector>();
        registrations.AddSingleton<SystemCore.Core.Uninstaller.IUninstallExecutor, SystemCore.Infrastructure.Uninstaller.UninstallExecutor>();
        registrations.AddSingleton<SystemCore.Core.Uninstaller.IBulkUninstallExecutor>(s => new SystemCore.Infrastructure.Uninstaller.BulkUninstallExecutor(
            (SystemCore.Infrastructure.Uninstaller.UninstallExecutor)s.GetRequiredService<SystemCore.Core.Uninstaller.IUninstallExecutor>()));
        registrations.AddSingleton<SystemCore.Infrastructure.Uninstaller.ApplicationRemovalEvidence>();
        registrations.AddSingleton<SystemCore.Infrastructure.Uninstaller.LeftoverScanner>();
        registrations.AddSingleton<SystemCore.Core.Uninstaller.ILeftoverScanner>(s => s.GetRequiredService<SystemCore.Infrastructure.Uninstaller.LeftoverScanner>());
        registrations.AddSingleton<SystemCore.Infrastructure.Uninstaller.IRegistryBackupWriter, SystemCore.Infrastructure.Uninstaller.RegistryBackupWriter>();
        registrations.AddSingleton<SystemCore.Core.Uninstaller.ILeftoverDeletionService>(s => SystemCore.Infrastructure.Uninstaller.LeftoverDeletionFactory.Create(
            s.GetRequiredService<SystemCore.Infrastructure.Uninstaller.LeftoverScanner>(),
            s.GetRequiredService<SystemCore.Infrastructure.Uninstaller.ApplicationRemovalEvidence>(),
            s.GetRequiredService<SystemCore.Core.Uninstaller.IInstalledApplicationInventory>(),
            s.GetRequiredService<SystemCore.Infrastructure.Uninstaller.IRegistryBackupWriter>()));
        registrations.AddSingleton<Uninstaller.UninstallerViewModel>();
#if STARTUP_VALIDATION
        registrations.AddSingleton<SystemCore.Infrastructure.Startup.StartupValidationEnvironment>();
        registrations.AddSingleton<SystemCore.Core.Startup.IStartupInventoryService>(s => s.GetRequiredService<SystemCore.Infrastructure.Startup.StartupValidationEnvironment>().Inventory);
        registrations.AddSingleton<SystemCore.Core.Startup.IStartupMutationService>(s => s.GetRequiredService<SystemCore.Infrastructure.Startup.StartupValidationEnvironment>().Mutation);
#else
        registrations.AddSingleton<SystemCore.Core.Startup.IRegistryStartupProvider, SystemCore.Infrastructure.Startup.RegistryStartupProvider>();
        registrations.AddSingleton<SystemCore.Core.Startup.IStartupFolderProvider, SystemCore.Infrastructure.Startup.StartupFolderProvider>();
        registrations.AddSingleton<SystemCore.Core.Startup.ITaskSchedulerStartupProvider, SystemCore.Infrastructure.Startup.TaskSchedulerStartupProvider>();
        registrations.AddSingleton<SystemCore.Infrastructure.Startup.StartupStateService>();
        registrations.AddSingleton<SystemCore.Core.Startup.IStartupStateService>(s => s.GetRequiredService<SystemCore.Infrastructure.Startup.StartupStateService>());
        registrations.AddSingleton<SystemCore.Infrastructure.Startup.StartupInventoryService>();
        registrations.AddSingleton<SystemCore.Core.Startup.IStartupInventoryService>(s => s.GetRequiredService<SystemCore.Infrastructure.Startup.StartupInventoryService>());
        registrations.AddSingleton<SystemCore.Core.Startup.IElevatedStartupClient, SystemCore.Infrastructure.Startup.WindowsElevatedStartupClient>();
        registrations.AddSingleton<SystemCore.Core.Startup.IStartupMutationService, SystemCore.Infrastructure.Startup.StartupMutationService>();
#endif
        registrations.AddSingleton<Startup.StartupViewModel>();
        registrations.AddSingleton<SystemCore.Core.MemoryTools.IMemoryPressureEvaluator, SystemCore.Core.MemoryTools.MemoryPressureEvaluator>();
        registrations.AddSingleton<SystemCore.Core.MemoryTools.IMemoryAnalysisService, SystemCore.Core.MemoryTools.MemoryAnalysisService>();
        registrations.AddSingleton<SystemCore.Core.MemoryTools.IProcessResourceAnalyzer, SystemCore.Infrastructure.MemoryTools.WindowsProcessResourceAnalyzer>();
        registrations.AddSingleton<MemoryTools.MemoryToolsViewModel>();
        registrations.AddSingleton<SystemCore.Core.MemoryTools.IMemoryOptimizationService, SystemCore.Core.MemoryTools.MemoryOptimizationService>();
#if MEMORY_VALIDATION
        registrations.AddSingleton<MemoryTools.MemoryValidationEnvironment>();
        registrations.AddSingleton<SystemCore.Core.MemoryTools.IMemoryAnalysisService>(s => s.GetRequiredService<MemoryTools.MemoryValidationEnvironment>());
        registrations.AddSingleton<SystemCore.Core.MemoryTools.ISystemCoreMemoryCacheOwner>(s => s.GetRequiredService<MemoryTools.MemoryValidationEnvironment>());
#endif
        registrations.AddSingleton<IOperatingSystemInfoProvider, WindowsOperatingSystemInfoProvider>();
        registrations.AddSingleton<ICpuInfoProvider, WindowsCpuInfoProvider>();
        registrations.AddSingleton<IMemoryInfoProvider, WindowsMemoryInfoProvider>();
        registrations.AddSingleton<IGpuInfoProvider, WindowsGpuInfoProvider>();
        registrations.AddSingleton<IMotherboardInfoProvider, WindowsMotherboardInfoProvider>();
        registrations.AddSingleton<IStorageInfoProvider, WindowsStorageInfoProvider>();
        registrations.AddSingleton<INetworkInfoProvider, WindowsNetworkInfoProvider>();
        registrations.AddSingleton<ISystemInformationService, SystemInformationService>();
        registrations.AddSingleton<SystemInformationViewModel>();
        registrations.AddSingleton<DashboardViewModel>();
        registrations.AddSingleton<SettingsViewModel>();
        registrations.AddSingleton<ShellViewModel>();
        registrations.AddSingleton<MainWindow>();
        services = registrations.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        diagnostics = services.GetRequiredService<SystemCore.Core.Diagnostics.IAppDiagnostics>();
        diagnosticListener = new(diagnostics);
        diagnostics.Record(SystemCore.Core.Diagnostics.DiagnosticFeature.App, SystemCore.Core.Diagnostics.DiagnosticEvent.Started);
        window = services.GetRequiredService<MainWindow>();
        window.AppWindow.Closing += OnClosing;
        window.Closed += (_, _) => { window = null; };
        window.Activate();
        _ = services.GetRequiredService<SystemCore.Core.Orchestration.IApplicationDataOrchestrator>().StartAsync();
    }

    private async void OnClosing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (allowClose) return;
        args.Cancel = true;
        if (closing) return;
        closing = true;
        (window as MainWindow)?.BeginShutdown();
        var current = services; services = null;
        try
        {
            diagnostics?.Record(SystemCore.Core.Diagnostics.DiagnosticFeature.App, SystemCore.Core.Diagnostics.DiagnosticEvent.Closed);
            // Keep the dispatcher/window alive until owned asynchronous disposal
            // finishes. An async Closed callback can be abandoned by native exit.
            if (current is not null) await current.DisposeAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"SystemCore shutdown failure: 0x{ex.HResult:X8}");
            SystemCore.Core.Diagnostics.ReliabilityDiagnostics.Log.UnexpectedFailure((int)SystemCore.Core.Diagnostics.ReliabilityComponent.Shutdown, ex.HResult);
        }
        finally
        {
            diagnosticListener?.Dispose(); diagnosticListener = null; diagnostics = null;
            allowClose = true;
            window?.Close();
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int FatalMessage(IntPtr owner, string text, string caption, uint type);
}
