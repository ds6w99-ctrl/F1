using Microsoft.UI.Xaml.Controls;
using SystemCore.App.ViewModels;

namespace SystemCore.App.Views;

public sealed partial class PlaceholderView : UserControl
{
    public ShellViewModel ViewModel { get; }
    public PlaceholderView(ShellViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }
}
