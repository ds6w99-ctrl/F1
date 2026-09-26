using Microsoft.UI.Xaml.Controls;
using SystemCore.App.ViewModels;

namespace SystemCore.App.Views;

public sealed partial class AboutView : UserControl
{
    public ShellViewModel ViewModel { get; }
    public AboutView(ShellViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DiagnosticsHost.Content = new DiagnosticsPanel(viewModel.Support);
        Loaded += (_, _) => { ViewModel.Support.PropertyChanged += OnChanged; Render(); };
        Unloaded += (_, _) => ViewModel.Support.PropertyChanged -= OnChanged;
        Render();
    }
    private void OnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => Render();
    private void Render()
    {
        Supported.Text = ViewModel.Support.Text("AboutSupported"); Privacy.Text = ViewModel.Support.Text("SettingsPrivacyNotice"); Copyright.Text = ViewModel.Support.Text("AboutCopyright");
        CopyVersion.Content = ViewModel.Support.Text("AboutCopyVersion"); Licenses.Header = ViewModel.Support.Text("AboutLicenses");
    }
    private void OnCopyVersion(object sender, Microsoft.UI.Xaml.RoutedEventArgs args) => ViewModel.Support.CopyVersion();
    private void OnNotices(Expander sender, ExpanderExpandingEventArgs args) => Notices.Text = ViewModel.Support.Notices;
}
