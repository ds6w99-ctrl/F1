using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace SystemCore.App.Startup;

public sealed partial class StartupView : UserControl, IDisposable
{
    private readonly StartupViewModel model;
    private readonly List<(ListViewItem Item, TextBlock Text)> rows = [];
    private bool rendering;
    public StartupView(StartupViewModel model)
    {
        this.model = model; InitializeComponent();
        for (var index = 0; index < 20; index++)
        {
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var row = new ListViewItem { Content = text, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            rows.Add((row, text)); Items.Items.Add(row);
        }
        model.PropertyChanged += OnChanged; Render();
    }
    private void OnChanged(object? sender, PropertyChangedEventArgs args) => Render();
    private void Render()
    {
        rendering = true;
        try
        {
            Title.Text = model.Text("NavStartup"); Search.PlaceholderText = model.Text("StartSearch");
            Refresh.Content = model.Text("StartRefresh"); Cancel.Content = model.Text("UnCancel");
            Previous.Content = model.Text("UnPrevious"); Next.Content = model.Text("UnNext");
            Disable.Content = model.Text("StartDisable"); Enable.Content = model.Text("StartEnable"); OpenLocation.Content = model.Text("StartOpenLocation");
            UpdateOptions(Filter, new[] { "All", "Enabled", "Disabled", "CurrentUser", "AllUsers", "Registry", "Folder", "Tasks", "Protected" }.Select(k => model.Text("StartFilter" + k)).ToArray());
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
                row.Text.Text = item is null ? "" : model.Label(item); row.Item.Tag = item?.Id;
                row.Item.Visibility = item is null ? Visibility.Collapsed : Visibility.Visible;
                row.Item.IsSelected = item is not null && item.Id == model.SelectedId;
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
    private async void OnRefresh(object sender, RoutedEventArgs args) => await model.RefreshAsync();
    private void OnCancel(object sender, RoutedEventArgs args) => model.Cancel();
    private void OnPrevious(object sender, RoutedEventArgs args) => model.MovePage(-1);
    private void OnNext(object sender, RoutedEventArgs args) => model.MovePage(1);
    private async void OnDisable(object sender, RoutedEventArgs args) => await ConfirmAsync(SystemCore.Core.Startup.StartupAction.Disable);
    private async void OnEnable(object sender, RoutedEventArgs args) => await ConfirmAsync(SystemCore.Core.Startup.StartupAction.Enable);
    private bool confirming;
    private async Task ConfirmAsync(SystemCore.Core.Startup.StartupAction action)
    {
        if (confirming || !(action == SystemCore.Core.Startup.StartupAction.Disable ? model.CanDisable : model.CanEnable)) return;
        confirming = true; var id = model.SelectedId;
        try
        {
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = model.Text(action == SystemCore.Core.Startup.StartupAction.Disable ? "StartDisable" : "StartEnable"),
                PrimaryButtonStyle = (Style)Application.Current.Resources["DangerButtonStyle"],
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
        if (model.Selected?.ExecutablePath is { } path) SystemCore.Infrastructure.Startup.StartupFileLocation.Open(path);
    }
    public void Dispose() { model.PropertyChanged -= OnChanged; foreach (var row in rows) { row.Item.Tag = null; row.Text.Text = ""; } Items.Items.Clear(); rows.Clear(); }
}
