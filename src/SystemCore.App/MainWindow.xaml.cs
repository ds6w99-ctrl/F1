using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SystemCore.App.Services;
using SystemCore.App.Startup;
using SystemCore.App.SystemInformation;
using SystemCore.App.Tools;
using SystemCore.App.Uninstaller;
using SystemCore.App.ViewModels;
using SystemCore.App.Views;
using SystemCore.Core.Orchestration;
using Windows.Graphics;

namespace SystemCore.App;

public sealed partial class MainWindow : Window
{
    public ShellViewModel ViewModel { get; }
    private readonly DashboardView dashboard;
    private readonly SettingsView settings;
    private readonly PlaceholderView placeholder;
    private readonly AboutView about;
    private readonly SystemInformation.SystemInformationView systemInformation;
    private readonly Performance.PerformanceView performance;
    private readonly Cleaner.CleanerView cleaner;
    private readonly Uninstaller.UninstallerView uninstaller;
    private readonly Startup.StartupView startup;
    private readonly MemoryTools.MemoryToolsView memoryTools;
    private readonly Tools.ToolsView tools;
    private readonly WindowAppearanceController appearance;
    private readonly IApplicationDataOrchestrator orchestrator;
    private readonly CancellationTokenSource activationLifetime = new();
    private readonly DatasetRefreshRequest[] activationRefreshes;
    private bool shuttingDown;
    private bool ready;
    private readonly LocalizationService localizer;
    // App-layer timings: the Core orchestrator deliberately does not own UI milestones.
    public static class StartupTimeline
    {
        public static DateTimeOffset? WindowVisibleAt { get; private set; }
        public static DateTimeOffset? NavigationUsableAt { get; private set; }
        internal static void WindowVisible() => WindowVisibleAt ??= DateTimeOffset.UtcNow;
        internal static void NavigationUsable() => NavigationUsableAt ??= DateTimeOffset.UtcNow;
    }

    public MainWindow(ShellViewModel viewModel, LocalizationService localizer, CompatibilityService compatibility, IApplicationDataOrchestrator orchestrator)
    {
        ViewModel = viewModel;
        this.localizer = localizer;
        this.orchestrator = orchestrator;
        InitializeComponent();
        Title = ViewModel.ProductName;
        AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Brand", "F1.ico"));
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleDragRegion);
        dashboard = new(ViewModel.Dashboard);
        settings = new(ViewModel.Settings);
        placeholder = new(ViewModel);
        about = new(ViewModel);
        systemInformation = new(ViewModel.SystemInformation);
        performance = new(ViewModel.Performance);
        cleaner = new(ViewModel.Cleaner);
        uninstaller = new(ViewModel.Uninstaller);
        startup = new(ViewModel.Startup);
        memoryTools = new(ViewModel.MemoryTools);
        tools = new(ViewModel.Tools);
        appearance = new(this, Root, SolidBackground, ViewModel.Settings, localizer, compatibility, Scenery, ContentHost);
        // Activation only decides what is stale. The orchestrator admits one refresh per
        // dataset and owns the lifetime token, so a burst of restore events cannot fan out into
        // duplicate provider work. It then runs each admitted action on a thread-pool thread,
        // and a page view model raises PropertyChanged into live XAML, which rejects a
        // cross-thread write. So each action is entered on the UI thread by OnUiAsync: the
        // staleness test and the load it starts both belong to the thread that owns the view.
        activationRefreshes =
        [
            new(DataDataset.InstalledApplications, UninstallerViewModel.ApplicationsFreshness, _ => OnUiAsync(ViewModel.Uninstaller.RefreshIfStaleAsync)),
            new(DataDataset.Startup, StartupViewModel.StartupFreshness, _ => OnUiAsync(ViewModel.Startup.RefreshIfStaleAsync)),
            new(DataDataset.SystemInformation, SystemInformationViewModel.SystemInformationFreshness, _ => OnUiAsync(ViewModel.SystemInformation.RefreshIfStaleAsync)),
            new(DataDataset.Tools, ToolsViewModel.ToolsFreshness, _ => OnUiAsync(ViewModel.Tools.RefreshIfStaleAsync))
        ];
        ready = true;
        Navigation.SelectedItem = DashboardItem;
        ContentHost.Content = dashboard;
        Root.Loaded += OnLoaded;
        Activated += OnActivated;
        AppWindow.Changed += OnWindowChanged;
        Closed += OnClosed;
        ViewModel.PropertyChanged += OnShellChanged;
        Root.SizeChanged += OnSizeChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        var scale = Root.XamlRoot.RasterizationScale;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        { presenter.PreferredMinimumWidth = Math.Min((int)(900 * scale), area.Width); presenter.PreferredMinimumHeight = Math.Min((int)(600 * scale), area.Height); }
        AppWindow.Resize(new SizeInt32(Math.Min((int)(1280 * scale), area.Width), Math.Min((int)(900 * scale), area.Height)));
        Root.Loaded -= OnLoaded;
        UpdateVisibility();
        // Establish a XAML input focus after the initial resize/layout. UI Automation
        // can otherwise reach controls before this desktop window presents its content.
        DispatcherQueue.TryEnqueue(() => { DashboardItem.Focus(FocusState.Programmatic); StartupTimeline.NavigationUsable(); });
    }

    private void OnNavigationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (!ready || args.SelectedItem is not NavigationViewItem { Tag: string page }) return;
        ViewModel.Navigate(page);
        if (page == "Performance") DispatcherQueue.TryEnqueue(() => PerformanceItem.IsExpanded = true);
        ContentHost.Content = page switch
        {
            "Dashboard" => dashboard,
            "Settings" => settings,
            "About" => about,
            "SystemInfo" => systemInformation,
            "Performance" => performance,
            "Cleaner" => cleaner,
            "Uninstaller" => uninstaller,
            "Startup" => startup,
            "MemoryTools" => memoryTools,
            "Tools" => tools,
            _ => placeholder
        };
    }

    private IEnumerable<NavigationViewItem> Routes() => Navigation.MenuItems.OfType<NavigationViewItem>()
        .SelectMany(item => new[] { item }.Concat(item.MenuItems.OfType<NavigationViewItem>()))
        .Concat(Navigation.FooterMenuItems.OfType<NavigationViewItem>());
    private void OnShellChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ShellViewModel.SelectedPage))
            Navigation.SelectedItem = Routes().First(item => Equals(item.Tag, ViewModel.SelectedPage));
    }
    private void OpenSettings(object sender, RoutedEventArgs args) => ViewModel.Navigate("Settings");
    private void OpenAbout(object sender, RoutedEventArgs args) => ViewModel.Navigate("About");
    private void OnSearchChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        sender.ItemsSource = Routes().Where(item => localizer["Nav" + item.Tag].Contains(sender.Text, StringComparison.CurrentCultureIgnoreCase))
            .Select(item => localizer["Nav" + item.Tag]).ToArray();
    }
    private void SearchRoute(string? title)
    {
        var item = Routes().FirstOrDefault(item => localizer["Nav" + item.Tag] == title);
        if (item is null) return;
        ViewModel.Navigate((string)item.Tag); SearchFlyout.Hide();
    }
    private void OnSearchChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args) => SearchRoute(args.SelectedItem as string);
    private void OnSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) => SearchRoute(args.ChosenSuggestion as string ?? args.QueryText);
    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        // Keep the left rail usable at the supported minimum without mirroring it.
        Navigation.OpenPaneLength = Root.ActualWidth < 1100 ? 224 : 294;
        var compact = Root.ActualHeight < 850;
        foreach (var item in Routes().Where(item => !Equals(item.Tag, "About")))
        {
            item.MinHeight = compact ? 36 : 48;
            item.Margin = new Thickness(8, compact ? 0 : 3, 8, compact ? 0 : 3);
        }
        CaptionInset.Width = AppWindow.TitleBar.RightInset / Math.Max(1, Root.XamlRoot?.RasterizationScale ?? 1);
    }

    private void OnPaneOpened(NavigationView sender, object args)
    {
        if (PaneFooterText is not null) PaneFooterText.Visibility = Visibility.Visible;
        if (PaneHeaderText is not null) PaneHeaderText.Visibility = Visibility.Visible;
    }
    private void OnPaneClosed(NavigationView sender, object args)
    {
        if (PaneFooterText is not null) PaneFooterText.Visibility = Visibility.Collapsed;
        if (PaneHeaderText is not null) PaneHeaderText.Visibility = Visibility.Collapsed;
    }
    private void UpdateVisibility() => ViewModel.SetWindowState(AppWindow.IsVisible,
        AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized });
    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        appearance.Apply();
        UpdateVisibility();
        if (AppWindow.IsVisible) StartupTimeline.WindowVisible();
        // Shutdown is announced at BeginShutdown, which runs long before Closed disposes the
        // page view models. Without this guard a restore event landing inside that window
        // would start refreshes whose lifetime token is already cancelled or disposed.
        if (shuttingDown || args.WindowActivationState == WindowActivationState.Deactivated || !AppWindow.IsVisible) return;
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized }) return;
        // Each action performs only IsStale before joining the shared background load.
        // No full scan and no direct producer call on activation.
        _ = orchestrator.RefreshStaleAsync(activationRefreshes, activationLifetime.Token);
    }
    // The orchestrator awaits each admitted refresh on a thread-pool thread, so the action is
    // queued onto the window's dispatcher and entered there. A fault is propagated rather than
    // absorbed, so the orchestrator still records it against the dataset exactly as before the
    // hop. A dispatcher that refuses the work means the window is already going away: the
    // dataset is released by the orchestrator either way and a later activation re-checks it.
    private Task OnUiAsync(Func<Task> action)
    {
        if (DispatcherQueue.HasThreadAccess) return action();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(async () =>
        {
            try { await action(); completion.TrySetResult(); }
            catch (Exception e) { completion.TrySetException(e); }
        })) completion.TrySetResult();
        return completion.Task;
    }
    private void OnWindowChanged(AppWindow sender, AppWindowChangedEventArgs args) => UpdateVisibility();
    public void BeginShutdown()
    {
        shuttingDown = true;
        // Cancel before anything else so no activation refresh can be admitted from here on.
        // IsCancellationRequested stays readable after Dispose, unlike the token itself.
        try { activationLifetime.Cancel(); } catch (ObjectDisposedException) { }
        Navigation.IsEnabled = false;
        ContentHost.IsEnabled = false;
        ViewModel.SetWindowState(false, false);
    }
    private void OnClosed(object sender, WindowEventArgs args)
    {
        ViewModel.SetWindowState(false, false);
        appearance.Dispose();
        cleaner.Dispose();
        uninstaller.Dispose();
        startup.Dispose();
        memoryTools.Dispose();
        tools.Dispose();
        AppWindow.Changed -= OnWindowChanged;
        Activated -= OnActivated;
        Closed -= OnClosed;
        ViewModel.PropertyChanged -= OnShellChanged;
        Root.SizeChanged -= OnSizeChanged;
    }
}
