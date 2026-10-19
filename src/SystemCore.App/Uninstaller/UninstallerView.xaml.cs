using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
using SystemCore.Core.Uninstaller;

using SystemCore.App.Services;

namespace SystemCore.App.Uninstaller;

public sealed partial class UninstallerView : UserControl, IDisposable
{
    public UninstallerViewModel ViewModel { get; }
    private readonly ApplicationIconCache icons = new();
    private readonly Dictionary<Image, CancellationTokenSource> iconRequests = [];
    private string[] displayed = [];
    private Dictionary<string, InstalledApplication> currentApps = [];
    private Dictionary<string, LeftoverItem> currentLeftovers = [];
    private bool disposed;
    private bool rendering;
    public UninstallerView(UninstallerViewModel model)
    {
        ViewModel = model; InitializeComponent();
        Unloaded += (_, _) => CancelIcons();
        Loaded += async (_, _) => { Render(); await ViewModel.InitializeAsync(); };
        ViewModel.PropertyChanged += OnChanged;
        Render();
    }
    private void OnChanged(object? sender, PropertyChangedEventArgs args) => Render();
    private string DisplayName(InstalledApplication app) => string.IsNullOrWhiteSpace(app.DisplayName) ? ViewModel.Text("UnUnknown") : app.DisplayName;
    private string Label(InstalledApplication a)
    {
        static string Technical(string value) => SystemCore.Core.SystemInformation.InfoFormatting.Technical(value);
        return $"{DisplayName(a)} â€” {a.Publisher ?? ViewModel.Text("UnUnknown")}\n{Technical(a.DisplayVersion ?? ViewModel.Text("UnUnknown"))} | {Technical(a.ApplicationType.ToString())} | {Technical(ViewModel.FormatSize(a.EstimatedSize))} | {Technical(ViewModel.FormatDate(a.InstallDate))}";
    }
    private void Render()
    {
        rendering = true;
        try
        {
            Title.Text = ViewModel.Title; Search.PlaceholderText = ViewModel.Text("UnSearch");
            Refresh.Content = ViewModel.Text("UnRefresh"); Uninstall.Content = ViewModel.Text("UnUninstall");
            CancelOperation.Content = ViewModel.Text("UnCancel"); CancelOperation.IsEnabled = ViewModel.Busy;
            Previous.Content = ViewModel.Text("UnPrevious"); Next.Content = ViewModel.Text("UnNext");
            Filter.Items.Clear(); foreach (var key in new[] { "UnAll", "UnWin32", "UnMsi", "UnMsix", "UnRecent", "UnLarge" }) Filter.Items.Add(key == "UnLarge" ? ViewModel.LargeFilterLabel : ViewModel.Text(key));
            Filter.SelectedIndex = ViewModel.Filter;
            KindFilter.Items.Clear(); foreach (var key in new[] { "UnAllKinds", "UnApplicationsKind", "UnGamesKind" }) KindFilter.Items.Add(ViewModel.Text(key));
            KindFilter.SelectedIndex = ViewModel.KindFilter;
            AutomationProperties.SetName(KindFilter, ViewModel.Text("UnKindFilter"));
            Sort.Items.Clear(); foreach (var key in new[] { "UnName", "UnPublisher", "UnDate", "UnSize" }) Sort.Items.Add(ViewModel.Text(key));
            Sort.SelectedIndex = ViewModel.Sort;
            Status.Text = string.IsNullOrEmpty(ViewModel.CleanupSummary) || ViewModel.InventoryLoadFailed ? ViewModel.Status : ViewModel.Text("UnCleanupComplete");
            WaitHintText.Visibility = ViewModel.ShowWaitHint ? Visibility.Visible : Visibility.Collapsed;
            WaitHintText.Text = ViewModel.WaitHint;
            Refresh.IsEnabled = !ViewModel.Busy; Search.IsEnabled = Filter.IsEnabled = Sort.IsEnabled = Applications.IsEnabled = !ViewModel.Busy;
            var rows = ViewModel.Rows;
            var leftovers = ViewModel.LeftoverRows;
            currentApps = rows.ToDictionary(a => a.Id);
            currentLeftovers = leftovers.ToDictionary(a => a.Id.ToString());
            Search.IsEnabled = Filter.IsEnabled = Sort.IsEnabled = !ViewModel.Busy && !ViewModel.ShowLeftovers;
            KindFilter.IsEnabled = Search.IsEnabled;
            var ids = ViewModel.ShowLeftovers ? leftovers.Select(a => a.Id.ToString()).ToArray() : rows.Select(a => a.Id).ToArray();
            if (!displayed.SequenceEqual(ids)) { CancelIcons(); displayed = ids; Applications.ItemsSource = ids; }
            Applications.SelectedItem = !ViewModel.ShowLeftovers && ids.Contains(ViewModel.SelectedId) ? ViewModel.SelectedId : null;
            for (var index = 0; index < displayed.Length; index++)
                if (Applications.ContainerFromIndex(index) is ListViewItem item) PresentRow(item, displayed[index]);
            Previous.IsEnabled = !ViewModel.Busy && ViewModel.HasPrevious; Next.IsEnabled = !ViewModel.Busy && ViewModel.HasNext;
            PageNumber.Text = $"{ViewModel.Page} / {Math.Max(1, (ViewModel.Count + 19) / 20)} â€” {ViewModel.Count}";
            Details.Text = ViewModel.ShowLeftovers ? "" : ViewModel.Selected is { } selected ? (UnifiedLibraryPresentation.IsLauncherManaged(selected) ? ViewModel.ManagedLabel(selected) + "\n" : "") + Label(selected) : ViewModel.Text("UnSelectDetails");
            Details.Visibility = ViewModel.ShowLeftovers ? Visibility.Collapsed : Visibility.Visible;
            AutomationProperties.SetHelpText(Status, $"Raw={ViewModel.RawCount};Hidden={ViewModel.HiddenCount};Removable={ViewModel.RemovableCount};Managed={ViewModel.ManagedCount}");
            Uninstall.IsEnabled = !ViewModel.Busy && ViewModel.Selected is { CanUninstall: true, IsProtected: false };
            Uninstall.Visibility = ViewModel.Selected is { } managed && UnifiedLibraryPresentation.IsLauncherManaged(managed) ? Visibility.Collapsed : Visibility.Visible;
            OpenLauncher.Visibility = ViewModel.Selected is { ItemKind: ItemKind.Game } ? Visibility.Visible : Visibility.Collapsed;
            OpenLauncher.Content = ViewModel.Text("UnOpenLauncher"); OpenLauncher.IsEnabled = ViewModel.CanOpenLauncher;
            ToolTipService.SetToolTip(OpenLauncher, ViewModel.Text(ViewModel.CanOpenLauncher ? "UnOpenLauncher" : "UnLauncherUnavailable"));
            ScanLeftovers.Content = ViewModel.Text("UnScanLeftovers"); ScanLeftovers.IsEnabled = ViewModel.CanScanLeftovers;
            AllowPersonalData.Content = ViewModel.Text("UnAllowPersonalData");
            AllowPersonalData.Visibility = ViewModel.ShowLeftovers && ViewModel.Leftovers.Any(i => i.Confidence == LeftoverConfidence.Review) ? Visibility.Visible : Visibility.Collapsed;
            AllowPersonalData.IsChecked = ViewModel.AllowPersonalData;
            AllowPersonalData.IsEnabled = ViewModel.CanChangeLeftoverSelection;
            ToolTipService.SetToolTip(AllowPersonalData, ViewModel.Text("UnPersonalData"));
            LeftoverSummary.Text = ViewModel.LeftoverSummary;
            DeleteLeftovers.Content = $"{ViewModel.Text("UnDeleteLeftovers")} ({ViewModel.SelectedLeftoverCount})";
            DeleteLeftovers.IsEnabled = ViewModel.CanDeleteLeftovers;
            // Bulk and orphan controls: selection is per visible row and never implies removal.
            UninstallSelected.Content = ViewModel.Text("UnUninstall");
            UninstallSelected.Visibility = ViewModel.SelectedApplicationCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            UninstallSelected.IsEnabled = ViewModel.CanUninstallSelected;
            SelectAll.Content = ViewModel.SelectedApplicationCount > 0 ? ViewModel.Text("UnClearSelection") : ViewModel.Text("UnSelectAll");
            SelectAll.IsEnabled = ViewModel.CanSelectApplications;
            SelectAll.Visibility = ViewModel.ShowLeftovers ? Visibility.Collapsed : Visibility.Visible;
            QuietToggle.Content = ViewModel.Text("UnQuietToggle");
            QuietToggle.IsChecked = ViewModel.UseQuietUninstaller;
            QuietToggle.IsEnabled = ViewModel.CanSelectApplications;
            QuietToggle.Visibility = ViewModel.ShowLeftovers ? Visibility.Collapsed : Visibility.Visible;
            ToolTipService.SetToolTip(QuietToggle, ViewModel.QuietNote);
            ScanOrphans.Content = ViewModel.Text("UnOrphanScan");
            ScanOrphans.IsEnabled = ViewModel.CanScanOrphans;
            ScanOrphans.Visibility = ViewModel.ShowLeftovers ? Visibility.Collapsed : Visibility.Visible;
            ExportButton.Content = ViewModel.Text("UnExport");
            ExportButton.IsEnabled = !ViewModel.Busy;
            BulkSummary.Text = ViewModel.BulkSummary;
            OrphanSummary.Text = ViewModel.Orphans.Count > 0 ? ViewModel.OrphanSummary + "\n" + ViewModel.Text("UnOrphanNote") : "";
            ExportNote.Text = ViewModel.LastExportFile.Length > 0 ? ViewModel.ExportNote : "";
            KeepLeftovers.Content = ViewModel.Text("UnKeepLeftovers");
            KeepLeftovers.Visibility = ViewModel.ShowLeftovers ? Visibility.Visible : Visibility.Collapsed;
            KeepLeftovers.IsEnabled = !ViewModel.Busy;
            CleanupSummary.Text = ViewModel.CleanupSummary;
        }
        finally { rendering = false; }
    }
    private void OnSearch(object sender, TextChangedEventArgs args) { if (!rendering) ViewModel.Search = Search.Text; }
    private void OnFilter(object sender, SelectionChangedEventArgs args) { if (!rendering) ViewModel.Filter = Filter.SelectedIndex; }
    private void OnKindFilter(object sender, SelectionChangedEventArgs args) { if (!rendering) ViewModel.KindFilter = KindFilter.SelectedIndex; }
    private void OnOpenLauncher(object sender, RoutedEventArgs args) => UiSafety.Run(ViewModel.OpenLauncherAsync);
    private void OnSort(object sender, SelectionChangedEventArgs args) { if (!rendering) ViewModel.Sort = Sort.SelectedIndex; }
    private void OnSelection(object sender, SelectionChangedEventArgs args) { if (!rendering) ViewModel.SelectedId = Applications.SelectedItem as string ?? ""; }
    private void OnRefresh(object sender, RoutedEventArgs args) => UiSafety.Run(async () =>
    {
        CancelIcons();
        icons.Refresh(ViewModel.Inventory);
        await ViewModel.RefreshAsync();
        icons.Refresh(ViewModel.Inventory);
    });
    private void OnCancelOperation(object sender, RoutedEventArgs args) => ViewModel.CancelOperation();
    private void OnScanLeftovers(object sender, RoutedEventArgs args) => UiSafety.Run(ViewModel.ScanLeftoversAsync);
    private void OnKeepLeftovers(object sender, RoutedEventArgs args) => ViewModel.KeepLeftovers();
    private void OnAllowPersonalData(object sender, RoutedEventArgs args) { if (!rendering) ViewModel.AllowPersonalData = AllowPersonalData.IsChecked == true; }
    private void OnLeftoverToggle(object sender, RoutedEventArgs args)
    {
        if (rendering || sender is not CheckBox { Tag: var tag } choice) return;
        if (tag is Guid id) { ViewModel.SelectLeftover(id, choice.IsChecked == true); return; }
        // Application rows carry the application id as a string tag; selection authorizes nothing
        // until the explicit bulk confirmation runs.
        if (FindRowId(choice) is { } applicationId) ViewModel.SelectApplication(applicationId, choice.IsChecked == true);
    }
    private string? FindRowId(CheckBox choice)
    {
        if (choice.DataContext is string context) return context;
        foreach (var id in displayed)
            if (Applications.ContainerFromItem(id) is ListViewItem item && item.ContentTemplateRoot is Grid grid
                && ReferenceEquals(grid.FindName("Choice"), choice)) return id;
        return null;
    }
    private void OnDeleteLeftovers(object sender, RoutedEventArgs args) => UiSafety.Run(async () =>
    {
        if (!ViewModel.CanDeleteLeftovers) return;
        var ids = ViewModel.SelectedLeftoverIds();
        var personal = ViewModel.AllowPersonalData && ViewModel.Leftovers.Any(i => i.Confidence != LeftoverConfidence.Safe && ViewModel.IsLeftoverSelected(i.Id));
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = ViewModel.Text("UnDeleteLeftovers"),
            PrimaryButtonStyle = (Style)Application.Current.Resources["DangerButtonStyle"],
            Content = $"{ids.Length}\n{ViewModel.Text("UnDeleteConfirm")}" + (personal ? "\n" + ViewModel.Text("UnPersonalDataConfirm") : "") + (ViewModel.HasSelectedRegistry ? "\n" + ViewModel.Text("UnRegistryWarning") : ""),
            PrimaryButtonText = ViewModel.Text("UnDeleteLeftovers"), CloseButtonText = ViewModel.Text("UnCancel"), DefaultButton = ContentDialogButton.Close, FlowDirection = FlowDirection, RequestedTheme = ActualTheme };
        AutomationProperties.SetAutomationId(dialog, "UnLeftoverConfirmation");
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await ViewModel.DeleteLeftoversConfirmedAsync(ids);
    });
    private void OnSelectAll(object sender, RoutedEventArgs args)
    {
        if (ViewModel.SelectedApplicationCount > 0) ViewModel.SelectVisibleApplications(false);
        else SelectAllVisible();
    }
    private void SelectAllVisible()
    {
        if (!ViewModel.CanSelectApplications) return;
        ViewModel.SelectVisibleApplications(true);
    }
    private void OnQuietToggle(object sender, RoutedEventArgs args) { if (!rendering) ViewModel.UseQuietUninstaller = QuietToggle.IsChecked == true; }
    private void OnScanOrphans(object sender, RoutedEventArgs args) => UiSafety.Run(ViewModel.ScanOrphansAsync);
    private void OnExport(object sender, RoutedEventArgs args) => UiSafety.Run(() => ViewModel.ExportAsync(SystemCore.Infrastructure.Uninstaller.UninstallerExportFormat.Csv));
    private void OnUninstallSelected(object sender, RoutedEventArgs args) => UiSafety.Run(async () =>
    {
        if (!ViewModel.CanUninstallSelected) return;
        var count = ViewModel.SelectedApplicationCount;
        var quiet = ViewModel.UseQuietUninstaller && ViewModel.AnySelectedHasQuiet;
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = ViewModel.Text("UnUninstall"),
            PrimaryButtonStyle = (Style)Application.Current.Resources["DangerButtonStyle"],
            Content = ViewModel.Text("UnBulkSelected").Replace("{0}", count.ToString()) + "\n" + ViewModel.Text("UnConfirm") + (quiet ? "\n" + ViewModel.Text("UnQuietNote") : ""),
            PrimaryButtonText = ViewModel.Text("UnUninstall"), CloseButtonText = ViewModel.Text("UnCancel"),
            DefaultButton = ContentDialogButton.Close, FlowDirection = FlowDirection, RequestedTheme = ActualTheme };
        AutomationProperties.SetAutomationId(dialog, "UnBulkConfirmation");
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var ids = ViewModel.Rows.Where(a => ViewModel.IsApplicationSelected(a.Id)).Select(a => a.Id)
            .Concat(ViewModel.Inventory.Where(a => ViewModel.IsApplicationSelected(a.Id)).Select(a => a.Id)).Distinct().ToArray();
        await ViewModel.UninstallSelectedConfirmedAsync(ids);
    });
    private void OnPrevious(object sender, RoutedEventArgs args) => ViewModel.MovePage(-1);
    private void OnNext(object sender, RoutedEventArgs args) => ViewModel.MovePage(1);
    private void OnUninstall(object sender, RoutedEventArgs args) => UiSafety.Run(async () =>
    {
        if (ViewModel.Selected is not { CanUninstall: true } selected) return;
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = ViewModel.Text("UnUninstall"), Content = DisplayName(selected) + "\n" + ViewModel.Text("UnConfirm"),
            PrimaryButtonStyle = (Style)Application.Current.Resources["DangerButtonStyle"],
            PrimaryButtonText = ViewModel.Text("UnUninstall"), CloseButtonText = ViewModel.Text("UnCancel"), DefaultButton = ContentDialogButton.Close, FlowDirection = FlowDirection, RequestedTheme = ActualTheme };
        AutomationProperties.SetAutomationId(dialog, "UnConfirmation");
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await ViewModel.UninstallConfirmedAsync(selected.Id);
    });
    public void Dispose()
    {
        disposed = true; CancelIcons();
        ViewModel.PropertyChanged -= OnChanged;
        Applications.ItemsSource = null; displayed = []; currentApps.Clear(); currentLeftovers.Clear();
    }

    private void OnContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue)
        {
            if (args.ItemContainer.ContentTemplateRoot is Grid grid && grid.FindName("AppIcon") is Image image) CancelIcon(image);
            return;
        }
        if (args.Item is string id && args.ItemContainer is ListViewItem item) PresentRow(item, id);
    }
    private void PresentRow(ListViewItem item, string id)
    {
        if (item.ContentTemplateRoot is not Grid grid) return;
        var text = (TextBlock)grid.FindName("RowText"); var metadata = (TextBlock)grid.FindName("RowMetadata");
        var image = (Image)grid.FindName("AppIcon"); var choice = (CheckBox)grid.FindName("Choice");
        var prior = rendering; rendering = true;
        try
        {
            var leftover = ViewModel.ShowLeftovers ? currentLeftovers.GetValueOrDefault(id) : null;
            var app = !ViewModel.ShowLeftovers ? currentApps.GetValueOrDefault(id) : null;
            choice.Visibility = leftover is not null ? Visibility.Visible : (ViewModel.CanSelectApplications ? Visibility.Visible : Visibility.Collapsed);
            choice.Tag = leftover?.Id;
            choice.IsEnabled = leftover is not null ? ViewModel.CanChangeLeftoverSelection && ViewModel.CanSelectLeftover(leftover) : ViewModel.CanSelectApplications;
            choice.IsChecked = leftover is not null ? ViewModel.IsLeftoverSelected(leftover.Id) : (app is not null && ViewModel.IsApplicationSelected(app.Id));
            AutomationProperties.SetName(choice, leftover?.DisplayLocation ?? "");
            text.TextWrapping = leftover is null ? TextWrapping.NoWrap : TextWrapping.Wrap;
            text.MaxLines = leftover is null ? 2 : 0;
            text.Text = app is not null ? $"{DisplayName(app)}\n{app.Publisher ?? ViewModel.Text("UnUnknown")} Â· {app.DisplayVersion ?? ViewModel.Text("UnUnknown")}" : leftover is not null ? ViewModel.LeftoverLabel(leftover) : "";
            metadata.Text = app is null ? "" : $"{ViewModel.FormatSize(app.EstimatedSize)}\n{(app.ItemKind == ItemKind.Game ? ViewModel.SourceLabel(app) : app.ApplicationType.ToString())}";
            grid.BorderThickness = app is not null && app.Id == ViewModel.SelectedId ? new Thickness(3, 0, 0, 0) : new Thickness(0);
            AutomationProperties.SetName(item, app is not null ? DisplayName(app) : leftover?.DisplayLocation ?? "");
            if (app is not null && !Equals(image.Tag, app)) LoadIcon(image, app);
            else if (app is null) { CancelIcon(image); image.Source = new BitmapImage(new Uri("ms-appx:///Assets/Brand/F1_Logo.png")); }
        }
        finally { rendering = prior; }
    }
    private async void LoadIcon(Image image, InstalledApplication app)
    {
        CancelIcon(image); image.Tag = app;
        image.Source = new BitmapImage(new Uri("ms-appx:///Assets/Brand/F1_Logo.png"));
        var request = new CancellationTokenSource(); iconRequests[image] = request;
        var token = request.Token;
        try
        {
            var bytes = await icons.GetAsync(app, token);
            if (bytes is null || token.IsCancellationRequested || disposed) return;
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(bytes); await writer.StoreAsync(); }
            stream.Seek(0); var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(stream);
            if (!token.IsCancellationRequested && !disposed && Equals(image.Tag, app)) image.Source = bitmap;
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or IOException or ArgumentException) { /* Local fallback stays visible. */ }
        finally
        {
            if (iconRequests.TryGetValue(image, out var current) && ReferenceEquals(current, request)) iconRequests.Remove(image);
            request.Dispose();
        }
    }
    private void CancelIcon(Image image)
    {
        if (iconRequests.Remove(image, out var request)) request.Cancel();
        image.Tag = null;
    }
    private void CancelIcons()
    {
        foreach (var image in iconRequests.Keys.ToArray()) CancelIcon(image);
    }
}
