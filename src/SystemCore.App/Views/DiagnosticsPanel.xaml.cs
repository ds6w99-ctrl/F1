using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SystemCore.App.ViewModels;
using SystemCore.Core.Diagnostics;

using SystemCore.App.Services;

namespace SystemCore.App.Views;

public sealed partial class DiagnosticsPanel : UserControl
{
    public ProductSupportViewModel ViewModel { get; }
    public DiagnosticsPanel(ProductSupportViewModel model)
    {
        ViewModel = model; InitializeComponent(); Loaded += OnLoaded; Unloaded += OnUnloaded; Render();
    }
    private void OnLoaded(object sender, RoutedEventArgs args) { ViewModel.PropertyChanged += OnChanged; UiSafety.Run(ViewModel.RefreshAsync); }
    private void OnUnloaded(object sender, RoutedEventArgs args) => ViewModel.PropertyChanged -= OnChanged;
    private void OnChanged(object? sender, PropertyChangedEventArgs args) => Render();
    private void Render()
    {
        Title.Text = ViewModel.Text("DiagnosticsTitle"); Description.Text = ViewModel.Text("DiagnosticsDescription");
        OpenLogs.Content = ViewModel.Text("DiagnosticsOpenLogs"); Export.Content = ViewModel.Text("DiagnosticsExport"); Clear.Content = ViewModel.Text("DiagnosticsClear");
        OpenExports.Content = ViewModel.Text("DiagnosticsOpenExports"); OpenData.Content = ViewModel.Text("DiagnosticsOpenData");
    }
    private void OnOpenLogs(object sender, RoutedEventArgs args) => UiSafety.Run(() => ViewModel.OpenAsync(DiagnosticFolder.Logs));
    private void OnOpenExports(object sender, RoutedEventArgs args) => UiSafety.Run(() => ViewModel.OpenAsync(DiagnosticFolder.Exports));
    private void OnOpenData(object sender, RoutedEventArgs args) => UiSafety.Run(() => ViewModel.OpenAsync(DiagnosticFolder.ApplicationData));
    private void OnExport(object sender, RoutedEventArgs args) => UiSafety.Run(ViewModel.ExportAsync);
    private void OnClear(object sender, RoutedEventArgs args) => UiSafety.Run(async () =>
    {
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = ViewModel.Text("DiagnosticsClear"), Content = ViewModel.Text("DiagnosticsClearConfirm"),
            PrimaryButtonStyle = (Style)Application.Current.Resources["DangerButtonStyle"],
            PrimaryButtonText = ViewModel.Text("DiagnosticsClear"), CloseButtonText = ViewModel.Text("UnCancel"), DefaultButton = ContentDialogButton.Close, FlowDirection = FlowDirection, RequestedTheme = ActualTheme };
        AutomationProperties.SetAutomationId(dialog, "DiagnosticsClearConfirmation");
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await ViewModel.ClearOldAsync();
    });
}
