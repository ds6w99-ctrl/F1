using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SystemCore.Core.Startup;
using SystemCore.Infrastructure.Startup;
using Windows.Storage.Streams;

using SystemCore.App.Services;

namespace SystemCore.App.Startup;

public sealed partial class StartupView : UserControl, IDisposable
{
    private readonly StartupViewModel model;
    private sealed record Row(ListViewItem Item, TextBlock Name, TextBlock Metadata, TextBlock State, Image Icon, FontIcon Fallback, Border Accent)
    {
        public StartupItem? Model { get; set; }
    }
    private readonly List<Row> rows = [];
    private static readonly StartupIconCache icons = new();
    private CancellationTokenSource iconLifetime = new();
    private bool disposed;
    private bool active;
    private bool wasBusy;
    private bool rendering;
    public StartupView(StartupViewModel model)
    {
        this.model = model; InitializeComponent();
        for (var index = 0; index < 20; index++)
        {
            var name = new TextBlock { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            var metadata = new TextBlock { FontSize = 12, Opacity = .75, TextTrimming = TextTrimming.CharacterEllipsis };
            var text = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center }; text.Children.Add(name); text.Children.Add(metadata);
            var image = new Image { Width = 36, Height = 36 };
            var fallback = new FontIcon { Glyph = "\uE7F4", FontSize = 26, Foreground = (Brush)Application.Current.Resources["F1BlueBrush"] };
            var icon = new Grid { Width = 36, Height = 36 }; icon.Children.Add(fallback); icon.Children.Add(image);
            AutomationProperties.SetAccessibilityView(icon, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            var state = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(10, 8, 10, 8) };
            grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            grid.Children.Add(icon); Grid.SetColumn(text, 1); grid.Children.Add(text); Grid.SetColumn(state, 2); grid.Children.Add(state);
            var accent = new Border { Child = grid, BorderThickness = new Thickness(3, 0, 0, 0), CornerRadius = new CornerRadius(4) };
            var row = new ListViewItem { Content = accent, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            rows.Add(new(row, name, metadata, state, image, fallback, accent)); Items.Items.Add(row);
        }
        Loaded += OnLoaded; Unloaded += OnUnloaded;
        model.PropertyChanged += OnChanged; Render();
    }
    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (disposed) return;
        active = true; ResetIconLifetime(); foreach (var row in rows) row.Model = null; Render();
        UiSafety.Run(model.InitializeAsync);
    }
    private void OnUnloaded(object sender, RoutedEventArgs args) { active = false; iconLifetime.Cancel(); }
    private void ResetIconLifetime() { iconLifetime.Cancel(); iconLifetime.Dispose(); iconLifetime = new(); }
    private void OnChanged(object? sender, PropertyChangedEventArgs args) => Render();
    private void Render()
    {
        if (disposed) return;
        if (model.Busy && !wasBusy) ResetIconLifetime();
        wasBusy = model.Busy;
        rendering = true;
        try
        {
            Title.Text = model.Text("NavStartup"); Search.PlaceholderText = model.Text("StartSearch");
            Refresh.Content = model.Text("StartRefresh"); Cancel.Content = model.Text("UnCancel");
            Previous.Content = model.Text("UnPrevious"); Next.Content = model.Text("UnNext");
            Disable.Content = model.Text("StartDisable"); Enable.Content = model.Text("StartEnable"); OpenLocation.Content = model.Text("StartOpenLocation");
            UpdateOptions(Filter, new[] { "All", "Enabled", "Disabled", "CurrentUser", "AllUsers", "Registry", "Folder", "Tasks" }.Select(k => model.Text("StartFilter" + k)).ToArray());
            Filter.SelectedIndex = model.Filter;
            UpdateOptions(Sort, new[] { "Name", "Publisher", "Status", "Source", "Scope" }.Select(k => model.Text("StartSort" + k)).ToArray());
            Sort.SelectedIndex = model.Sort;
            Status.Text = model.StatusText; Details.Text = model.Details; Technical.Text = model.TechnicalDetails; Page.Text = model.PageText;
            Refresh.IsEnabled = Search.IsEnabled = Filter.IsEnabled = Sort.IsEnabled = Items.IsEnabled = !model.Busy;
            Cancel.IsEnabled = model.CanCancel; Next.IsEnabled = model.HasNext; Previous.IsEnabled = model.HasPrevious;
            Disable.IsEnabled = model.CanDisable; Enable.IsEnabled = model.CanEnable;
            OpenLocation.IsEnabled = !model.Busy && model.Selected?.ExecutablePath is not null;
            AutomationProperties.SetName(Search, model.Text("StartSearch"));
            AutomationProperties.SetName(Filter, model.Text("StartFilterAll"));
            AutomationProperties.SetName(Sort, model.Text("StartSortName"));
            var items = model.Rows;
            for (var index = 0; index < rows.Count; index++)
            {
                var item = index < items.Count ? items[index] : null; var row = rows[index];
                row.Name.Text = item?.Name ?? "";
                row.Metadata.Text = item is null ? "" : $"{item.Publisher ?? model.Text("StartUnknown")} • {model.Text("StartSource" + item.SourceType)}";
                row.State.Text = item is null ? "" : model.Text("StartStatus" + item.Status);
                row.Item.Tag = item?.Id;
                row.Item.Visibility = item is null ? Visibility.Collapsed : Visibility.Visible;
                row.Item.IsSelected = item is not null && item.Id == model.SelectedId;
                row.Accent.BorderBrush = row.Item.IsSelected ? (Brush)Application.Current.Resources["F1BlueBrush"] : null;
                row.Accent.Background = row.Item.IsSelected ? (Brush)Application.Current.Resources["F1SelectedBrush"] : null;
                if (row.Model != item)
                {
                    row.Model = item; row.Icon.Source = null; row.Fallback.Visibility = Visibility.Visible;
                    if (active && !model.Busy && item is not null) _ = LoadIconAsync(row, item, iconLifetime.Token);
                }
                AutomationProperties.SetName(row.Item, item?.Name ?? "");
            }
        }
        finally { rendering = false; }
    }
    private static void UpdateOptions(ComboBox combo, string[] options)
    {
        if (combo.Items.Cast<string>().SequenceEqual(options)) return;
        combo.Items.Clear(); foreach (var option in options) combo.Items.Add(option);
    }
    private void OnSearch(object sender, TextChangedEventArgs args) { if (!rendering) model.Search = Search.Text; }
    private void OnFilter(object sender, SelectionChangedEventArgs args) { if (!rendering) model.Filter = Filter.SelectedIndex; }
    private void OnSort(object sender, SelectionChangedEventArgs args) { if (!rendering) model.Sort = Sort.SelectedIndex; }
    private void OnSelection(object sender, SelectionChangedEventArgs args) { if (!rendering) model.SelectedId = (Items.SelectedItem as ListViewItem)?.Tag as string ?? ""; }
    private void OnRefresh(object sender, RoutedEventArgs args)
    {
        ResetIconLifetime();
        UiSafety.Run(model.RefreshAsync);
    }
    private async Task LoadIconAsync(Row row, StartupItem item, CancellationToken token)
    {
        try
        {
            var result = await icons.GetAsync(item, token);
            if (result.Png is not { } bytes || token.IsCancellationRequested || row.Model != item || disposed) return;
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(bytes); await writer.StoreAsync().AsTask(token); }
            stream.Seek(0); var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(stream).AsTask(token);
            if (token.IsCancellationRequested || row.Model != item || disposed) return;
            row.Icon.Source = bitmap; row.Fallback.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOException or System.Runtime.InteropServices.COMException or ArgumentException) { }
    }
    private void OnCancel(object sender, RoutedEventArgs args) => model.Cancel();
    private void OnPrevious(object sender, RoutedEventArgs args) => model.MovePage(-1);
    private void OnNext(object sender, RoutedEventArgs args) => model.MovePage(1);
    private void OnDisable(object sender, RoutedEventArgs args) => UiSafety.Run(() => ConfirmAsync(SystemCore.Core.Startup.StartupAction.Disable));
    private void OnEnable(object sender, RoutedEventArgs args) => UiSafety.Run(() => ConfirmAsync(SystemCore.Core.Startup.StartupAction.Enable));
    private bool confirming;
    private async Task ConfirmAsync(SystemCore.Core.Startup.StartupAction action)
    {
        if (confirming || !(action == SystemCore.Core.Startup.StartupAction.Disable ? model.CanDisable : model.CanEnable)) return;
        confirming = true; var id = model.SelectedId;
        try
        {
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = model.Text(action == SystemCore.Core.Startup.StartupAction.Disable ? "StartDisable" : "StartEnable"),
                PrimaryButtonStyle = (Style)Application.Current.Resources[action == SystemCore.Core.Startup.StartupAction.Disable ? "DangerButtonStyle" : "F1PrimaryButtonStyle"],
                Content = new TextBlock { Text = model.Confirmation(action), TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = model.Text(action == SystemCore.Core.Startup.StartupAction.Disable ? "StartDisable" : "StartEnable"),
                CloseButtonText = model.Text("UnCancel"), DefaultButton = ContentDialogButton.Close,
                FlowDirection = FlowDirection, RequestedTheme = ActualTheme == ElementTheme.Dark ? ElementTheme.Dark : ElementTheme.Light };
            AutomationProperties.SetAutomationId(dialog, "StartConfirmation");
            if (await dialog.ShowAsync() == ContentDialogResult.Primary) await model.ChangeAsync(action, id);
        }
        finally { confirming = false; }
    }
    private void OnOpenLocation(object sender, RoutedEventArgs args)
    {
        if (model.Selected?.ExecutablePath is { } path) model.ReportOpenLocation(SystemCore.Infrastructure.Startup.StartupFileLocation.Open(path));
    }
    public void Dispose() { disposed = true; Loaded -= OnLoaded; Unloaded -= OnUnloaded; iconLifetime.Cancel(); iconLifetime.Dispose(); model.PropertyChanged -= OnChanged; foreach (var row in rows) { row.Model = null; row.Item.Tag = null; row.Icon.Source = null; } Items.Items.Clear(); rows.Clear(); }
}
