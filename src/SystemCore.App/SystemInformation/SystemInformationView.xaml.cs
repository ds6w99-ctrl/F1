using Microsoft.UI.Xaml.Controls;

namespace SystemCore.App.SystemInformation;

public sealed partial class SystemInformationView : UserControl
{
    public SystemInformationViewModel ViewModel { get; }
    public SystemInformationView(SystemInformationViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }
}
