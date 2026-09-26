using Microsoft.UI.Xaml.Controls;
namespace SystemCore.App.Performance;
public sealed partial class PerformanceView : UserControl
{
    public PerformanceViewModel ViewModel { get; }
    public PerformanceView(PerformanceViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        // WinUI's selection-box text can cache the old localized ComboBoxItem content.
        // Language changes on Settings while this view is unloaded; refresh labels on return.
        Loaded += (_, _) =>
        {
            var mode = ViewModel.Mode;
            var sort = ViewModel.Sort;
            ModePicker.ItemsSource = new[] { ViewModel.TopCpu, ViewModel.TopMemory, ViewModel.AllProcesses };
            SortPicker.ItemsSource = new[] { ViewModel.CpuTitle, ViewModel.WorkingSetLabel };
            ModePicker.SelectedIndex = mode;
            SortPicker.SelectedIndex = sort;
        };
    }
}
