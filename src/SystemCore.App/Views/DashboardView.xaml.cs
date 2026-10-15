using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using SystemCore.App.ViewModels;

namespace SystemCore.App.Views;

public sealed partial class DashboardView : UserControl
{
    public DashboardViewModel ViewModel { get; }
    public DashboardView(DashboardViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Loaded += (_, _) => { ViewModel.MetricsChanged += OnUpdated; Render(); };
        Unloaded += (_, _) => ViewModel.MetricsChanged -= OnUpdated;
    }
    private void OnUpdated(object? sender, EventArgs args) => Render();
    private void Render()
    {
        var history = ViewModel.History; var rtl = ViewModel.Culture.TextInfo.IsRightToLeft;
        Cpu.Present(ViewModel.Cards[0], ViewModel.GaugeText(0), ViewModel.CpuMetadata, "\uE950", "F1BlueBrush", history, 0, ViewModel.HistoryLabel, rtl);
        Memory.Present(ViewModel.Cards[1], ViewModel.GaugeText(1), ViewModel.MemoryMetadata, "\uE964", "F1PurpleBrush", history, 1, ViewModel.HistoryLabel, rtl);
        Disk.Present(ViewModel.Cards[2], ViewModel.GaugeText(2), ViewModel.DiskMetadata, "\uEDA2", "F1TealBrush", ViewModel.CapacityHistory, 2, ViewModel.HistoryLabel, rtl);
    }
    private void OnLayoutChanged(object sender, SizeChangedEventArgs args)
    {
        var width = DashboardLayout.ActualWidth - 64;
        var columns = width >= 800 ? 3 : width >= 520 ? 2 : 1;
        Arrange(MetricRow, columns); Arrange(ActionRow, columns); Arrange(StatusRow, columns);
        HeroAction.Visibility = width >= 700 ? Visibility.Visible : Visibility.Collapsed; // Same scan command stays in the action card.
    }
    private static void Arrange(Grid grid, int columns)
    {
        if (grid.ColumnDefinitions.Count == columns) return;
        grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
        for (var i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < (grid.Children.Count + columns - 1) / columns; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < grid.Children.Count; i++) { Grid.SetColumn((FrameworkElement)grid.Children[i], i % columns); Grid.SetRow((FrameworkElement)grid.Children[i], i / columns); }
    }
}
